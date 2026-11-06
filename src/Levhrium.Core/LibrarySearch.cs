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

    private static bool Matches(LibraryEntry entry, string term) =>
        entry.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
        entry.Url.Contains(term, StringComparison.OrdinalIgnoreCase);
}
