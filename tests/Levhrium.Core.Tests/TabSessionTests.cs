using Levhrium.Core;

namespace Levhrium.Core.Tests;

/// <summary>
/// Logica de abas e containers. Testada sem janela porque e aqui que um bug
/// silencioso custaria progresso de jogo -- aba fechada no container errado,
/// Ctrl+Tab pulando aba, reabertura trazendo a conta errada.
/// </summary>
public sealed class TabSessionTests
{
    private static TabSession NewSession() => new(new ContainerRegistry());

    [Fact]
    public void AbreNoFimEAtivaAAbaNova()
    {
        var session = NewSession();

        var primeira = session.Open("padrao", "https://a.test");
        var segunda = session.Open("padrao", "https://b.test");

        Assert.Equal(2, session.Tabs.Count);
        Assert.Equal(1, session.ActiveIndex);
        Assert.Same(segunda, session.Active);
        Assert.Same(primeira, session.Tabs[0]);
    }

    [Fact]
    public void AbasPodemViverEmContainersDiferentes()
    {
        var session = NewSession();

        var conta1 = session.Open("Conta 1", "https://jogo.test");
        var conta2 = session.Open("Conta 2", "https://jogo.test");

        // O requisito central: mesma URL, contas separadas.
        Assert.NotEqual(conta1.Container.Name, conta2.Container.Name);
        Assert.NotEqual(conta1.Container.Color, conta2.Container.Color);
        Assert.Equal(conta1.Url, conta2.Url);
    }

    [Fact]
    public void CtrlTabPercorreEmOrdemVisualEDaAVolta()
    {
        var session = NewSession();
        session.Open("padrao", "https://a.test");
        session.Open("padrao", "https://b.test");
        session.Open("padrao", "https://c.test");

        session.Activate(0);

        session.ActivateNext();
        Assert.Equal(1, session.ActiveIndex);

        session.ActivateNext();
        Assert.Equal(2, session.ActiveIndex);

        // Da a volta, nao para na ultima.
        session.ActivateNext();
        Assert.Equal(0, session.ActiveIndex);
    }

    [Fact]
    public void CtrlShiftTabVaiParaAEsquerdaEDaAVolta()
    {
        var session = NewSession();
        session.Open("padrao", "https://a.test");
        session.Open("padrao", "https://b.test");
        session.Open("padrao", "https://c.test");

        session.Activate(0);

        session.ActivatePrevious();
        Assert.Equal(2, session.ActiveIndex);

        session.ActivatePrevious();
        Assert.Equal(1, session.ActiveIndex);
    }

    [Fact]
    public void FecharAbaAtivaVaiParaAVizinhaDaDireita()
    {
        var session = NewSession();
        session.Open("padrao", "https://a.test");
        var meio = session.Open("padrao", "https://b.test");
        session.Open("padrao", "https://c.test");

        session.Activate(1);
        session.Close(meio.Id);

        Assert.Equal(2, session.Tabs.Count);
        Assert.Equal(1, session.ActiveIndex);
        Assert.Equal("https://c.test", session.Active!.Url);
    }

    [Fact]
    public void FecharAUltimaAbaAtivaAAnterior()
    {
        var session = NewSession();
        session.Open("padrao", "https://a.test");
        var ultima = session.Open("padrao", "https://b.test");

        session.Activate(1);
        session.Close(ultima.Id);

        Assert.Equal(0, session.ActiveIndex);
        Assert.Equal("https://a.test", session.Active!.Url);
    }

    [Fact]
    public void FecharAbaAEsquerdaMantemAMesmaAbaAtiva()
    {
        var session = NewSession();
        var primeira = session.Open("padrao", "https://a.test");
        session.Open("padrao", "https://b.test");
        session.Open("padrao", "https://c.test");

        session.Activate(2);
        session.Close(primeira.Id);

        // Continua na mesma aba, so que uma posicao antes.
        Assert.Equal(1, session.ActiveIndex);
        Assert.Equal("https://c.test", session.Active!.Url);
    }

