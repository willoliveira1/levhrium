using Levhrium.Core;

namespace Levhrium.Core.Tests;

public sealed class LibraryTests
{
    [Fact]
    public void FavoritarEDesfavoritarAlternam()
    {
        var library = new Library();

        Assert.True(library.ToggleBookmark("https://jogo.test/", "Jogo"));
        Assert.True(library.IsBookmarked("https://jogo.test/"));

        // Ctrl+D numa pagina ja favoritada desfavorita.
        Assert.False(library.ToggleBookmark("https://jogo.test/", "Jogo"));
        Assert.Empty(library.Bookmarks);
    }

    [Fact]
    public void FavoritoSemTituloUsaAUrl()
    {
        var library = new Library();
        library.ToggleBookmark("https://jogo.test/", string.Empty);

        Assert.Equal("https://jogo.test/", library.Bookmarks[0].Title);
    }

    [Fact]
    public void AboutBlankNaoViraFavorito()
    {
        var library = new Library();

        Assert.False(library.ToggleBookmark("about:blank", "Nova aba"));
        Assert.Empty(library.Bookmarks);
    }

    [Fact]
    public void RevisitarNaoDuplicaEContaAsVisitas()
    {
        var library = new Library();

        library.RecordVisit("https://a.test/", "A");
        library.RecordVisit("https://b.test/", "B");
        library.RecordVisit("https://a.test/", "A");

        // Recarregar uma pagina nao pode enterrar o historico inteiro.
        Assert.Equal(2, library.History.Count);
        Assert.Equal("https://a.test/", library.History[0].Url);
        Assert.Equal(2, library.History[0].VisitCount);
    }

    [Fact]
    public void HistoricoFicaDoMaisRecenteParaOMaisAntigo()
    {
        var library = new Library();

        library.RecordVisit("https://a.test/", "A");
        library.RecordVisit("https://b.test/", "B");

        Assert.Equal("https://b.test/", library.History[0].Url);
        Assert.Equal("https://a.test/", library.History[1].Url);
    }

    [Fact]
    public void HistoricoRespeitaOLimite()
    {
        var library = new Library();

        for (var i = 0; i < BrowserState.HistoryCapacity + 50; i++)
        {
            library.RecordVisit($"https://{i}.test/", "t");
        }

        Assert.Equal(BrowserState.HistoryCapacity, library.History.Count);

        // O que sobra e o mais recente.
        Assert.Equal($"https://{BrowserState.HistoryCapacity + 49}.test/", library.History[0].Url);
    }

    [Fact]
    public void EsquemaNaoWebNaoEntraNoHistorico()
    {
        var library = new Library();

        library.RecordVisit("about:blank", "vazio");
        library.RecordVisit("file:///C:/temp/a.txt", "arquivo");

        Assert.Empty(library.History);
    }

    [Fact]
    public void ZoomEPorHostENaoPorAba()
    {
        var library = new Library();
        library.SetZoom("https://jogo.test/play", 1.25);

        // Outra pagina do mesmo host herda o zoom.
        Assert.Equal(1.25, library.ZoomFor("https://jogo.test/outra"));
        Assert.Equal(1.0, library.ZoomFor("https://outro.test/"));
    }

    [Fact]
    public void ZoomDeVoltaAoNormalNaoOcupaEspaco()
    {
        var library = new Library();
        library.SetZoom("https://jogo.test/", 1.5);
        library.SetZoom("https://jogo.test/", 1.0);

        // Nao vale gravar "este site usa 100%", que e o padrao.
        Assert.Empty(library.ZoomByHost);
    }

    [Theory]
    [InlineData(1.0, 1, 1.1)]
    [InlineData(1.0, -1, 0.9)]
    [InlineData(5.0, 1, 5.0)]
    [InlineData(0.25, -1, 0.25)]
    public void DegrausDeZoomParamNosExtremos(double atual, int direcao, double esperado)
    {
        Assert.Equal(esperado, Library.NextZoomStep(atual, direcao), 3);
    }

    [Fact]
    public void DownloadEntraNaListaEPodeSerConcluido()
    {
        var library = new Library();
        library.RecordDownload("https://a.test/arquivo.zip", @"C:\Downloads\arquivo.zip");

        Assert.Equal("arquivo.zip", library.Downloads[0].FileName);
        Assert.False(library.Downloads[0].Completed);

        library.MarkDownloadCompleted(@"C:\Downloads\arquivo.zip");
        Assert.True(library.Downloads[0].Completed);
    }

