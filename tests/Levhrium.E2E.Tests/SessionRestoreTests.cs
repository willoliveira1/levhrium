namespace Levhrium.E2E.Tests;

/// <summary>
/// Criterio de pronto do M5: fechar e reabrir com tudo no lugar. Cada teste
/// sobe o Levhrium duas vezes sobre a MESMA pasta de dados.
/// </summary>
[Collection("app")]
public sealed class SessionRestoreTests
{
    [Fact]
    public async Task AbasEContainersVoltamAoReabrir()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        using var dados = new TemporaryDataFolder();

        List<int> coresAntes;

        await using (var app = await AppHarness.StartAsync(fixtures.Url("/um"), dataFolder: dados.Path))
        {
            await app.WaitForStateAsync(s => s.Tabs[0].Title == "Pagina Um", "primeira aba");
            await app.NewTabAsync("Conta 2", fixtures.Url("/dois"));
            await app.WaitForStateAsync(
                s => s.Tabs.Count == 2 && s.Tabs[1].Title == "Pagina Dois",
                "segunda aba");

            var antes = await app.GetStateAsync();
            coresAntes = antes.Tabs.Select(t => t.ContainerColor).ToList();

            await app.SaveStateAsync();
        }

        // Segunda execucao, sem --url: tem de restaurar a sessao gravada.
        await using var reaberto = await AppHarness.StartAsync(dataFolder: dados.Path);

        var depois = await reaberto.WaitForStateAsync(
            s => s.Tabs.Count == 2 && s.Tabs.All(t => t.Title.StartsWith("Pagina ", StringComparison.Ordinal)),
            "duas abas restauradas");

        Assert.Equal("padrao", depois.Tabs[0].Container);
        Assert.Equal("Conta 2", depois.Tabs[1].Container);

        // Cor trocada derrubaria a unica pista de qual aba e qual conta.
        Assert.Equal(coresAntes, depois.Tabs.Select(t => t.ContainerColor).ToList());
    }

    [Fact]
    public async Task FavoritosEHistoricoSobrevivemAoFechamento()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        using var dados = new TemporaryDataFolder();

        await using (var app = await AppHarness.StartAsync(fixtures.Url("/um"), dataFolder: dados.Path))
        {
            await app.WaitForStateAsync(s => s.Tabs[0].Title == "Pagina Um", "aba carregada");
            await app.ToggleBookmarkAsync();

            await app.WaitForStateAsync(s => s.Library.BookmarkCount == 1, "favorito adicionado");
            await app.NavigateAsync(fixtures.Url("/dois"));
            await app.WaitForStateAsync(s => s.Library.HistoryCount >= 2, "duas paginas no historico");

            await app.SaveStateAsync();
        }

        await using var reaberto = await AppHarness.StartAsync(dataFolder: dados.Path);

        var depois = await reaberto.WaitForStateAsync(
            s => s.Library.BookmarkCount == 1,
            "favorito restaurado");

        Assert.True(depois.Library.HistoryCount >= 2);
        Assert.Contains("Pagina Um", depois.Library.BookmarkTitles);
    }

    [Fact]
    public async Task ZoomPorSiteVoltaNaProximaAbertura()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        using var dados = new TemporaryDataFolder();

        await using (var app = await AppHarness.StartAsync(fixtures.Url("/um"), dataFolder: dados.Path))
        {
            await app.WaitForStateAsync(s => s.Tabs[0].Title == "Pagina Um", "aba carregada");

            await app.ZoomAsync("in");
            await app.ZoomAsync("in");

            var comZoom = await app.WaitForStateAsync(
                s => s.Library.ActiveZoom > 1.0,
                "zoom aplicado");

            Assert.True(comZoom.Library.ActiveZoom >= 1.25);
            await app.SaveStateAsync();
        }

        await using var reaberto = await AppHarness.StartAsync(dataFolder: dados.Path);

        // Ajustar o zoom de um site uma vez tem de valer na proxima vez.
        var depois = await reaberto.WaitForStateAsync(
            s => s.Library.ActiveZoom > 1.0,
            "zoom restaurado");

        Assert.True(depois.Library.ActiveZoom >= 1.25);
    }

    [Fact]
    public async Task AbaFixadaContinuaFixadaDepoisDeReabrir()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        using var dados = new TemporaryDataFolder();

        await using (var app = await AppHarness.StartAsync(fixtures.Url("/um"), dataFolder: dados.Path))
        {
            await app.WaitForStateAsync(s => s.Tabs.Count == 1, "aba aberta");
            await app.PinTabAsync(0, pinned: true);
            await app.WaitForStateAsync(s => s.Tabs[0].Pinned, "aba fixada");
            await app.SaveStateAsync();
        }

        await using var reaberto = await AppHarness.StartAsync(dataFolder: dados.Path);

        // Aba do jogo que voltasse desfixada hibernaria e perderia progresso.
        var depois = await reaberto.WaitForStateAsync(s => s.Tabs.Count == 1, "aba restaurada");
        Assert.True(depois.Tabs[0].Pinned);
    }

    [Fact]
    public async Task CtrlDAlternaOFavoritoPeloTeclado()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/um"));

        await app.WaitForStateAsync(s => s.Tabs[0].Title == "Pagina Um", "aba carregada");

        await app.HotkeyAsync("ctrl+d");
        await app.WaitForStateAsync(s => s.Library.ActiveIsBookmarked, "favoritado");

        await app.HotkeyAsync("ctrl+d");
        await app.WaitForStateAsync(s => !s.Library.ActiveIsBookmarked, "desfavoritado");
    }

    [Fact]
    public async Task CtrlZeroVoltaOZoomAoNormal()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/um"));

        await app.WaitForStateAsync(s => s.Tabs[0].Title == "Pagina Um", "aba carregada");

        await app.HotkeyAsync("ctrl+plus");
        await app.WaitForStateAsync(s => s.Library.ActiveZoom > 1.0, "zoom aumentado");

        await app.HotkeyAsync("ctrl+0");
        await app.WaitForStateAsync(
            s => Math.Abs(s.Library.ActiveZoom - 1.0) < 0.01,
            "zoom de volta ao normal");
    }

    [Fact]
    public async Task HistoricoNaoDuplicaAoRecarregar()
    {
        await using var fixtures = await FixtureServer.StartAsync();
        await using var app = await AppHarness.StartAsync(fixtures.Url("/um"));

        var inicial = await app.WaitForStateAsync(
            s => s.Library.HistoryCount == 1,
            "primeira visita registrada");

        await app.ReloadAsync();
        await Task.Delay(1500);

        // Recarregar nao pode enterrar o historico inteiro com repeticoes.
        var depois = await app.GetStateAsync();
        Assert.Equal(inicial.Library.HistoryCount, depois.Library.HistoryCount);
    }

    /// <summary>Pasta de dados que sobrevive entre as duas execucoes do teste.</summary>
    private sealed class TemporaryDataFolder : IDisposable
    {
        public TemporaryDataFolder()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"levhrium-restore-{Guid.NewGuid():n}");

            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // O runtime do WebView2 pode ainda estar soltando handles.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
