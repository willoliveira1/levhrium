using Levhrium.Core;

namespace Levhrium.E2E.Tests;

/// <summary>
/// Testes da casca via named pipe. Cobrem o critério de pronto do M1: a janela
/// abre, navega, volta e avanca. O Playwright nao alcanca esta camada.
/// </summary>
[Collection("app")]
public sealed class ShellNavigationTests
{
    [Fact]
    public async Task AbreComUmaAbaNoContainerPadrao()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/um"));

        var state = await app.WaitForStateAsync(
            s => s.Tabs.Count == 1 && s.Tabs[0].Url.Contains("/um", StringComparison.Ordinal),
            "primeira aba carregada");

        Assert.Equal(0, state.ActiveTabIndex);
        Assert.Equal("padrao", state.Tabs[0].Container);
        Assert.False(state.Tabs[0].CanGoBack);
        Assert.False(state.Tabs[0].CanGoForward);
    }

    [Fact]
    public async Task TituloDaAbaAcompanhaOTituloDaPagina()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/um"));

        var state = await app.WaitForStateAsync(
            s => s.Tabs.Count == 1 && s.Tabs[0].Title == "Pagina Um",
            "titulo da pagina refletido na aba");

        Assert.Equal("Pagina Um", state.Tabs[0].Title);
    }

    [Fact]
    public async Task NavegaPeloHookEAtualizaOEstado()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/um"));

        await app.WaitForStateAsync(s => s.Tabs[0].Title == "Pagina Um", "pagina um carregada");

        await app.NavigateAsync(fixtures.Url("/dois"));

        var state = await app.WaitForStateAsync(
            s => s.Tabs[0].Title == "Pagina Dois",
            "pagina dois carregada");

        Assert.Contains("/dois", state.Tabs[0].Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VoltarEAvancarPercorremOHistorico()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/um"));

        await app.WaitForStateAsync(s => s.Tabs[0].Title == "Pagina Um", "pagina um carregada");
        await app.NavigateAsync(fixtures.Url("/dois"));
        await app.WaitForStateAsync(
            s => s.Tabs[0].Title == "Pagina Dois" && s.Tabs[0].CanGoBack,
            "pagina dois carregada com historico");

        await app.GoBackAsync();
        var voltou = await app.WaitForStateAsync(
            s => s.Tabs[0].Title == "Pagina Um",
            "voltou para pagina um");

        Assert.True(voltou.Tabs[0].CanGoForward);

        await app.GoForwardAsync();
        await app.WaitForStateAsync(
            s => s.Tabs[0].Title == "Pagina Dois",
            "avancou para pagina dois");
    }

    [Fact]
    public async Task RecarregarMantemAUrl()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/um"));

        var antes = await app.WaitForStateAsync(
            s => s.Tabs[0].Title == "Pagina Um",
            "pagina um carregada");

        await app.ReloadAsync();

        var depois = await app.WaitForStateAsync(
            s => s.Tabs[0].Title == "Pagina Um" && !s.Tabs[0].IsLoading,
            "pagina um recarregada");

        Assert.Equal(antes.Tabs[0].Url, depois.Tabs[0].Url);
    }

    [Fact]
    public async Task BarraDeEnderecoMostraAUrlDesdeAAbertura()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/um"));

        // Regressao: a barra nasce com foco e por isso ficava em branco depois de
        // navegar -- defeito visto no primeiro screenshot do M1.
        var state = await app.WaitForStateAsync(
            s => s.AddressBarText.Contains("/um", StringComparison.Ordinal),
            "barra de endereco preenchida na abertura");

        Assert.Equal(state.Tabs[0].Url, state.AddressBarText);
    }

    [Fact]
    public async Task BarraDeEnderecoAcompanhaNavegacao()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/um"));

        await app.WaitForStateAsync(
            s => s.AddressBarText.Contains("/um", StringComparison.Ordinal),
            "barra com pagina um");

        await app.NavigateAsync(fixtures.Url("/dois"));

        await app.WaitForStateAsync(
            s => s.AddressBarText.Contains("/dois", StringComparison.Ordinal),
            "barra com pagina dois");
    }

    [Fact]
    public async Task TermoDeBuscaNaBarraDeEnderecoNaoViraNavegacaoDireta()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/um"));

        await app.WaitForStateAsync(s => s.Tabs[0].Title == "Pagina Um", "pagina um carregada");

        // javascript: digitado na barra de endereco tem de cair na busca, nunca executar.
        await app.NavigateAsync("javascript:alert(1)");

        var state = await app.WaitForStateAsync(
            s => !s.Tabs[0].Url.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase),
            "esquema perigoso recusado");

        Assert.DoesNotContain("javascript:", state.Tabs[0].Url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OsDadosDeTesteFicamIsoladosEmTemp()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/um"));

        await app.WaitForStateAsync(s => s.Tabs.Count == 1, "app pronto");

        // Garantia de que nenhum teste encosta nos dados reais de navegacao.
        Assert.StartsWith(Path.GetTempPath(), app.Handshake.RootFolder, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            AppPaths.ForNormalUse().RootFolder,
            app.Handshake.RootFolder,
            StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Cada teste sobe um Levhrium e um runtime WebView2; rodar em serie evita
/// disputa por janela em primeiro plano e por pasta temporaria.
/// </summary>
[CollectionDefinition("app", DisableParallelization = true)]
public sealed class AppCollection;
