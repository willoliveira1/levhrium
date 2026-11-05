using System.Drawing.Drawing2D;
using Levhrium.Core;

namespace Levhrium.App;

/// <summary>
/// Faixa de abas desenhada a mao. Existe custom-desenhada por um motivo concreto:
/// o selo de cor do container precisa estar visivel em cada aba, e o TabControl do
/// WinForms nao tem onde coloca-lo.
/// </summary>
internal sealed class TabStrip : Panel
{
    internal const int StripHeight = 36;

    private const int BadgeWidth = 3;
    private const int DragThreshold = 6;

    private readonly List<TabBounds> _layout = [];

    private IReadOnlyList<TabSlot> _slots = [];

    private TabSession? _session;
    private Guid? _hoverTabId;
    private bool _hoverClose;
    private bool _hoverNew;
    private bool _hoverContainers;

    private Guid? _dragTabId;
    private Point _dragOrigin;
    private bool _dragging;

    internal TabStrip()
    {
        Dock = DockStyle.Top;
        Height = StripHeight;
        BackColor = Theme.StripBackground;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }

    internal event EventHandler<Guid>? TabSelected;

    internal event EventHandler<Guid>? TabCloseRequested;

    /// <summary>Clique no "+": nova aba no container padrao.</summary>
    internal event EventHandler? NewTabRequested;

    /// <summary>Clique no "v": abrir o menu de containers na posicao informada.</summary>
    internal event EventHandler<Point>? ContainerMenuRequested;

    internal event EventHandler<(int From, int To)>? TabMoved;

    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal TabSession? Session
    {
        get => _session;
        set
        {
            _session = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.StripBackground);

        RebuildLayout();

        if (_session is null)
        {
            return;
        }

        foreach (var bounds in _layout)
        {
            DrawTab(g, _session, bounds);
        }

        DrawStripButton(g, NewTabBounds, "+", _hoverNew);
        DrawStripButton(g, ContainerMenuBounds, "▾", _hoverContainers);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_dragTabId is { } draggingId && !_dragging &&
            Math.Abs(e.X - _dragOrigin.X) > DragThreshold)
        {
            _dragging = true;
            _ = draggingId;
        }

        if (_dragging)
        {
            UpdateDrag(e.Location);
            return;
        }

        var hit = HitTest(e.Location);
        var changed =
            _hoverTabId != hit.TabId ||
            _hoverClose != hit.OnClose ||
            _hoverNew != hit.OnNewTab ||
            _hoverContainers != hit.OnContainerMenu;

        _hoverTabId = hit.TabId;
        _hoverClose = hit.OnClose;
        _hoverNew = hit.OnNewTab;
        _hoverContainers = hit.OnContainerMenu;

        if (changed)
        {
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);

        _hoverTabId = null;
        _hoverClose = false;
        _hoverNew = false;
        _hoverContainers = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        var hit = HitTest(e.Location);

        // Botao do meio fecha a aba, como em qualquer navegador.
        if (e.Button == MouseButtons.Middle && hit.TabId is { } middleId)
        {
            TabCloseRequested?.Invoke(this, middleId);
            return;
        }

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (hit.OnNewTab)
        {
            NewTabRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (hit.OnContainerMenu)
        {
            ContainerMenuRequested?.Invoke(this, new Point(ContainerMenuBounds.Left, Height));
            return;
        }

        if (hit.TabId is not { } id)
        {
            return;
        }

        if (hit.OnClose)
        {
            TabCloseRequested?.Invoke(this, id);
            return;
        }

        TabSelected?.Invoke(this, id);
        _dragTabId = id;
        _dragOrigin = e.Location;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        _dragTabId = null;
        _dragging = false;
    }

    private Rectangle NewTabBounds => new(
        TabStripLayout.NewTabLeft(_slots),
        (Height - 24) / 2,
        TabStripLayout.NewTabWidth,
        24);

    private Rectangle ContainerMenuBounds => new(
        NewTabBounds.Right + 1,
        NewTabBounds.Top,
        TabStripLayout.ContainerMenuWidth,
        24);

    /// <summary>
    /// Posicoes vem de <see cref="TabStripLayout"/>, no nucleo: a conta e a
    /// mesma que os testes exercitam, nao uma copia paralela.
    /// </summary>
    private void RebuildLayout()
    {
        _layout.Clear();
        _slots = _session is null ? [] : TabStripLayout.Compute(Width, _session.Tabs.Count);

        for (var i = 0; i < _slots.Count; i++)
        {
            _layout.Add(new TabBounds(
                _session!.Tabs[i].Id,
                i,
                new Rectangle(_slots[i].Left, 4, _slots[i].Width, Height - 4)));
        }
    }

