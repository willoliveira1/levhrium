using Levhrium.Core;

namespace Levhrium.Core.Tests;

/// <summary>
/// A troca de arquivos com o navegador aberto. O teste que importa de verdade e
/// <see cref="TrocaArquivoSeguradoComoDllCarregada"/>: e a aposta sobre a qual
/// toda a atualizacao incremental se apoia.
/// </summary>
public sealed class UpdateApplierTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "levhrium-apply-" + Guid.NewGuid().ToString("n")[..8]);

    private readonly string _install;
    private readonly string _staging;

    public UpdateApplierTests()
    {
        _install = Path.Combine(_root, "Programs", "Levhrium");
        _staging = Path.Combine(_root, "staging");
        Directory.CreateDirectory(_install);
        Directory.CreateDirectory(_staging);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void WriteInstalled(string relativePath, string content) =>
        WriteTo(_install, relativePath, content);

    private void WriteStaged(string relativePath, string content) =>
        WriteTo(_staging, relativePath, content);

    private static void WriteTo(string root, string relativePath, string content)
    {
        var full = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private string InstalledPath(string relativePath) => Path.Combine(_install, relativePath);

    private static UpdatePlan PlanFor(string stagingFolder, params string[] paths)
    {
        var files = paths.Select(path => new UpdateFile
        {
            Path = path,
            Sha256 = UpdateManifest.HashFile(Path.Combine(stagingFolder, path)),
            Size = new FileInfo(Path.Combine(stagingFolder, path)).Length,
        }).ToList();

        return new UpdatePlan { Download = files, Delete = [], Unchanged = 0 };
    }

    [Fact]
    public void TrocaOArquivoEAposentaOAntigo()
    {
        WriteInstalled("levhrium.dll", "versao antiga");
        WriteStaged("levhrium.dll", "versao nova");

        var result = UpdateApplier.Apply(_install, _staging, PlanFor(_staging, "levhrium.dll"));

        Assert.True(result.Success);
        Assert.Equal(1, result.Replaced);
        Assert.Equal("versao nova", File.ReadAllText(InstalledPath("levhrium.dll")));
        Assert.Equal(
            "versao antiga",
            File.ReadAllText(InstalledPath("levhrium.dll" + UpdateApplier.BackupSuffix)));
    }

    [Fact]
    public void TrocaArquivoSeguradoComoDllCarregada()
    {
        WriteInstalled("levhrium.dll", "versao antiga");
        WriteStaged("levhrium.dll", "versao nova");

        // O Windows mapeia uma dll carregada com FileShare.Delete: apagar e
        // sobrescrever sao recusados, renomear e permitido. E essa brecha que
        // deixa a atualizacao acontecer com o navegador aberto.
        using (var carregada = new FileStream(
            InstalledPath("levhrium.dll"),
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete))
        {
            var result = UpdateApplier.Apply(_install, _staging, PlanFor(_staging, "levhrium.dll"));

            Assert.True(result.Success, result.Error);

            // O processo que segurava o arquivo continua lendo o conteudo antigo.
            using var leitor = new StreamReader(carregada);
            Assert.Equal("versao antiga", leitor.ReadToEnd());
        }

        // E quem abrir agora ja pega o novo.
        Assert.Equal("versao nova", File.ReadAllText(InstalledPath("levhrium.dll")));
    }

    [Fact]
    public void CriaSubpastaDeArquivoNovo()
    {
        WriteStaged("runtimes/win-x64/native/novo.dll", "conteudo");

        var result = UpdateApplier.Apply(
            _install, _staging, PlanFor(_staging, "runtimes/win-x64/native/novo.dll"));

        Assert.True(result.Success, result.Error);
        Assert.True(File.Exists(InstalledPath(Path.Combine("runtimes", "win-x64", "native", "novo.dll"))));
    }

    [Fact]
    public void ArquivoRemovidoSaiDoCaminhoMasNaoSomeNaHora()
    {
        WriteInstalled("velho.dll", "sobra");

        var plan = new UpdatePlan { Download = [], Delete = ["velho.dll"], Unchanged = 0 };
        var result = UpdateApplier.Apply(_install, _staging, plan);

        Assert.True(result.Success, result.Error);
        Assert.Equal(1, result.Removed);
        Assert.False(File.Exists(InstalledPath("velho.dll")));

        // Aposentado, nao apagado: o processo em execucao ainda pode precisar dele.
        Assert.True(File.Exists(InstalledPath("velho.dll" + UpdateApplier.BackupSuffix)));
    }

    [Fact]
    public void PacoteIncompletoDesfazTudo()
    {
        WriteInstalled("a.dll", "a original");
        WriteInstalled("b.dll", "b original");
        WriteStaged("a.dll", "a nova");
        WriteStaged("b.dll", "b nova");

        var plan = PlanFor(_staging, "a.dll", "b.dll");

        // O segundo arquivo some do pacote depois do plano pronto -- download
        // truncado, disco cheio, antivirus. A instalacao nao pode ficar mista.
        File.Delete(Path.Combine(_staging, "b.dll"));

        var result = UpdateApplier.Apply(_install, _staging, plan);

        Assert.False(result.Success);
        Assert.Equal("a original", File.ReadAllText(InstalledPath("a.dll")));
        Assert.Equal("b original", File.ReadAllText(InstalledPath("b.dll")));
        Assert.False(File.Exists(InstalledPath("a.dll" + UpdateApplier.BackupSuffix)));
    }

    [Fact]
    public void CaminhoComTravessiaERecusado()
    {
        WriteStaged("inocente.dll", "conteudo");

        var plan = new UpdatePlan
        {
            Download =
            [
                new UpdateFile
                {
                    Path = "../../fora.dll",
                    Sha256 = new string('a', 64),
                    Size = 8,
                },
            ],
            Delete = [],
            Unchanged = 0,
        };

        var result = UpdateApplier.Apply(_install, _staging, plan);

        Assert.False(result.Success);
        Assert.False(File.Exists(Path.Combine(_root, "Programs", "fora.dll")));
        Assert.False(File.Exists(Path.Combine(_root, "fora.dll")));
    }

    [Fact]
    public void LimpezaApagaAsSobras()
    {
        WriteInstalled("levhrium.dll", "atual");
        WriteInstalled("levhrium.dll" + UpdateApplier.BackupSuffix, "antigo");
        WriteInstalled("pt-BR/recursos.dll" + UpdateApplier.BackupSuffix, "antigo");

        var removed = UpdateApplier.CleanupBackups(_install);

        Assert.Equal(2, removed);
        Assert.True(File.Exists(InstalledPath("levhrium.dll")));
        Assert.Empty(Directory.EnumerateFiles(
            _install, "*" + UpdateApplier.BackupSuffix, SearchOption.AllDirectories));
    }

    [Fact]
    public void SegundaAtualizacaoReaproveitaONomeDaSobra()
    {
        WriteInstalled("levhrium.dll", "v1");
        WriteStaged("levhrium.dll", "v2");
        Assert.True(UpdateApplier.Apply(_install, _staging, PlanFor(_staging, "levhrium.dll")).Success);

        WriteStaged("levhrium.dll", "v3");
        var result = UpdateApplier.Apply(_install, _staging, PlanFor(_staging, "levhrium.dll"));

        Assert.True(result.Success, result.Error);
        Assert.Equal("v3", File.ReadAllText(InstalledPath("levhrium.dll")));
        Assert.Equal(
            "v2",
            File.ReadAllText(InstalledPath("levhrium.dll" + UpdateApplier.BackupSuffix)));
    }

    [Fact]
    public void PastaDeInstalacaoInexistenteFalhaSemExplodir()
    {
        var result = UpdateApplier.Apply(
            Path.Combine(_root, "nao-existe"),
            _staging,
            new UpdatePlan { Download = [], Delete = [], Unchanged = 0 });

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }
}
