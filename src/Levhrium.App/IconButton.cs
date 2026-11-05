using System.Drawing.Drawing2D;

namespace Levhrium.App;

/// <summary>
/// Botao de icone circular da toolbar. Existe no lugar do <see cref="Button"/>
/// do WinForms porque o botao padrao nao desenha realce circular e nao aceita
/// uma fonte de icones sem herdar a borda achatada.
/// </summary>
internal sealed class IconButton : Control
{
    private bool _hover;
    private bool _pressed;

    internal IconButton(string glyph, string tooltip, int size = 30)
    {
        // SetStyle vem ANTES do BackColor: sem SupportsTransparentBackColor
        // ligado, atribuir Color.Transparent estoura no construtor.
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.SupportsTransparentBackColor,
            true);

        Glyph = glyph;
        Size = new Size(size, size);
        TabStop = false;
        BackColor = Color.Transparent;
        ForeColor = Theme.Foreground;
        Font = new Font(Theme.IconFontName, 10.5f);

        if (tooltip is { Length: > 0 })
        {
            new ToolTip().SetToolTip(this, tooltip);
        }
    }

    /// <summary>Glifo do Segoe MDL2 Assets; ver <see cref="Glyphs"/>.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal string Glyph { get; set; }

    /// <summary>
    /// Desabilitado continua visivel, so apagado: sumir com o botao moveria os
    /// vizinhos de lugar a cada navegacao.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool Muted { get; set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.ToolbarBackground);

        if (_hover || _pressed)
        {
            using var brush = new SolidBrush(_pressed ? Theme.ButtonActive : Theme.ButtonHover);
            g.FillEllipse(brush, 0, 0, Width - 1, Height - 1);
        }

        TextRenderer.DrawText(
            g,
            Glyph,
            Font,
            ClientRectangle,
            Muted ? Theme.ForegroundFaint : ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
        _pressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        _pressed = e.Button == MouseButtons.Left;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _pressed = false;
        Invalidate();
    }
}
