using Levhrium.Core;

namespace Levhrium.Core.Tests;

/// <summary>
/// Busca da biblioteca. A mesma conta vai alimentar as sugestoes da barra de
/// endereco, entao errar aqui erra em dois lugares.
/// </summary>
public sealed class LibrarySearchTests
{
    private static LibraryEntry Entry(string title, string url) =>
        new() { Title = title, Url = url };

    private static readonly LibraryEntry[] Acervo =
    [
        Entry("Pagina Um", "https://exemplo.test/um"),
        Entry("Repositorio", "https://github.com/willoliveira1/levhrium"),
        Entry("Pokemon", "https://pt.wikipedia.org/wiki/Pokemon"),
    ];

    [Fact]
    public void BuscaVaziaDevolveTudoNaOrdem()
    {
        var resultado = LibrarySearch.Filter(Acervo, null);

        Assert.Equal(3, resultado.Count);
        Assert.Equal("Pagina Um", resultado[0].Title);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BuscaEmBrancoNaoFiltra(string query)
    {
        Assert.Equal(3, LibrarySearch.Filter(Acervo, query).Count);
    }

    [Fact]
    public void CasaNoTitulo()
    {
        var resultado = LibrarySearch.Filter(Acervo, "pagina");

        Assert.Single(resultado);
        Assert.Equal("Pagina Um", resultado[0].Title);
    }

    [Fact]
    public void CasaNoEndereco()
    {
        // Procurar pelo site e tao natural quanto procurar pelo nome.
        var resultado = LibrarySearch.Filter(Acervo, "github");

        Assert.Single(resultado);
        Assert.Equal("Repositorio", resultado[0].Title);
    }

    [Fact]
    public void IgnoraCaixa()
    {
        Assert.Single(LibrarySearch.Filter(Acervo, "POKEMON"));
    }

    [Fact]
    public void TermoSemResultadoDevolveVazio()
    {
        Assert.Empty(LibrarySearch.Filter(Acervo, "nao-existe-isso"));
    }

    [Fact]
    public void LimiteCorta()
    {
        Assert.Equal(2, LibrarySearch.Filter(Acervo, null, limit: 2).Count);
    }

    [Theory]
    [InlineData("https://github.com/x", "github.com")]
    [InlineData("https://www.google.com/", "google.com")]
    [InlineData("https://pt.wikipedia.org/wiki/x", "pt.wikipedia.org")]
    [InlineData("http://localhost:3000/", "localhost")]
    public void HostSaiSemWww(string url, string esperado)
    {
        Assert.Equal(esperado, Entry("t", url).Host);
    }

    [Theory]
    [InlineData("https://github.com/x", "G")]
    [InlineData("https://pt.wikipedia.org/x", "W")]
    [InlineData("http://localhost:3000/", "L")]
    public void SeloEALetraDoRotuloPrincipal(string url, string esperado)
    {
        // "pt.wikipedia.org" vira W, nao P: o rotulo principal e o penultimo.
        Assert.Equal(esperado, Entry("t", url).Badge);
    }

    [Fact]
    public void EnderecoIlegivelNaoDerruba()
    {
        var entry = Entry("estranho", "nao e url");

        Assert.Equal("nao e url", entry.Host);
        Assert.NotEmpty(entry.Badge);
    }
}
