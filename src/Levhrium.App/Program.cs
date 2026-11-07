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

        // Tema escuro tambem nos menus, inclusive nos submenus que o WinForms
        // cria sozinho e que nunca passam pelas nossas maos.
        DarkMenu.Install();

#if LEVHRIUM_TESTHOOKS
        // Abre so as Configuracoes, para conferir o desenho sem subir um
        // WebView2 inteiro. Compilado so no Debug, como os demais hooks.
        if (Array.Exists(rawArgs, arg => arg == "--settings-preview"))
        {
            ShowSettingsPreview(paths);
            return;
        }

        if (Array.Exists(rawArgs, arg => arg == "--menu-preview"))
        {
            ShowMenuPreview();
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
    /// <summary>
    /// Abre so o painel da biblioteca, para conferir o desenho sem subir um
    /// WebView2. Os itens espelham os do painel de verdade.
    /// </summary>
    private static void ShowMenuPreview()
    {
        using var host = new Form
        {
            Text = "Biblioteca",
            BackColor = Theme.WindowBackground,
            ClientSize = new Size(460, 560),
            StartPosition = FormStartPosition.Manual,
            Location = new Point(60, 60),
        };

        LibraryEntry Entry(string titulo, string url) =>
            new() { Title = titulo, Url = url };

        IReadOnlyList<LibraryEntry> Load(LibrarySection section) => section switch
        {
            LibrarySection.History =>
            [
                Entry("Pagina Um", "https://exemplo.test/um"),
                Entry("Pagina Dois", "https://exemplo.test/dois"),
            ],
            LibrarySection.Downloads => [],
            _ =>
            [
                Entry("willoliveira1/levhrium", "https://github.com/willoliveira1/levhrium"),
                Entry("Pokemon - Wikipedia", "https://pt.wikipedia.org/wiki/Pokemon"),
                Entry("Pagina Um", "https://exemplo.test/um"),
            ],
        };

        var panel = new LibraryPanel(LibrarySection.Bookmarks, Load);

        // A lista de sugestoes entra no mesmo preview: ela usa flags de janela
        // delicadas (sem ativacao) e vale ver desenhada antes de publicar.
        var sugestoes = new SuggestionPopup();

        host.Shown += (_, _) =>
        {
            panel.Location = new Point(host.Left + 30, host.Top + 40);
            panel.Show(host);

            sugestoes.SetItems(
                LibrarySearch.Suggest(Load(LibrarySection.History), Load(LibrarySection.Bookmarks), "test"),
                new Rectangle(host.Left + 440, host.Top + 40, 420, 30));
        };

        host.FormClosed += (_, _) => sugestoes.Dispose();

        Application.Run(host);
    }

    private static void ShowSettingsPreview(AppPaths paths)
    {
        using var updates = new UpdateService(paths);

        using var settings = new SettingsForm(
            paths,
            DomainBlocklist.LoadOrSeed(null),
            updates,
            () => 5,
            _ => { },
            () => true,
            _ => { });

        Application.Run(settings);
    }
#endif
}
