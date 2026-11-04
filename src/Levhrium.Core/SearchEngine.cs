namespace Levhrium.Core;

/// <summary>
/// Motor de busca usado quando a barra de endereco recebe um termo em vez de uma URL.
/// Trocar o padrao e uma linha; nada no resto do app assume qual e.
/// </summary>
public sealed class SearchEngine
{
    /// <summary>Padrao da v1. Alterar aqui muda o navegador inteiro.</summary>
    public static readonly SearchEngine Default =
        new("Google", "https://www.google.com/search?q={0}");

    private readonly string _urlTemplate;

    public SearchEngine(string name, string urlTemplate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(urlTemplate);

        if (!urlTemplate.Contains("{0}", StringComparison.Ordinal))
        {
            throw new ArgumentException("O template precisa conter {0} para o termo.", nameof(urlTemplate));
        }

        Name = name;
        _urlTemplate = urlTemplate;
    }

    public string Name { get; }

    public Uri BuildQueryUrl(string term) =>
        new(string.Format(null, _urlTemplate, Uri.EscapeDataString(term)));
}
