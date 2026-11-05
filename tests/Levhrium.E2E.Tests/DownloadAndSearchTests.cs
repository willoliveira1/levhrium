namespace Levhrium.E2E.Tests;

/// <summary>
/// Duas funcionalidades que existiam no codigo e nunca tinham sido exercidas:
/// baixar um arquivo e buscar um termo pela barra de endereco.
/// </summary>
[Collection("app")]
public sealed class DownloadTests
{
    [Fact]
    public async Task BaixarArquivoGravaEmDiscoEEntraNaBiblioteca()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/um"));

        await app.WaitForStateAsync(s => s.Tabs[0].Title == "Pagina Um", "pagina carregada");

        await app.NavigateAsync(fixtures.Url("/baixar.txt"));

        var state = await app.WaitForStateAsync(
            s => s.Library.DownloadCount == 1,
            "download registrado na biblioteca");

        Assert.Equal(1, state.Library.DownloadCount);

        // Em modo de teste os downloads vao para a pasta descartavel, entao da
        // para afirmar o arquivo em disco sem sujar a pasta do usuario.
        var pasta = Path.Combine(app.Handshake.RootFolder, "Downloads");
        var arquivo = await WaitForFileAsync(pasta, "levhrium-teste*.txt");

        Assert.Contains("Levhrium", await File.ReadAllTextAsync(arquivo), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DownloadNaoAconteceSemPedir()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/um"));

        await app.WaitForStateAsync(s => s.Tabs[0].Title == "Pagina Um", "pagina carregada");
        await Task.Delay(1500);

        // Falso positivo aqui faria a biblioteca encher de lixo sozinha.
        Assert.Equal(0, (await app.GetStateAsync()).Library.DownloadCount);
    }

    private static async Task<string> WaitForFileAsync(string folder, string pattern)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);

        while (DateTime.UtcNow < deadline)
        {
            if (Directory.Exists(folder))
            {
                // Ignora o .crdownload temporario enquanto o arquivo nao fechou.
                var found = Directory.GetFiles(folder, pattern)
                    .FirstOrDefault(f => !f.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase));

                if (found is not null)
                {
                    return found;
                }
            }

            await Task.Delay(200);
        }

        var existentes = Directory.Exists(folder)
            ? string.Join(", ", Directory.GetFiles(folder).Select(Path.GetFileName))
            : "(pasta nao existe)";

        throw new TimeoutException($"Nenhum arquivo '{pattern}' em {folder}. Tem: {existentes}");
    }
}

[Collection("app")]
public sealed class AddressBarSearchTests
{
    [Fact]
    public async Task TermoNaBarraDeEnderecoViraBusca()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/um"));

        await app.WaitForStateAsync(s => s.Tabs[0].Title == "Pagina Um", "pagina carregada");

        await app.NavigateAsync("como pegar shiny no jogo");

        var state = await app.WaitForStateAsync(
            s => s.Tabs[0].Url.Contains("google.com/search", StringComparison.OrdinalIgnoreCase),
            "termo virou busca no motor configurado");

        Assert.Contains("shiny", state.Tabs[0].Url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DominioDigitadoNaoViraBusca()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/um"));

        await app.WaitForStateAsync(s => s.Tabs[0].Title == "Pagina Um", "pagina carregada");

        // 127.0.0.1:porta tem de ser tratado como endereco, nao como termo.
        await app.NavigateAsync(fixtures.Url("/dois").Replace("http://", string.Empty, StringComparison.Ordinal));

        var state = await app.WaitForStateAsync(
            s => s.Tabs[0].Title == "Pagina Dois",
            "endereco local sem esquema navegou direto, em http");

        Assert.DoesNotContain("google.com", state.Tabs[0].Url, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("http://", state.Tabs[0].Url, StringComparison.Ordinal);
    }
}
