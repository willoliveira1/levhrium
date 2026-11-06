namespace Levhrium.Core;

public sealed record UpdateApplyResult
{
    public required bool Success { get; init; }

    public string? Error { get; init; }

    public int Replaced { get; init; }

    public int Removed { get; init; }

    public static UpdateApplyResult Failed(string message) =>
        new() { Success = false, Error = message };
}

/// <summary>
/// Troca os arquivos da instalacao pelos que foram baixados, com o navegador
/// ainda aberto.
///
/// O truque e renomear em vez de sobrescrever: o Windows recusa apagar ou
/// gravar por cima de um .exe em execucao ou de uma .dll carregada, mas permite
/// renomear os dois. O processo atual segue usando o arquivo antigo ate fechar,
/// e o novo ja esta no lugar para a proxima abertura -- e por isso que a
/// atualizacao termina em "reinicie para aplicar" e nao num instalador.
///
/// Se qualquer passo falhar, tudo volta ao que era: uma instalacao pela metade
/// deixaria o navegador sem abrir.
/// </summary>
public static class UpdateApplier
{
    /// <summary>Sufixo dos arquivos aposentados, varridos na proxima abertura.</summary>
    public const string BackupSuffix = ".levhrium-old";

    public static bool IsBackup(string path) =>
        path.EndsWith(BackupSuffix, StringComparison.OrdinalIgnoreCase);

    public static UpdateApplyResult Apply(string installFolder, string stagingFolder, UpdatePlan plan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingFolder);
        ArgumentNullException.ThrowIfNull(plan);

        var install = Path.GetFullPath(installFolder);
        var staging = Path.GetFullPath(stagingFolder);

        if (!Directory.Exists(install))
        {
            return UpdateApplyResult.Failed($"A pasta de instalacao nao existe: {install}");
        }

        // Desfeitas na ordem inversa se algo falhar no meio.
        var renamed = new List<(string Target, string Backup)>();
        var placed = new List<string>();

        try
        {
            foreach (var file in plan.Download)
            {
                var source = Resolve(staging, file.Path);
                var target = Resolve(install, file.Path);

                // Rollback tambem aqui, nao so no catch: desistir no meio sem
                // desfazer deixaria metade dos arquivos trocados.
                if (source is null || target is null)
                {
                    Rollback(renamed, placed);
                    return UpdateApplyResult.Failed($"Caminho recusado: {file.Path}");
                }

                if (!File.Exists(source))
                {
                    Rollback(renamed, placed);
                    return UpdateApplyResult.Failed($"Faltou no pacote baixado: {file.Path}");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                if (File.Exists(target))
                {
                    renamed.Add((target, Retire(target)));
                }

                File.Move(source, target);
                placed.Add(target);
            }

            var removed = 0;

            foreach (var relative in plan.Delete)
            {
                var target = Resolve(install, relative);

                if (target is not null && File.Exists(target))
                {
                    renamed.Add((target, Retire(target)));
                    removed++;
                }
            }

            return new UpdateApplyResult
            {
                Success = true,
                Replaced = plan.Download.Count,
                Removed = removed,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Rollback(renamed, placed);
            return UpdateApplyResult.Failed(ex.Message);
        }
    }

    /// <summary>
    /// Apaga as sobras da atualizacao anterior. Chamada na abertura, quando as
    /// dlls antigas ja nao estao carregadas. Falhar aqui nao e problema: o
    /// arquivo fica e a proxima abertura tenta de novo.
    /// </summary>
    public static int CleanupBackups(string installFolder)
    {
        if (!Directory.Exists(installFolder))
        {
            return 0;
        }

        var removed = 0;

        foreach (var path in Directory.EnumerateFiles(
            installFolder, "*" + BackupSuffix, SearchOption.AllDirectories))
        {
            try
            {
                File.Delete(path);
                removed++;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return removed;
    }

    /// <summary>Tira o arquivo do caminho sem apaga-lo, mesmo carregado.</summary>
    private static string Retire(string target)
    {
        var backup = target + BackupSuffix;

        if (File.Exists(backup))
        {
            // Sobra de uma atualizacao anterior que ainda estava carregada.
            try
            {
                File.Delete(backup);
            }
            catch (IOException)
            {
                backup = target + "." + Guid.NewGuid().ToString("n")[..8] + BackupSuffix;
            }
            catch (UnauthorizedAccessException)
            {
                backup = target + "." + Guid.NewGuid().ToString("n")[..8] + BackupSuffix;
            }
        }

        File.Move(target, backup);
        return backup;
    }

    private static void Rollback(List<(string Target, string Backup)> renamed, List<string> placed)
    {
        foreach (var target in Enumerable.Reverse(placed))
        {
            try
            {
                File.Delete(target);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        foreach (var (target, backup) in Enumerable.Reverse(renamed))
        {
            try
            {
                if (File.Exists(backup) && !File.Exists(target))
                {
                    File.Move(backup, target);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// Junta o caminho relativo a raiz e confere que o resultado continua dentro
    /// dela. Segunda barreira contra travessia: a primeira e no manifesto, mas
    /// esta e a que protege na hora de escrever.
    /// </summary>
    private static string? Resolve(string root, string relativePath)
    {
        if (!UpdateManifest.IsSafeRelativePath(relativePath))
        {
            return null;
        }

        var full = Path.GetFullPath(Path.Combine(root, relativePath));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;

        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? full : null;
    }
}
