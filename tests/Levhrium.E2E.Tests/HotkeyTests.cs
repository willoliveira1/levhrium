namespace Levhrium.E2E.Tests;

/// <summary>
/// Atalhos de teclado. Cada teste passa pelo mesmo ProcessCmdKey que o teclado
/// usa, entao prova a ligacao real -- e nao um metodo chamado por baixo.
/// </summary>
[Collection("app")]
public sealed class HotkeyTests
{
    [Fact]
    public async Task CtrlTAbreNovaAbaNoContainerPadrao()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/um"));

        await app.WaitForStateAsync(s => s.Tabs.Count == 1, "primeira aba");
        await app.HotkeyAsync("ctrl+t");

        var state = await app.WaitForStateAsync(s => s.Tabs.Count == 2, "segunda aba aberta");

        Assert.Equal("padrao", state.Tabs[1].Container);
        Assert.Equal(1, state.ActiveTabIndex);
    }

    [Fact]
    public async Task CtrlTabVaiParaAAbaDaDireitaEDaAVolta()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/um"));

        await app.WaitForStateAsync(s => s.Tabs.Count == 1, "primeira aba");
        await app.NewTabAsync("padrao", fixtures.Url("/dois"));
        await app.NewTabAsync("Conta 2", fixtures.Url("/um"));
        await app.WaitForStateAsync(s => s.Tabs.Count == 3, "tres abas");

        await app.SelectTabAsync(0);
        await app.WaitForStateAsync(s => s.ActiveTabIndex == 0, "primeira ativa");

        await app.HotkeyAsync("ctrl+tab");
        await app.WaitForStateAsync(s => s.ActiveTabIndex == 1, "segunda ativa");

        await app.HotkeyAsync("ctrl+tab");
        await app.WaitForStateAsync(s => s.ActiveTabIndex == 2, "terceira ativa");

        // Da a volta na ultima, nao trava.
        await app.HotkeyAsync("ctrl+tab");
        await app.WaitForStateAsync(s => s.ActiveTabIndex == 0, "voltou para a primeira");
    }

    [Fact]
    public async Task CtrlShiftTabVaiParaAAbaDaEsquerda()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/um"));

        await app.WaitForStateAsync(s => s.Tabs.Count == 1, "primeira aba");
        await app.NewTabAsync("padrao", fixtures.Url("/dois"));
        await app.WaitForStateAsync(s => s.Tabs.Count == 2 && s.ActiveTabIndex == 1, "segunda ativa");

        await app.HotkeyAsync("ctrl+shift+tab");
        await app.WaitForStateAsync(s => s.ActiveTabIndex == 0, "primeira ativa");

        // Da a volta para a ultima.
        await app.HotkeyAsync("ctrl+shift+tab");
        await app.WaitForStateAsync(s => s.ActiveTabIndex == 1, "voltou para a ultima");
    }

    [Fact]
    public async Task CtrlWFechaAAbaAtiva()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/um"));

        await app.WaitForStateAsync(s => s.Tabs.Count == 1, "primeira aba");
        await app.NewTabAsync("padrao", fixtures.Url("/dois"));
        await app.WaitForStateAsync(
            s => s.Tabs.Count == 2 && s.Tabs[1].Title == "Pagina Dois",
            "segunda aba carregada");

        await app.HotkeyAsync("ctrl+w");

        var state = await app.WaitForStateAsync(s => s.Tabs.Count == 1, "voltou a uma aba");
        Assert.Equal("Pagina Um", state.Tabs[0].Title);
    }

    [Fact]
    public async Task CtrlWNaUltimaAbaMantemAJanelaComAbaEmBranco()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/um"));

        await app.WaitForStateAsync(
            s => s.Tabs.Count == 1 && s.Tabs[0].Title == "Pagina Um",
            "unica aba carregada");

        await app.HotkeyAsync("ctrl+w");

        // Fechar o navegador inteiro por um Ctrl+W a mais seria perder todas as
        // sessoes abertas; a janela fica em pe com uma aba em branco.
        var state = await app.WaitForStateAsync(
            s => s.Tabs.Count == 1 && !s.Tabs[0].Url.Contains("/um", StringComparison.Ordinal),
            "aba em branco no lugar");

        Assert.Equal("padrao", state.Tabs[0].Container);
    }

    [Fact]
    public async Task CtrlShiftTReabreNoContainerOriginal()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/um"));

        await app.WaitForStateAsync(s => s.Tabs.Count == 1, "primeira aba");
        await app.NewTabAsync("Conta 2", fixtures.Url("/dois"));
        await app.WaitForStateAsync(
            s => s.Tabs.Count == 2 && s.Tabs[1].Title == "Pagina Dois",
            "aba da Conta 2 carregada");

        await app.HotkeyAsync("ctrl+w");
        await app.WaitForStateAsync(s => s.Tabs.Count == 1, "aba fechada");

        await app.HotkeyAsync("ctrl+shift+t");

        var state = await app.WaitForStateAsync(
            s => s.Tabs.Count == 2 && s.Tabs[1].Url.Contains("/dois", StringComparison.Ordinal),
            "aba reaberta e navegada");

        // Sem o container na pilha de fechadas, a Conta 2 voltaria dentro da Conta 1.
        Assert.Equal("Conta 2", state.Tabs[1].Container);
        Assert.Contains("/dois", state.Tabs[1].Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CtrlShiftTReabreNaPosicaoOriginal()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/um"));

        await app.WaitForStateAsync(s => s.Tabs.Count == 1, "primeira aba");
        await app.NewTabAsync("Conta 2", fixtures.Url("/dois"));
        await app.NewTabAsync("padrao", fixtures.Url("/um"));
        await app.WaitForStateAsync(s => s.Tabs.Count == 3, "tres abas");

        await app.CloseTabAsync(1);
        await app.WaitForStateAsync(s => s.Tabs.Count == 2, "aba do meio fechada");

        await app.HotkeyAsync("ctrl+shift+t");

        var state = await app.WaitForStateAsync(s => s.Tabs.Count == 3, "aba reaberta");
        Assert.Equal("Conta 2", state.Tabs[1].Container);
    }

    [Fact]
    public async Task AbaFixadaAparecNoEstado()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/um"));

        await app.WaitForStateAsync(s => s.Tabs.Count == 1, "primeira aba");
        Assert.False((await app.GetStateAsync()).Tabs[0].Pinned);

        await app.PinTabAsync(0, pinned: true);

        var state = await app.WaitForStateAsync(s => s.Tabs[0].Pinned, "aba fixada");
        Assert.True(state.Tabs[0].Pinned);
    }
}
