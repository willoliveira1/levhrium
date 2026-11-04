using Levhrium.Core;

namespace Levhrium.App;

/// <summary>
/// Janela principal. Barra de titulo nativa do Windows (decisao da rodada 4);
/// a faixa de abas desenhada a mao entra no M2, abaixo da toolbar.
/// </summary>
internal sealed class MainForm : Form, IAutomationTarget
{
    private const int ToolbarHeight = 44;
    private const string HomeUrl = "about:blank";

    private readonly LaunchOptions _options;
    private readonly WebViewSettings _webViewSettings;
    private readonly SearchEngine _searchEngine = SearchEngine.Default;

    private readonly Panel _toolbar;
    private readonly Button _back;
    private readonly Button _forward;
    private readonly Button _reload;
    private readonly TextBox _address;
    private readonly Panel _content;

    private readonly TaskCompletionSource _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private BrowserTab? _tab;

    /// <summary>Verdadeiro enquanto a toolbar esta sendo atualizada pelo codigo.</summary>
    private bool _suppressAddressSync;

    internal MainForm(LaunchOptions options, AppPaths paths)
    {
        _options = options;
        _webViewSettings = new WebViewSettings(options, paths);

        Text = AppPaths.ProductName;
        BackColor = Theme.WindowBackground;
        ForeColor = Theme.Foreground;
        MinimumSize = new Size(640, 420);
        ClientSize = new Size(1180, 760);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        _back = CreateToolbarButton("←", "Voltar (Alt+Seta esquerda)");
        _forward = CreateToolbarButton("→", "Avancar (Alt+Seta direita)");
        _reload = CreateToolbarButton("↻", "Recarregar (F5)");

        _address = new TextBox
        {
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.FieldBackground,
            ForeColor = Theme.Foreground,
            Font = new Font("Segoe UI", 10f),
            Height = 26,
        };

        _toolbar = new Panel
        {
            Dock = DockStyle.Top,
            Height = ToolbarHeight,
            BackColor = Theme.ToolbarBackground,
            Padding = new Padding(8),
        };

        _content = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.WindowBackground,
        };

        _toolbar.Controls.Add(_back);
        _toolbar.Controls.Add(_forward);
        _toolbar.Controls.Add(_reload);
        _toolbar.Controls.Add(_address);
        _toolbar.Resize += (_, _) => LayoutToolbar();

        Controls.Add(_content);
        Controls.Add(_toolbar);

