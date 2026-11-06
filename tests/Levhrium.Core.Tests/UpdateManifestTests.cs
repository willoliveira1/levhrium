using Levhrium.Core;

namespace Levhrium.Core.Tests;

/// <summary>
/// O manifesto chega pela rede e diz onde escrever arquivos dentro da pasta do
/// programa. Por isso a maior parte destes testes e sobre recusar caminho ruim,
/// nao sobre aceitar caminho bom.
/// </summary>
public sealed class UpdateManifestTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "levhrium-manifest-" + Guid.NewGuid().ToString("n")[..8]);

    public UpdateManifestTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Write(string relativePath, string content)
    {
        var full = Path.Combine(_folder, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    [Fact]
    public void ScanRegistraCaminhoRelativoEHash()
    {
        Write("Levhrium.dll", "codigo");
        Write("pt-BR/recursos.dll", "traducao");

        var manifest = UpdateManifest.Scan(_folder, "1.0.5");

        Assert.Equal("1.0.5", manifest.Version);
        Assert.Equal(2, manifest.Files.Count);

        // Caminho normalizado: separador unico, minusculas.
        Assert.Contains(manifest.Files, file => file.Path == "pt-br/recursos.dll");
        Assert.All(manifest.Files, file => Assert.Equal(64, file.Sha256.Length));
        Assert.All(manifest.Files, file => Assert.True(file.Size > 0));
    }

    [Fact]
    public void ScanIgnoraSobrasDaAtualizacaoAnterior()
    {
        Write("Levhrium.dll", "novo");
        Write("Levhrium.dll" + UpdateApplier.BackupSuffix, "antigo");

        var manifest = UpdateManifest.Scan(_folder, "1.0.5");

        // A sobra nao pertence a instalacao: contada, viraria "apagar" eterno.
        Assert.Single(manifest.Files);
        Assert.Equal("levhrium.dll", manifest.Files[0].Path);
    }

    [Fact]
    public void ConteudoDiferenteDaHashDiferente()
    {
        var a = Write("a.txt", "um");
        var b = Write("b.txt", "dois");

        Assert.NotEqual(UpdateManifest.HashFile(a), UpdateManifest.HashFile(b));
    }

    [Fact]
    public void JsonVaiEVolta()
    {
        Write("Levhrium.dll", "codigo");
        var original = UpdateManifest.Scan(_folder, "1.0.5");

        var round = UpdateManifest.Parse(original.ToJson());

        Assert.NotNull(round);
        Assert.Equal(original.Version, round!.Version);
        Assert.Equal(original.Files[0].Sha256, round.Files[0].Sha256);
        Assert.Equal(original.TotalSize, round.TotalSize);
    }

    [Theory]
    [InlineData("../fora.txt")]
    [InlineData("pasta/../../fora.txt")]
    [InlineData("/raiz.txt")]
    [InlineData("C:/absoluto.txt")]
    [InlineData("a//b.txt")]
    [InlineData("./a.txt")]
    [InlineData("")]
    public void CaminhoInseguroERecusado(string path)
    {
        Assert.False(UpdateManifest.IsSafeRelativePath(path));
    }

    [Theory]
    [InlineData("Levhrium.dll")]
    [InlineData("pt-BR/recursos.dll")]
    [InlineData("runtimes/win-x64/native/WebView2Loader.dll")]
    public void CaminhoNormalEAceito(string path)
    {
        Assert.True(UpdateManifest.IsSafeRelativePath(path));
    }

    [Fact]
    public void ManifestoComTravessiaDerrubaOManifestoInteiro()
    {
        // Uma entrada so basta: se a lista veio adulterada, o resto dela tambem
        // nao merece confianca.
        var json = """
        {"version":"1.0.6","files":[
          {"path":"Levhrium.dll","sha256":"aa11bb22cc33dd44ee55ff6677889900aabbccddeeff00112233445566778899","size":10},
          {"path":"../../Inicializar/qualquer.exe","sha256":"bb11bb22cc33dd44ee55ff6677889900aabbccddeeff00112233445566778899","size":10}
        ]}
        """;

        Assert.Null(UpdateManifest.Parse(json));
    }

    [Theory]
    [InlineData("""{"version":"nao-e-versao","files":[]}""")]
    [InlineData("""{"version":"1.0.6","files":[{"path":"a.dll","sha256":"curto","size":1}]}""")]
    [InlineData("""{"version":"1.0.6","files":[{"path":"a.dll","sha256":"aa11bb22cc33dd44ee55ff6677889900aabbccddeeff00112233445566778899","size":-5}]}""")]
    [InlineData("isto nao e json")]
    public void ManifestoInvalidoViraNulo(string json)
    {
        Assert.Null(UpdateManifest.Parse(json));
    }
}
