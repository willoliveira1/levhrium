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

    internal BrowserTab(TabModel model, CoreWebView2CreationProperties creationProperties)
    {
        Model = model;

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
    }

    /// <summary>Disparado quando algo exibido na aba ou na toolbar muda.</summary>
    internal event EventHandler? Changed;

    internal TabModel Model { get; }

    internal WebView2 View { get; }

    /// <summary>Completa quando o CoreWebView2 esta pronto para receber comandos.</summary>
    internal Task Ready => _ready.Task;

    internal string Url => View.Source?.ToString() ?? "about:blank";

    internal string Title => View.CoreWebView2?.DocumentTitle ?? string.Empty;

    internal bool CanGoBack => View.CoreWebView2?.CanGoBack ?? false;

    internal bool CanGoForward => View.CoreWebView2?.CanGoForward ?? false;

    internal bool IsLoading => _isLoading;

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

        _ready.TrySetResult();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SetLoading(bool value)
    {
        _isLoading = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
