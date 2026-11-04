namespace Levhrium.E2E.Tests;

/// <summary>
/// Hibernacao de verdade, com o WebView2 no meio: TrySuspend congela o renderer
/// e Resume o traz de volta. Os testes usam limiar de 1 segundo para nao esperar
/// os cinco minutos reais.
/// </summary>
[Collection("app")]
public sealed class HibernationTests
{
    private static readonly TimeSpan LimiarCurto = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task AbaDeFundoOciosaEHibernada()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/um"), LimiarCurto);

        await app.WaitForStateAsync(s => s.Tabs.Count == 1, "primeira aba");
        await app.NewTabAsync("padrao", fixtures.Url("/dois"));
        await app.WaitForStateAsync(
            s => s.Tabs.Count == 2 && s.Tabs[1].Title == "Pagina Dois",
            "segunda aba carregada");

        // A primeira aba virou fundo; depois do limiar ela deve congelar.
        await Task.Delay(1500);
        await app.HibernateTickAsync();

        var state = await app.WaitForStateAsync(
            s => s.Tabs[0].Suspended,
            "primeira aba hibernada");

        Assert.True(state.Tabs[0].Suspended);
        Assert.False(state.Tabs[1].Suspended);
    }

    [Fact]
    public async Task AbaAtivaNuncaHiberna()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/um"), LimiarCurto);

        await app.WaitForStateAsync(s => s.Tabs[0].Title == "Pagina Um", "aba carregada");

        await Task.Delay(2000);
        await app.HibernateTickAsync();

        var state = await app.GetStateAsync();
        Assert.False(state.Tabs[0].Suspended);
    }

    [Fact]
    public async Task VoltarParaAAbaADescongela()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/um"), LimiarCurto);

        await app.WaitForStateAsync(s => s.Tabs.Count == 1, "primeira aba");
        await app.NewTabAsync("padrao", fixtures.Url("/dois"));
        await app.WaitForStateAsync(s => s.Tabs.Count == 2, "segunda aba");

        await Task.Delay(1500);
        await app.HibernateTickAsync();
        await app.WaitForStateAsync(s => s.Tabs[0].Suspended, "primeira aba hibernada");

        await app.SelectTabAsync(0);

        var state = await app.WaitForStateAsync(
            s => !s.Tabs[0].Suspended,
            "primeira aba descongelada ao voltar");

        Assert.Equal(0, state.ActiveTabIndex);
    }

    [Fact]
    public async Task AbaFixadaNaoHiberna()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/um"), LimiarCurto);

        await app.WaitForStateAsync(s => s.Tabs.Count == 1, "primeira aba");
        await app.PinTabAsync(0, pinned: true);
        await app.NewTabAsync("padrao", fixtures.Url("/dois"));
        await app.WaitForStateAsync(s => s.Tabs.Count == 2, "segunda aba");

        await Task.Delay(2000);
        await app.HibernateTickAsync();
        await app.HibernateTickAsync();

        // O pin e a isencao permanente, e e por ele que a aba do jogo sobrevive.
        var state = await app.GetStateAsync();
        Assert.True(state.Tabs[0].Pinned);
        Assert.False(state.Tabs[0].Suspended);
    }

    [Fact]
    public async Task PaginaCongeladaParaDeExecutarEVoltaDoPontoOndeParou()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/contador"), LimiarCurto);

        await app.WaitForStateAsync(
            s => s.Tabs[0].Title.StartsWith("Contador", StringComparison.Ordinal),
            "contador rodando");

        await app.NewTabAsync("padrao", fixtures.Url("/dois"));
        await app.WaitForStateAsync(s => s.Tabs.Count == 2, "segunda aba");

        await Task.Delay(1500);
        await app.HibernateTickAsync();
        var congelada = await app.WaitForStateAsync(s => s.Tabs[0].Suspended, "contador congelado");

        var valorAoCongelar = LerContador(congelada.Tabs[0].Title);

        // Congelada de verdade: o timer da pagina nao avanca.
        await Task.Delay(2500);
        var durante = await app.GetStateAsync();
        Assert.Equal(valorAoCongelar, LerContador(durante.Tabs[0].Title));

        await app.SelectTabAsync(0);

        // E volta viva, do ponto onde parou -- congelar, nao descartar: se fosse
        // descarte a pagina recarregaria e o contador voltaria a zero.
        var retomada = await app.WaitForStateAsync(
            s => LerContador(s.Tabs[0].Title) > valorAoCongelar,
            "contador voltou a andar");

        Assert.True(LerContador(retomada.Tabs[0].Title) >= valorAoCongelar);
    }

    private static int LerContador(string title) =>
        int.TryParse(title.Replace("Contador ", string.Empty, StringComparison.Ordinal), out var value)
            ? value
            : -1;
}
