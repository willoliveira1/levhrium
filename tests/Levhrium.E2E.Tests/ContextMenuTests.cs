using Microsoft.Playwright;

namespace Levhrium.E2E.Tests;

/// <summary>
/// "Abrir link em container" no menu de contexto -- o caminho natural para pôr a
/// segunda conta numa aba, e que estava no codigo sem nunca ter rodado.
///
/// O menu e nativo do WebView2 e nao da para inspecionar de fora, entao o teste
/// afirma que o nosso handler rodou e montou o submenu. O conteudo do submenu em
/// si e coberto pela mesma lista de containers que os outros testes exercitam.
/// </summary>
[Collection("app")]
public sealed class ContextMenuTests
{
    [Fact]
    public async Task CliqueDireitoEmLinkRecebeOSubmenuDeContainer()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/com-link"));

        await app.WaitForStateAsync(s => s.Tabs[0].Title == "Com Link", "pagina com link carregada");
        Assert.Equal(0, (await app.GetStateAsync()).LinkMenusCustomized);

        using var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.ConnectOverCDPAsync(app.CdpEndpoint);
        var page = await ResolvePageAsync(browser, "/com-link");

        await page.Locator("#link").ClickAsync(new LocatorClickOptions { Button = MouseButton.Right });

        var state = await app.WaitForStateAsync(
            s => s.LinkMenusCustomized > 0,
            "submenu de container montado para o link");

        Assert.True(state.LinkMenusCustomized >= 1);

        // Fecha o menu nativo para nao deixar a janela presa.
        await page.Keyboard.PressAsync("Escape");
    }

    [Fact]
    public async Task CliqueDireitoForaDeLinkNaoMontaSubmenu()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/com-link"));

        await app.WaitForStateAsync(s => s.Tabs[0].Title == "Com Link", "pagina carregada");

        using var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.ConnectOverCDPAsync(app.CdpEndpoint);
        var page = await ResolvePageAsync(browser, "/com-link");

        await page.Locator("#titulo").ClickAsync(new LocatorClickOptions { Button = MouseButton.Right });
        await Task.Delay(1500);

        // Sem link no alvo nao ha o que abrir em outro container.
        Assert.Equal(0, (await app.GetStateAsync()).LinkMenusCustomized);

        await page.Keyboard.PressAsync("Escape");
    }

    private static async Task<IPage> ResolvePageAsync(IBrowser browser, string urlPart)
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
}
