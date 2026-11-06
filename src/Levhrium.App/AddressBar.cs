using System.Drawing.Drawing2D;

namespace Levhrium.App;

/// <summary>
/// A pilula de endereço: cadeado, o endereço, o contador de bloqueios e a
/// estrela de favorito, tudo dentro de uma borda só.
///
/// O endereço aparece com o domínio em negrito e o caminho apagado — o que
/// importa para saber onde você está é o domínio, não o resto da URL.
/// Enquanto o campo tem foco, mostra a URL inteira, para dar para editar.
/// </summary>
internal sealed class AddressBar : Control
{
    private const int Radius = 16;
    private const int IconWidth = 26;

    private readonly TextBox _input;
    private readonly IconButton _bookmark;

    private string _domain = string.Empty;
    private string _path = string.Empty;
    private int _blocked;
    private bool _secure;
    private bool _editing;

    internal AddressBar()
    {
        BackColor = Theme.ToolbarBackground;
        ForeColor = Theme.Foreground;

        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw,
            true);

        _input = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.FieldBackground,
            ForeColor = Theme.Foreground,
            Font = new Font("Segoe UI", 10f),
        };

        _input.Enter += (_, _) =>
        {
            _editing = true;
            _input.Visible = true;
            _input.SelectAll();
            Invalidate();
        };

        _input.Leave += (_, _) =>
        {
            _editing = false;
            _input.Visible = false;
            Invalidate();
        };

        _input.KeyDown += (s, e) => Submitted?.Invoke(this, e);

        _bookmark = new IconButton(Glyphs.Star, "Favoritar (Ctrl+D)", 24)
        {
            ForeColor = Theme.ForegroundDim,
            Font = new Font(Theme.IconFontName, 9f),

            // Sobre a pilula, nao sobre a toolbar: sem isto a estrela fica
            // dentro de um quadrado mais claro.
            Backdrop = Theme.FieldBackground,
        };
        _bookmark.Click += (_, _) => BookmarkClicked?.Invoke(this, EventArgs.Empty);

        Controls.Add(_input);
        Controls.Add(_bookmark);

        _input.Visible = false;

        // A altura fica por ultimo: atribui-la dispara OnResize, que posiciona
        // os filhos -- e no inicio do construtor eles ainda nao existem.
        Height = 32;
    }

    /// <summary>Tecla pressionada no campo; o formulario trata o Enter.</summary>
    internal event KeyEventHandler? Submitted;

    internal event EventHandler? BookmarkClicked;

    /// <summary>Texto cru do campo, que é o que a automação lê.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal string Address
    {
        get => _input.Text;
        set
        {
            if (_input.Text == value)
            {
                return;
            }

            _input.Text = value;
            SplitUrl(value);
            Invalidate();
        }
    }

    internal bool UserIsTyping => _editing && _input.Modified;

    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal int BlockedRequests
    {
        get => _blocked;
        set
        {
            if (_blocked == value)
            {
                return;
            }

            _blocked = value;
            LayoutChildren();
            Invalidate();
        }
    }

    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool IsBookmarked
    {
        set
        {
            _bookmark.Glyph = value ? Glyphs.StarFilled : Glyphs.Star;
            _bookmark.ForeColor = value ? Theme.Accent : Theme.ForegroundDim;
            _bookmark.Invalidate();
        }
    }

    internal void FocusInput()
    {
        _input.Visible = true;
        _input.Focus();
        _input.SelectAll();
    }

    internal void ClearModified() => _input.Modified = false;

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutChildren();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.ToolbarBackground);

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);

        using (var path = Theme.RoundedRect(bounds, Radius))
        using (var fill = new SolidBrush(Theme.FieldBackground))
        using (var border = new Pen(_editing ? Theme.Accent : Theme.FieldBorder, _editing ? 1.4f : 1f))
        {
            g.FillPath(fill, path);
            g.DrawPath(border, path);
        }

        using (var iconFont = new Font(Theme.IconFontName, 9f))
        {
            TextRenderer.DrawText(
                g,
                _secure ? Glyphs.Lock : string.Empty,
                iconFont,
                new Rectangle(4, 0, IconWidth, Height),
                Theme.Shield,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        if (!_editing)
        {
            DrawSplitAddress(g);
        }

        DrawBlockedPill(g);
    }

    /// <summary>Domínio em negrito, caminho apagado.</summary>
    private void DrawSplitAddress(Graphics g)
    {
        var left = IconWidth + 4;
        var available = BookmarkLeft - 8 - left;

        if (available <= 0)
        {
            return;
        }

        using var strong = new Font("Segoe UI", 10f, FontStyle.Bold);
        using var weak = new Font("Segoe UI", 10f);

        const TextFormatFlags Flags =
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        var domainSize = TextRenderer.MeasureText(g, _domain, strong, new Size(available, Height), Flags);

        TextRenderer.DrawText(
            g,
            _domain,
            strong,
            new Rectangle(left, 0, Math.Min(domainSize.Width, available), Height),
            Theme.Foreground,
            Flags);

        var pathLeft = left + domainSize.Width;
        var pathWidth = available - domainSize.Width;

        if (pathWidth > 12 && _path.Length > 0)
        {
            TextRenderer.DrawText(
                g,
                _path,
                weak,
                new Rectangle(pathLeft, 0, pathWidth, Height),
                Theme.ForegroundDim,
                Flags | TextFormatFlags.EndEllipsis);
        }
    }

    private void DrawBlockedPill(Graphics g)
    {
        if (_blocked <= 0)
        {
            return;
        }

        var width = BlockedPillWidth;
        var bounds = new Rectangle(BookmarkLeft - width - 4, (Height - 20) / 2, width, 20);

        using (var path = Theme.RoundedRect(bounds, 10))
        using (var fill = new SolidBrush(Theme.ShieldBackground))
        {
            g.FillPath(fill, path);
        }

        using var iconFont = new Font(Theme.IconFontName, 8f);
        using var textFont = new Font("Segoe UI", 8.5f);

        TextRenderer.DrawText(
            g,
            Glyphs.Shield,
            iconFont,
            new Rectangle(bounds.Left + 4, bounds.Top, 14, bounds.Height),
            Theme.Shield,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        TextRenderer.DrawText(
            g,
            _blocked.ToString(System.Globalization.CultureInfo.InvariantCulture),
            textFont,
            new Rectangle(bounds.Left + 17, bounds.Top, bounds.Width - 20, bounds.Height),
            Theme.Shield,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    private int BlockedPillWidth =>
        _blocked <= 0 ? 0 : 24 + (_blocked.ToString(System.Globalization.CultureInfo.InvariantCulture).Length * 7);

    private int BookmarkLeft => Width - 28;

    private void LayoutChildren()
    {
        _bookmark.Location = new Point(BookmarkLeft, (Height - _bookmark.Height) / 2);

        var left = IconWidth + 4;
        var right = BookmarkLeft - BlockedPillWidth - 8;

        _input.Location = new Point(left, (Height - _input.Height) / 2);
        _input.Width = Math.Max(40, right - left);
    }

    private void SplitUrl(string url)
    {
        _secure = url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Host.Length == 0)
        {
            _domain = url;
            _path = string.Empty;
            return;
        }

        _domain = parsed.Host;
        _path = parsed.PathAndQuery is "/" ? string.Empty : parsed.PathAndQuery;
    }
}
