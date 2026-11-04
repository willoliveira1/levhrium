using Microsoft.Playwright;

namespace Levhrium.E2E.Tests;

/// <summary>
/// O requisito que originou o projeto: duas contas do mesmo site abertas ao mesmo
/// tempo, na mesma janela. Prova que os perfis do WebView2 isolam de verdade os
/// tres armazenamentos que um site usaria para detectar sessao duplicada.
/// </summary>
[Collection("app")]
public sealed class ContainerIsolationTests
{
    [Fact]
    public async Task DuasContasDoMesmoSiteConvivemEmAbasDiferentes()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/conta"));

        await app.WaitForStateAsync(
            s => s.Tabs.Count == 1 && s.Tabs[0].Title.StartsWith("Conta ", StringComparison.Ordinal),
            "primeira conta carregada");

        await app.NewTabAsync("Conta 2", fixtures.Url("/conta"));

        var state = await app.WaitForStateAsync(
            s => s.Tabs.Count == 2 &&
                 s.Tabs.All(t => t.Title.StartsWith("Conta ", StringComparison.Ordinal)),
            "segunda conta carregada");

        var primeira = state.Tabs[0].Title;
        var segunda = state.Tabs[1].Title;

        // Mesma URL, mesma janela, contas diferentes. Se os perfis nao isolassem,
        // a segunda aba herdaria o cookie da primeira e os titulos seriam iguais.
        Assert.NotEqual(primeira, segunda);
        Assert.Equal("padrao", state.Tabs[0].Container);
        Assert.Equal("Conta 2", state.Tabs[1].Container);
    }

    [Fact]
    public async Task CadaContainerTemSeuProprioLocalStorage()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/conta"));

        await app.WaitForStateAsync(
            s => s.Tabs[0].Title.StartsWith("Conta ", StringComparison.Ordinal),
            "primeira conta carregada");

        await app.NewTabAsync("Conta 2", fixtures.Url("/conta"));
        await app.WaitForStateAsync(
            s => s.Tabs.Count == 2 && s.Tabs[1].Title.StartsWith("Conta ", StringComparison.Ordinal),
            "segunda conta carregada");

        using var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.ConnectOverCDPAsync(app.CdpEndpoint);

        var identificadores = new List<string>();
        foreach (var page in await ResolvePagesAsync(browser, 2))
        {
            identificadores.Add(
                await page.EvaluateAsync<string>("() => localStorage.getItem('levhrium-conta')"));
        }

        Assert.Equal(2, identificadores.Count);
        Assert.Equal(2, identificadores.Distinct().Count());
        Assert.DoesNotContain(identificadores, string.IsNullOrEmpty);
    }

    [Fact]
    public async Task ContainersRecebemCoresDiferentes()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/um"));

        await app.WaitForStateAsync(s => s.Tabs.Count == 1, "primeira aba");
        await app.NewTabAsync("Conta 2", fixtures.Url("/um"));
        await app.NewTabAsync("Conta 3", fixtures.Url("/um"));

        var state = await app.WaitForStateAsync(s => s.Tabs.Count == 3, "tres abas");

        // O selo de cor e o unico jeito de saber, de relance, qual aba e qual conta.
        var cores = state.Tabs.Select(t => t.ContainerColor).ToList();
        Assert.Equal(3, cores.Distinct().Count());
    }

    [Fact]
    public async Task AbaNoMesmoContainerCompartilhaASessao()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/conta"));

        var inicial = await app.WaitForStateAsync(
            s => s.Tabs[0].Title.StartsWith("Conta ", StringComparison.Ordinal),
            "primeira conta carregada");

        // Mesmo container: tem de ser a mesma conta, senao abrir uma aba nova
        // dentro do container deslogaria voce.
        await app.NewTabAsync("padrao", fixtures.Url("/conta"));

        var state = await app.WaitForStateAsync(
            s => s.Tabs.Count == 2 && s.Tabs[1].Title.StartsWith("Conta ", StringComparison.Ordinal),
            "segunda aba no mesmo container");

        Assert.Equal(inicial.Tabs[0].Title, state.Tabs[1].Title);
    }

    private static async Task<IReadOnlyList<IPage>> ResolvePagesAsync(IBrowser browser, int expected)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);

        while (DateTime.UtcNow < deadline)
        {
            var pages = browser.Contexts
                .SelectMany(context => context.Pages)
                .Where(page => page.Url.Contains("/conta", StringComparison.Ordinal))
                .ToList();

            if (pages.Count >= expected)
            {
                return pages;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException(
            $"O Playwright nao encontrou {expected} paginas de conta no WebView2.");
    }
}
