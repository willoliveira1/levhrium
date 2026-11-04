using Levhrium.Core;

namespace Levhrium.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] rawArgs)
    {
        var options = LaunchOptions.Parse(rawArgs);
        var paths = options.ResolvePaths();

        ApplicationConfiguration.Initialize();

        // Tema escuro de ponta a ponta, incluindo a barra de titulo nativa.
        Application.SetColorMode(SystemColorMode.Dark);

        using var form = new MainForm(options, paths);

#if LEVHRIUM_TESTHOOKS
        using var automation = options.TestMode
            ? TestHooks.AutomationServer.Start(options, form)
            : null;

        if (options.TestMode)
        {
            TestHooks.Handshake.PublishWhenReady(options, paths, form.Ready);
        }
#endif

        Application.Run(form);

        paths.DeleteIfDisposable();
    }
}