        _back.Click += (_, _) => _tab?.GoBack();
        _forward.Click += (_, _) => _tab?.GoForward();
        _reload.Click += (_, _) => _tab?.Reload();
        _address.KeyDown += OnAddressKeyDown;
        _address.Enter += (_, _) => BeginInvoke(_address.SelectAll);
    }

    /// <summary>Completa quando a primeira aba esta pronta para navegar.</summary>
    internal Task Ready => _ready.Task;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeTheme.ApplyDarkTitleBar(Handle);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        LayoutToolbar();
        _ = InitializeFirstTabAsync();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.L:
                _address.Focus();
                _address.SelectAll();
                return true;

            case Keys.F5:
            case Keys.Control | Keys.R:
                _tab?.Reload();
                return true;

            case Keys.Alt | Keys.Left:
                _tab?.GoBack();
                return true;

            case Keys.Alt | Keys.Right:
                _tab?.GoForward();
                return true;

            default:
                return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    private async Task InitializeFirstTabAsync()
    {
        var tab = new BrowserTab(
            WebViewSettings.DefaultContainer,
            _webViewSettings.For(WebViewSettings.DefaultContainer));

        tab.Changed += (_, _) => SyncToolbar();
        _tab = tab;
        _content.Controls.Add(tab.View);

        try
        {
            await tab.EnsureInitializedAsync();
        }
        catch (Exception ex)
        {
            _ready.TrySetException(ex);
            MessageBox.Show(
                this,
                "Nao foi possivel iniciar o WebView2." + Environment.NewLine + Environment.NewLine + ex.Message,
                AppPaths.ProductName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        var startUrl = _options.InitialUrl is { Length: > 0 } requested
            ? UrlResolver.Resolve(requested, _searchEngine)
            : new Uri(HomeUrl);

        tab.Navigate(startUrl);
        SyncToolbar();

        // Foco na pagina, nao na barra: abrir o jogo e ja poder digitar o login.
        tab.View.Focus();

        _ready.TrySetResult();
    }

    private Button CreateToolbarButton(string glyph, string tooltip)
    {
        var button = new Button
        {
            Text = glyph,
            Width = 32,
            Height = 28,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.ToolbarBackground,
            ForeColor = Theme.Foreground,
            Font = new Font("Segoe UI", 11f),
            TabStop = false,
        };

        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Theme.FieldBackground;
        new ToolTip().SetToolTip(button, tooltip);
        return button;
    }

    /// <summary>Posiciona a toolbar a mao: a barra de endereco ocupa o que sobra.</summary>
    private void LayoutToolbar()
    {
        const int gap = 4;
        var top = (ToolbarHeight - _back.Height) / 2;
        var x = _toolbar.Padding.Left;

        foreach (var button in new[] { _back, _forward, _reload })
        {
            button.Location = new Point(x, top);
            x += button.Width + gap;
        }

        x += gap;
        _address.Location = new Point(x, (ToolbarHeight - _address.Height) / 2);
        _address.Width = Math.Max(120, _toolbar.ClientSize.Width - x - _toolbar.Padding.Right);
    }

    private void OnAddressKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
        {
            return;
        }

        e.Handled = true;
        e.SuppressKeyPress = true;

        var target = UrlResolver.Resolve(_address.Text, _searchEngine);
        _tab?.Navigate(target);
        _content.Focus();
    }

    private void SyncToolbar()
    {
        if (_tab is null || IsDisposed)
        {
            return;
        }

        _back.Enabled = _tab.CanGoBack;
        _forward.Enabled = _tab.CanGoForward;
        _back.ForeColor = _tab.CanGoBack ? Theme.Foreground : Theme.ForegroundDim;
        _forward.ForeColor = _tab.CanGoForward ? Theme.Foreground : Theme.ForegroundDim;
        _reload.Text = _tab.IsLoading ? "×" : "↻";

        Text = _tab.Title is { Length: > 0 } title
            ? title + " — " + AppPaths.ProductName
            : AppPaths.ProductName;

        // Só não sobrescreve se o usuario estiver de fato digitando. Checar apenas
        // o foco deixava a barra em branco na abertura, porque ela nasce focada.
        if (_suppressAddressSync || (_address.Focused && _address.Modified))
        {
            return;
        }

        _suppressAddressSync = true;
        _address.Text = _tab.Url is HomeUrl ? string.Empty : _tab.Url;
        _address.Modified = false;
        _suppressAddressSync = false;
    }

    // ---- IAutomationTarget: chamado da thread do named pipe, so em --test-mode ----

    public Task<AutomationState> GetStateAsync() => OnUiAsync(() => new AutomationState
    {
        Tabs = _tab is null ? [] : [_tab.ToAutomationTab()],
        ActiveTabIndex = _tab is null ? -1 : 0,
        AddressBarText = _address.Text,
    });

    public Task NavigateAsync(string input) => OnUiAction(() =>
        _tab?.Navigate(UrlResolver.Resolve(input, _searchEngine)));

    public Task GoBackAsync() => OnUiAction(() => _tab?.GoBack());

    public Task GoForwardAsync() => OnUiAction(() => _tab?.GoForward());

    public Task ReloadAsync() => OnUiAction(() => _tab?.Reload());

    /// <summary>Executa na thread da UI e devolve o resultado para quem chamou.</summary>
    private Task<T> OnUiAsync<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (IsDisposed || !IsHandleCreated)
        {
            completion.SetException(new InvalidOperationException("A janela ainda nao esta pronta."));
            return completion.Task;
        }

        BeginInvoke(() =>
        {
            try
            {
                completion.TrySetResult(work());
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

        return completion.Task;
    }

    private Task OnUiAction(Action work) => OnUiAsync(() =>
    {
        work();
        return true;
    });
}