    private void DrawTab(Graphics g, TabSession session, TabBounds item)
    {
        var tab = session.Find(item.TabId);
        if (tab is null)
        {
            return;
        }

        var isActive = session.ActiveIndex == item.Index;
        var isHover = _hoverTabId == item.TabId;
        var bounds = item.Bounds;

        // Ativa com a cor da toolbar: as duas se fundem e a aba parece erguida.
        var background = isActive
            ? Theme.ToolbarBackground
            : isHover
                ? Theme.TabHover
                : Theme.TabInactive;

        using (var path = RoundedTop(bounds, 6))
        using (var brush = new SolidBrush(background))
        {
            g.FillPath(brush, path);
        }

        // Selo do container: barra vertical na borda esquerda da aba.
        var badge = new Rectangle(bounds.Left, bounds.Top + 6, BadgeWidth, bounds.Height - 10);
        using (var badgeBrush = new SolidBrush(Color.FromArgb(tab.Container.Color)))
        {
            g.FillRectangle(badgeBrush, badge);
        }

        var showClose = isActive || isHover;
        var textLeft = bounds.Left + BadgeWidth + 8;
        var textRight = bounds.Right - 8 - (showClose ? TabStripLayout.CloseSize : 0);
        var textBounds = Rectangle.FromLTRB(textLeft, bounds.Top, Math.Max(textLeft, textRight), bounds.Bottom);

        var label = tab.Title is { Length: > 0 } title ? title : ShortenUrl(tab.Url);
        if (tab.Pinned)
        {
            label = "• " + label;
        }

        TextRenderer.DrawText(
            g,
            label,
            Font,
            textBounds,
            isActive ? Theme.Foreground : Theme.ForegroundDim,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix);

        if (!showClose)
        {
            return;
        }

        var close = CloseBounds(bounds);
        if (_hoverTabId == item.TabId && _hoverClose)
        {
            using var hoverBrush = new SolidBrush(Theme.CloseHover);
            g.FillEllipse(hoverBrush, close);
        }

        using var pen = new Pen(isActive ? Theme.Foreground : Theme.ForegroundDim, 1.4f);
        var pad = 5;
        g.DrawLine(pen, close.Left + pad, close.Top + pad, close.Right - pad, close.Bottom - pad);
        g.DrawLine(pen, close.Right - pad, close.Top + pad, close.Left + pad, close.Bottom - pad);
    }

    private void DrawStripButton(Graphics g, Rectangle bounds, string glyph, bool hover)
    {
        if (hover)
        {
            using var brush = new SolidBrush(Theme.TabHover);
            using var path = RoundedTop(bounds, 4);
            g.FillPath(brush, path);
        }

        TextRenderer.DrawText(
            g,
            glyph,
            Font,
            bounds,
            hover ? Theme.Foreground : Theme.ForegroundDim,
            TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
    }

    private void UpdateDrag(Point location)
    {
        if (_session is null || _dragTabId is not { } id)
        {
            return;
        }

        var from = _layout.FindIndex(item => item.TabId == id);
        var target = TabStripLayout.DropIndex(_slots, location.X);

        if (from >= 0 && from != target)
        {
            TabMoved?.Invoke(this, (from, target));
            Invalidate();
        }
    }

    private static Rectangle CloseBounds(Rectangle tab)
    {
        var slot = new TabSlot(0, tab.Left, tab.Width);
        var (left, size) = TabStripLayout.CloseBounds(slot);

        return new Rectangle(left, tab.Top + ((tab.Height - size) / 2), size, size);
    }

    private TabHit HitTest(Point location)
    {
        var hoverIndex = _hoverTabId is { } id
            ? _layout.FindIndex(item => item.TabId == id)
            : -1;

        var hit = TabStripLayout.HitTest(
            _slots,
            location.X,
            _session?.ActiveIndex ?? -1,
            hoverIndex);

        if (hit.OnNewTab)
        {
            return new TabHit(null, false, true, false);
        }

        if (hit.OnContainerMenu)
        {
            return new TabHit(null, false, false, true);
        }

        if (hit.TabIndex < 0 || hit.TabIndex >= _layout.Count)
        {
            return new TabHit(null, false, false, false);
        }

        return new TabHit(_layout[hit.TabIndex].TabId, hit.OnClose, false, false);
    }

    /// <summary>Retangulo com os dois cantos de cima arredondados.</summary>
    private static GraphicsPath RoundedTop(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;

        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddLine(bounds.Right, bounds.Bottom, bounds.Left, bounds.Bottom);
        path.CloseFigure();

        return path;
    }

    /// <summary>Rotulo de aba que ainda nao tem titulo: host sem esquema.</summary>
    private static string ShortenUrl(string url)
    {
        if (url is "about:blank")
        {
            return "Nova aba";
        }

        return Uri.TryCreate(url, UriKind.Absolute, out var parsed) ? parsed.Host : url;
    }

    private readonly record struct TabBounds(Guid TabId, int Index, Rectangle Bounds);

    private readonly record struct TabHit(Guid? TabId, bool OnClose, bool OnNewTab, bool OnContainerMenu);
}
