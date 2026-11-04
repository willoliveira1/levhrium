using System.Net;
using System.Net.Sockets;

namespace Levhrium.Core;

/// <summary>
/// Opcoes de linha de comando. O modo de teste existe apenas em builds com
/// LEVHRIUM_TESTHOOKS definido (Debug): no Release nao ha caminho de codigo que
/// abra a porta CDP nem o named pipe -- com CDP aberta, qualquer processo local
/// controlaria o navegador e leria as sessoes do usuario.
/// </summary>
public sealed class LaunchOptions
{
    public const string TestModeFlag = "--test-mode";
    public const string UrlFlag = "--url";
    public const string HandshakeFlag = "--test-handshake";

    private LaunchOptions(
        bool testMode,
        string? initialUrl,
        string instanceId,
        int cdpPort,
        string? handshakeFilePath)
    {
        TestMode = testMode;
        InitialUrl = initialUrl;
        InstanceId = instanceId;
        CdpPort = cdpPort;
        HandshakeFilePath = handshakeFilePath;
    }

    public bool TestMode { get; }

    /// <summary>URL para abrir na primeira aba, se informada.</summary>
    public string? InitialUrl { get; }

    /// <summary>Identifica esta instancia: nomeia a pasta temporaria e o named pipe.</summary>
    public string InstanceId { get; }

    /// <summary>Porta de depuracao remota. 0 fora do modo de teste.</summary>
    public int CdpPort { get; }

    /// <summary>
    /// Arquivo onde o app publica porta CDP e nome do pipe depois de ficar pronto.
    /// E assim que o teste descobre uma porta efemera sem adivinhar.
    /// </summary>
    public string? HandshakeFilePath { get; }

    /// <summary>Nome do named pipe de automacao desta instancia.</summary>
    public string AutomationPipeName => $"{AppPaths.ProductName}-test-{InstanceId}";

    public AppPaths ResolvePaths() =>
        TestMode ? AppPaths.ForTests(InstanceId) : AppPaths.ForNormalUse();

    public static LaunchOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var testMode = false;
        string? url = null;
        string? handshake = null;

        for (var i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], TestModeFlag, StringComparison.OrdinalIgnoreCase))
            {
                testMode = true;
            }
            else if (string.Equals(args[i], UrlFlag, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                url = args[++i];
            }
            else if (string.Equals(args[i], HandshakeFlag, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                handshake = args[++i];
            }
            else if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                url ??= args[i];
            }
        }

#if !LEVHRIUM_TESTHOOKS
        // Trava de seguranca: no Release a flag e ignorada por completo.
        testMode = false;
#endif

        return testMode
            ? new LaunchOptions(true, url, Guid.NewGuid().ToString("n")[..12], FindFreePort(), handshake)
            : new LaunchOptions(false, url, string.Empty, 0, handshakeFilePath: null);
    }

    /// <summary>
    /// Porta efemera sorteada pelo sistema. Nunca a 9222 fixa: porta previsivel
    /// num navegador de uso diario e convite para outro processo se conectar.
    /// </summary>
    private static int FindFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
