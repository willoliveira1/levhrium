using Levhrium.Core;
using Microsoft.Web.WebView2.Core;

namespace Levhrium.App;

/// <summary>
/// Janela principal. Barra de titulo nativa do Windows (decisao da rodada 4);
/// abaixo dela a faixa de abas desenhada a mao, a toolbar e o conteudo.
/// </summary>
internal sealed class MainForm : Form, IAutomationTarget
{
    private const int ToolbarHeight = 44;
    private const string HomeUrl = "about:blank";

    private readonly LaunchOptions _options;
    private readonly WebViewSettings _webViewSettings;
    private readonly SearchEngine _searchEngine = SearchEngine.Default;
    private readonly TabSession _session;
    private readonly Dictionary<Guid, BrowserTab> _views = [];

    private readonly TabStrip _tabStrip;
    private readonly Panel _toolbar;
    private readonly Button _back;
    private readonly Button _forward;
    private readonly Button _reload;
    private readonly TextBox _address;
    private readonly Panel _content;

    private readonly TaskCompletionSource _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Verdadeiro enquanto a toolbar esta sendo atualizada pelo codigo.</summary>
    private bool _suppressAddressSync;

    internal MainForm(LaunchOptions options, AppPaths paths)
    {
        _options = options;
        _webViewSettings = new WebViewSettings(options, paths);
        _session = new TabSession(new ContainerRegistry());

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

        _tabStrip = new TabStrip { Session = _session };

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

        // Ordem importa: o Fill entra primeiro, os Top empilham por cima.
        Controls.Add(_content);
        Controls.Add(_toolbar);
        Controls.Add(_tabStrip);

        _back.Click += (_, _) => ActiveView?.GoBack();
        _forward.Click += (_, _) => ActiveView?.GoForward();
        _reload.Click += (_, _) => ActiveView?.Reload();
        _address.KeyDown += OnAddressKeyDown;
        _address.Enter += (_, _) => BeginInvoke(_address.SelectAll);

        _tabStrip.TabSelected += (_, id) => _session.Activate(id);
        _tabStrip.TabCloseRequested += (_, id) => CloseTab(id);
        _tabStrip.NewTabRequested += (_, _) => _ = OpenTabAsync(ContainerRegistry.DefaultName, HomeUrl);
        _tabStrip.ContainerMenuRequested += (_, point) => ShowContainerMenu(point);
        _tabStrip.TabMoved += (_, move) => _session.Move(move.From, move.To);

        _session.ActiveChanged += (_, _) => OnActiveTabChanged();
        _session.OrderChanged += (_, _) => _tabStrip.Invalidate();
    }

    /// <summary>Completa quando a primeira aba esta pronta para navegar.</summary>
    internal Task Ready => _ready.Task;

    private BrowserTab? ActiveView =>
        _session.Active is { } tab && _views.TryGetValue(tab.Id, out var view) ? view : null;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeTheme.ApplyDarkTitleBar(Handle);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        LayoutToolbar();
        _ = InitializeAsync();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.T:
                _ = OpenTabAsync(ContainerRegistry.DefaultName, HomeUrl);
                return true;

            case Keys.Control | Keys.W:
                CloseActiveTab();
                return true;

            case Keys.Control | Keys.Tab:
                _session.ActivateNext();
                return true;

            case Keys.Control | Keys.Shift | Keys.Tab:
                _session.ActivatePrevious();
                return true;

            case Keys.Control | Keys.Shift | Keys.T:
                _ = ReopenLastClosedAsync();
                return true;

            case Keys.Control | Keys.L:
                _address.Focus();
                _address.SelectAll();
                return true;

            case Keys.F5:
            case Keys.Control | Keys.R:
                ActiveView?.Reload();
                return true;

            case Keys.Alt | Keys.Left:
                ActiveView?.GoBack();
                return true;

            case Keys.Alt | Keys.Right:
                ActiveView?.GoForward();
                return true;

            default:
                return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    // ---- Ciclo de vida das abas ----

