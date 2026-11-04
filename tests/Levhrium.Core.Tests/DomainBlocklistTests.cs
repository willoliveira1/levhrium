using Levhrium.Core;

namespace Levhrium.Core.Tests;

public sealed class DomainBlocklistTests
{
    [Fact]
    public void ListaVaziaNaoBloqueiaNada()
    {
        Assert.False(DomainBlocklist.Empty.IsBlocked("https://doubleclick.net/pixel"));
        Assert.Equal(0, DomainBlocklist.Empty.Count);
    }

    [Fact]
    public void BloqueiaDominioExato()
    {
        var list = new DomainBlocklist(["ads.test"]);

        Assert.True(list.IsBlocked("https://ads.test/banner.js"));
        Assert.False(list.IsBlocked("https://exemplo.com/"));
    }

    [Fact]
    public void BloqueiaSubdominioDeDominioListado()
    {
        var list = new DomainBlocklist(["doubleclick.net"]);

        // "pixel.ads.doubleclick.net" tem de cair por "doubleclick.net".
        Assert.True(list.IsBlocked("https://pixel.ads.doubleclick.net/x.gif"));
        Assert.True(list.IsBlocked("https://doubleclick.net/x.gif"));
    }

    [Fact]
    public void NaoBloqueiaDominioQueApenasTerminaParecido()
    {
        var list = new DomainBlocklist(["ads.test"]);

        // "naoads.test" nao e subdominio de "ads.test".
        Assert.False(list.IsBlocked("https://naoads.test/"));
        Assert.False(list.IsBlocked("https://meusads.test/"));
    }

    [Theory]
    [InlineData("0.0.0.0 ads.test", "https://ads.test/x", true)]
    [InlineData("127.0.0.1 tracker.test", "https://tracker.test/x", true)]
    [InlineData("ads2.test", "https://ads2.test/x", true)]
    public void LeFormatoHosts(string linha, string url, bool esperado)
    {
        var list = DomainBlocklist.Parse([linha]);

        Assert.Equal(esperado, list.IsBlocked(url));
    }

    [Fact]
    public void IgnoraComentariosELinhasVazias()
    {
        var list = DomainBlocklist.Parse(
        [
            "# cabecalho do arquivo",
            string.Empty,
            "   ",
            "0.0.0.0 ads.test   # anuncio",
            "# 0.0.0.0 naobloquear.test",
        ]);

        Assert.Equal(1, list.Count);
        Assert.True(list.IsBlocked("https://ads.test/"));
        Assert.False(list.IsBlocked("https://naobloquear.test/"));
    }

    [Fact]
    public void NaoBloqueiaLocalhostDeArquivoHosts()
    {
        // Todo arquivo hosts comeca com "127.0.0.1 localhost"; bloquear isso
        // quebraria qualquer desenvolvimento local.
        var list = DomainBlocklist.Parse(["127.0.0.1 localhost", "::1 localhost"]);

        Assert.False(list.IsBlocked("http://localhost:5000/"));
    }

    [Fact]
    public void DocumentoPrincipalNuncaEBloqueado()
    {
        var list = new DomainBlocklist(["ads.test"]);

        // Digitar um dominio listado na barra tem de abrir a pagina, nao um branco.
        Assert.False(list.ShouldBlock("https://ads.test/", isTopLevelDocument: true));
        Assert.True(list.ShouldBlock("https://ads.test/pixel.gif", isTopLevelDocument: false));
    }

    [Fact]
    public void UrlInvalidaNaoQuebra()
    {
        var list = new DomainBlocklist(["ads.test"]);

        Assert.False(list.IsBlocked("about:blank"));
        Assert.False(list.IsBlocked("nao e url"));
        Assert.False(list.IsBlocked(string.Empty));
    }

    [Fact]
    public void SementeEmbutidaCobreOsRastreadoresMaisComuns()
    {
        var list = DomainBlocklist.LoadOrSeed(userFilePath: null);

        Assert.True(list.IsBlocked("https://www.google-analytics.com/collect"));
        Assert.True(list.IsBlocked("https://stats.g.doubleclick.net/x"));
        Assert.True(list.IsBlocked("https://connect.facebook.net/sdk.js"));
        Assert.False(list.IsBlocked("https://poke.sigmaproject.online/play"));
    }

    [Fact]
    public void ArquivoDoUsuarioSeSomaASemente()
    {
        var path = Path.Combine(Path.GetTempPath(), $"levhrium-bl-{Guid.NewGuid():n}.txt");
        File.WriteAllLines(path, ["# minha lista", "0.0.0.0 meuanuncio.test"]);

        try
        {
            var list = DomainBlocklist.LoadOrSeed(path);

            Assert.True(list.IsBlocked("https://meuanuncio.test/x"));
            Assert.True(list.IsBlocked("https://doubleclick.net/x"));
            Assert.True(list.Count > DomainBlocklist.SeedDomains.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ArquivoAusenteNaoEErro()
    {
        var list = DomainBlocklist.LoadOrSeed(
            Path.Combine(Path.GetTempPath(), "levhrium-nao-existe.txt"));

        Assert.Equal(DomainBlocklist.SeedDomains.Count, list.Count);
    }

    [Fact]
    public void EntradaComEsquemaViraSoOHost()
    {
        var list = new DomainBlocklist(["https://ads.test/caminho"]);

        Assert.True(list.IsBlocked("https://ads.test/outro"));
    }
}
