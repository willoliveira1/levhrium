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

    /// <summary>Segundos de ociosidade antes de hibernar; 0 desliga a hibernacao.</summary>
    public const string HibernateAfterFlag = "--hibernate-after";

    /// <summary>Arquivo hosts a somar a lista de bloqueio embutida.</summary>
    public const string BlocklistFlag = "--blocklist";

    /// <summary>Pasta de dados fixa; so vale em --test-mode.</summary>
    public const string TestDataDirFlag = "--test-data-dir";

    private LaunchOptions(
        bool testMode,
        string? initialUrl,
        string instanceId,
        int cdpPort,
        string? handshakeFilePath,
        TimeSpan idleThreshold,
        string? blocklistPath,
        string? testDataDir)
    {
        TestMode = testMode;
        InitialUrl = initialUrl;
        InstanceId = instanceId;
        CdpPort = cdpPort;
        HandshakeFilePath = handshakeFilePath;
        IdleThreshold = idleThreshold;
        BlocklistPath = blocklistPath;
        TestDataDir = testDataDir;
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

    /// <summary>
    /// Quanto tempo uma aba de fundo espera antes de hibernar. Configuravel para
    /// que os testes nao precisem esperar cinco minutos de verdade.
    /// </summary>
    public TimeSpan IdleThreshold { get; }

    /// <summary>
    /// Arquivo hosts extra. Quando nulo, usa blocklist.txt da pasta de dados se
    /// existir; de todo jeito a semente embutida sempre entra.
    /// </summary>
    public string? BlocklistPath { get; }

    /// <summary>Pasta de dados fixa para testes; ignorada fora de --test-mode.</summary>
    public string? TestDataDir { get; }

    /// <summary>Nome do named pipe de automacao desta instancia.</summary>
    public string AutomationPipeName => $"{AppPaths.ProductName}-test-{InstanceId}";

    public AppPaths ResolvePaths()
    {
        if (!TestMode)
        {
            return AppPaths.ForNormalUse();
        }

        return TestDataDir is { Length: > 0 } fixedFolder
            ? AppPaths.ForTestFolder(fixedFolder)
            : AppPaths.ForTests(InstanceId);
    }

    public static LaunchOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var testMode = false;
        string? url = null;
        string? handshake = null;
        var idleThreshold = HibernationPolicy.DefaultIdleThreshold;
        string? blocklist = null;
        string? testDataDir = null;

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
            else if (string.Equals(args[i], BlocklistFlag, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                blocklist = args[++i];
            }
            else if (string.Equals(args[i], TestDataDirFlag, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                testDataDir = args[++i];
            }
            else if (string.Equals(args[i], HibernateAfterFlag, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count
                     && double.TryParse(args[++i], System.Globalization.CultureInfo.InvariantCulture, out var seconds))
            {
                idleThreshold = TimeSpan.FromSeconds(Math.Max(0, seconds));
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
            ? new LaunchOptions(true, url, Guid.NewGuid().ToString("n")[..12], FindFreePort(), handshake, idleThreshold, blocklist, testDataDir)
            : new LaunchOptions(false, url, string.Empty, 0, handshakeFilePath: null, idleThreshold, blocklist, testDataDir);
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
