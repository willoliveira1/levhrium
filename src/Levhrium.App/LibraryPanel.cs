using System.Drawing.Drawing2D;
using Levhrium.Core;

namespace Levhrium.App;

/// <summary>Qual lista o painel esta mostrando.</summary>
internal enum LibrarySection
{
    Bookmarks,
    History,
    Downloads,
}

/// <summary>
/// Painel de favoritos, historico e downloads.
///
/// Era um menu do Windows: lista chapada, sem busca e sem dizer de onde veio
/// cada item. Dois favoritos chamados "Inicio" ficavam indistinguiveis, e achar
/// algo no historico so dava navegando a lista inteira.
///
/// Agora e um painel proprio, com as tres listas em abas, busca que filtra
/// enquanto se digita e o site embaixo de cada titulo. E uma janela sem moldura
/// e nao um menu porque um ContextMenuStrip nao aceita campo de texto dentro.
/// </summary>
internal sealed class LibraryPanel : Form
{
    private const int Radius = 12;
    private const int PanelWidth = 380;
    private const int PanelHeight = 460;
    private const int Edge = 8;
    private const int RowHeight = 42;

    private readonly Func<LibrarySection, IReadOnlyList<LibraryEntry>> _load;
    private readonly SectionTab[] _tabs;
    private readonly TextBox _search;
    private readonly Panel _list;

    private LibrarySection _section;

    internal LibraryPanel(
        LibrarySection section,
        Func<LibrarySection, IReadOnlyList<LibraryEntry>> load)
    {
        _load = load;
        _section = section;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(PanelWidth, PanelHeight);
        BackColor = Theme.CardBackground;
        ForeColor = Theme.Foreground;
        Font = new Font("Segoe UI", 9.5f);
        DoubleBuffered = true;

        _tabs =
        [
            new SectionTab("Favoritos", LibrarySection.Bookmarks),
            new SectionTab("Histórico", LibrarySection.History),
            new SectionTab("Downloads", LibrarySection.Downloads),
        ];

        var largura = (PanelWidth - (Edge * 2) - 4) / 3;

        for (var i = 0; i < _tabs.Length; i++)
        {
            var tab = _tabs[i];
            tab.Location = new Point(Edge + (i * (largura + 2)), Edge);
            tab.Size = new Size(largura, 30);
            tab.Active = tab.Section == _section;

            var alvo = tab.Section;
            tab.Click += (_, _) => Select(alvo);

            Controls.Add(tab);
        }

        Controls.Add(new Divider
        {
            Location = new Point(0, Edge + 30),
            Width = PanelWidth,
        });

        var campo = new SunkenRow
        {
            Location = new Point(Edge, Edge + 39),
            Size = new Size(PanelWidth - (Edge * 2), 30),
        };

        _search = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.RowBackground,
            ForeColor = Theme.Foreground,
            Font = new Font("Segoe UI", 9.5f),
            PlaceholderText = "Buscar...",
            Location = new Point(11, 7),
            Width = campo.Width - 22,
        };

        _search.TextChanged += (_, _) => Refill();
        campo.Controls.Add(_search);
        Controls.Add(campo);

        _list = new Panel
        {
            Location = new Point(Edge, Edge + 77),
            Size = new Size(PanelWidth - (Edge * 2), PanelHeight - Edge - 77 - 44),
            AutoScroll = true,
            BackColor = Theme.CardBackground,
        };
        Controls.Add(_list);

        Controls.Add(new Divider
        {
            Location = new Point(Edge, PanelHeight - 42),
            Width = PanelWidth - (Edge * 2),
        });

        var settings = new LibraryRow(
            new LibraryEntry { Title = "Configurações", Url = string.Empty },
            Glyphs.Menu)
        {
            Location = new Point(Edge, PanelHeight - 37),
            Size = new Size(PanelWidth - (Edge * 2), 32),
        };

        settings.Click += (_, _) =>
        {
            Close();
            SettingsRequested?.Invoke(this, EventArgs.Empty);
        };

        Controls.Add(settings);

