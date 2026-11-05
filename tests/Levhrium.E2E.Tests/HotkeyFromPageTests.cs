using Microsoft.Playwright;

namespace Levhrium.E2E.Tests;

/// <summary>
/// Teclado com o foco DENTRO da pagina.
///
/// ATENCAO ao que este arquivo NAO consegue testar: teclas enviadas pelo
/// Playwright entram pelo CDP direto no renderer e nao passam pelo tratamento de
/// acelerador do WebView2, que acontece antes, no processo do browser. Um teste
/// de Ctrl+T por aqui falharia mesmo com o atalho funcionando -- falso negativo.
/// Por isso sobrou so o caso inverso, que o CDP cobre de verdade: digitar texto
/// comum continua chegando na pagina.
///
/// A unica prova de que os atalhos funcionam com o foco na pagina e teclado
/// fisico, com a janela em primeiro plano. Ver ESPECIFICACAO.md, secao Teclado.
/// </summary>
[Collection("app")]
public sealed class HotkeyFromPageTests
{
    [Fact]
    public async Task TeclaComumNaoEInterceptada()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/digitar"));

        await app.WaitForStateAsync(s => s.Tabs[0].Title == "Digitar", "pagina com campo carregada");

        await using var sessao = await PageSession.OpenAsync(app, "/digitar");
        await sessao.Page.Locator("#campo").ClickAsync();
        await sessao.Page.Keyboard.TypeAsync("levhrium");

        // Interceptar demais seria pior que de menos: digitar num site tem de
        // continuar funcionando.
        Assert.Equal("levhrium", await sessao.Page.Locator("#campo").InputValueAsync());
        Assert.Single((await app.GetStateAsync()).Tabs);
    }

    /// <summary>
    /// Conexao Playwright viva pelo teste inteiro. Precisa ser um objeto
    /// descartavel pelo chamador: descartar o Playwright ao sair de um metodo
    /// auxiliar fecha a conexao e a pagina devolvida morre junto.
    /// </summary>
    private sealed class PageSession : IAsyncDisposable
    {
        private readonly IPlaywright _playwright;

        private PageSession(IPlaywright playwright, IPage page)
        {
            _playwright = playwright;
            Page = page;
        }

        public IPage Page { get; }

        /// <summary>
        /// Conecta, acha a pagina da aba pedida e poe o foco no corpo dela, para
        /// a tecla seguinte sair de dentro do conteudo e nao da casca.
        /// </summary>
        public static async Task<PageSession> OpenAsync(AppHarness app, string urlPart)
        {
            var playwright = await Playwright.CreateAsync();

            try
            {
                var browser = await playwright.Chromium.ConnectOverCDPAsync(app.CdpEndpoint);
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);

                while (DateTime.UtcNow < deadline)
                {
                    var page = browser.Contexts
                        .SelectMany(context => context.Pages)
                        .FirstOrDefault(p => p.Url.Contains(urlPart, StringComparison.Ordinal));

                    if (page is not null)
                    {
                        await page.BringToFrontAsync();
                        await page.Locator("body").ClickAsync();
                        return new PageSession(playwright, page);
                    }

                    await Task.Delay(150);
                }
            }
            catch
            {
                playwright.Dispose();
                throw;
            }

            playwright.Dispose();
            throw new TimeoutException($"Nenhuma pagina com '{urlPart}' apareceu no WebView2.");
        }

        public ValueTask DisposeAsync()
        {
            _playwright.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
