namespace Levhrium.Core;

/// <summary>
/// Decide se o que o usuario digitou na barra de endereco e uma URL para navegar
/// ou um termo para buscar. Toda a logica fica aqui (sem UI) para ser testavel.
/// </summary>
public static class UrlResolver
{
    /// <summary>Esquemas que aceitamos navegar diretamente.</summary>
    private static readonly string[] AllowedSchemes = ["http", "https", "file", "about"];

    /// <summary>
    /// Resolve a entrada do usuario em uma URI navegavel.
    /// Nunca devolve <c>javascript:</c> nem <c>data:</c>: digitar isso na barra de
    /// endereco e um vetor classico de ataque, entao tratamos como busca.
    /// </summary>
    public static Uri Resolve(string? input, SearchEngine searchEngine)
    {
        ArgumentNullException.ThrowIfNull(searchEngine);

        var text = input?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return new Uri("about:blank");
        }

        if (TryParseExplicitScheme(text, out var explicitUri))
        {
            return explicitUri;
        }

        if (LooksLikeHost(text))
        {
            return new Uri(DefaultSchemeFor(text) + text);
        }

        return searchEngine.BuildQueryUrl(text);
    }

    /// <summary>Verdadeiro quando o texto e uma URL com esquema permitido.</summary>
    private static bool TryParseExplicitScheme(string text, out Uri uri)
    {
        uri = null!;

        var separator = text.IndexOf(':');
        if (separator <= 0)
        {
            return false;
        }

        var scheme = text[..separator];
        if (!AllowedSchemes.Contains(scheme, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        // "localhost:3000" tem dois-pontos mas nao e esquema; cai no teste de host.
        return Uri.TryCreate(text, UriKind.Absolute, out uri!);
    }

    /// <summary>
    /// Heuristica de host: localhost (com ou sem porta), IPv4, ou algo com ponto
    /// e sem espaco. Qualquer coisa com espaco e busca, sempre.
    /// </summary>
    private static bool LooksLikeHost(string text)
    {
        if (text.AsSpan().ContainsAny(' ', '\t'))
        {
            return false;
        }

        var authority = text.Split('/', 2)[0];
        var hostPart = StripPort(authority);

        if (hostPart.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (System.Net.IPAddress.TryParse(hostPart, out _))
        {
            return true;
        }

        // Precisa de um ponto com rotulos nao vazios dos dois lados: "a.b" sim, "a." nao.
        var lastDot = hostPart.LastIndexOf('.');
        if (lastDot <= 0 || lastDot == hostPart.Length - 1)
        {
            return false;
        }

        return hostPart.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_');
    }

    /// <summary>
    /// https por padrao, mas http para endereco local: servidor de
    /// desenvolvimento em localhost ou rede interna quase nunca tem TLS, e
    /// insistir em https so devolveria erro de conexao.
    /// </summary>
    private static string DefaultSchemeFor(string text)
    {
        var host = StripPort(text.Split('/', 2)[0]);

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return "http://";
        }

        if (!System.Net.IPAddress.TryParse(host, out var address))
        {
            return "https://";
        }

        if (System.Net.IPAddress.IsLoopback(address))
        {
            return "http://";
        }

        var bytes = address.GetAddressBytes();

        // Faixas privadas do RFC 1918.
        var isPrivate = bytes.Length == 4 && (
            bytes[0] == 10 ||
            (bytes[0] == 192 && bytes[1] == 168) ||
            (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31));

        return isPrivate ? "http://" : "https://";
    }

    /// <summary>Remove ":porta" do fim, preservando IPv6 entre colchetes.</summary>
    private static string StripPort(string authority)
    {
        if (authority.StartsWith('['))
        {
            return authority;
        }

        var colon = authority.LastIndexOf(':');
        if (colon < 0)
        {
            return authority;
        }

        var portText = authority[(colon + 1)..];
        return portText.Length > 0 && portText.All(char.IsAsciiDigit)
            ? authority[..colon]
            : authority;
    }
}
