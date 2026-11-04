#if LEVHRIUM_TESTHOOKS
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Levhrium.Core;

namespace Levhrium.App.TestHooks;

/// <summary>
/// Hook de automacao dos testes de UI: um named pipe que aceita comandos de texto
/// e responde JSON. Existe somente em builds Debug (LEVHRIUM_TESTHOOKS) e somente
/// quando o app roda com --test-mode -- ver ESPECIFICACAO.md, secao Testes.
///
/// O Playwright dirige o conteudo das abas via CDP, mas nao alcanca a casca
/// (faixa de abas e GDI+, nao DOM). Este pipe e o que cobre a casca.
/// </summary>
internal sealed class AutomationServer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _pipeName;
    private readonly IAutomationTarget _target;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _loop;

    private AutomationServer(string pipeName, IAutomationTarget target)
    {
        _pipeName = pipeName;
        _target = target;
        _loop = Task.Run(() => AcceptLoopAsync(_shutdown.Token));
    }

    internal static AutomationServer Start(LaunchOptions options, IAutomationTarget target)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(target);

        if (!options.TestMode)
        {
            throw new InvalidOperationException("O hook de automacao exige --test-mode.");
        }

        return new AutomationServer(options.AutomationPipeName, target);
    }

    public void Dispose()
    {
        _shutdown.Cancel();

        try
        {
            _loop.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Desligando: a conexao pendente morre com o processo.
        }

        _shutdown.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                using var pipe = CreatePipe();
                await pipe.WaitForConnectionAsync(cancellation).ConfigureAwait(false);
                await ServeAsync(pipe, cancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                // Cliente desconectou no meio; proxima conexao.
            }
        }
    }

    /// <summary>
    /// Pipe restrito ao usuario atual. Mesmo sendo so de teste, um pipe de
    /// automacao aberto a todos daria controle do navegador a qualquer sessao.
    /// </summary>
    private NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        var currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Nao foi possivel identificar o usuario atual.");

        security.AddAccessRule(new PipeAccessRule(
            currentUser,
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            _pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            security);
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken cancellation)
    {
        using var reader = new StreamReader(pipe, leaveOpen: true);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

        while (!cancellation.IsCancellationRequested && pipe.IsConnected)
        {
            var line = await reader.ReadLineAsync(cancellation).ConfigureAwait(false);
            if (line is null)
            {
                return;
            }

            var response = await HandleAsync(line.Trim()).ConfigureAwait(false);
            await writer.WriteLineAsync(response).ConfigureAwait(false);
        }
    }

    private async Task<string> HandleAsync(string line)
    {
        if (line.Length == 0)
        {
            return Error("comando vazio");
        }

        var parts = line.Split(' ', 2, StringSplitOptions.TrimEntries);
        var command = parts[0];
        var argument = parts.Length > 1 ? parts[1] : string.Empty;

        try
        {
            switch (command)
            {
                case "get-state":
                    var state = await _target.GetStateAsync().ConfigureAwait(false);
                    return JsonSerializer.Serialize(state, JsonOptions);

                case "navigate":
                    if (argument.Length == 0)
                    {
                        return Error("navigate exige uma URL");
                    }

                    await _target.NavigateAsync(argument).ConfigureAwait(false);
                    return Ok();

                case "back":
                    await _target.GoBackAsync().ConfigureAwait(false);
                    return Ok();

                case "forward":
                    await _target.GoForwardAsync().ConfigureAwait(false);
                    return Ok();

                case "reload":
                    await _target.ReloadAsync().ConfigureAwait(false);
                    return Ok();

                case "new-tab":
                    // new-tab <container> <url>. Corta no ultimo espaco, nao no
                    // primeiro: nome de container tem espaco ("Conta 2"), URL nao.
                    var cut = argument.LastIndexOf(' ');
                    if (cut <= 0)
                    {
                        return Error("new-tab exige <container> <url>");
                    }

                    await _target
                        .NewTabAsync(argument[..cut].Trim(), argument[(cut + 1)..].Trim())
                        .ConfigureAwait(false);
                    return Ok();

                case "close-tab":
                    await _target.CloseTabAsync(ParseIndex(argument)).ConfigureAwait(false);
                    return Ok();

                case "select-tab":
                    await _target.SelectTabAsync(ParseIndex(argument)).ConfigureAwait(false);
                    return Ok();

                case "pin":
                    // pin <indice> <true|false>
                    var pinArgs = argument.Split(' ', 2, StringSplitOptions.TrimEntries);
                    var pinned = pinArgs.Length < 2 || bool.Parse(pinArgs[1]);
                    await _target.PinTabAsync(ParseIndex(pinArgs[0]), pinned).ConfigureAwait(false);
                    return Ok();

                case "hibernate-tick":
                    await _target.HibernateTickAsync().ConfigureAwait(false);
                    return Ok();

                case "hotkey":
                    if (argument.Length == 0)
                    {
                        return Error("hotkey exige um nome");
                    }

                    await _target.HotkeyAsync(argument).ConfigureAwait(false);
                    return Ok();

                default:
                    return Error("comando desconhecido: " + command);
            }
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private static int ParseIndex(string argument) =>
        int.TryParse(argument, out var index)
            ? index
            : throw new ArgumentException("Indice invalido: " + argument, nameof(argument));

    private static string Ok() => """{"ok":true}""";

    private static string Error(string message) =>
        JsonSerializer.Serialize(new { ok = false, error = message }, JsonOptions);
}
#endif
