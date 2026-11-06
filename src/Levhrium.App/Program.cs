using Levhrium.Core;

namespace Levhrium.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] rawArgs)
    {
        var options = LaunchOptions.Parse(rawArgs);
        var paths = options.ResolvePaths();

        // Varre o que a atualizacao anterior aposentou. So da certo aqui, na
        // abertura: enquanto o navegador rodava, aquelas dlls estavam carregadas
        // e o Windows recusava apagar.
        if (!options.TestMode)
        {
            UpdateApplier.CleanupBackups(AppPaths.InstallFolder);
        }

        ApplicationConfiguration.Initialize();

        // Tema escuro de ponta a ponta, incluindo a barra de titulo nativa.
        Application.SetColorMode(SystemColorMode.Dark);

#if LEVHRIUM_TESTHOOKS
        // Abre so as Configuracoes, para conferir o desenho sem subir um
        // WebView2 inteiro. Compilado so no Debug, como os demais hooks.
        if (Array.Exists(rawArgs, arg => arg == "--settings-preview"))
        {
            ShowSettingsPreview(paths);
            return;
        }
#endif

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

#if LEVHRIUM_TESTHOOKS
    private static void ShowSettingsPreview(AppPaths paths)
    {
        var containers = new ContainerRegistry();
        _ = containers.GetOrCreate("Conta 2");
        _ = containers.GetOrCreate("Trabalho");

        using var updates = new UpdateService(paths);

        using var settings = new SettingsForm(
            paths,
            DomainBlocklist.LoadOrSeed(null),
            updates,
            containers,
            name => name == ContainerRegistry.DefaultName ? 2 : name == "Conta 2" ? 1 : 0,
            () => 5,
            _ => { });

        Application.Run(settings);
    }
#endif
}
