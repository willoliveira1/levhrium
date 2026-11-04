using Levhrium.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Levhrium.App;

/// <summary>
/// Uma aba: um controle WebView2 amarrado a um container. No M1 existe sempre
/// uma so; a colecao de abas e a faixa desenhada chegam no M2.
/// </summary>
internal sealed class BrowserTab : IDisposable
{
    private readonly TaskCompletionSource _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private bool _isLoading;

    internal BrowserTab(string container, CoreWebView2CreationProperties creationProperties)
    {
        Container = container;

        View = new WebView2
        {
            Dock = DockStyle.Fill,
            CreationProperties = creationProperties,
            DefaultBackgroundColor = Theme.WindowBackground,
            TabStop = false,
        };

        View.CoreWebView2InitializationCompleted += OnInitializationCompleted;
        View.NavigationStarting += (_, _) => SetLoading(true);
        View.NavigationCompleted += (_, _) => SetLoading(false);
        View.SourceChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        View.CoreWebView2InitializationCompleted += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Disparado quando algo exibido na toolbar muda (URL, titulo, carregamento).</summary>
    internal event EventHandler? Changed;

    internal string Container { get; }

    internal WebView2 View { get; }

    /// <summary>Completa quando o CoreWebView2 esta pronto para receber comandos.</summary>
    internal Task Ready => _ready.Task;

    internal string Url => View.Source?.ToString() ?? "about:blank";

    internal string Title => View.CoreWebView2?.DocumentTitle ?? string.Empty;

    internal bool CanGoBack => View.CoreWebView2?.CanGoBack ?? false;

    internal bool CanGoForward => View.CoreWebView2?.CanGoForward ?? false;

    internal bool IsLoading => _isLoading;

    internal AutomationTab ToAutomationTab() => new()
    {
        Url = Url,
        Title = Title,
        Container = Container,
        CanGoBack = CanGoBack,
        CanGoForward = CanGoForward,
        IsLoading = IsLoading,
    };

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

    public void Dispose()
    {
        View.Dispose();
    }

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

        // O menu de contexto do Edge some no M2, quando tivermos o nosso (com
        // "abrir em container"). DevTools fica: vem de graca e esta no escopo da v1.
        settings.AreDevToolsEnabled = true;

        core.DocumentTitleChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        core.HistoryChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);

        _ready.TrySetResult();
    }

    private void SetLoading(bool value)
    {
        _isLoading = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
