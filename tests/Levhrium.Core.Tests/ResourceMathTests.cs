using Levhrium.Core;

namespace Levhrium.Core.Tests;

/// <summary>
/// Contas do medidor de consumo. O número da barra de status muda sozinho o
/// tempo todo, então um erro de fator passa despercebido a olho -- é aqui que
/// ele tem de ser pego.
/// </summary>
public sealed class ResourceMathTests
{
    [Fact]
    public void UmNucleoOcupadoEmQuatroDaVinteECinco()
    {
        // 1s de CPU em 1s de relógio, numa máquina de 4 núcleos, é 25%.
        var percent = ResourceMath.CpuPercent(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1),
            cores: 4);

        Assert.Equal(25, percent, precision: 3);
    }

    [Fact]
    public void TodosOsNucleosOcupadosDaCem()
    {
        var percent = ResourceMath.CpuPercent(
            TimeSpan.FromSeconds(8),
            TimeSpan.FromSeconds(1),
            cores: 8);

        Assert.Equal(100, percent, precision: 3);
    }

    [Fact]
    public void NuncaPassaDeCem()
    {
        // Processo que nasceu entre duas leituras traz tempo de CPU anterior
        // ao intervalo, e a conta estoura.
        var percent = ResourceMath.CpuPercent(
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(1),
            cores: 8);

        Assert.Equal(100, percent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SemNucleosOuSemTempoDaZero(int cores)
    {
        Assert.Equal(0, ResourceMath.CpuPercent(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), cores));
    }

    [Fact]
    public void IntervaloZeroNaoDivide()
    {
        Assert.Equal(0, ResourceMath.CpuPercent(TimeSpan.FromSeconds(1), TimeSpan.Zero, cores: 4));
    }

    [Theory]
    [InlineData(0L, "0 MB")]
    [InlineData(812L * 1024 * 1024, "812 MB")]
    [InlineData(1024L * 1024 * 1024, "1,0 GB")]
    public void FormatoDeMemoria(long bytes, string esperado)
    {
        Assert.Equal(esperado, ResourceMath.FormatBytes(bytes));
    }

    [Fact]
    public void GigabyteSaiComUmaCasa()
    {
        var texto = ResourceMath.FormatBytes((long)(2.94 * 1024 * 1024 * 1024));

        Assert.Equal("2,9 GB", texto);
    }

    [Fact]
    public void LeituraFalhaNaoViraNumero()
    {
        Assert.Equal("--", ResourceMath.FormatBytes(-1));
    }

    [Theory]
    [InlineData(7.7, "7,7% CPU")]
    [InlineData(0.04, "0% CPU")]
    [InlineData(12.0, "12% CPU")]
    public void FormatoDeCpu(double percent, string esperado)
    {
        Assert.Equal(esperado, ResourceMath.FormatCpu(percent));
    }

    [Fact]
    public void AmostraVaziaSeDeclaraVazia()
    {
        var amostra = new ResourceSample { Bytes = 0, CpuPercent = 0, Processes = 0 };

        Assert.True(amostra.IsEmpty);
    }
}
