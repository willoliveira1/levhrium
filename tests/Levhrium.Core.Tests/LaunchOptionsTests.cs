using Levhrium.Core;

namespace Levhrium.Core.Tests;

/// <summary>
/// O modo de teste abre porta CDP e named pipe. Estes testes existem para que
/// ninguem relaxe essa trava por acidente numa versao futura.
/// </summary>
public sealed class LaunchOptionsTests
{
    [Fact]
    public void SemArgumentosNaoHaModoDeTeste()
    {
        var options = LaunchOptions.Parse([]);

        Assert.False(options.TestMode);
        Assert.Equal(0, options.CdpPort);
        Assert.Null(options.HandshakeFilePath);
    }

    [Fact]
    public void ModoNormalNuncaRecebePortaCdp()
    {
        var options = LaunchOptions.Parse(["--url", "https://exemplo.com"]);

        Assert.False(options.TestMode);
        Assert.Equal(0, options.CdpPort);
    }

    [Fact]
    public void UrlSoltaEAceitaComoPrimeiroArgumento()
    {
        var options = LaunchOptions.Parse(["https://exemplo.com"]);

        Assert.Equal("https://exemplo.com", options.InitialUrl);
    }

    [Fact]
    public void PastaDeDadosDependeDoModo()
    {
        var normal = LaunchOptions.Parse([]).ResolvePaths();

        Assert.False(normal.IsDisposable);
        Assert.EndsWith(AppPaths.ProductName, normal.RootFolder, StringComparison.Ordinal);
    }

#if LEVHRIUM_TESTHOOKS
    [Fact]
    public void ModoDeTesteSorteiaPortaEIsolaOsDados()
    {
        var options = LaunchOptions.Parse([LaunchOptions.TestModeFlag]);

        Assert.True(options.TestMode);
        Assert.InRange(options.CdpPort, 1024, 65535);

        // Porta fixa (9222) seria previsivel; cada instancia precisa da sua.
        var outra = LaunchOptions.Parse([LaunchOptions.TestModeFlag]);
        Assert.NotEqual(options.InstanceId, outra.InstanceId);

        var paths = options.ResolvePaths();
        Assert.True(paths.IsDisposable);
        Assert.StartsWith(Path.GetTempPath(), paths.RootFolder, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NomeDoPipeEExclusivoDaInstancia()
    {
        var primeira = LaunchOptions.Parse([LaunchOptions.TestModeFlag]);
        var segunda = LaunchOptions.Parse([LaunchOptions.TestModeFlag]);

        Assert.NotEqual(primeira.AutomationPipeName, segunda.AutomationPipeName);
    }
#else
    [Fact]
    public void NoReleaseAFlagDeTesteEIgnorada()
    {
        // Trava de seguranca: sem LEVHRIUM_TESTHOOKS nao existe caminho de codigo
        // que abra a porta CDP, mesmo se alguem passar a flag.
        var options = LaunchOptions.Parse([LaunchOptions.TestModeFlag]);

        Assert.False(options.TestMode);
        Assert.Equal(0, options.CdpPort);
    }
#endif
}
