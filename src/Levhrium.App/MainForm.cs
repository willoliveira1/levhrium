using Levhrium.Core;
using Microsoft.Web.WebView2.Core;

namespace Levhrium.App;

/// <summary>
/// Janela principal. Barra de titulo nativa do Windows (decisao da rodada 4);
/// abaixo dela a faixa de abas desenhada a mao, a toolbar e o conteudo.
/// </summary>
internal sealed class MainForm : Form, IAutomationTarget
{
    private const int ToolbarHeight = 46;
    private const string HomeUrl = "about:blank";

    private readonly LaunchOptions _options;
    private readonly WebViewSettings _webViewSettings;
    private readonly SearchEngine _searchEngine = SearchEngine.Default;
    private readonly TabSession _session;
    private HibernationPolicy _hibernationPolicy;
    private readonly UpdateService _updates;
    private readonly HibernationService _hibernation;
    private readonly DomainBlocklist _blocklist;
    private readonly StateStore _stateStore;
    private readonly Library _library;
    private readonly System.Windows.Forms.Timer _saveTimer;
    private readonly BrowserState _restoredState;
    private readonly AppPaths _paths;

    /// <summary>Pasta de download forcada; nula em uso normal.</summary>
    private readonly string? _downloadFolder;

    /// <summary>Limiar de hibernacao em minutos, ajustavel nas configuracoes.</summary>
    private int _hibernateMinutes;

    /// <summary>Dominios que nunca hibernam, vindos do estado gravado.</summary>
    private readonly List<string> _neverHibernate;

    /// <summary>Diagnostico de teclado: toda tecla vinda da pagina conta aqui.</summary>
    private int _keysFromPage;
    private string _lastKeyFromPage = string.Empty;

    /// <summary>Diagnostico do menu de link; ver AutomationState.</summary>
    private int _linkMenusCustomized;
    private readonly Dictionary<Guid, BrowserTab> _views = [];
    private readonly Dictionary<Keys, Action> _hotkeys;

    private readonly TabStrip _tabStrip;
    private readonly Panel _toolbar;
    private readonly IconButton _back;
    private readonly IconButton _forward;
    private readonly IconButton _reload;
    private readonly IconButton _libraryButton;
    private readonly AddressBar _address;
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
        _blocklist = DomainBlocklist.LoadOrSeed(options.BlocklistPath ?? paths.BlocklistFile);
        _stateStore = new StateStore(paths.StateFile);
        _restoredState = _stateStore.Load();
        _library = new Library(_restoredState);
        _paths = paths;
        _updates = new UpdateService(paths);
        _downloadFolder = options.TestMode ? Path.Combine(paths.RootFolder, "Downloads") : null;

        // A linha de comando vence a configuracao gravada: e assim que os testes
        // conseguem fixar um limiar de poucos segundos.
        _hibernateMinutes = options.IdleThresholdWasSet
            ? (int)Math.Round(options.IdleThreshold.TotalMinutes)
            : _restoredState.HibernateAfterMinutes ?? (int)HibernationPolicy.DefaultIdleThreshold.TotalMinutes;

        _neverHibernate = [.. _restoredState.NeverHibernateDomains];

        _hibernationPolicy = options.IdleThresholdWasSet
            ? new HibernationPolicy(options.IdleThreshold, _neverHibernate)
            : new HibernationPolicy(TimeSpan.FromMinutes(_hibernateMinutes), _neverHibernate);

        _hibernation = new HibernationService(_session, () => _hibernationPolicy, id => _views.GetValueOrDefault(id));

