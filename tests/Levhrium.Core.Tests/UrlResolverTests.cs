using Levhrium.Core;

namespace Levhrium.Core.Tests;

/// <summary>
/// A barra de endereco decide entre navegar e buscar. Como e a porta de entrada de
/// tudo que o usuario digita, inclui os casos de seguranca (javascript:, data:).
/// </summary>
public sealed class UrlResolverTests
{
    private static readonly SearchEngine Engine =
        new("Teste", "https://busca.exemplo/?q={0}");

    [Theory]
    [InlineData("https://poke.sigmaproject.online/play", "https://poke.sigmaproject.online/play")]
    [InlineData("http://exemplo.com", "http://exemplo.com/")]
    [InlineData("about:blank", "about:blank")]
    public void MantemUrlComEsquemaExplicito(string input, string expected)
    {
        Assert.Equal(expected, UrlResolver.Resolve(input, Engine).ToString());
    }

    [Theory]
    [InlineData("poke.sigmaproject.online", "https://poke.sigmaproject.online/")]
    [InlineData("exemplo.com/caminho?a=1", "https://exemplo.com/caminho?a=1")]
    [InlineData("localhost", "https://localhost/")]
    [InlineData("localhost:5000", "https://localhost:5000/")]
    [InlineData("127.0.0.1:8080", "https://127.0.0.1:8080/")]
    public void CompletaEsquemaQuandoParecerHost(string input, string expected)
    {
        Assert.Equal(expected, UrlResolver.Resolve(input, Engine).ToString());
    }

    [Theory]
    [InlineData("pokeidle como pegar shiny")]
    [InlineData("exemplo com espaco.com")]
    [InlineData("gato")]
    [InlineData("1+1")]
    public void BuscaQuandoNaoParecerUrl(string input)
    {
        var resolved = UrlResolver.Resolve(input, Engine);

        Assert.Equal("busca.exemplo", resolved.Host);
        Assert.Contains("q=", resolved.Query, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("javascript:alert(document.cookie)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("vbscript:msgbox(1)")]
    public void NuncaNavegaParaEsquemaPerigoso(string input)
    {
        // Digitar javascript: na barra de endereco e vetor classico de ataque:
        // o esperado e cair na busca, nunca executar.
        var resolved = UrlResolver.Resolve(input, Engine);

        Assert.Equal("busca.exemplo", resolved.Host);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EntradaVaziaVaiParaPaginaEmBranco(string? input)
    {
        Assert.Equal("about:blank", UrlResolver.Resolve(input, Engine).ToString());
    }

    [Fact]
    public void TermoDeBuscaEEscapado()
    {
        var resolved = UrlResolver.Resolve("a&b=c d", Engine);

        // AbsoluteUri, nao ToString(): ToString() desescapa para exibicao, do mesmo
        // jeito que a barra de endereco de qualquer navegador faz.
        Assert.Contains("q=a%26b%3Dc%20d", resolved.AbsoluteUri, StringComparison.Ordinal);
        Assert.DoesNotContain(' ', resolved.AbsoluteUri);
    }

    [Fact]
    public void HostIncompletoNaoViraNavegacao()
    {
        // "exemplo." tem ponto mas nao tem rotulo depois dele.
        var resolved = UrlResolver.Resolve("exemplo.", Engine);

        Assert.Equal("busca.exemplo", resolved.Host);
    }
}
