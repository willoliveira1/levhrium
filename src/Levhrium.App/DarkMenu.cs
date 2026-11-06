using System.Drawing.Drawing2D;

namespace Levhrium.App;

/// <summary>
/// Cores do menu. O <see cref="ToolStripProfessionalRenderer"/> busca tudo o que
/// pinta nesta tabela, entao trocar as cores aqui alcanca tambem os submenus,
/// que sao criados pelo WinForms e nunca passam pelas nossas maos.
/// </summary>
internal sealed class DarkMenuColors : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => Theme.CardBackground;

    public override Color MenuBorder => Theme.CardBorder;

    public override Color MenuItemBorder => Theme.ButtonHover;

    public override Color MenuItemSelected => Theme.ButtonHover;

    public override Color MenuItemSelectedGradientBegin => Theme.ButtonHover;

    public override Color MenuItemSelectedGradientEnd => Theme.ButtonHover;

    public override Color MenuItemPressedGradientBegin => Theme.ButtonActive;

    public override Color MenuItemPressedGradientMiddle => Theme.ButtonActive;

    public override Color MenuItemPressedGradientEnd => Theme.ButtonActive;

    // A faixa da esquerda, onde iriam icones: sem ela o menu fica com uma
    // coluna cinza de sistema que nao combina com nada.
    public override Color ImageMarginGradientBegin => Theme.CardBackground;

    public override Color ImageMarginGradientMiddle => Theme.CardBackground;

    public override Color ImageMarginGradientEnd => Theme.CardBackground;

    public override Color SeparatorDark => Theme.CardBorder;

    public override Color SeparatorLight => Theme.CardBorder;
}

/// <summary>
/// Desenha o menu no tema escuro: fundo do cartao, realce arredondado no item
/// sob o cursor, risco fino no lugar do sulco de duas linhas e borda de 1px.
///
/// O menu do Windows vinha cinza, com moldura 3D e uma coluna de icones vazia
/// -- destoava do resto da janela, que e toda desenhada a mao.
/// </summary>
internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    internal DarkMenuRenderer()
        : base(new DarkMenuColors()) => RoundedEdges = false;

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        // Item desabilitado continua legivel, so apagado: e assim que
        // "Favoritos (vazio)" diz que a secao existe e esta vazia.
        e.TextColor = e.Item.Enabled ? Theme.Foreground : Theme.ForegroundFaint;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.CardBackground);

        if (!e.Item.Selected || !e.Item.Enabled)
        {
            return;
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new Rectangle(3, 1, e.Item.Width - 7, e.Item.Height - 2);

        using var path = Theme.RoundedRect(bounds, 6);
        using var brush = new SolidBrush(Theme.ButtonHover);

        g.FillPath(brush, path);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.CardBackground);

        var y = e.Item.Height / 2;

        using var pen = new Pen(Theme.CardBorder);
        g.DrawLine(pen, 10, y, e.Item.Width - 10, y);
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        e.Graphics.Clear(Theme.CardBackground);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new Rectangle(0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);

        using var path = Theme.RoundedRect(bounds, Radius);
        using var pen = new Pen(Theme.CardBorder);

        g.DrawPath(pen, path);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        // Seta do submenu na cor do texto, nao no preto do sistema.
        // Item pode vir nulo quando a seta e de um ToolStrip sem item associado.
        e.ArrowColor = e.Item?.Enabled is false ? Theme.ForegroundFaint : Theme.ForegroundDim;
        base.OnRenderArrow(e);
    }

    internal const int Radius = 10;
}

/// <summary>Fabrica dos menus do Levhrium, para que todos saiam iguais.</summary>
internal static class DarkMenu
{
    /// <summary>
    /// Liga o tema escuro em todo menu do processo, inclusive nos submenus que
    /// o WinForms cria sozinho. Chamada uma vez, na inicializacao.
    /// </summary>
    internal static void Install()
    {
        ToolStripManager.RenderMode = ToolStripManagerRenderMode.Professional;
        ToolStripManager.Renderer = new DarkMenuRenderer();
    }

    internal static ContextMenuStrip Create()
    {
        var menu = new ContextMenuStrip
        {
            BackColor = Theme.CardBackground,
            ForeColor = Theme.Foreground,
            ShowImageMargin = false,
            DropShadowEnabled = false,
            Font = new Font("Segoe UI", 9.5f),
            Padding = new Padding(0, 5, 0, 5),
        };

        // O contorno arredondado so pode ser recortado depois que o menu tem
        // tamanho, e ele muda de tamanho a cada abertura.
        menu.Opened += (_, _) => ApplyRoundedShape(menu);

        return menu;
    }

    /// <summary>Deixa o item com a altura e o respiro do desenho.</summary>
    internal static ToolStripMenuItem Item(string text)
    {
        return new ToolStripMenuItem(text)
        {
            Padding = new Padding(4, 3, 4, 3),
            ForeColor = Theme.Foreground,
            BackColor = Theme.CardBackground,
        };
    }

    private static void ApplyRoundedShape(ToolStripDropDown menu)
    {
        using var path = Theme.RoundedRect(
            new Rectangle(0, 0, menu.Width, menu.Height),
            DarkMenuRenderer.Radius);

        menu.Region = new Region(path);
    }
}
