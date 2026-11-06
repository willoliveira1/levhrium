using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Levhrium.Core;

/// <summary>Um arquivo instalado, com o hash que identifica o conteudo.</summary>
public sealed record UpdateFile
{
    /// <summary>Caminho relativo a pasta de instalacao, sempre com "/".</summary>
    public required string Path { get; init; }

    public required string Sha256 { get; init; }

    public required long Size { get; init; }
}

/// <summary>
/// Lista de tudo que compoe uma versao instalada, com hash por arquivo. E o que
/// permite atualizar so o que mudou: o navegador compara o manifesto publicado
/// com os arquivos que tem no disco e baixa a diferenca.
///
/// Publicado como anexo da release (~30 KB) ao lado do instalador.
/// </summary>
public sealed record UpdateManifest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    /// <summary>Caracteres que nunca aparecem num caminho relativo valido.</summary>
    private static readonly char[] InvalidPathChars = ['\0', ':', '*', '?', '\"', '<', '>', '|'];

    public required string Version { get; init; }

    public required IReadOnlyList<UpdateFile> Files { get; init; }

    public long TotalSize => Files.Sum(file => file.Size);

    /// <summary>
    /// Le o manifesto publicado. Qualquer entrada com caminho inseguro derruba o
    /// manifesto inteiro em vez de ser ignorada em silencio: se a lista veio
    /// adulterada, nao da para confiar no resto dela.
    /// </summary>
    public static UpdateManifest? Parse(string json)
    {
        UpdateManifest? manifest;

        try
        {
            manifest = JsonSerializer.Deserialize<UpdateManifest>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (manifest is null || manifest.Files is null || AppVersion.ParseTag(manifest.Version) is null)
        {
            return null;
        }

        foreach (var file in manifest.Files)
        {
            if (!IsSafeRelativePath(file.Path) || file.Size < 0 || !IsHexHash(file.Sha256))
            {
                return null;
            }
        }

        return manifest;
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Hash de todos os arquivos de uma pasta, em caminhos relativos a ela.</summary>
    public static UpdateManifest Scan(string folder, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        var root = System.IO.Path.GetFullPath(folder);
        var files = new List<UpdateFile>();

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            // Sobras de uma atualizacao anterior nao fazem parte da instalacao.
            if (UpdateApplier.IsBackup(path))
            {
                continue;
            }

            files.Add(new UpdateFile
            {
                Path = Normalize(System.IO.Path.GetRelativePath(root, path)),
                Sha256 = HashFile(path),
                Size = new FileInfo(path).Length,
            });
        }

        files.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));

        return new UpdateManifest { Version = version, Files = files };
    }

    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>Separador unico e minusculas: Windows nao distingue, o JSON sim.</summary>
    public static string Normalize(string relativePath) =>
        relativePath.Replace('\\', '/').Trim().ToLowerInvariant();

    /// <summary>
    /// Barreira contra travessia de caminho. O manifesto vem da rede, e uma
    /// entrada como "../../Inicializar/qualquer.exe" escreveria fora da pasta do
    /// programa -- em vez de atualizar o navegador, plantaria um arquivo.
    /// </summary>
    public static bool IsSafeRelativePath(string? relativePath)
    {
        if (relativePath is not { Length: > 0 } path || path.Length > 260)
        {
            return false;
        }

        if (path.IndexOfAny(InvalidPathChars) >= 0)
        {
            return false;
        }

        if (System.IO.Path.IsPathRooted(path) || path[0] is '/' or '\\')
        {
            return false;
        }

        foreach (var segment in path.Split('/', '\\'))
        {
            // Segmento vazio pega "a//b"; ".." pega a travessia; "." nao acrescenta nada.
            if (segment is "" or "." or "..")
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Arquivos que pertencem ao instalador, nao a release. O desinstalador do
    /// Inno Setup mora na pasta do programa mas nunca aparece no manifesto --
    /// sem esta excecao, toda atualizacao incremental o marcaria como sobra e
    /// deixaria o Levhrium instalado sem como ser desinstalado.
    /// </summary>
    public static bool IsInstallerOwned(string relativePath)
    {
        var name = relativePath.Replace('\\', '/');
        var slash = name.LastIndexOf('/');

        if (slash >= 0)
        {
            // So na raiz da instalacao: uma subpasta chamada assim nao conta.
            return false;
        }

        return name.StartsWith("unins", StringComparison.OrdinalIgnoreCase) &&
            (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
             name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) ||
             name.EndsWith(".msg", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsHexHash(string? value) =>
        value is { Length: 64 } hash && hash.All(Uri.IsHexDigit);
}
