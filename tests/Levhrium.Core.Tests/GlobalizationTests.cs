using System.Globalization;

namespace Levhrium.Core.Tests;

/// <summary>
/// Trava de configuracao, nao de codigo: com InvariantGlobalization ligado o
/// WinForms estoura ao trocar o layout de teclado, porque
/// InputLanguageChangedEventArgs chama CultureInfo.GetCultureInfo(lcid) e em
/// modo invariante nao existe cultura nenhuma. Aconteceu em producao.
/// </summary>
public sealed class GlobalizationTests
{
    [Theory]
    [InlineData(1046)] // pt-BR
    [InlineData(1033)] // en-US
    [InlineData(2070)] // pt-PT
    public void CulturaDeLayoutDeTecladoPodeSerResolvida(int lcid)
    {
        var culture = CultureInfo.GetCultureInfo(lcid);

        // Nome vazio significa modo invariante: resolve sem estourar, mas sem
        // dado nenhum -- datas e numeros sairiam no formato errado.
        Assert.False(
            string.IsNullOrEmpty(culture.Name),
            $"LCID {lcid} resolveu para cultura vazia: InvariantGlobalization esta ligado.");
    }

    [Fact]
    public void FormatoDeDataBrasileiroFunciona()
    {
        var data = new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal("04/11/2026", data.ToString("d", CultureInfo.GetCultureInfo("pt-BR")));
    }
}
