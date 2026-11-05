namespace Levhrium.Core;

/// <summary>Area ocupada por uma aba na faixa.</summary>
public readonly record struct TabSlot(int Index, int Left, int Width)
{
    public int Right => Left + Width;

    public bool Contains(int x) => x >= Left && x < Right;
}

/// <summary>O que esta sob o cursor na faixa de abas.</summary>
public readonly record struct TabStripHit(int TabIndex, bool OnClose, bool OnNewTab, bool OnContainerMenu)
{
    public static TabStripHit None => new(-1, false, false, false);

    public bool OnTab => TabIndex >= 0;
}

/// <summary>
/// Geometria da faixa de abas, separada do desenho. Fica aqui porque e onde os
/// defeitos de arrastar e de clicar no "x" errado aparecem, e porque assim da
/// para testar sem abrir janela.
/// </summary>
public static class TabStripLayout
{
    public const int MaxTabWidth = 220;
    public const int MinTabWidth = 64;
    public const int TabGap = 2;
    public const int LeftMargin = 4;
    public const int CloseSize = 16;
    public const int ClosePadding = 6;
    public const int NewTabWidth = 30;
    public const int ContainerMenuWidth = 22;

    /// <summary>
    /// Divide a largura disponivel entre as abas. Elas encolhem conforme abrem
    /// mais, ate um minimo; dali em diante passam a transbordar, em vez de virar
    /// tiras ilegiveis.
    /// </summary>
    public static IReadOnlyList<TabSlot> Compute(int stripWidth, int tabCount)
    {
        if (tabCount <= 0)
        {
            return [];
        }

        var available = stripWidth - LeftMargin - NewTabWidth - ContainerMenuWidth - TabGap;
        var width = Math.Clamp((available / tabCount) - TabGap, MinTabWidth, MaxTabWidth);

        var slots = new List<TabSlot>(tabCount);
        var x = LeftMargin;

        for (var i = 0; i < tabCount; i++)
        {
            slots.Add(new TabSlot(i, x, width));
            x += width + TabGap;
        }

        return slots;
    }

    /// <summary>Onde comeca o botao "+", logo depois da ultima aba.</summary>
    public static int NewTabLeft(IReadOnlyList<TabSlot> slots) =>
        slots.Count == 0 ? LeftMargin : slots[^1].Right + TabGap;

    /// <summary>Retangulo do "x" de fechar, dentro da aba.</summary>
    public static (int Left, int Width) CloseBounds(TabSlot slot) =>
        (slot.Right - CloseSize - ClosePadding, CloseSize);

    /// <summary>
    /// O que esta em (x, y). O "x" so conta quando a aba o mostra -- ativa ou
    /// sob o cursor --, senao clicar perto da borda fecharia aba sem querer.
    /// </summary>
    public static TabStripHit HitTest(
        IReadOnlyList<TabSlot> slots,
        int x,
        int activeIndex,
        int hoverIndex)
    {
        ArgumentNullException.ThrowIfNull(slots);

        var newTabLeft = NewTabLeft(slots);

        if (x >= newTabLeft && x < newTabLeft + NewTabWidth)
        {
            return new TabStripHit(-1, false, true, false);
        }

        var menuLeft = newTabLeft + NewTabWidth + 1;
        if (x >= menuLeft && x < menuLeft + ContainerMenuWidth)
        {
            return new TabStripHit(-1, false, false, true);
        }

        foreach (var slot in slots)
        {
            if (!slot.Contains(x))
            {
                continue;
            }

            var showsClose = slot.Index == activeIndex || slot.Index == hoverIndex;
            var (closeLeft, closeWidth) = CloseBounds(slot);
            var onClose = showsClose && x >= closeLeft && x < closeLeft + closeWidth;

            return new TabStripHit(slot.Index, onClose, false, false);
        }

        return TabStripHit.None;
    }

    /// <summary>
    /// Para onde a aba arrastada deve ir, dado o x do cursor. Fora da faixa,
    /// prende nas pontas em vez de soltar a aba no vazio.
    /// </summary>
    public static int DropIndex(IReadOnlyList<TabSlot> slots, int x)
    {
        ArgumentNullException.ThrowIfNull(slots);

        if (slots.Count == 0)
        {
            return 0;
        }

        if (x < slots[0].Left)
        {
            return 0;
        }

        foreach (var slot in slots)
        {
            if (slot.Contains(x))
            {
                return slot.Index;
            }
        }

        return slots.Count - 1;
    }
}
