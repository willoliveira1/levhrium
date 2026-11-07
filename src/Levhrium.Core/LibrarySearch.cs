namespace Levhrium.Core;

/// <summary>
/// Um item da biblioteca pronto para a tela: titulo, endereco, o site de onde
/// veio e a letra do selo.
///
/// O site aparece embaixo do titulo porque dois favoritos podem ter o mesmo
/// nome -- "Inicio", "Login" -- e sem o dominio nao da para saber qual e qual.
/// </summary>
public sealed record LibraryEntry
{
    public required string Title { get; init; }

    public required string Url { get; init; }

    /// <summary>
    /// Quando o item foi visitado. Nulo em favoritos, que nao guardam hora --
    /// so o historico tem essa informacao, e e justamente ela que diz se a
    /// pagina e de agora ou da semana passada.
    /// </summary>
    public DateTimeOffset? VisitedAt { get; init; }

    /// <summary>Host sem "www.", ou o proprio endereco quando nao der para ler.</summary>
    public string Host => HostOf(Url);

    /// <summary>
    /// Uma letra so, do rotulo principal do dominio. Duas letras exigiriam
    /// saber onde termina o dominio de verdade ("com.br" tem dois rotulos), e
    /// errar isso daria selos como "CO" em metade dos sites.
    /// </summary>
    public string Badge
    {
        get
        {
            var host = Host;
            var labels = host.Split('.', StringSplitOptions.RemoveEmptyEntries);

            // O rotulo principal e o penultimo em "github.com", e o unico em
            // "localhost".
            var main = labels.Length >= 2 ? labels[^2] : labels.FirstOrDefault() ?? host;

            return main.Length > 0
                ? char.ToUpperInvariant(main[0]).ToString()
                : "?";
        }
    }

    public static string HostOf(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Host.Length == 0)
        {
            return url;
        }

        return parsed.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? parsed.Host[4..]
            : parsed.Host;
    }
}

/// <summary>
/// Filtro da busca da biblioteca. Fica no nucleo porque e a parte que da para
/// errar em silencio -- e a mesma conta vai servir as sugestoes da barra de
/// endereco, que ainda estao na lista de pendencias.
/// </summary>
public static class LibrarySearch
{
    /// <summary>
    /// Casa o termo no titulo ou no endereco, ignorando caixa. Busca vazia
    /// devolve tudo, na ordem em que veio: a lista ja chega ordenada por
    /// relevancia (historico pelo mais recente, favoritos pela ordem de quem
    /// salvou).
    /// </summary>
    public static IReadOnlyList<LibraryEntry> Filter(
        IEnumerable<LibraryEntry> entries,
        string? query,
        int limit = 50)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var term = query?.Trim();
        var filtered = term is not { Length: > 0 }
            ? entries
            : entries.Where(entry => Matches(entry, term));

        return [.. filtered.Take(Math.Max(0, limit))];
    }

    /// <summary>
    /// Sugestoes para a barra de endereco: historico e favoritos no mesmo
    /// bolo, sem repetir endereco.
    ///
    /// O historico vem primeiro porque o que voce acabou de visitar e quase
    /// sempre para onde quer voltar. Um endereco que esta nos dois aparece uma
    /// vez so, marcado como favorito -- ver duas linhas iguais faria duvidar
    /// se sao a mesma pagina.
    ///
    /// Busca vazia devolve nada: a lista so aparece quando se digita algo, se
    /// nao ela abriria sozinha ao clicar na barra.
    /// </summary>
    public static IReadOnlyList<LibraryEntry> Suggest(
        IEnumerable<LibraryEntry> history,
        IEnumerable<LibraryEntry> bookmarks,
        string? query,
        int limit = 8)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(bookmarks);

        if (query?.Trim() is not { Length: > 0 } term)
        {
            return [];
        }

        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resultado = new List<LibraryEntry>();

        foreach (var entry in history.Concat(bookmarks))
        {
            if (resultado.Count >= limit)
            {
                break;
            }

            if (!Matches(entry, term) || !vistos.Add(Key(entry.Url)))
            {
                continue;
            }

            resultado.Add(entry);
        }

        return resultado;
    }

    /// <summary>Barra final nao faz diferenca: /um e /um/ sao a mesma pagina.</summary>
    private static string Key(string url) => url.TrimEnd('/');

    private static bool Matches(LibraryEntry entry, string term) =>
        entry.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
        entry.Url.Contains(term, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// "ha 5 min", "ontem", "12 de nov". Fica no nucleo para ser testavel com um
/// relogio fixo: data relativa e o tipo de conta que so quebra na virada do
/// dia, quando ninguem esta olhando.
/// </summary>
public static class RelativeTime
{
    private static readonly System.Globalization.CultureInfo Brasil =
        System.Globalization.CultureInfo.GetCultureInfo("pt-BR");

    public static string Describe(DateTimeOffset when, DateTimeOffset now)
    {
        var delta = now - when;

        // Relogio do sistema atrasado, ou registro gravado adiante: tratar
        // como agora e menos errado do que dizer "ha -3 min".
        if (delta < TimeSpan.Zero || delta.TotalMinutes < 1)
        {
            return "agora";
        }

        if (delta.TotalMinutes < 60)
        {
            return $"ha {(int)delta.TotalMinutes} min";
        }

        if (delta.TotalHours < 24)
        {
            return $"ha {(int)delta.TotalHours} h";
        }

        if (delta.TotalDays < 2)
        {
            return "ontem";
        }

        if (delta.TotalDays < 7)
        {
            return $"ha {(int)delta.TotalDays} dias";
        }

        return when.ToLocalTime().ToString("d MMM", Brasil);
    }
}
