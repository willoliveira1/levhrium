using Levhrium.Core;

namespace Levhrium.Core.Tests;

/// <summary>
/// O plano e o que transforma "existe versao nova" em "baixe estes tres
/// arquivos". Errar para mais custa banda; errar para menos deixa a instalacao
/// pela metade, que e bem pior.
/// </summary>
public sealed class UpdatePlanTests
{
    private const string HashA = "aa11bb22cc33dd44ee55ff6677889900aabbccddeeff00112233445566778899";
    private const string HashB = "bb11bb22cc33dd44ee55ff6677889900aabbccddeeff00112233445566778899";

    private static UpdateFile File(string path, string hash, long size = 100) =>
        new() { Path = path, Sha256 = hash, Size = size };

    private static UpdateManifest Manifest(params UpdateFile[] files) =>
        new() { Version = "1.0.6", Files = files };

    [Fact]
    public void ArquivoIgualNaoEBaixado()
    {
        var plan = UpdatePlan.Compute(
            [File("coreclr.dll", HashA)],
            Manifest(File("coreclr.dll", HashA)));

        Assert.True(plan.IsEmpty);
        Assert.Equal(1, plan.Unchanged);
        Assert.Equal(0, plan.BytesToDownload);
    }

    [Fact]
    public void SoOQueMudouEntraNoPlano()
    {
        // O caso real: o runtime inteiro fica, so o nosso codigo muda.
        var instalado = new[]
        {
            File("coreclr.dll", HashA, 4_400_000),
            File("system.windows.forms.dll", HashA, 13_100_000),
            File("levhrium.dll", HashA, 286_720),
        };

        var publicado = Manifest(
            File("coreclr.dll", HashA, 4_400_000),
            File("system.windows.forms.dll", HashA, 13_100_000),
            File("levhrium.dll", HashB, 290_000));

        var plan = UpdatePlan.Compute(instalado, publicado);

        Assert.Equal(2, plan.Unchanged);
        Assert.Single(plan.Download);
        Assert.Equal("levhrium.dll", plan.Download[0].Path);
        Assert.Equal(290_000, plan.BytesToDownload);
    }

    [Fact]
    public void ArquivoNovoEBaixado()
    {
        var plan = UpdatePlan.Compute(
            [File("levhrium.dll", HashA)],
            Manifest(File("levhrium.dll", HashA), File("Levhrium.Novo.dll", HashB)));

        Assert.Single(plan.Download);
        Assert.Equal("levhrium.novo.dll", plan.Download[0].Path);
    }

    [Fact]
    public void ArquivoQueSumiuEntraParaRemocao()
    {
        var plan = UpdatePlan.Compute(
            [File("levhrium.dll", HashA), File("velho.dll", HashA)],
            Manifest(File("levhrium.dll", HashA)));

        Assert.Empty(plan.Download);
        Assert.Equal(["velho.dll"], plan.Delete);
        Assert.False(plan.IsEmpty);
    }

    [Fact]
    public void CaixaDoCaminhoNaoImporta()
    {
        // O Windows nao distingue maiuscula de minuscula; o JSON distingue, e
        // sem isto o mesmo arquivo apareceria como novo e como removido.
        var plan = UpdatePlan.Compute(
            [File("Levhrium.DLL", HashA)],
            Manifest(File("levhrium.dll", HashA)));

        Assert.True(plan.IsEmpty);
        Assert.Equal(1, plan.Unchanged);
    }

    [Fact]
    public void MesmoTamanhoComConteudoDiferenteAindaEMudanca()
    {
        var plan = UpdatePlan.Compute(
            [File("levhrium.dll", HashA, 1000)],
            Manifest(File("levhrium.dll", HashB, 1000)));

        Assert.Single(plan.Download);
    }

    [Fact]
    public void PacoteCompletoSatisfazOPlano()
    {
        var plan = UpdatePlan.Compute(
            [File("levhrium.dll", HashA)],
            Manifest(File("levhrium.dll", HashB)));

        Assert.True(plan.IsSatisfiedBy([File("levhrium.dll", HashB)]));
    }

    [Theory]
    [InlineData("levhrium.dll", HashA)]
    [InlineData("outro.dll", HashB)]
    public void PacoteQueNaoCobreOPlanoERecusado(string path, string hash)
    {
        // Hash errado ou arquivo faltando: em vez de aplicar pela metade, o
        // atualizador cai para o instalador completo.
        var plan = UpdatePlan.Compute(
            [File("levhrium.dll", HashA)],
            Manifest(File("levhrium.dll", HashB)));

        Assert.False(plan.IsSatisfiedBy([File(path, hash)]));
    }

    [Fact]
    public void DesinstaladorNuncaEMarcadoComoSobra()
    {
        // unins000.exe e unins000.dat sao do Inno Setup, ficam na pasta do
        // programa e nunca aparecem no manifesto. Tratados como sobra, cada
        // atualizacao aposentaria o desinstalador do Levhrium.
        var plan = UpdatePlan.Compute(
            [
                File("levhrium.dll", HashA),
                File("unins000.exe", HashA),
                File("unins000.dat", HashA),
            ],
            Manifest(File("levhrium.dll", HashA)));

        Assert.Empty(plan.Delete);
        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void ArquivoDeDentroDeSubpastaNaoEscapaPelaRegraDoDesinstalador()
    {
        // A excecao vale so na raiz: "ferramentas/unins000.exe" nao e do Inno.
        var plan = UpdatePlan.Compute(
            [File("levhrium.dll", HashA), File("ferramentas/unins000.exe", HashA)],
            Manifest(File("levhrium.dll", HashA)));

        Assert.Equal(["ferramentas/unins000.exe"], plan.Delete);
    }

    [Fact]
    public void InstalacaoVaziaBaixaTudo()
    {
        var plan = UpdatePlan.Compute(
            [],
            Manifest(File("a.dll", HashA, 10), File("b.dll", HashB, 20)));

        Assert.Equal(2, plan.Download.Count);
        Assert.Equal(30, plan.BytesToDownload);
        Assert.Equal(0, plan.Unchanged);
    }
}