    private async Task InitializeAsync()
    {
        var startUrl = _options.InitialUrl is { Length: > 0 } requested
            ? UrlResolver.Resolve(requested, _searchEngine).ToString()
            : HomeUrl;

        try
        {
            await OpenTabAsync(ContainerRegistry.DefaultName, startUrl);
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

        _ready.TrySetResult();
    }

    private async Task<TabModel> OpenTabAsync(
        string containerName,
        string url,
        int? atIndex = null,
        bool activate = true)
    {
        var model = _session.Open(containerName, url, atIndex, activate);

        var view = new BrowserTab(model, _webViewSettings.For(model.Container));
        view.Changed += (_, _) => OnTabContentChanged(model.Id);

        _views[model.Id] = view;
        _content.Controls.Add(view.View);
        view.View.Visible = _session.Active?.Id == model.Id;

        await view.EnsureInitializedAsync();

        view.View.CoreWebView2.ContextMenuRequested += OnContextMenuRequested;
        view.Navigate(UrlResolver.Resolve(url, _searchEngine));

        if (_session.Active?.Id == model.Id)
        {
            view.View.Focus();
        }

        SyncToolbar();
        _tabStrip.Invalidate();
        return model;
    }

    /// <summary>
    /// Ctrl+W. Na ultima aba a janela fica em pe com uma aba em branco: fechar o
    /// navegador inteiro por um Ctrl+W a mais seria perder todas as sessoes.
    /// </summary>
    private void CloseActiveTab()
    {
        if (_session.Active is not { } active)
        {
            return;
        }

        if (_session.Tabs.Count == 1)
        {
            CloseTab(active.Id);
            _ = OpenTabAsync(ContainerRegistry.DefaultName, HomeUrl);
            return;
        }

        CloseTab(active.Id);
    }

    private void CloseTab(Guid id)
    {
        // Sincroniza o modelo com a view antes de fechar: a pilha de fechadas copia
        // do modelo, e o Ctrl+Shift+T precisa da URL que estava de fato aberta.
        OnTabContentChanged(id);

        if (_session.Close(id) is null)
        {
            return;
        }

        if (_views.Remove(id, out var view))
        {
            _content.Controls.Remove(view.View);
            view.Dispose();
        }

        OnActiveTabChanged();
        _tabStrip.Invalidate();
    }

    private async Task ReopenLastClosedAsync()
    {
        if (_session.ReopenLastClosed() is not { } restored)
        {
            return;
        }

        // O modelo ja voltou para a sessao; falta criar a view no mesmo container.
        var view = new BrowserTab(restored, _webViewSettings.For(restored.Container));
        view.Changed += (_, _) => OnTabContentChanged(restored.Id);

        _views[restored.Id] = view;
        _content.Controls.Add(view.View);
        view.View.Visible = _session.Active?.Id == restored.Id;

        await view.EnsureInitializedAsync();
        view.View.CoreWebView2.ContextMenuRequested += OnContextMenuRequested;
        view.Navigate(UrlResolver.Resolve(restored.Url, _searchEngine));

        OnActiveTabChanged();
    }

    private void OnActiveTabChanged()
    {
        foreach (var (id, view) in _views)
        {
            view.View.Visible = _session.Active?.Id == id;
        }

        ActiveView?.View.Focus();
        SyncToolbar();
        _tabStrip.Invalidate();
    }

    private void OnTabContentChanged(Guid id)
    {
        if (_session.Find(id) is not { } model || !_views.TryGetValue(id, out var view))
        {
            return;
        }

        // O WebView2 dispara Changed assim que inicializa, antes de navegar, e ali
        // Source ainda e nulo. Sem esta guarda o modelo perdia a URL de destino --
        // e o Ctrl+Shift+T reabria a aba em branco.
        if (view.Url is not "about:blank" || model.Url is "about:blank")
        {
            model.Url = view.Url;
        }

        model.Title = view.Title;

        _tabStrip.Invalidate();

        if (_session.Active?.Id == id)
        {
            SyncToolbar();
        }
    }

    // ---- Containers ----

    private void ShowContainerMenu(Point location)
    {
        var menu = new ContextMenuStrip
        {
            BackColor = Theme.ToolbarBackground,
            ForeColor = Theme.Foreground,
            ShowImageMargin = false,
        };

        foreach (var container in _session.Containers.All)
        {
            var item = new ToolStripMenuItem("Nova aba em " + container.Name)
            {
                ForeColor = Color.FromArgb(container.Color),
            };

            var name = container.Name;
            item.Click += (_, _) => _ = OpenTabAsync(name, HomeUrl);
            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());

        var suggestion = _session.Containers.SuggestName();
        var create = new ToolStripMenuItem($"Novo container ({suggestion})");
        create.Click += (_, _) => _ = OpenTabAsync(suggestion, HomeUrl);
        menu.Items.Add(create);

        menu.Show(_tabStrip, location);
    }

    /// <summary>
    /// Acrescenta "Abrir link em container" ao menu de contexto do WebView2. E o
    /// caminho natural para abrir a segunda conta: botao direito no link, container 2.
    /// </summary>
    private void OnContextMenuRequested(object? sender, CoreWebView2ContextMenuRequestedEventArgs e)
    {
        // Sem link no alvo nao ha o que abrir em outro container.
        if (sender is not CoreWebView2 core || e.ContextMenuTarget.LinkUri is not { Length: > 0 } link)
        {
            return;
        }

        var submenu = core.Environment.CreateContextMenuItem(
            "Abrir link em container",
            iconStream: null,
            CoreWebView2ContextMenuItemKind.Submenu);

        foreach (var container in _session.Containers.All)
        {
            var item = core.Environment.CreateContextMenuItem(
                container.Name,
                iconStream: null,
                CoreWebView2ContextMenuItemKind.Command);

            var name = container.Name;
            item.CustomItemSelected += (_, _) => BeginInvoke(() => _ = OpenTabAsync(name, link));
            submenu.Children.Add(item);
        }

        var novo = core.Environment.CreateContextMenuItem(
            "Novo container (" + _session.Containers.SuggestName() + ")",
            iconStream: null,
            CoreWebView2ContextMenuItemKind.Command);

        var suggestion = _session.Containers.SuggestName();
        novo.CustomItemSelected += (_, _) => BeginInvoke(() => _ = OpenTabAsync(suggestion, link));
        submenu.Children.Add(novo);

        e.MenuItems.Insert(0, submenu);
    }

    // ---- Toolbar ----

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

        ActiveView?.Navigate(UrlResolver.Resolve(_address.Text, _searchEngine));
        ActiveView?.View.Focus();
    }

