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

/// <summary>
/// Data relativa. Testada com relogio fixo: e a conta que so quebra na virada
/// do dia, quando ninguem esta olhando.
/// </summary>
public sealed class RelativeTimeTests
{
    private static readonly DateTimeOffset Agora =
        new(2026, 11, 7, 15, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, "agora")]
    [InlineData(30, "agora")]
    [InlineData(60, "ha 1 min")]
    [InlineData(59 * 60, "ha 59 min")]
    [InlineData(60 * 60, "ha 1 h")]
    [InlineData(23 * 60 * 60, "ha 23 h")]
    public void MinutosEHoras(int segundosAtras, string esperado)
    {
        Assert.Equal(esperado, RelativeTime.Describe(Agora.AddSeconds(-segundosAtras), Agora));
    }

    [Fact]
    public void OntemTemNomeProprio()
    {
        Assert.Equal("ontem", RelativeTime.Describe(Agora.AddHours(-30), Agora));
    }

    [Fact]
    public void DentroDaSemanaContaEmDias()
    {
        Assert.Equal("ha 3 dias", RelativeTime.Describe(Agora.AddDays(-3), Agora));
    }

    [Fact]
    public void MaisVelhoQueUmaSemanaViraData()
    {
        var texto = RelativeTime.Describe(Agora.AddDays(-20), Agora);

        Assert.DoesNotContain("ha ", texto, StringComparison.Ordinal);
        Assert.Contains("out", texto, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RegistroNoFuturoNaoViraNumeroNegativo()
    {
        // Relogio do sistema atrasado acontece; "ha -3 min" nao pode aparecer.
        Assert.Equal("agora", RelativeTime.Describe(Agora.AddMinutes(10), Agora));
    }
}

/// <summary>
/// Sugestoes da barra de endereco. O que aparece aqui enquanto se digita e a
/// diferenca entre acertar o endereco de primeira e digitar tudo de novo.
/// </summary>
public sealed class SuggestTests
{
    private static LibraryEntry Entry(string title, string url) =>
        new() { Title = title, Url = url };

    private static readonly LibraryEntry[] Historico =
    [
        Entry("Pagina Um", "https://exemplo.test/um"),
        Entry("Outra", "https://outro.test/"),
    ];

    private static readonly LibraryEntry[] Favoritos =
    [
        Entry("Repositorio", "https://github.com/willoliveira1/levhrium"),
        Entry("Pagina Um salva", "https://exemplo.test/um"),
    ];

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SemTermoNaoSugereNada(string? query)
    {
        // A lista nao pode abrir sozinha so por clicar na barra.
        Assert.Empty(LibrarySearch.Suggest(Historico, Favoritos, query));
    }

    [Fact]
    public void HistoricoVemAntesDosFavoritos()
    {
        var r = LibrarySearch.Suggest(Historico, Favoritos, "test");

        Assert.Equal("Pagina Um", r[0].Title);
    }

    [Fact]
    public void EnderecoRepetidoApareceUmaVezSo()
    {
        // exemplo.test/um esta no historico e nos favoritos.
        var r = LibrarySearch.Suggest(Historico, Favoritos, "exemplo");

        Assert.Single(r);
    }

    [Fact]
    public void BarraFinalNaoCriaDuplicata()
    {
        var r = LibrarySearch.Suggest(
            [Entry("A", "https://um.test/pagina")],
            [Entry("B", "https://um.test/pagina/")],
            "um.test");

        Assert.Single(r);
    }

    [Fact]
    public void RespeitaOLimite()
    {
        var muitos = Enumerable.Range(0, 30)
            .Select(i => Entry($"Item {i}", $"https://teste.test/{i}"))
            .ToArray();

        Assert.Equal(5, LibrarySearch.Suggest(muitos, [], "teste", limit: 5).Count);
    }

    [Fact]
    public void CasaNoTituloEnquantoSeDigita()
    {
        var r = LibrarySearch.Suggest(Historico, Favoritos, "reposit");

        Assert.Single(r);
        Assert.Equal("Repositorio", r[0].Title);
    }
}