    [Fact]
    public void ReabrirRestauraContainerUrlEPosicao()
    {
        var session = NewSession();
        session.Open("padrao", "https://a.test");
        var conta2 = session.Open("Conta 2", "https://jogo.test");
        session.Open("padrao", "https://c.test");

        session.Close(conta2.Id);
        var reaberta = session.ReopenLastClosed();

        Assert.NotNull(reaberta);

        // Sem o container na pilha, a Conta 2 voltaria dentro da Conta 1.
        Assert.Equal("Conta 2", reaberta.Container.Name);
        Assert.Equal("https://jogo.test", reaberta.Url);
        Assert.Equal(1, session.Tabs.ToList().IndexOf(reaberta));
    }

    [Fact]
    public void ReabrirPreservaOEstadoDeFixada()
    {
        var session = NewSession();
        var tab = session.Open("padrao", "https://jogo.test");
        tab.Pinned = true;

        session.Close(tab.Id);
        var reaberta = session.ReopenLastClosed();

        // Aba do jogo fixada nao pode voltar desfixada: hibernaria e perderia progresso.
        Assert.True(reaberta!.Pinned);
    }

    [Fact]
    public void ReabrirSemNadaFechadoNaoFazNada()
    {
        var session = NewSession();

        Assert.Null(session.ReopenLastClosed());
    }

    [Fact]
    public void PilhaDeFechadasParaEmVinteECinco()
    {
        var session = NewSession();

        for (var i = 0; i < 40; i++)
        {
            var tab = session.Open("padrao", $"https://{i}.test");
            session.Close(tab.Id);
        }

        Assert.Equal(TabSession.ClosedHistoryCapacity, session.ClosedCount);

        // A mais recente e a primeira a voltar.
        Assert.Equal("https://39.test", session.ReopenLastClosed()!.Url);
    }

    [Fact]
    public void MoverAbaMantemAMesmaAbaAtiva()
    {
        var session = NewSession();
        session.Open("padrao", "https://a.test");
        session.Open("padrao", "https://b.test");
        var ativa = session.Open("padrao", "https://c.test");

        session.Move(0, 2);

        Assert.Equal("https://a.test", session.Tabs[2].Url);
        Assert.Same(ativa, session.Active);
    }

    [Fact]
    public void FecharTodasDeixaSessaoVazia()
    {
        var session = NewSession();
        var a = session.Open("padrao", "https://a.test");
        var b = session.Open("padrao", "https://b.test");

        session.Close(a.Id);
        session.Close(b.Id);

        Assert.Empty(session.Tabs);
        Assert.Equal(-1, session.ActiveIndex);
        Assert.Null(session.Active);
    }
}

public sealed class ContainerRegistryTests
{
    [Fact]
    public void ContainerPadraoExisteDesdeOInicio()
    {
        var registry = new ContainerRegistry();

        Assert.Equal(ContainerRegistry.DefaultName, registry.Default.Name);
        Assert.Single(registry.All);
    }

    [Fact]
    public void ContainersGanhamCoresDiferentesEmSequencia()
    {
        var registry = new ContainerRegistry();

        var cores = new[] { "Conta 1", "Conta 2", "Conta 3" }
            .Select(name => registry.GetOrCreate(name).Color)
            .ToList();

        // Duas contas abertas em sequencia nunca podem sair com a mesma cor:
        // o selo e o unico jeito de saber qual aba e qual conta.
        Assert.Equal(cores.Count, cores.Distinct().Count());
        Assert.DoesNotContain(registry.Default.Color, cores);
    }

    [Fact]
    public void BuscarContainerExistenteNaoCriaOutro()
    {
        var registry = new ContainerRegistry();

        var primeiro = registry.GetOrCreate("Conta 1");
        var segundo = registry.GetOrCreate("conta 1");

        Assert.Equal(primeiro.Color, segundo.Color);
        Assert.Equal(2, registry.All.Count);
    }

    [Theory]
    [InlineData("Conta 1", "Conta-1")]
    [InlineData("Trabalho/Pessoal", "Trabalho-Pessoal")]
    [InlineData("conta@2", "conta-2")]
    public void NomeDePerfilESaneadoParaOWebView2(string nome, string esperado)
    {
        // O WebView2 recusa perfil com caractere fora de [A-Za-z0-9-_.].
        Assert.Equal(esperado, new Container { Name = nome, Color = 0 }.ProfileName);
    }

    [Fact]
    public void SugestaoDeNomePulaOsJaUsados()
    {
        var registry = new ContainerRegistry();
        registry.GetOrCreate("Conta 1");
        registry.GetOrCreate("Conta 2");

        Assert.Equal("Conta 3", registry.SuggestName());
    }
}
