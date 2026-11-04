using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using Levhrium.Core;

namespace Levhrium.E2E.Tests;

/// <summary>
/// Sobe o Levhrium em --test-mode e da aos testes os dois canais de controle:
/// o named pipe (casca: abas, hotkeys, estado) e a porta CDP (conteudo, via
/// Playwright). A porta e o pipe sao descobertos pelo arquivo de handshake --
/// a porta e sorteada justamente para nao ser previsivel.
/// </summary>
public sealed class AppHarness : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(60);

    private readonly Process _process;
    private readonly string _handshakeFile;

    private readonly bool _ownsDataFolder;

    private AppHarness(Process process, string handshakeFile, TestHandshake handshake, bool ownsDataFolder)
    {
        _process = process;
        _handshakeFile = handshakeFile;
        Handshake = handshake;
        _ownsDataFolder = ownsDataFolder;
    }

    public TestHandshake Handshake { get; }

    /// <summary>Endpoint CDP para <c>ConnectOverCDPAsync</c>.</summary>
    public string CdpEndpoint => $"http://127.0.0.1:{Handshake.CdpPort}";

    public static async Task<AppHarness> StartAsync(
        string? initialUrl = null,
        TimeSpan? hibernateAfter = null,
        string? blocklistPath = null,
        string? dataFolder = null)
    {
        var executable = ResolveExecutable();
        var handshakeFile = Path.Combine(
            Path.GetTempPath(),
            $"levhrium-handshake-{Guid.NewGuid():n}.json");

        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable),
        };

        startInfo.ArgumentList.Add(LaunchOptions.TestModeFlag);
        startInfo.ArgumentList.Add(LaunchOptions.HandshakeFlag);
        startInfo.ArgumentList.Add(handshakeFile);

        if (initialUrl is { Length: > 0 })
        {
            startInfo.ArgumentList.Add(LaunchOptions.UrlFlag);
            startInfo.ArgumentList.Add(initialUrl);
        }

        if (dataFolder is { Length: > 0 })
        {
            startInfo.ArgumentList.Add(LaunchOptions.TestDataDirFlag);
            startInfo.ArgumentList.Add(dataFolder);
        }

        if (blocklistPath is { Length: > 0 })
        {
            startInfo.ArgumentList.Add(LaunchOptions.BlocklistFlag);
            startInfo.ArgumentList.Add(blocklistPath);
        }

        if (hibernateAfter is { } threshold)
        {
            // Evita que o teste espere cinco minutos de verdade.
            startInfo.ArgumentList.Add(LaunchOptions.HibernateAfterFlag);
            startInfo.ArgumentList.Add(
                threshold.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Nao foi possivel iniciar o Levhrium.");

        try
        {
            var handshake = await WaitForHandshakeAsync(process, handshakeFile);
            return new AppHarness(process, handshakeFile, handshake, ownsDataFolder: dataFolder is null);
        }
        catch
        {
            KillQuietly(process);
            throw;
        }
    }

    /// <summary>Estado atual da casca, como a janela o reporta.</summary>
    public async Task<AutomationState> GetStateAsync()
    {
        var response = await SendAsync("get-state");
        return JsonSerializer.Deserialize<AutomationState>(response, JsonOptions)
            ?? throw new InvalidOperationException("Resposta de get-state vazia: " + response);
    }

    public Task<string> NavigateAsync(string input) => SendAsync("navigate " + input);

    public Task<string> GoBackAsync() => SendAsync("back");

    public Task<string> GoForwardAsync() => SendAsync("forward");

    public Task<string> ReloadAsync() => SendAsync("reload");

    public Task<string> NewTabAsync(string container, string url) =>
        SendAsync($"new-tab {container} {url}");

    public Task<string> CloseTabAsync(int index) => SendAsync("close-tab " + index);

    public Task<string> SelectTabAsync(int index) => SendAsync("select-tab " + index);

    public Task<string> PinTabAsync(int index, bool pinned) =>
        SendAsync($"pin {index} {pinned.ToString().ToLowerInvariant()}");

    /// <summary>Dispara o atalho pelo mesmo despacho de teclas do teclado real.</summary>
    public Task<string> HotkeyAsync(string name) => SendAsync("hotkey " + name);

    /// <summary>Forca a avaliacao da politica de hibernacao agora.</summary>
    public Task<string> HibernateTickAsync() => SendAsync("hibernate-tick");

    public Task<string> ToggleBookmarkAsync() => SendAsync("bookmark-toggle");

    public Task<string> ZoomAsync(string direction) => SendAsync("zoom " + direction);

    /// <summary>Grava o estado agora, sem esperar o atraso de 2s.</summary>
    public Task<string> SaveStateAsync() => SendAsync("save-state");

    /// <summary>Espera uma condicao da casca, com timeout: a navegacao e assincrona.</summary>
    public async Task<AutomationState> WaitForStateAsync(
        Func<AutomationState, bool> condition,
        string description,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        AutomationState? last = null;

        while (DateTime.UtcNow < deadline)
        {
            last = await GetStateAsync();
            if (condition(last))
            {
                return last;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException(
            $"A casca nunca atingiu a condicao '{description}'. Ultimo estado: " +
            JsonSerializer.Serialize(last, JsonOptions));
    }

    public async Task<string> SendAsync(string command)
    {
        using var pipe = new NamedPipeClientStream(
            ".",
            Handshake.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        await pipe.ConnectAsync(10_000);

        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, leaveOpen: true);

        await writer.WriteLineAsync(command);
        var response = await reader.ReadLineAsync();

        return response ?? throw new InvalidOperationException(
            "O hook de automacao fechou a conexao sem responder a: " + command);
    }

    public async ValueTask DisposeAsync()
    {
        KillQuietly(_process);

        try
        {
            if (File.Exists(_handshakeFile))
            {
                File.Delete(_handshakeFile);
            }

            if (_ownsDataFolder && Directory.Exists(Handshake.RootFolder))
            {
                Directory.Delete(Handshake.RootFolder, recursive: true);
            }
        }
        catch (IOException)
        {
            // O runtime do WebView2 pode ainda estar soltando handles; sobra em %TEMP%.
        }
        catch (UnauthorizedAccessException)
        {
        }

        await Task.CompletedTask;
    }

    private static async Task<TestHandshake> WaitForHandshakeAsync(Process process, string handshakeFile)
    {
        var deadline = DateTime.UtcNow + StartupTimeout;

        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    $"O Levhrium saiu com codigo {process.ExitCode} antes de publicar o handshake.");
            }

            if (File.Exists(handshakeFile))
            {
                var json = await File.ReadAllTextAsync(handshakeFile);
                var handshake = JsonSerializer.Deserialize<TestHandshake>(json, JsonOptions);

                if (handshake is not null)
                {
                    return handshake;
                }
            }

            await Task.Delay(100);
        }

        throw new TimeoutException(
            $"O Levhrium nao publicou o handshake em {StartupTimeout.TotalSeconds:0}s.");
    }

    /// <summary>
    /// Acha o executavel do app subindo a arvore a partir do diretorio de teste.
    /// O app e referenciado com ReferenceOutputAssembly=false, entao ele e
    /// compilado mas nao copiado para ca.
    /// </summary>
    private static string ResolveExecutable()
    {
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif

        var relative = Path.Combine(
            "src", "Levhrium.App", "bin", configuration, "net10.0-windows", "Levhrium.exe");

        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Levhrium.exe nao encontrado. Rode 'dotnet build' na solucao antes dos testes.");
    }

    private static void KillQuietly(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            process.Dispose();
        }
    }
}
