namespace Levhrium.Core;

/// <summary>
/// Lista de dominios bloqueados, no formato de um arquivo <c>hosts</c>. Um
/// <see cref="HashSet{T}"/> de dominios custa ~10 MB para 150 mil entradas e
/// economiza muito mais que isso em paginas pesadas -- a melhor relacao
/// custo/RAM do projeto.
///
/// Nao para anuncio servido pelo dominio da propria pagina: limitacao aceita
/// conscientemente em vez de embutir um motor de sintaxe ABP inteiro.
/// </summary>
public sealed class DomainBlocklist
{
    /// <summary>
    /// Semente embutida: os rastreadores e redes de anuncio mais comuns. Para
    /// cobertura de verdade, deixe um arquivo hosts em blocklist.txt na pasta de
    /// dados -- as duas listas sao somadas.
    /// </summary>
    public static readonly IReadOnlyList<string> SeedDomains =
    [
        "doubleclick.net",
        "googleadservices.com",
        "googlesyndication.com",
        "googletagmanager.com",
        "googletagservices.com",
        "google-analytics.com",
        "analytics.google.com",
        "adservice.google.com",
        "ads.yahoo.com",
        "adnxs.com",
        "adsrvr.org",
        "amazon-adsystem.com",
        "criteo.com",
        "criteo.net",
        "taboola.com",
        "outbrain.com",
        "pubmatic.com",
        "rubiconproject.com",
        "openx.net",
        "casalemedia.com",
        "scorecardresearch.com",
        "quantserve.com",
        "moatads.com",
        "adsafeprotected.com",
        "serving-sys.com",
        "bluekai.com",
        "demdex.net",
        "everesttech.net",
        "omtrdc.net",
        "branch.io",
        "mixpanel.com",
        "segment.io",
        "segment.com",
        "amplitude.com",
        "hotjar.com",
        "fullstory.com",
        "clarity.ms",
        "mouseflow.com",
        "crazyegg.com",
        "optimizely.com",
        "newrelic.com",
        "nr-data.net",
        "sentry-cdn.com",
        "facebook.net",
        "connect.facebook.net",
        "ct.pinterest.com",
        "analytics.tiktok.com",
        "bat.bing.com",
        "ads.linkedin.com",
        "zedo.com",
        "revcontent.com",
        "mgid.com",
        "propellerads.com",
        "popads.net",
        "onesignal.com",
    ];

    private readonly HashSet<string> _domains;

    public DomainBlocklist(IEnumerable<string> domains)
    {
        ArgumentNullException.ThrowIfNull(domains);

        _domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var domain in domains)
        {
            var normalized = Normalize(domain);
            if (normalized is not null)
            {
                _domains.Add(normalized);
            }
        }
    }

    public static DomainBlocklist Empty { get; } = new([]);

    public int Count => _domains.Count;

    /// <summary>
    /// Le o formato hosts: ignora comentarios e linhas vazias, e aceita tanto
    /// "0.0.0.0 dominio.com" quanto "dominio.com" sozinho.
    /// </summary>
    public static DomainBlocklist Parse(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var domains = new List<string>();

        foreach (var raw in lines)
        {
            var line = raw;

            var comment = line.IndexOf('#');
            if (comment >= 0)
            {
                line = line[..comment];
            }

            line = line.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

            // "0.0.0.0 dominio" / "127.0.0.1 dominio" -> o dominio e a segunda coluna.
            var candidate = parts.Length >= 2 ? parts[1] : parts[0];

            // "0.0.0.0 localhost" aparece em todo hosts e nao e para bloquear.
            if (candidate.Equals("localhost", StringComparison.OrdinalIgnoreCase) && parts.Length >= 2)
            {
                continue;
            }

            domains.Add(candidate);
        }

        return new DomainBlocklist(domains);
    }

    /// <summary>
    /// Semente embutida mais, se existir, o arquivo hosts do usuario. Arquivo
    /// ausente nao e erro: o navegador so fica com a semente.
    /// </summary>
    public static DomainBlocklist LoadOrSeed(string? userFilePath)
    {
        var domains = new List<string>(SeedDomains);

        if (userFilePath is { Length: > 0 } path && File.Exists(path))
        {
            try
            {
                domains.AddRange(Parse(File.ReadLines(path))._domains);
            }
            catch (IOException)
            {
                // Lista do usuario ilegivel: segue com a semente.
            }
        }

        return new DomainBlocklist(domains);
    }

    /// <summary>
    /// Verdadeiro quando o host da URL, ou algum dominio acima dele, esta na
    /// lista: "pixel.ads.doubleclick.net" cai por "doubleclick.net".
    /// </summary>
    public bool IsBlocked(string url)
    {
        if (_domains.Count == 0 || !Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        var host = parsed.Host;
        if (host.Length == 0)
        {
            return false;
        }

        if (_domains.Contains(host))
        {
            return true;
        }

        // Sobe rotulo a rotulo: a.b.c.com -> b.c.com -> c.com -> com
        var index = host.IndexOf('.');
        while (index >= 0 && index < host.Length - 1)
        {
            var parent = host[(index + 1)..];
            if (_domains.Contains(parent))
            {
                return true;
            }

            index = host.IndexOf('.', index + 1);
        }

        return false;
    }

    /// <summary>
    /// Decide sobre uma requisicao. O documento principal nunca e bloqueado:
    /// digitar um dominio da lista na barra de endereco tem de abrir a pagina,
    /// nao devolver um branco inexplicavel.
    /// </summary>
    public bool ShouldBlock(string url, bool isTopLevelDocument) =>
        !isTopLevelDocument && IsBlocked(url);

    private static string? Normalize(string domain)
    {
        var trimmed = domain.Trim().TrimEnd('.');

        if (trimmed.Length == 0 || trimmed.Contains(' ', StringComparison.Ordinal))
        {
            return null;
        }

        // Entradas com esquema ou caminho viram so o host.
        if (trimmed.Contains("://", StringComparison.Ordinal) &&
            Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed))
        {
            return parsed.Host.Length > 0 ? parsed.Host : null;
        }

        return trimmed.Contains('/', StringComparison.Ordinal) ? null : trimmed;
    }
}
