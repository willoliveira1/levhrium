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
    internal const int StripHeight = 40;

    private const int BadgeHeight = 3;
    private const int TabRadius = 9;
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

        DrawStripButton(g, NewTabBounds, Glyphs.Add, _hoverNew);
        DrawStripButton(g, ContainerMenuBounds, Glyphs.ChevronDown, _hoverContainers);
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
        Height - 33,
        TabStripLayout.NewTabWidth,
        29);

    private Rectangle ContainerMenuBounds => new(
        NewTabBounds.Right + 1,
        NewTabBounds.Top,
        TabStripLayout.ContainerMenuWidth,
        29);

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

        // A ativa e mais alta e usa a cor da toolbar: as duas se fundem e a aba
        // parece continuar na barra de baixo.
        var bounds = isActive
            ? item.Bounds
            : new Rectangle(item.Bounds.Left, item.Bounds.Top + 4, item.Bounds.Width, item.Bounds.Height - 4);

        var background = isActive
            ? Theme.ToolbarBackground
            : isHover
                ? Theme.TabHover
                : Theme.TabInactive;

        using (var path = Theme.RoundedTop(bounds, TabRadius))
        using (var brush = new SolidBrush(background))
        {
            g.FillPath(brush, path);
        }

        // Selo do container: barra no topo, atravessando a largura. Com duas
        // contas do mesmo site os titulos sao iguais e o selo e a unica pista,
        // entao vale a maior area colorida possivel.
        using (var badgePath = Theme.RoundedTop(
            new Rectangle(bounds.Left, bounds.Top, bounds.Width, TabRadius * 2), TabRadius))
        using (var badgeBrush = new SolidBrush(Color.FromArgb(tab.Container.Color)))
        {
            var clip = g.Clip;
            g.SetClip(new Rectangle(bounds.Left, bounds.Top, bounds.Width, BadgeHeight));
            g.FillPath(badgeBrush, badgePath);
            g.Clip = clip;
        }

        var showClose = isActive || isHover;
        var textLeft = bounds.Left + 12;
        var textRight = bounds.Right - 8 - (showClose ? TabStripLayout.CloseSize : 0);
        var textBounds = Rectangle.FromLTRB(textLeft, bounds.Top, Math.Max(textLeft, textRight), bounds.Bottom);

        var label = tab.Title is { Length: > 0 } title ? title : ShortenUrl(tab.Url);
        if (tab.Pinned)
        {
            // Alfinete em vez de um ponto: o ponto nao dizia o que significava.
            using var pinFont = new Font(Theme.IconFontName, 7.5f);

            TextRenderer.DrawText(
                g,
                Glyphs.Pin,
                pinFont,
                new Rectangle(textBounds.Left, textBounds.Top, 14, textBounds.Height),
                Theme.ForegroundDim,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);

            textBounds = Rectangle.FromLTRB(
                textBounds.Left + 16, textBounds.Top, textBounds.Right, textBounds.Bottom);
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
        var closeHovered = _hoverTabId == item.TabId && _hoverClose;

        using (var closeBrush = new SolidBrush(closeHovered ? Theme.CloseHover : Theme.CloseBackground))
        {
            g.FillEllipse(closeBrush, close);
        }

        using var closeFont = new Font(Theme.IconFontName, 6.5f);

        TextRenderer.DrawText(
            g,
            Glyphs.Close,
            closeFont,
            close,
            closeHovered ? Theme.Foreground : Theme.ForegroundDim,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    private static void DrawStripButton(Graphics g, Rectangle bounds, string glyph, bool hover)
    {
        var side = Math.Min(bounds.Width, bounds.Height);
        var circle = new Rectangle(bounds.Left, bounds.Top, side, side);

        if (hover)
        {
            using var brush = new SolidBrush(Theme.ButtonHover);
            g.FillEllipse(brush, circle);
        }

        using var iconFont = new Font(Theme.IconFontName, 8.5f);

        TextRenderer.DrawText(
            g,
            glyph,
            iconFont,
            circle,
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