    private void SyncToolbar()
    {
        if (IsDisposed)
        {
            return;
        }

        var view = ActiveView;

        _back.Enabled = view?.CanGoBack ?? false;
        _forward.Enabled = view?.CanGoForward ?? false;
        _back.ForeColor = _back.Enabled ? Theme.Foreground : Theme.ForegroundDim;
        _forward.ForeColor = _forward.Enabled ? Theme.Foreground : Theme.ForegroundDim;
        _reload.Text = view?.IsLoading == true ? "×" : "↻";

        var container = _session.Active?.Container;
        Text = _session.Active?.Title is { Length: > 0 } title
            ? $"{title} — {container?.Name} — {AppPaths.ProductName}"
            : AppPaths.ProductName;

        // Só não sobrescreve se o usuario estiver de fato digitando. Checar apenas
        // o foco deixava a barra em branco na abertura, porque ela nasce focada.
        if (_suppressAddressSync || (_address.Focused && _address.Modified))
        {
            return;
        }

        _suppressAddressSync = true;
        var url = view?.Url ?? HomeUrl;
        _address.Text = url is HomeUrl ? string.Empty : url;
        _address.Modified = false;
        _suppressAddressSync = false;
    }

    // ---- IAutomationTarget: chamado da thread do named pipe, so em --test-mode ----

    public Task<AutomationState> GetStateAsync() => OnUiAsync(() => new AutomationState
    {
        Tabs = [.. _session.Tabs.Select(ToAutomationTab)],
        ActiveTabIndex = _session.ActiveIndex,
        AddressBarText = _address.Text,
    });

    public Task NavigateAsync(string input) => OnUiAction(() =>
        ActiveView?.Navigate(UrlResolver.Resolve(input, _searchEngine)));

    public Task GoBackAsync() => OnUiAction(() => ActiveView?.GoBack());

    public Task GoForwardAsync() => OnUiAction(() => ActiveView?.GoForward());

    public Task ReloadAsync() => OnUiAction(() => ActiveView?.Reload());

    public Task NewTabAsync(string container, string url) =>
        OnUiAction(() => _ = OpenTabAsync(container, url));

    public Task CloseTabAsync(int index) => OnUiAction(() =>
    {
        if (index >= 0 && index < _session.Tabs.Count)
        {
            CloseTab(_session.Tabs[index].Id);
        }
    });

    public Task SelectTabAsync(int index) => OnUiAction(() => _session.Activate(index));

    public Task PinTabAsync(int index, bool pinned) => OnUiAction(() =>
    {
        if (index >= 0 && index < _session.Tabs.Count)
        {
            _session.Tabs[index].Pinned = pinned;
            _tabStrip.Invalidate();
        }
    });

    public Task HotkeyAsync(string name) => OnUiAction(() =>
    {
        var keys = ParseHotkey(name);
        var message = Message.Create(Handle, 0x0100 /* WM_KEYDOWN */, 0, 0);

        // Passa pelo mesmo ProcessCmdKey do teclado: o teste prova a ligacao real,
        // nao um atalho que chama o metodo por baixo.
        ProcessCmdKey(ref message, keys);
    });

    private static Keys ParseHotkey(string name) => name.ToLowerInvariant() switch
    {
        "ctrl+t" => Keys.Control | Keys.T,
        "ctrl+w" => Keys.Control | Keys.W,
        "ctrl+tab" => Keys.Control | Keys.Tab,
        "ctrl+shift+tab" => Keys.Control | Keys.Shift | Keys.Tab,
        "ctrl+shift+t" => Keys.Control | Keys.Shift | Keys.T,
        "ctrl+l" => Keys.Control | Keys.L,
        "f5" => Keys.F5,
        "alt+left" => Keys.Alt | Keys.Left,
        "alt+right" => Keys.Alt | Keys.Right,
        _ => throw new ArgumentException("Atalho desconhecido: " + name, nameof(name)),
    };

    private AutomationTab ToAutomationTab(TabModel model)
    {
        var view = _views.GetValueOrDefault(model.Id);

        return new AutomationTab
        {
            Url = view?.Url ?? model.Url,
            Title = view?.Title ?? model.Title,
            Container = model.Container.Name,
            ContainerColor = model.Container.Color,
            CanGoBack = view?.CanGoBack ?? false,
            CanGoForward = view?.CanGoForward ?? false,
            IsLoading = view?.IsLoading ?? false,
            Pinned = model.Pinned,
        };
    }

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
