using Levhrium.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Levhrium.App;

/// <summary>
/// A parte viva de uma aba: o controle WebView2 amarrado ao container do
/// <see cref="TabModel"/>. O modelo vive no nucleo; aqui fica so o que precisa
/// de janela.
/// </summary>
internal sealed class BrowserTab : IDisposable
{
    private readonly TaskCompletionSource _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private bool _isLoading;

    private readonly DomainBlocklist _blocklist;

    internal BrowserTab(
        TabModel model,
        CoreWebView2CreationProperties creationProperties,
        DomainBlocklist blocklist)
    {
        Model = model;
        _blocklist = blocklist;

        View = new WebView2
        {
            Dock = DockStyle.Fill,
            CreationProperties = creationProperties,
            DefaultBackgroundColor = Theme.WindowBackground,
            TabStop = false,
        };

        View.CoreWebView2InitializationCompleted += OnInitializationCompleted;
        View.NavigationStarting += (_, _) => SetLoading(true);
        View.NavigationCompleted += (_, e) =>
        {
            SetLoading(false);

            if (e.IsSuccess)
            {
                Navigated?.Invoke(this, EventArgs.Empty);
            }
        };
        View.SourceChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Disparado quando algo exibido na aba ou na toolbar muda.</summary>
    internal event EventHandler? Changed;

    /// <summary>Disparado quando a pagina termina de carregar, com URL e titulo.</summary>
    internal event EventHandler? Navigated;

    /// <summary>Disparado quando um download comeca, com a URL e o destino.</summary>
    internal event EventHandler<(string Url, string FilePath)>? DownloadStarted;

    /// <summary>Disparado quando um download termina, com o destino.</summary>
    internal event EventHandler<string>? DownloadCompleted;

    internal TabModel Model { get; }

    internal WebView2 View { get; }

    /// <summary>Completa quando o CoreWebView2 esta pronto para receber comandos.</summary>
    internal Task Ready => _ready.Task;

    internal string Url => View.Source?.ToString() ?? "about:blank";

    internal string Title => View.CoreWebView2?.DocumentTitle ?? string.Empty;

    internal bool CanGoBack => View.CoreWebView2?.CanGoBack ?? false;

    internal bool CanGoForward => View.CoreWebView2?.CanGoForward ?? false;

    internal bool IsLoading => _isLoading;

    /// <summary>Renderer congelado por <see cref="TrySuspendAsync"/>.</summary>
    internal bool IsSuspended { get; private set; }

    /// <summary>Quantas requisicoes esta aba ja recusou pela lista de dominios.</summary>
    internal int BlockedRequests { get; private set; }

    /// <summary>Fator de zoom aplicado ao conteudo.</summary>
    internal double ZoomFactor
    {
        get => View.ZoomFactor;
        set => View.ZoomFactor = value;
    }

    internal async Task EnsureInitializedAsync()
    {
        await View.EnsureCoreWebView2Async().ConfigureAwait(true);
        await Ready.ConfigureAwait(true);
    }

    internal void Navigate(Uri target)
    {
        ArgumentNullException.ThrowIfNull(target);
        View.Source = target;
    }

    internal void GoBack()
    {
        if (CanGoBack)
        {
            View.CoreWebView2!.GoBack();
        }
    }

    internal void GoForward()
    {
        if (CanGoForward)
        {
            View.CoreWebView2!.GoForward();
        }
    }

    internal void Reload() => View.CoreWebView2?.Reload();

    /// <summary>
    /// Congela o renderer. Nao descarta: o DOM continua vivo e retomar e
    /// instantaneo -- perder estado numa aba de jogo idle seria pior que a RAM.
    /// </summary>
    internal async Task<bool> TrySuspendAsync()
    {
        if (View.CoreWebView2 is not { } core || IsSuspended)
        {
            return false;
        }

        // O WebView2 recusa suspender uma aba visivel; so hibernamos abas de fundo.
        var suspended = await core.TrySuspendAsync().ConfigureAwait(true);
        IsSuspended = suspended;
        return suspended;
    }

    internal void Resume()
    {
        if (View.CoreWebView2 is not { } core || !IsSuspended)
        {
            return;
        }

        core.Resume();
        IsSuspended = false;
    }

    /// <summary>
    /// Pede ao Chromium que solte caches desta aba. Vale para toda aba de fundo,
    /// inclusive as fixadas, que nao podem ser congeladas mas podem emagrecer.
    /// </summary>
    internal void SetMemoryUsageLow(bool low)
    {
        if (View.CoreWebView2 is { } core)
        {
            core.MemoryUsageTargetLevel = low
                ? CoreWebView2MemoryUsageTargetLevel.Low
                : CoreWebView2MemoryUsageTargetLevel.Normal;
        }
    }

    public void Dispose() => View.Dispose();

    private void OnInitializationCompleted(object? sender, CoreWebView2InitializationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            _ready.TrySetException(e.InitializationException);
            return;
        }

        var core = View.CoreWebView2;
        var settings = core.Settings;

        // Os atalhos do navegador sao nossos, nao do Edge: sem isso o Ctrl+T e o
        // Ctrl+W seriam engolidos pelo WebView2 antes de chegarem ao form.
        settings.AreBrowserAcceleratorKeysEnabled = false;

        // DevTools esta no escopo da v1 e vem de graca no WebView2.
        settings.AreDevToolsEnabled = true;

        core.DocumentTitleChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        core.HistoryChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);

        core.DownloadStarting += OnDownloadStarting;

        // Bloqueio por dominio direto no nativo: sem extensao e sem renderer extra.
        if (_blocklist.Count > 0)
        {
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += OnWebResourceRequested;
        }

        _ready.TrySetResult();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        var operation = e.DownloadOperation;
        DownloadStarted?.Invoke(this, (operation.Uri, operation.ResultFilePath));

        operation.StateChanged += (_, _) =>
        {
            if (operation.State == CoreWebView2DownloadState.Completed)
            {
                DownloadCompleted?.Invoke(this, operation.ResultFilePath);
            }
        };
    }

    /// <summary>
    /// Recusa requisicoes para dominios da lista devolvendo 403 vazio. Nunca
    /// bloqueia o documento principal: isso transformaria um dominio listado
    /// numa pagina em branco sem explicacao.
    /// </summary>
    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var isDocument = e.ResourceContext == CoreWebView2WebResourceContext.Document;

        if (!_blocklist.ShouldBlock(e.Request.Uri, isDocument))
        {
            return;
        }

        e.Response = View.CoreWebView2!.Environment.CreateWebResourceResponse(
            Content: null,
            StatusCode: 403,
            ReasonPhrase: "Blocked by Levhrium",
            Headers: string.Empty);

        BlockedRequests++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SetLoading(bool value)
    {
        _isLoading = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
