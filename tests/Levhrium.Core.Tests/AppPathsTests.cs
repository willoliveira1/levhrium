using Levhrium.Core;

namespace Levhrium.Core.Tests;

public sealed class AppPathsTests
{
    [Fact]
    public void UsaUmaUnicaPastaDeDadosParaTodosOsContainers()
    {
        var paths = AppPaths.ForNormalUse();

        // Uma pasta so e o que mantem um processo de browser para todos os
        // containers; pasta por container era a abordagem antiga e mais pesada.
        Assert.Equal(Path.Combine(paths.RootFolder, "WebView2"), paths.WebView2Folder);
        Assert.Equal(Path.Combine(paths.RootFolder, "state.json"), paths.StateFile);
    }

    [Fact]
    public void PastaDeTesteFicaEmTempEEDescartavel()
    {
        var paths = AppPaths.ForTests("abc123");

        Assert.True(paths.IsDisposable);
        Assert.StartsWith(Path.GetTempPath(), paths.RootFolder, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("abc123", paths.RootFolder, StringComparison.Ordinal);
    }

    [Fact]
    public void ApagarRecusaPastaQueNaoEDeTeste()
    {
        var real = AppPaths.ForNormalUse();
        real.EnsureCreated();

        real.DeleteIfDisposable();

        // A pasta real do usuario nunca pode ser apagada por este caminho.
        Assert.True(Directory.Exists(real.RootFolder));
    }

    [Fact]
    public void ApagarRemovePastaDeTeste()
    {
        var paths = AppPaths.ForTests(Guid.NewGuid().ToString("n")[..8]);
        paths.EnsureCreated();
        Assert.True(Directory.Exists(paths.RootFolder));

        paths.DeleteIfDisposable();

        Assert.False(Directory.Exists(paths.RootFolder));
    }
}