    [Fact]
    public void BibliotecaSobreviveAoCicloDeGravacao()
    {
        var original = new Library();
        original.ToggleBookmark("https://jogo.test/", "Jogo");
        original.RecordVisit("https://a.test/", "A");
        original.SetZoom("https://jogo.test/", 1.25);
        original.RecordDownload("https://a.test/x.zip", @"C:\d\x.zip");

        var state = original.ToState([], [], -1, []);
        var reloaded = new Library(state);

        Assert.True(reloaded.IsBookmarked("https://jogo.test/"));
        Assert.Single(reloaded.History);
        Assert.Equal(1.25, reloaded.ZoomFor("https://jogo.test/"));
        Assert.Single(reloaded.Downloads);
    }
}

public sealed class StateStoreTests
{
    [Fact]
    public void ArquivoAusenteDevolveEstadoVazio()
    {
        var store = new StateStore(Path.Combine(Path.GetTempPath(), $"lev-{Guid.NewGuid():n}.json"));

        Assert.Empty(store.Load().Tabs);
    }

    [Fact]
    public void GravaELeDeVolta()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lev-{Guid.NewGuid():n}.json");
        var store = new StateStore(path);

        try
        {
            store.Save(new BrowserState
            {
                Tabs =
                [
                    new PersistedTab { Container = "Conta 2", Url = "https://jogo.test/", Title = "Jogo", Pinned = true },
                ],
                Containers = [new PersistedContainer { Name = "Conta 2", Color = 123 }],
                ActiveTabIndex = 0,
            });

            var loaded = store.Load();

            Assert.Single(loaded.Tabs);
            Assert.Equal("Conta 2", loaded.Tabs[0].Container);
            Assert.True(loaded.Tabs[0].Pinned);
            Assert.Equal(123, loaded.Containers[0].Color);
            Assert.Equal(0, loaded.ActiveTabIndex);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ArquivoCorrompidoNaoImpedeDeAbrir()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lev-{Guid.NewGuid():n}.json");
        File.WriteAllText(path, "{ isto nao e json valido");

        try
        {
            // Comecar limpo e melhor que recusar abrir o navegador.
            Assert.Empty(new StateStore(path).Load().Tabs);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void NaoDeixaTemporarioParaTras()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lev-{Guid.NewGuid():n}.json");
        var store = new StateStore(path);

        try
        {
            store.Save(new BrowserState());

            Assert.True(File.Exists(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public sealed class SessionRestoreTests
{
    [Fact]
    public void ContainersVoltamComAsCoresOriginais()
    {
        var registry = new ContainerRegistry();
        registry.Restore(
        [
            new PersistedContainer { Name = "Conta 2", Color = 42 },
            new PersistedContainer { Name = "Trabalho", Color = 99 },
        ]);

        // Cor trocada ao reabrir derrubaria a unica pista de qual aba e qual conta.
        Assert.Equal(42, registry.GetOrCreate("Conta 2").Color);
        Assert.Equal(99, registry.GetOrCreate("Trabalho").Color);
    }

    [Fact]
    public void RestaurarNaoDuplicaContainerExistente()
    {
        var registry = new ContainerRegistry();
        registry.Restore([new PersistedContainer { Name = ContainerRegistry.DefaultName, Color = 7 }]);

        Assert.Single(registry.All);
    }

    [Fact]
    public void PilhaDeFechadasVoltaDoDisco()
    {
        var session = new TabSession(new ContainerRegistry());
        session.RestoreClosed(
        [
            new ClosedTabEntry { ContainerName = "Conta 2", Url = "https://jogo.test/", Index = 1, Pinned = true },
        ]);

        var reaberta = session.ReopenLastClosed();

        // Ctrl+Shift+T tem de alcancar abas fechadas antes de fechar o navegador.
        Assert.Equal("Conta 2", reaberta!.Container.Name);
        Assert.True(reaberta.Pinned);
    }

    [Fact]
    public void PilhaRestauradaRespeitaOLimite()
    {
        var session = new TabSession(new ContainerRegistry());

        session.RestoreClosed(Enumerable.Range(0, 60).Select(i => new ClosedTabEntry
        {
            ContainerName = "padrao",
            Url = $"https://{i}.test/",
            Index = 0,
            Pinned = false,
        }));

        Assert.Equal(TabSession.ClosedHistoryCapacity, session.ClosedCount);
    }
}
