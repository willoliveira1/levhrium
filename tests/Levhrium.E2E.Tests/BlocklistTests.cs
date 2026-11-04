namespace Levhrium.E2E.Tests;

/// <summary>
/// Bloqueio por dominio com o Chromium no meio. A prova nao e o contador do
/// proprio navegador: e o servidor da fixture nunca ter recebido a requisicao.
/// </summary>
[Collection("app")]
public sealed class BlocklistTests
{
    [Fact]
    public async Task RequisicaoBloqueadaNuncaChegaAoServidor()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        using var lista = new TemporaryBlocklist("localhost");

        // A pagina vem de 127.0.0.1 e pede um recurso de "localhost": mesmo
        // servidor, host diferente, e so o de "localhost" esta na lista.
        await using var app = await AppHarness.StartAsync(
            fixtures.Url("/pagina-com-anuncio"),
            blocklistPath: lista.Path);

        await app.WaitForStateAsync(
            s => s.Tabs[0].Title == "Pagina Com Anuncio",
            "pagina carregada");

        await app.WaitForStateAsync(
            s => s.Tabs[0].BlockedRequests > 0,
            "pelo menos uma requisicao bloqueada");

        // Deu tempo de sobra para a requisicao bloqueada chegar, se fosse chegar.
        await Task.Delay(1500);

        Assert.Equal(1, fixtures.HitsFor("/recurso-permitido.png"));
        Assert.Equal(0, fixtures.HitsFor("/recurso-anuncio.png"));
    }

    [Fact]
    public async Task ContadorDeBloqueiosFicaVisivelNaAba()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        using var lista = new TemporaryBlocklist("localhost");

        await using var app = await AppHarness.StartAsync(
            fixtures.Url("/pagina-com-anuncio"),
            blocklistPath: lista.Path);

        var state = await app.WaitForStateAsync(
            s => s.Tabs[0].BlockedRequests > 0,
            "contador de bloqueios subiu");

        Assert.True(state.BlocklistSize > 0);
        Assert.True(state.Tabs[0].BlockedRequests >= 1);
    }

    [Fact]
    public async Task PaginaSemAnuncioNaoBloqueiaNada()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        using var lista = new TemporaryBlocklist("localhost");

        await using var app = await AppHarness.StartAsync(
            fixtures.Url("/um"),
            blocklistPath: lista.Path);

        await app.WaitForStateAsync(s => s.Tabs[0].Title == "Pagina Um", "pagina carregada");
        await Task.Delay(1000);

        // Falso positivo aqui seria pior que nao bloquear: quebraria sites.
        var state = await app.GetStateAsync();
        Assert.Equal(0, state.Tabs[0].BlockedRequests);
    }

    [Fact]
    public async Task SementeEmbutidaJaVemCarregada()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/um"));

        var state = await app.WaitForStateAsync(s => s.Tabs.Count == 1, "aba aberta");

        // Sem configurar nada, o navegador ja sobe com a lista embutida.
        Assert.True(state.BlocklistSize >= 50, $"lista veio com {state.BlocklistSize} dominios");
    }

    /// <summary>
    /// Arquivo hosts temporario, apagado ao fim do teste. Usa a forma de coluna
    /// unica porque "0.0.0.0 localhost" e deliberadamente ignorado pelo parser:
    /// todo arquivo hosts real comeca com essa linha.
    /// </summary>
    private sealed class TemporaryBlocklist : IDisposable
    {
        public TemporaryBlocklist(params string[] lines)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"levhrium-test-blocklist-{Guid.NewGuid():n}.txt");

            File.WriteAllLines(Path, lines);
        }

        public string Path { get; }

        public void Dispose() => File.Delete(Path);
    }
}
