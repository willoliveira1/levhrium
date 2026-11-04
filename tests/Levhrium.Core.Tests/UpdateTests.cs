using Levhrium.Core;

namespace Levhrium.Core.Tests;

public sealed class AppVersionTests
{
    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("V2.0.0", "2.0.0")]
    [InlineData("v1.2", "1.2.0")]
    [InlineData("v1.2.3-beta", "1.2.3")]
    [InlineData("v1.2.3-rc.1", "1.2.3")]
    public void LeTagDeVersaoDoGitHub(string tag, string esperado)
    {
        Assert.Equal(esperado, AppVersion.ParseTag(tag)!.ToString(3));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("release-final")]
    [InlineData("latest")]
    public void TagSemVersaoNaoViraAtualizacao(string? tag)
    {
        Assert.Null(AppVersion.ParseTag(tag));
    }

    [Theory]
    [InlineData("1.0.1", "1.0.0", true)]
    [InlineData("1.1.0", "1.0.9", true)]
    [InlineData("2.0.0", "1.9.9", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("0.9.0", "1.0.0", false)]
    public void ComparaVersoes(string publicada, string atual, bool esperado)
    {
        Assert.Equal(
            esperado,
            AppVersion.IsNewer(Version.Parse(publicada), Version.Parse(atual)));
    }

    [Fact]
    public void VersaoCurtaNaoParecemaisVelhaQueAEquivalenteLonga()
    {
        // "v1.2" e 1.2.0 sao a mesma versao; sem normalizar, Version as trata
        // como diferentes e o navegador ofereceria uma atualizacao fantasma.
        Assert.False(AppVersion.IsNewer(AppVersion.ParseTag("v1.2")!, Version.Parse("1.2.0")));
        Assert.False(AppVersion.IsNewer(Version.Parse("1.2.0"), AppVersion.ParseTag("v1.2")!));
    }

    [Fact]
    public void VersaoAtualEValida()
    {
        Assert.True(AppVersion.Current.Major >= 0);
        Assert.Matches(@"^\d+\.\d+\.\d+$", AppVersion.CurrentDisplay);
    }
}

public sealed class UpdateCheckerTests
{
    private const string ReleaseJson = """
        {
          "tag_name": "v2.0.0",
          "body": "Novidades da versao 2",
          "assets": [
            { "name": "codigo.zip", "browser_download_url": "https://exemplo/codigo.zip" },
            { "name": "Levhrium-Setup-2.0.0.exe", "browser_download_url": "https://exemplo/setup.exe" }
          ]
        }
        """;

    [Fact]
    public void OfereceAtualizacaoQuandoAPublicadaEMaisNova()
    {
        var result = UpdateChecker.Evaluate(ReleaseJson, Version.Parse("1.0.0"));

        Assert.True(result.HasUpdate);
        Assert.Equal("2.0.0", result.PublishedVersion!.ToString(3));

        // Entre os anexos, escolhe o instalador -- baixar o zip do codigo-fonte
        // nao atualizaria nada.
        Assert.Equal("https://exemplo/setup.exe", result.InstallerUrl);
        Assert.Equal("Novidades da versao 2", result.ReleaseNotes);
    }

    [Fact]
    public void NaoOfereceQuandoJaEstaNaUltima()
    {
        var result = UpdateChecker.Evaluate(ReleaseJson, Version.Parse("2.0.0"));

        Assert.False(result.HasUpdate);
        Assert.Null(result.Error);
        Assert.Equal("2.0.0", result.PublishedVersion!.ToString(3));
    }

    [Fact]
    public void NaoOfereceVersaoMaisVelhaQueAAtual()
    {
        var result = UpdateChecker.Evaluate(ReleaseJson, Version.Parse("3.0.0"));

        Assert.False(result.HasUpdate);
    }

    [Fact]
    public void ReleaseSemInstaladorVirAviso()
    {
        var json = """
            { "tag_name": "v9.0.0", "assets": [
              { "name": "notas.txt", "browser_download_url": "https://exemplo/notas.txt" }
            ] }
            """;

        var result = UpdateChecker.Evaluate(json, Version.Parse("1.0.0"));

        Assert.False(result.HasUpdate);
        Assert.Contains("sem instalador", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReleaseSemTagDeVersaoNaoQuebra()
    {
        var result = UpdateChecker.Evaluate("""{ "tag_name": "latest", "assets": [] }""", Version.Parse("1.0.0"));

        Assert.False(result.HasUpdate);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void RespostaMalFormadaNaoQuebra()
    {
        var result = UpdateChecker.Evaluate("isto nao e json", Version.Parse("1.0.0"));

        Assert.False(result.HasUpdate);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task SemReleaseNenhumaOAvisoEClaro()
    {
        using var http = new HttpClient(new StubHandler(System.Net.HttpStatusCode.NotFound, string.Empty));
        var checker = new UpdateChecker(http, "exemplo/repo");

        var result = await checker.CheckAsync(Version.Parse("1.0.0"));

        Assert.False(result.HasUpdate);
        Assert.Contains("Nenhuma versao publicada", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SemRedeOAvisoEClaroENaoEstoura()
    {
        using var http = new HttpClient(new ThrowingHandler());
        var checker = new UpdateChecker(http, "exemplo/repo");

        // Verificar atualizacao sem internet nao pode derrubar o navegador.
        var result = await checker.CheckAsync(Version.Parse("1.0.0"));

        Assert.False(result.HasUpdate);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task ConsultaOEndpointDeUltimaRelease()
    {
        var handler = new StubHandler(System.Net.HttpStatusCode.OK, ReleaseJson);
        using var http = new HttpClient(handler);
        var checker = new UpdateChecker(http, "dono/repo");

        var result = await checker.CheckAsync(Version.Parse("1.0.0"));

        Assert.True(result.HasUpdate);
        Assert.Equal("https://api.github.com/repos/dono/repo/releases/latest", handler.LastUrl);

        // A API do GitHub recusa requisicao sem User-Agent.
        Assert.NotEmpty(handler.LastUserAgent ?? string.Empty);
    }

    private sealed class StubHandler(System.Net.HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? LastUrl { get; private set; }

        public string? LastUserAgent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastUrl = request.RequestUri?.ToString();
            LastUserAgent = request.Headers.UserAgent.ToString();

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body),
            });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("sem rede");
    }
}
