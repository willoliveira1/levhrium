#if LEVHRIUM_TESTHOOKS
using System.Text.Json;
using Levhrium.Core;

namespace Levhrium.App.TestHooks;

/// <summary>
/// Publica o handshake de teste (porta CDP, named pipe, pasta descartavel) somente
/// depois que a primeira aba esta pronta -- antes disso a porta CDP ainda nao
/// aceita conexao e o Playwright falharia por corrida.
/// </summary>
internal static class Handshake
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    internal static void PublishWhenReady(LaunchOptions options, AppPaths paths, Task ready)
    {
        if (options.HandshakeFilePath is not { Length: > 0 } destination)
        {
            return;
        }

        _ = ready.ContinueWith(
            completed =>
            {
                if (!completed.IsCompletedSuccessfully)
                {
                    return;
                }

                var handshake = new TestHandshake
                {
                    ProcessId = Environment.ProcessId,
                    CdpPort = options.CdpPort,
                    PipeName = options.AutomationPipeName,
                    RootFolder = paths.RootFolder,
                };

                WriteAtomically(destination, JsonSerializer.Serialize(handshake, JsonOptions));
            },
            TaskScheduler.Default);
    }

    /// <summary>
    /// Grava num temporario e move: o teste fica num laco esperando o arquivo
    /// aparecer, e nunca deve ler um JSON pela metade.
    /// </summary>
    private static void WriteAtomically(string destination, string content)
    {
        var directory = Path.GetDirectoryName(destination);
        if (directory is { Length: > 0 })
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = destination + ".tmp";
        File.WriteAllText(temporary, content);
        File.Move(temporary, destination, overwrite: true);
    }
}
#endif