        Refill();
    }

    /// <summary>Item escolhido; o endereco abre numa aba nova.</summary>
    internal event EventHandler<string>? ItemChosen;

    internal event EventHandler? SettingsRequested;

    protected override bool ShowWithoutActivation => false;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _search.Focus();
    }

    /// <summary>Clicar fora fecha, como um menu faria.</summary>
    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Close();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        using var path = Theme.RoundedRect(new Rectangle(0, 0, Width, Height), Radius);
        Region = new Region(path);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var path = Theme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), Radius);
        using var pen = new Pen(Theme.CardBorder);

        g.DrawPath(pen, path);
    }

    private void Select(LibrarySection section)
    {
        _section = section;

        foreach (var tab in _tabs)
        {
            tab.Active = tab.Section == section;
            tab.Invalidate();
        }

        Refill();
    }

    private void Refill()
    {
        _list.SuspendLayout();

        foreach (Control velho in _list.Controls)
        {
            velho.Dispose();
        }

        _list.Controls.Clear();

        var entries = LibrarySearch.Filter(_load(_section), _search.Text);

        if (entries.Count == 0)
        {
            _list.Controls.Add(new Label
            {
                Text = _search.TextLength > 0 ? "Nada encontrado." : "Nada aqui ainda.",
                AutoSize = true,
                Location = new Point(12, 12),
                ForeColor = Theme.ForegroundFaint,
                BackColor = Theme.CardBackground,
            });

            _list.ResumeLayout();
            return;
        }

        var topo = 0;

        foreach (var entry in entries)
        {
            var row = new LibraryRow(entry)
            {
                Location = new Point(0, topo),
                Size = new Size(_list.ClientSize.Width - 2, RowHeight),
            };

            var url = entry.Url;
            row.Click += (_, _) =>
            {
                Close();
                ItemChosen?.Invoke(this, url);
            };

            _list.Controls.Add(row);
            topo += RowHeight;
        }

        _list.ResumeLayout();
    }

    /// <summary>Aba do painel: ativa fica com fundo e texto cheio.</summary>
    private sealed class SectionTab : Control
    {
        private bool _hover;

        internal SectionTab(string text, LibrarySection section)
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint,
                true);

            Text = text;
            Section = section;
            TabStop = false;
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        }

        internal LibrarySection Section { get; }

        [System.ComponentModel.DesignerSerializationVisibility(
            System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal bool Active { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.CardBackground);

            if (Active || _hover)
            {
                using var path = Theme.RoundedTop(new Rectangle(0, 0, Width, Height), 7);
                using var brush = new SolidBrush(Active ? Theme.ToolbarBackground : Theme.TabHover);
                g.FillPath(brush, path);
            }

            TextRenderer.DrawText(
                g,
                Text,
                Font,
                ClientRectangle,
                Active ? Theme.Foreground : Theme.CardSubtitle,
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
            Invalidate();
        }
    }

    /// <summary>
    /// Uma linha da lista: selo com a letra do site, titulo e, embaixo, o
    /// dominio. O dominio e o que distingue dois itens de mesmo nome.
    /// </summary>
    private sealed class LibraryRow : Control
    {
        private readonly LibraryEntry _entry;
        private readonly string? _glyph;

        private bool _hover;

        internal LibraryRow(LibraryEntry entry, string? glyph = null)
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint,
                true);

            _entry = entry;
            _glyph = glyph;
            TabStop = false;
            Cursor = Cursors.Hand;

            if (entry.Url is { Length: > 0 })
            {
                new ToolTip().SetToolTip(this, entry.Url);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.CardBackground);

            if (_hover)
            {
                using var path = Theme.RoundedRect(new Rectangle(0, 1, Width - 1, Height - 3), 8);
                using var brush = new SolidBrush(Theme.ToolbarBackground);
                g.FillPath(brush, path);
            }

            var temHost = _glyph is null && _entry.Url is { Length: > 0 };
            var meio = Height / 2;

            DrawBadge(g, meio);

            // O horario sai primeiro: e ele que define onde o titulo precisa
            // ser cortado.
            var reservado = 10;

            if (_entry.VisitedAt is { } quando)
            {
                using var fonteHora = new Font("Segoe UI", 8f);

                var texto = RelativeTime.Describe(quando, DateTimeOffset.UtcNow);
                var largura = TextRenderer.MeasureText(g, texto, fonteHora).Width + 4;

                TextRenderer.DrawText(
                    g,
                    texto,
                    fonteHora,
                    new Rectangle(Width - largura - 10, 0, largura, Height),
                    Theme.ForegroundFaint,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

                reservado += largura;
            }

            using var titulo = new Font("Segoe UI", 9.5f);

            TextRenderer.DrawText(
                g,
                _entry.Title,
                titulo,
                new Rectangle(42, temHost ? 5 : 0, Width - 42 - reservado, temHost ? 16 : Height),
                Theme.Foreground,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);

            if (!temHost)
            {
                return;
            }

            using var host = new Font("Segoe UI", 8f);

            TextRenderer.DrawText(
                g,
                _entry.Host,
                host,
                new Rectangle(42, 21, Width - 52, 14),
                Theme.CardSubtitle,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
        }

        private void DrawBadge(Graphics g, int meio)
        {
            var caixa = new Rectangle(10, meio - 11, 22, 22);

            using (var path = Theme.RoundedRect(caixa, 6))
            using (var brush = new SolidBrush(Theme.ButtonHover))
            {
                g.FillPath(brush, path);
            }

            if (_glyph is { } glifo)
            {
                using var fonte = new Font(Theme.IconFontName, 9f);

                TextRenderer.DrawText(
                    g, glifo, fonte, caixa, Theme.ForegroundDim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPrefix);

                return;
            }

            using var letra = new Font("Segoe UI", 8.5f, FontStyle.Bold);

            TextRenderer.DrawText(
                g, _entry.Badge, letra, caixa, Theme.ForegroundDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix);
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
            Invalidate();
        }
    }
}
