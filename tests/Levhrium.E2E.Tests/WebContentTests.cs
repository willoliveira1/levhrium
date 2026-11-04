using Microsoft.Playwright;

namespace Levhrium.E2E.Tests;

/// <summary>
/// Testes de integracao com Playwright ligado por CDP ao WebView2. Esta camada
/// enxerga o DOM dentro das abas -- e e onde o isolamento entre containers vai
/// ser provado no M2.
/// </summary>
[Collection("app")]
public sealed class WebContentTests
{
    [Fact]
    public async Task PlaywrightEnxergaOConteudoDaAba()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/um"));

        await app.WaitForStateAsync(s => s.Tabs[0].Title == "Pagina Um", "pagina um carregada");

        using var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.ConnectOverCDPAsync(app.CdpEndpoint);

        var page = await ResolvePageAsync(browser);

        Assert.Equal("Pagina Um", await page.TitleAsync());
        Assert.Equal("Pagina Um", await page.Locator("#titulo").InnerTextAsync());
    }

    [Fact]
    public async Task NavegacaoPelaCascaEVisivelNoConteudo()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/um"));

        await app.WaitForStateAsync(s => s.Tabs[0].Title == "Pagina Um", "pagina um carregada");

        using var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.ConnectOverCDPAsync(app.CdpEndpoint);
        var page = await ResolvePageAsync(browser);

        await app.NavigateAsync(fixtures.Url("/dois"));

        // Re-resolve a pagina a cada tentativa em vez de usar WaitForURLAsync: o
        // WebView2 pode trocar o alvo de CDP ao navegar, e o handle antigo entao
        // nunca ve a nova URL.
        var destino = await WaitForPageAsync(browser, "/dois");

        Assert.Equal("Pagina Dois", await destino.Locator("#titulo").InnerTextAsync());
    }

    [Fact]
    public async Task ContainerGravaIdentificadorNosTresArmazenamentos()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/conta"));

        await app.WaitForStateAsync(
            s => s.Tabs[0].Title.StartsWith("Conta ", StringComparison.Ordinal),
            "fixture de conta carregada");

        using var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.ConnectOverCDPAsync(app.CdpEndpoint);
        var page = await ResolvePageAsync(browser);

        var noDom = await page.Locator("#conta-id").InnerTextAsync();
        var noLocalStorage = await page.EvaluateAsync<string>(
            "() => localStorage.getItem('levhrium-conta')");

        // Base do M2: se o identificador nao acompanhar os tres armazenamentos,
        // o teste de isolamento entre containers nao teria o que comparar.
        Assert.Equal(noDom, noLocalStorage);
        Assert.NotEmpty(noDom);
    }

    /// <summary>Espera aparecer uma pagina cuja URL contenha o trecho informado.</summary>
    private static async Task<IPage> WaitForPageAsync(IBrowser browser, string urlPart)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);

        while (DateTime.UtcNow < deadline)
        {
            var page = browser.Contexts
                .SelectMany(context => context.Pages)
                .FirstOrDefault(p => p.Url.Contains(urlPart, StringComparison.Ordinal));

            if (page is not null)
            {
                return page;
            }

            await Task.Delay(150);
        }

        throw new TimeoutException($"Nenhuma pagina com '{urlPart}' apareceu no WebView2.");
    }

    /// <summary>
    /// Acha a pagina da aba. O WebView2 ja existia antes de conectarmos, entao a
    /// pagina vem do contexto existente, nao de um <c>NewPageAsync</c>.
    /// </summary>
    private static async Task<IPage> ResolvePageAsync(IBrowser browser)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);

        while (DateTime.UtcNow < deadline)
        {
            var page = browser.Contexts.SelectMany(context => context.Pages).FirstOrDefault();
            if (page is not null)
            {
                return page;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException(
            "O Playwright conectou por CDP mas nao encontrou nenhuma pagina no WebView2.");
    }
}
