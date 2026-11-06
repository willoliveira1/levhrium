namespace Levhrium.Core;

/// <summary>
/// O que precisa sair da rede para ir da versao instalada ate a publicada.
///
/// Existe porque o grosso de uma instalacao do Levhrium e o runtime do .NET, que
/// so muda quando trocamos a versao do SDK: comparando hash a hash, uma
/// atualizacao normal mexe em tres arquivos em vez de reinstalar 112 MB.
/// </summary>
public sealed record UpdatePlan
{
    public required IReadOnlyList<UpdateFile> Download { get; init; }

    /// <summary>Arquivos que a versao nova nao tem mais.</summary>
    public required IReadOnlyList<string> Delete { get; init; }

    /// <summary>Quantos arquivos ja estao corretos no disco.</summary>
    public required int Unchanged { get; init; }

    public long BytesToDownload => Download.Sum(file => file.Size);

    /// <summary>Verdadeiro quando o disco ja bate com o manifesto publicado.</summary>
    public bool IsEmpty => Download.Count == 0 && Delete.Count == 0;

    /// <summary>
    /// Compara o que esta instalado com o que a release publicou. Um arquivo so
    /// e baixado quando falta ou quando o hash difere -- tamanho igual com
    /// conteudo diferente continua sendo uma diferenca.
    /// </summary>
    public static UpdatePlan Compute(IEnumerable<UpdateFile> installed, UpdateManifest published)
    {
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(published);

        var local = new Dictionary<string, UpdateFile>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in installed)
        {
            local[UpdateManifest.Normalize(file.Path)] = file;
        }

        var download = new List<UpdateFile>();
        var unchanged = 0;
        var remotePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in published.Files)
        {
            var path = UpdateManifest.Normalize(file.Path);
            remotePaths.Add(path);

            if (local.TryGetValue(path, out var current) &&
                string.Equals(current.Sha256, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                unchanged++;
                continue;
            }

            download.Add(file with { Path = path });
        }

        // Sobras da versao anterior. Nunca apagamos nada que a release nova
        // ainda declare, e por isso a comparacao e sobre o manifesto inteiro.
        var delete = local.Keys
            .Where(path => !remotePaths.Contains(path) && !UpdateManifest.IsInstallerOwned(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        return new UpdatePlan
        {
            Download = download,
            Delete = delete,
            Unchanged = unchanged,
        };
    }

    /// <summary>
    /// Verdadeiro quando o pacote entregue traz todos os arquivos do plano, com
    /// o hash certo. Falso manda o atualizador cair para o instalador completo,
    /// em vez de aplicar uma atualizacao pela metade.
    /// </summary>
    public bool IsSatisfiedBy(IEnumerable<UpdateFile> staged)
    {
        ArgumentNullException.ThrowIfNull(staged);

        var available = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in staged)
        {
            available[UpdateManifest.Normalize(file.Path)] = file.Sha256;
        }

        return Download.All(file =>
            available.TryGetValue(UpdateManifest.Normalize(file.Path), out var hash) &&
            string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase));
    }
}
