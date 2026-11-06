using System.Drawing.Drawing2D;

namespace Levhrium.App;

/// <summary>
/// Cartao das configuracoes: fundo um degrau acima do da janela, contorno fino e
/// cantos macios. O WinForms nao tem painel com canto arredondado, e empilhar
/// rotulo e caixinha solta -- que era o desenho antigo -- nao agrupa nada.
/// </summary>
internal sealed class Card : Panel
{
    private const int Radius = 12;

    internal Card()
    {
        // O BackColor descreve o INTERIOR do cartao, nao a moldura: e dele que
        // os filhos (botoes-pilula, ladrilhos) se servem para limpar o proprio
        // fundo. Com a cor da janela aqui, cada botao redondo aparecia dentro
        // de um retangulo mais escuro.
        BackColor = Theme.CardBackground;
        DoubleBuffered = true;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Os cantos arredondados deixam ver o que esta atras do cartao.
        g.Clear(Parent?.BackColor ?? Theme.WindowBackground);

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);

        using var path = Theme.RoundedRect(bounds, Radius);
        using var fill = new SolidBrush(Theme.CardBackground);
        using var border = new Pen(Theme.CardBorder);

        g.FillPath(fill, path);
        g.DrawPath(border, path);
    }
}

/// <summary>
/// Linha funda dentro de um cartao, para a lista de containers. Mesma ideia do
/// <see cref="Card"/>, raio menor.
/// </summary>
internal sealed class SunkenRow : Panel
{
    private const int Radius = 9;

    internal SunkenRow()
    {
        BackColor = Theme.RowBackground;
        DoubleBuffered = true;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
    }

    /// <summary>Cor do ponto a esquerda; vazia nao desenha ponto.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Color Dot { get; set; } = Color.Empty;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.CardBackground);

        using (var path = Theme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), Radius))
        using (var fill = new SolidBrush(Theme.RowBackground))
        {
            g.FillPath(fill, path);
        }

        if (Dot.IsEmpty)
        {
            return;
        }

        using var brush = new SolidBrush(Dot);
        g.FillEllipse(brush, 10, (Height - 10) / 2, 10, 10);
    }
}

/// <summary>
/// Ladrilho de icone: quadrado de cantos macios, pintado com a propria cor do
/// icone bem diluida. E o que da a cada linha do cartao uma ancora visual sem
/// precisar de imagem nenhuma -- o glifo vem da fonte do Windows.
/// </summary>
internal sealed class IconTile : Control
{
    private const int Radius = 9;

    internal IconTile(string glyph, Color color)
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.SupportsTransparentBackColor,
            true);

        Glyph = glyph;
        Accent = color;
        Size = new Size(34, 34);
        TabStop = false;
        BackColor = Color.Transparent;
        Font = new Font(Theme.IconFontName, 11f);
    }

    internal string Glyph { get; }

    internal Color Accent { get; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.CardBackground);

        using (var path = Theme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), Radius))
        using (var fill = new SolidBrush(Theme.Tint(Accent)))
        {
            g.FillPath(fill, path);
        }

        TextRenderer.DrawText(
            g,
            Glyph,
            Font,
            ClientRectangle,
            Accent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

/// <summary>
/// Botao-pilula: so contorno, sem preenchimento, como no desenho. O Button do
/// WinForms nao chega nisso nem com FlatStyle -- a borda sai quadrada e o
/// realce de foco vem junto.
/// </summary>
internal sealed class PillButton : Control
{
    private bool _hover;
    private bool _pressed;

    internal PillButton(string text, int width = 92)
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.SupportsTransparentBackColor,
            true);

        Text = text;
        Size = new Size(width, 30);
        TabStop = false;
        BackColor = Color.Transparent;
        ForeColor = Theme.PillForeground;
        Font = new Font("Segoe UI", 9f);
        Cursor = Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.CardBackground);

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);

        using (var path = Theme.RoundedRect(bounds, Height / 2))
        using (var border = new Pen(_hover ? Theme.Accent : Theme.PillBorder))
        {
            if (_pressed || _hover)
            {
                using var fill = new SolidBrush(_pressed ? Theme.ButtonActive : Theme.ButtonHover);
                g.FillPath(fill, path);
            }

            g.DrawPath(border, path);
        }

        TextRenderer.DrawText(
            g,
            Text,
            Font,
            ClientRectangle,
            Enabled ? ForeColor : Theme.ForegroundFaint,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix |
            TextFormatFlags.EndEllipsis);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        Invalidate();
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

/// <summary>Risco de 1px que separa as linhas de um cartao.</summary>
internal sealed class Divider : Control
{
    internal Divider()
    {
        Height = 1;
        TabStop = false;
        BackColor = Theme.CardBorder;
    }
}