        // Grava com atraso: navegar dispara varios eventos seguidos e nao vale
        // reescrever o arquivo a cada um.
        _saveTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveState();
        };

        Text = AppPaths.ProductName;
        BackColor = Theme.WindowBackground;
        ForeColor = Theme.Foreground;
        MinimumSize = new Size(640, 420);
        ClientSize = new Size(1180, 760);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        _back = new IconButton(Glyphs.Back, "Voltar (Alt+Seta esquerda)");
        _forward = new IconButton(Glyphs.Forward, "Avancar (Alt+Seta direita)");
        _reload = new IconButton(Glyphs.Reload, "Recarregar (F5)");
        _libraryButton = new IconButton(Glyphs.Menu, "Favoritos, historico e downloads");

        _address = new AddressBar();
        _address.Submitted += OnAddressKeyDown;
        _address.BookmarkClicked += (_, _) => ToggleBookmark();

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
        _toolbar.Controls.Add(_libraryButton);
        _toolbar.Controls.Add(_address);
        _toolbar.Resize += (_, _) => LayoutToolbar();

        // Ordem importa: o Fill entra primeiro, os Top empilham por cima.
        Controls.Add(_content);
        Controls.Add(_toolbar);
        Controls.Add(_tabStrip);

        _back.Click += (_, _) => ActiveView?.GoBack();
        _forward.Click += (_, _) => ActiveView?.GoForward();
        _reload.Click += (_, _) => ActiveView?.Reload();
        _libraryButton.Click += (_, _) => ShowLibraryMenu();

        _tabStrip.TabSelected += (_, id) => _session.Activate(id);
        _tabStrip.TabCloseRequested += (_, id) => CloseTab(id);
        _tabStrip.NewTabRequested += (_, _) => _ = OpenTabAsync(ContainerRegistry.DefaultName, HomeUrl);
        _tabStrip.ContainerMenuRequested += (_, point) => ShowContainerMenu(point);
        _tabStrip.TabMoved += (_, move) => _session.Move(move.From, move.To);

        _session.ActiveChanged += (_, _) => OnActiveTabChanged();
        _session.OrderChanged += (_, _) => _tabStrip.Invalidate();

        _hotkeys = BuildHotkeys();
    }

    /// <summary>Completa quando a primeira aba esta pronta para navegar.</summary>
    internal Task Ready => _ready.Task;

    private BrowserTab? ActiveView =>
        _session.Active is { } tab && _views.TryGetValue(tab.Id, out var view) ? view : null;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeTheme.ApplyDarkTitleBar(Handle);
        ApplyWindowIcon();
    }

    /// <summary>
    /// Icone da janela e da barra de tarefas, vindo do .ico embutido -- que tem
    /// todos os tamanhos, ao contrario do que o Windows extrairia do .exe.
    /// </summary>
    private void ApplyWindowIcon()
    {
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("Levhrium.icon");

        if (stream is not null)
        {
            Icon = new Icon(stream);
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        LayoutToolbar();
        _ = InitializeAsync();
    }

    /// <summary>
    /// Caminho de teclado quando o foco esta na casca (barra de endereco, abas).
    /// Com o foco dentro da pagina as teclas chegam por
    /// <see cref="BrowserTab.HotkeyPressed"/>, nao por aqui.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) =>
        TryHandleHotkey(keyData) || base.ProcessCmdKey(ref msg, keyData);

    /// <summary>
    /// Tabela unica de atalhos, construida uma vez. Ser uma so e o ponto: antes
    /// havia uma lista para executar e outra para reconhecer, e um atalho
    /// adicionado so na primeira funcionaria pela barra de endereco e ficaria
    /// silenciosamente morto dentro da pagina.
    /// </summary>
    private Dictionary<Keys, Action> BuildHotkeys() => new()
    {
        [Keys.Control | Keys.T] = () => _ = OpenTabAsync(ContainerRegistry.DefaultName, HomeUrl),
        [Keys.Control | Keys.W] = CloseActiveTab,
        [Keys.Control | Keys.Tab] = _session.ActivateNext,
        [Keys.Control | Keys.Shift | Keys.Tab] = _session.ActivatePrevious,
        [Keys.Control | Keys.Shift | Keys.T] = () => _ = ReopenLastClosedAsync(),

        [Keys.Control | Keys.L] = _address.FocusInput,

        [Keys.F5] = () => ActiveView?.Reload(),
        [Keys.Control | Keys.R] = () => ActiveView?.Reload(),
        [Keys.F12] = () => ActiveView?.OpenDevTools(),
        [Keys.Alt | Keys.Left] = () => ActiveView?.GoBack(),
        [Keys.Alt | Keys.Right] = () => ActiveView?.GoForward(),

        [Keys.Control | Keys.D] = ToggleBookmark,
        [Keys.Control | Keys.H] = () => ShowLibraryMenu(LibrarySection.History),
        [Keys.Control | Keys.J] = () => ShowLibraryMenu(LibrarySection.Downloads),

        [Keys.Control | Keys.Oemplus] = () => StepZoom(1),
        [Keys.Control | Keys.Add] = () => StepZoom(1),
        [Keys.Control | Keys.OemMinus] = () => StepZoom(-1),
        [Keys.Control | Keys.Subtract] = () => StepZoom(-1),
        [Keys.Control | Keys.D0] = ResetZoom,
        [Keys.Control | Keys.NumPad0] = ResetZoom,
    };

    /// <summary>
    /// Executa o atalho. Devolve true quando a tecla e nossa e nao deve seguir
    /// para a pagina.
    /// </summary>
    internal bool TryHandleHotkey(Keys keyData)
    {
        if (!_hotkeys.TryGetValue(keyData, out var action))
        {
            return false;
        }

        action();
        return true;
    }

    // ---- Ciclo de vida das abas ----

    private async Task InitializeAsync()
    {
        _session.Containers.Restore(_restoredState.Containers);
        _session.RestoreClosed(_restoredState.ClosedTabs);

        try
        {
            if (_options.InitialUrl is { Length: > 0 } requested)
            {
                // URL pedida na linha de comando tem precedencia sobre a sessao.
                await OpenTabAsync(
                    ContainerRegistry.DefaultName,
                    UrlResolver.Resolve(requested, _searchEngine).ToString());
            }
            else if (_restoredState.Tabs.Count > 0)
            {
                await RestoreTabsAsync();
            }
            else
            {
                await OpenTabAsync(ContainerRegistry.DefaultName, HomeUrl);
            }
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

    /// <summary>
    /// Reabre as abas gravadas na ordem e nos containers originais, e volta para
    /// a aba que estava ativa.
    /// </summary>
    private async Task RestoreTabsAsync()
    {
        foreach (var saved in _restoredState.Tabs)
        {
            var model = await OpenTabAsync(saved.Container, saved.Url, activate: false);
            model.Title = saved.Title;
            model.Pinned = saved.Pinned || model.Pinned;
        }

        _session.Activate(Math.Clamp(_restoredState.ActiveTabIndex, 0, _session.Tabs.Count - 1));
        _tabStrip.Invalidate();
    }

    private async Task<TabModel> OpenTabAsync(
        string containerName,
        string url,
        int? atIndex = null,
        bool activate = true)
    {
        var model = _session.Open(containerName, url, atIndex, activate);

        // Aba de dominio protegido nasce fixada: um jogo idle congelado para de
        // progredir, e a fixacao deixa a protecao visivel na faixa de abas.
        model.Pinned = _hibernationPolicy.ShouldPinOnOpen(url);

        var view = new BrowserTab(model, _webViewSettings.For(model.Container), _blocklist, _downloadFolder);
        view.Changed += (_, _) => OnTabContentChanged(model.Id);
        view.HotkeyPressed += OnHotkeyFromPage;
        WireLibraryEvents(view, model.Id);

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
        var view = new BrowserTab(restored, _webViewSettings.For(restored.Container), _blocklist, _downloadFolder);
        view.Changed += (_, _) => OnTabContentChanged(restored.Id);
        view.HotkeyPressed += OnHotkeyFromPage;
        WireLibraryEvents(view, restored.Id);

        _views[restored.Id] = view;
        _content.Controls.Add(view.View);
        view.View.Visible = _session.Active?.Id == restored.Id;

        await view.EnsureInitializedAsync();
        view.View.CoreWebView2.ContextMenuRequested += OnContextMenuRequested;
        view.Navigate(UrlResolver.Resolve(restored.Url, _searchEngine));

        OnActiveTabChanged();
    }

    /// <summary>
    /// Atalho vindo de dentro da pagina. O evento do WebView2 bloqueia o
    /// processo do browser enquanto roda, entao aqui so reconhecemos a tecla e
    /// mandamos a acao para a fila da UI.
    /// </summary>
    private bool OnHotkeyFromPage(Keys keyData)
    {
        _keysFromPage++;
        _lastKeyFromPage = keyData.ToString();

        if (!IsOurHotkey(keyData))
        {
            return false;
        }

        BeginInvoke(() => TryHandleHotkey(keyData));
        return true;
    }

    /// <summary>
    /// Reconhece a tecla sem executar nada: o evento do WebView2 bloqueia o
    /// processo do browser, entao o reconhecimento e imediato e a acao, adiada.
    /// </summary>
    private bool IsOurHotkey(Keys keyData) => _hotkeys.ContainsKey(keyData);

    private void OnActiveTabChanged()
    {
        foreach (var (id, view) in _views)
        {
            var isActive = _session.Active?.Id == id;
            view.View.Visible = isActive;

            // Retoma na hora, sem esperar o proximo tique: clicar numa aba
            // hibernada tem de responder imediatamente.
            if (isActive && view.IsSuspended)
            {
                view.Resume();

                if (_session.Find(id) is { } model)
                {
                    model.Suspended = false;
                }
            }
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

        _linkMenusCustomized++;

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

    /// <summary>
    /// Posiciona a toolbar a mao: os botoes circulares a esquerda, a biblioteca
    /// a direita, e a pilula de endereco ocupando o que sobra.
    /// </summary>
    private void LayoutToolbar()
    {
        const int gap = 3;
        var x = _toolbar.Padding.Left;

        foreach (var button in new[] { _back, _forward, _reload })
        {
            button.Location = new Point(x, (ToolbarHeight - button.Height) / 2);
            x += button.Width + gap;
        }

        _libraryButton.Location = new Point(
            _toolbar.ClientSize.Width - _toolbar.Padding.Right - _libraryButton.Width,
            (ToolbarHeight - _libraryButton.Height) / 2);

        var left = x + 6;
        _address.Location = new Point(left, (ToolbarHeight - _address.Height) / 2);
        _address.Width = Math.Max(140, _libraryButton.Left - 6 - left);
    }

    private void OnAddressKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
        {
            return;
        }

        e.Handled = true;
        e.SuppressKeyPress = true;

        ActiveView?.Navigate(UrlResolver.Resolve(_address.Address, _searchEngine));
        ActiveView?.View.Focus();
    }

    private void SyncToolbar()
    {
        if (IsDisposed)
        {
            return;
        }

        var view = ActiveView;

        // Botao desabilitado fica apagado, nao some: sumir moveria os vizinhos
        // de lugar a cada navegacao.
        _back.Muted = view?.CanGoBack != true;
        _forward.Muted = view?.CanGoForward != true;
        _back.Invalidate();
        _forward.Invalidate();

        _reload.Glyph = view?.IsLoading == true ? Glyphs.Stop : Glyphs.Reload;
        _reload.Invalidate();

        var container = _session.Active?.Container;
        Text = _session.Active?.Title is { Length: > 0 } title
            ? $"{title} \u2014 {container?.Name} \u2014 {AppPaths.ProductName}"
            : AppPaths.ProductName;

        _address.BlockedRequests = view?.BlockedRequests ?? 0;
        _address.IsBookmarked = view is not null && _library.IsBookmarked(view.Url);

        // So nao sobrescreve se o usuario estiver de fato digitando.
        if (_suppressAddressSync || _address.UserIsTyping)
        {
            return;
        }

        _suppressAddressSync = true;
        var url = view?.Url ?? HomeUrl;
        _address.Address = url is HomeUrl ? string.Empty : url;
        _address.ClearModified();
        _suppressAddressSync = false;
    }

    // ---- Biblioteca: favoritos, historico, downloads e zoom ----

    private enum LibrarySection
    {
        Bookmarks,
        History,
        Downloads,
    }

    /// <summary>
    /// Liga a aba a biblioteca: cada pagina carregada vira historico, o zoom do
    /// site e reaplicado e os downloads entram na lista.
    /// </summary>
    private void WireLibraryEvents(BrowserTab view, Guid id)
    {
        view.Navigated += (_, _) =>
        {
            _library.RecordVisit(view.Url, view.Title);

            // Zoom e por host: o site reabre no tamanho que voce deixou.
            var zoom = _library.ZoomFor(view.Url);
            if (Math.Abs(view.ZoomFactor - zoom) > 0.001)
            {
                view.ZoomFactor = zoom;
            }

            ScheduleSave();
        };

        view.DownloadStarted += (_, download) =>
        {
            _library.RecordDownload(download.Url, download.FilePath);
            ScheduleSave();
        };

        view.DownloadCompleted += (_, filePath) =>
        {
            _library.MarkDownloadCompleted(filePath);
            ScheduleSave();
        };

        _ = id;
    }

    private void ToggleBookmark()
    {
        if (ActiveView is not { } view)
        {
            return;
        }

        _library.ToggleBookmark(view.Url, view.Title);
        ScheduleSave();
        SyncToolbar();
    }

    private void StepZoom(int direction)
    {
        if (ActiveView is not { } view)
        {
            return;
        }

        var next = Library.NextZoomStep(view.ZoomFactor, direction);
        view.ZoomFactor = next;
        _library.SetZoom(view.Url, next);
        ScheduleSave();
    }

    private void ResetZoom()
    {
        if (ActiveView is not { } view)
        {
            return;
        }

        view.ZoomFactor = 1.0;
        _library.SetZoom(view.Url, 1.0);
        ScheduleSave();
    }

    /// <summary>
    /// Menu unico para favoritos, historico e downloads. Um menu custa muito
    /// menos codigo que uma janela de biblioteca e resolve o mesmo uso.
    /// </summary>
    private void ShowLibraryMenu(LibrarySection section = LibrarySection.Bookmarks)
    {
        var menu = new ContextMenuStrip
        {
            BackColor = Theme.ToolbarBackground,
            ForeColor = Theme.Foreground,
            ShowImageMargin = false,
        };

        var favorita = ActiveView is { } active && _library.IsBookmarked(active.Url);
        var toggle = new ToolStripMenuItem(
            favorita ? "Remover dos favoritos (Ctrl+D)" : "Adicionar aos favoritos (Ctrl+D)");
        toggle.Click += (_, _) => ToggleBookmark();
        menu.Items.Add(toggle);
        menu.Items.Add(new ToolStripSeparator());

        AppendSection(menu, "Favoritos", section == LibrarySection.Bookmarks,
            [.. _library.Bookmarks.Take(15).Select(b => (b.Title, b.Url))]);

        AppendSection(menu, "Historico (Ctrl+H)", section == LibrarySection.History,
            [.. _library.History.Take(15).Select(h => (h.Title, h.Url))]);

        AppendSection(menu, "Downloads (Ctrl+J)", section == LibrarySection.Downloads,
            [.. _library.Downloads.Take(15).Select(d => (d.FileName, d.Url))]);

        menu.Items.Add(new ToolStripSeparator());

        var settings = new ToolStripMenuItem("Configuracoes...");
        settings.Click += (_, _) => ShowSettings();
        menu.Items.Add(settings);

        menu.Show(_libraryButton, new Point(0, _libraryButton.Height));
    }

    /// <summary>
    /// A secao pedida vem aberta na raiz; as outras ficam em submenu, para o
    /// Ctrl+H cair no historico sem mais um clique.
    /// </summary>
    private void AppendSection(
        ContextMenuStrip menu,
        string title,
        bool expanded,
        IReadOnlyList<(string Title, string Url)> entries)
    {
        if (entries.Count == 0)
        {
            var vazio = new ToolStripMenuItem(title + " (vazio)") { Enabled = false };
            menu.Items.Add(vazio);
            return;
        }

        var items = entries.Select(entry =>
        {
            var item = new ToolStripMenuItem(Shorten(entry.Title))
            {
                ToolTipText = entry.Url,
            };

            var url = entry.Url;
            item.Click += (_, _) => _ = OpenTabAsync(ContainerRegistry.DefaultName, url);
            return (ToolStripItem)item;
        }).ToArray();

        if (expanded)
        {
            menu.Items.Add(new ToolStripMenuItem(title) { Enabled = false });
            menu.Items.AddRange(items);
            menu.Items.Add(new ToolStripSeparator());
            return;
        }

        var parent = new ToolStripMenuItem(title);
        parent.DropDownItems.AddRange(items);
        menu.Items.Add(parent);
    }

    private void ShowSettings()
    {
        using var dialog = new SettingsForm(
            _paths,
            _blocklist,
            _updates,
            _session.Containers,
            name => _session.Tabs.Count(tab =>
                string.Equals(tab.Container.Name, name, StringComparison.OrdinalIgnoreCase)),
            () => _hibernateMinutes,
            minutes =>
            {
                _hibernateMinutes = minutes;

                // Troca a politica na hora: o servico le por funcao a cada tique.
                _hibernationPolicy = new HibernationPolicy(TimeSpan.FromMinutes(minutes), _neverHibernate);
                ScheduleSave();
            });

        dialog.ShowDialog(this);
    }

    private static string Shorten(string text) =>
        text.Length <= 60 ? text : text[..57] + "...";

    // ---- Persistencia ----

    /// <summary>Reinicia o atraso de gravacao; grava 2s depois do ultimo evento.</summary>
    private void ScheduleSave()
    {
        if (IsDisposed)
        {
            return;
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveState()
    {
        var tabs = _session.Tabs.Select(tab =>
        {
            var view = _views.GetValueOrDefault(tab.Id);

            return new PersistedTab
            {
                Container = tab.Container.Name,
                Url = view?.Url ?? tab.Url,
                Title = view?.Title ?? tab.Title,
                Pinned = tab.Pinned,
            };
        }).ToList();

        var state = _library.ToState(
            tabs,
            _session.Containers.ToPersisted(),
            _session.ActiveIndex,
            _session.ClosedTabs) with
        {
            HibernateAfterMinutes = _hibernateMinutes,
            NeverHibernateDomains = _neverHibernate,
        };

        _stateStore.Save(state);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Grava na saida sem esperar o atraso: fechar nao pode perder a sessao.
        _saveTimer.Stop();
        SaveState();

        base.OnFormClosing(e);
    }

    // ---- IAutomationTarget: chamado da thread do named pipe, so em --test-mode ----

    public Task<AutomationState> GetStateAsync() => OnUiAsync(() => new AutomationState
    {
        Tabs = [.. _session.Tabs.Select(ToAutomationTab)],
        ActiveTabIndex = _session.ActiveIndex,
        AddressBarText = _address.Address,
        BlocklistSize = _blocklist.Count,
        KeysFromPage = _keysFromPage,
        LastKeyFromPage = _lastKeyFromPage,
        LinkMenusCustomized = _linkMenusCustomized,
        Library = new AutomationLibrary
        {
            BookmarkCount = _library.Bookmarks.Count,
            HistoryCount = _library.History.Count,
            DownloadCount = _library.Downloads.Count,
            ActiveIsBookmarked = ActiveView is { } active && _library.IsBookmarked(active.Url),
            ActiveZoom = ActiveView?.ZoomFactor ?? 1.0,
            BookmarkTitles = [.. _library.Bookmarks.Select(b => b.Title)],
            HistoryUrls = [.. _library.History.Select(h => h.Url)],
        },
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

    /// <summary>Forca uma avaliacao da politica de hibernacao agora.</summary>
    public Task HibernateTickAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        BeginInvoke(async () =>
        {
            try
            {
                await _hibernation.TickAsync();
                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

        return completion.Task;
    }

    public Task ToggleBookmarkAsync() => OnUiAction(ToggleBookmark);

    public Task ZoomAsync(string direction) => OnUiAction(() =>
    {
        switch (direction.ToLowerInvariant())
        {
            case "in":
                StepZoom(1);
                break;

            case "out":
                StepZoom(-1);
                break;

            case "reset":
                ResetZoom();
                break;

            default:
                throw new ArgumentException("Direcao de zoom desconhecida: " + direction, nameof(direction));
        }
    });

    public Task SaveStateAsync() => OnUiAction(() =>
    {
        _saveTimer.Stop();
        SaveState();
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
        "ctrl+d" => Keys.Control | Keys.D,
        "ctrl+plus" => Keys.Control | Keys.Oemplus,
        "ctrl+minus" => Keys.Control | Keys.OemMinus,
        "ctrl+0" => Keys.Control | Keys.D0,
        "f5" => Keys.F5,
        "f12" => Keys.F12,
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
            Suspended = view?.IsSuspended ?? model.Suspended,
            BlockedRequests = view?.BlockedRequests ?? 0,
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
