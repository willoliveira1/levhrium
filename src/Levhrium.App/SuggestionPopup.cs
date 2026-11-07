using System.Drawing.Drawing2D;
using Levhrium.Core;

namespace Levhrium.App;

/// <summary>
/// Lista de sugestões sob a barra de endereço, alimentada pelo histórico e
/// pelos favoritos enquanto se digita.
///
/// É uma janela e não um controle dentro do form porque precisa aparecer por
/// cima da página, que é uma superfície do WebView2 — um controle irmão
/// ficaria embaixo dela.
///
/// Nunca recebe foco. Se recebesse, o campo de texto perderia o foco, a barra
/// voltaria ao modo de exibição e a lista sumiria no mesmo instante em que
/// apareceu. Por isso WS_EX_NOACTIVATE: o cursor continua piscando no campo
/// enquanto a lista se mexe embaixo.
/// </summary>
internal sealed class SuggestionPopup : Form
{
    private const int RowHeight = 40;
    private const int Radius = 10;
    private const int Inset = 6;

    /// <summary>WS_EX_NOACTIVATE: a janela aparece sem roubar o foco.</summary>
    private const int NoActivate = 0x08000000;

    /// <summary>WS_EX_TOOLWINDOW: fora da barra de tarefas e do Alt+Tab.</summary>
    private const int ToolWindow = 0x00000080;

    private IReadOnlyList<LibraryEntry> _items = [];
    private int _selected = -1;
    private int _hover = -1;

    internal SuggestionPopup()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.CardBackground;
        DoubleBuffered = true;

        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw,
            true);
    }

    /// <summary>Sugestão escolhida, por clique ou por Enter.</summary>
    internal event EventHandler<string>? Chosen;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= NoActivate | ToolWindow;
            return parameters;
        }
    }

    internal bool HasItems => _items.Count > 0;

    /// <summary>A sugestão destacada, ou nula enquanto nenhuma estiver.</summary>
    internal LibraryEntry? Current =>
        _selected >= 0 && _selected < _items.Count ? _items[_selected] : null;

    /// <summary>
    /// Troca a lista. Nenhuma sugestão nasce destacada: Enter sem seta precisa
    /// navegar para o que foi digitado, não para o primeiro palpite.
    /// </summary>
    internal void SetItems(IReadOnlyList<LibraryEntry> items, Rectangle anchor)
    {
        _items = items;
        _selected = -1;
        _hover = -1;

        if (items.Count == 0)
        {
            Hide();
            return;
        }

        Bounds = new Rectangle(
            anchor.Left,
            anchor.Bottom + 3,
            anchor.Width,
            (items.Count * RowHeight) + (Inset * 2));

        using (var path = Theme.RoundedRect(new Rectangle(0, 0, Width, Height), Radius))
        {
            Region = new Region(path);
        }

        Invalidate();

        if (!Visible)
        {
            Show();
        }
    }

    /// <summary>Move o destaque; devolve falso quando não há lista aberta.</summary>
    internal bool MoveSelection(int delta)
    {
        if (!Visible || _items.Count == 0)
        {
            return false;
        }

        _selected = _selected < 0 && delta < 0
            ? _items.Count - 1
            : Math.Clamp(_selected + delta, -1, _items.Count - 1);

        Invalidate();
        return true;
    }

    internal void Dismiss()
    {
        _items = [];
        _selected = -1;
        Hide();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.CardBackground);

        using (var border = new Pen(Theme.CardBorder))
        using (var path = Theme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), Radius))
        {
            g.DrawPath(border, path);
        }

        for (var i = 0; i < _items.Count; i++)
        {
            DrawRow(g, i);
        }
    }

    private void DrawRow(Graphics g, int index)
    {
        var entry = _items[index];
        var top = Inset + (index * RowHeight);
        var destacada = index == _selected || index == _hover;

        if (destacada)
        {
            using var path = Theme.RoundedRect(new Rectangle(4, top + 1, Width - 9, RowHeight - 2), 7);
            using var brush = new SolidBrush(Theme.ToolbarBackground);
            g.FillPath(brush, path);
        }

        var caixa = new Rectangle(12, top + ((RowHeight - 22) / 2), 22, 22);

        using (var path = Theme.RoundedRect(caixa, 6))
        using (var brush = new SolidBrush(Theme.ButtonHover))
        {
            g.FillPath(brush, path);
        }

        using (var letra = new Font("Segoe UI", 8.5f, FontStyle.Bold))
        {
            TextRenderer.DrawText(
                g, entry.Badge, letra, caixa, Theme.ForegroundDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix);
        }

        using var titulo = new Font("Segoe UI", 9.5f);
        using var host = new Font("Segoe UI", 8f);

        TextRenderer.DrawText(
            g,
            entry.Title,
            titulo,
            new Rectangle(44, top + 4, Width - 56, 16),
            Theme.Foreground,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix);

        TextRenderer.DrawText(
            g,
            entry.Host,
            host,
            new Rectangle(44, top + 20, Width - 56, 14),
            Theme.CardSubtitle,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var index = IndexAt(e.Y);

        if (index != _hover)
        {
            _hover = index;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (IndexAt(e.Y) is var index && index >= 0)
        {
            Chosen?.Invoke(this, _items[index].Url);
        }
    }

    private int IndexAt(int y)
    {
        var index = (y - Inset) / RowHeight;
        return index >= 0 && index < _items.Count ? index : -1;
    }
}
