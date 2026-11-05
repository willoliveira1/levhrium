using Levhrium.Core;

namespace Levhrium.Core.Tests;

/// <summary>
/// Geometria da faixa de abas. E a parte da interface que antes nao tinha teste
/// nenhum: clicar no "x" errado fecha a aba errada, e arrastar para a posicao
/// errada embaralha os containers.
/// </summary>
public sealed class TabStripLayoutTests
{
    private const int Largura = 1180;

    [Fact]
    public void SemAbasNaoHaNada()
    {
        Assert.Empty(TabStripLayout.Compute(Largura, 0));
    }

    [Fact]
    public void PoucasAbasUsamALarguraMaxima()
    {
        var slots = TabStripLayout.Compute(Largura, 2);

        Assert.Equal(2, slots.Count);
        Assert.All(slots, s => Assert.Equal(TabStripLayout.MaxTabWidth, s.Width));
    }

    [Fact]
    public void AbasEncolhemConformeAbremMais()
    {
        var poucas = TabStripLayout.Compute(Largura, 3)[0].Width;
        var muitas = TabStripLayout.Compute(Largura, 8)[0].Width;

        Assert.True(muitas < poucas, $"8 abas ({muitas}px) deveriam ser mais estreitas que 3 ({poucas}px)");
    }

    [Fact]
    public void AbasParamDeEncolherNoMinimo()
    {
        var slots = TabStripLayout.Compute(Largura, 40);

        // Abaixo do minimo elas virariam tiras ilegiveis; melhor transbordar.
        Assert.All(slots, s => Assert.Equal(TabStripLayout.MinTabWidth, s.Width));
    }

    [Fact]
    public void AbasNaoSeSobrepoem()
    {
        var slots = TabStripLayout.Compute(Largura, 5);

        for (var i = 1; i < slots.Count; i++)
        {
            Assert.True(
                slots[i].Left >= slots[i - 1].Right,
                $"aba {i} comeca em {slots[i].Left}, antes do fim da {i - 1} em {slots[i - 1].Right}");
        }
    }

    [Fact]
    public void CliqueNoMeioDaAbaSelecionaAAba()
    {
        var slots = TabStripLayout.Compute(Largura, 3);
        var meio = slots[1].Left + (slots[1].Width / 2);

        var hit = TabStripLayout.HitTest(slots, meio, activeIndex: 0, hoverIndex: -1);

        Assert.Equal(1, hit.TabIndex);
        Assert.False(hit.OnClose);
    }

    [Fact]
    public void CliqueNoXDaAbaAtivaFecha()
    {
        var slots = TabStripLayout.Compute(Largura, 3);
        var (closeLeft, closeWidth) = TabStripLayout.CloseBounds(slots[1]);

        var hit = TabStripLayout.HitTest(
            slots,
            closeLeft + (closeWidth / 2),
            activeIndex: 1,
            hoverIndex: -1);

        Assert.Equal(1, hit.TabIndex);
        Assert.True(hit.OnClose);
    }

    [Fact]
    public void XDeAbaQueNaoMostraOBotaoNaoFecha()
    {
        var slots = TabStripLayout.Compute(Largura, 3);
        var (closeLeft, closeWidth) = TabStripLayout.CloseBounds(slots[2]);

        // A aba 2 nao esta nem ativa nem sob o cursor: ali nao ha "x" desenhado,
        // e clicar perto da borda tem de so selecionar, nao fechar.
        var hit = TabStripLayout.HitTest(
            slots,
            closeLeft + (closeWidth / 2),
            activeIndex: 0,
            hoverIndex: -1);

        Assert.Equal(2, hit.TabIndex);
        Assert.False(hit.OnClose);
    }

    [Fact]
    public void AbaSobOCursorMostraOXEFecha()
    {
        var slots = TabStripLayout.Compute(Largura, 3);
        var (closeLeft, closeWidth) = TabStripLayout.CloseBounds(slots[2]);

        var hit = TabStripLayout.HitTest(
            slots,
            closeLeft + (closeWidth / 2),
            activeIndex: 0,
            hoverIndex: 2);

        Assert.True(hit.OnClose);
    }

    [Fact]
    public void BotoesDeNovaAbaEDeContainerFicamDepoisDasAbas()
    {
        var slots = TabStripLayout.Compute(Largura, 3);
        var novaAba = TabStripLayout.NewTabLeft(slots);

        Assert.True(novaAba >= slots[^1].Right, "o + invadiu a ultima aba");

        var noMais = TabStripLayout.HitTest(slots, novaAba + 5, 0, -1);
        Assert.True(noMais.OnNewTab);
        Assert.False(noMais.OnTab);

        var noMenu = TabStripLayout.HitTest(
            slots,
            novaAba + TabStripLayout.NewTabWidth + 5,
            0,
            -1);

        Assert.True(noMenu.OnContainerMenu);
    }

    [Fact]
    public void CliqueNoVazioNaoAcertaNada()
    {
        var slots = TabStripLayout.Compute(Largura, 2);

        var hit = TabStripLayout.HitTest(slots, Largura - 5, 0, -1);

        Assert.False(hit.OnTab);
        Assert.False(hit.OnNewTab);
        Assert.False(hit.OnContainerMenu);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ArrastarParaDentroDeUmaAbaCaiNaquelaPosicao(int alvo)
    {
        var slots = TabStripLayout.Compute(Largura, 3);
        var x = slots[alvo].Left + (slots[alvo].Width / 2);

        Assert.Equal(alvo, TabStripLayout.DropIndex(slots, x));
    }

    [Fact]
    public void ArrastarParaForaPrendeNasPontas()
    {
        var slots = TabStripLayout.Compute(Largura, 3);

        // Soltar a aba no vazio nao pode produzir indice invalido.
        Assert.Equal(0, TabStripLayout.DropIndex(slots, -200));
        Assert.Equal(2, TabStripLayout.DropIndex(slots, Largura + 500));
    }

    [Fact]
    public void ArrastarComUmaAbaSoNaoQuebra()
    {
        var slots = TabStripLayout.Compute(Largura, 1);

        Assert.Equal(0, TabStripLayout.DropIndex(slots, 9999));
        Assert.Equal(0, TabStripLayout.DropIndex([], 100));
    }

    [Fact]
    public void FaixaEstreitaNaoProduzLarguraNegativa()
    {
        // Janela no tamanho minimo com muitas abas ja produziu largura negativa
        // em implementacoes ingenuas.
        var slots = TabStripLayout.Compute(320, 12);

        Assert.All(slots, s => Assert.True(s.Width > 0, $"largura {s.Width}"));
    }
}
