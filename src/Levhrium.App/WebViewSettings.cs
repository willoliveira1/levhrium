using Levhrium.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Levhrium.App;

/// <summary>
/// Monta as propriedades de criacao de cada WebView2. Todas apontam para a mesma
/// pasta de dados do usuario -- e isso que faz os containers compartilharem um
/// processo de browser, em vez de subir um runtime por container.
/// </summary>
internal sealed class WebViewSettings
{
    private readonly LaunchOptions _options;
    private readonly AppPaths _paths;

    internal WebViewSettings(LaunchOptions options, AppPaths paths)
    {
        _options = options;
        _paths = paths;
    }

    internal CoreWebView2CreationProperties For(Container container)
    {
        ArgumentNullException.ThrowIfNull(container);
        _paths.EnsureCreated();

        return new CoreWebView2CreationProperties
        {
            UserDataFolder = _paths.WebView2Folder,
            ProfileName = container.ProfileName,
            AdditionalBrowserArguments = BuildBrowserArguments(),
        };
    }

    /// <summary>
    /// Argumentos do Chromium. A porta de depuracao remota entra somente em
    /// --test-mode: num navegador de uso diario ela entregaria cookies e sessoes
    /// a qualquer processo local.
    /// </summary>
    private string BuildBrowserArguments()
    {
        var arguments = new List<string>();

#if LEVHRIUM_TESTHOOKS
        if (_options.TestMode)
        {
            arguments.Add($"--remote-debugging-port={_options.CdpPort}");
        }
#endif

        return string.Join(' ', arguments);
    }
}
