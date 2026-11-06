using System.Diagnostics;
using System.IO.Compression;
using Levhrium.Core;
using Microsoft.Win32;

namespace Levhrium.App;

/// <summary>Atualizacao incremental pronta para ser aplicada.</summary>
internal sealed record PreparedUpdate
{
    internal required UpdatePlan Plan { get; init; }

    internal required string StagingFolder { get; init; }

    internal required Version Version { get; init; }
}

/// <summary>
/// Verifica, baixa e aplica a atualizacao. A decisao de "e mais nova?" e a de
/// "o que mudou?" moram no nucleo e sao testadas sem rede; aqui fica so o que
/// precisa de disco, processo e registro.
///
/// Dois caminhos, nesta ordem:
///
/// 1. Incremental -- compara o hash de cada arquivo instalado com o manifesto da
///    release e baixa so a diferenca. Numa atualizacao normal sao tres arquivos
///    (~650 KB), porque o runtime do .NET que ocupa os outros 112 MB nao muda.
///    Os arquivos sao trocados com o navegador aberto e valem na proxima vez
///    que ele abrir.
///
/// 2. Instalador completo -- quando nao ha manifesto (releases antigas), quando
///    o pacote baixado nao cobre o que o plano pediu, ou quando a troca falha.
///    Baixa 35 MB e reinstala por cima, que e o que o Levhrium fazia sempre.
/// </summary>
internal sealed class UpdateService : IDisposable
{
    /// <summary>AppId do instalador, usado para corrigir a versao no registro.</summary>
    private const string UninstallKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{8F2C5A31-4D7E-4C19-9B6A-7E3D1F8A2C45}_is1";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly UpdateChecker _checker;
    private readonly AppPaths _paths;

    internal UpdateService(AppPaths paths, string repository = UpdateChecker.DefaultRepository)
    {
        ArgumentNullException.ThrowIfNull(paths);

        _paths = paths;
        _checker = new UpdateChecker(_http, repository);
    }

    internal Task<UpdateCheckResult> CheckAsync(CancellationToken cancellation = default) =>
        _checker.CheckAsync(AppVersion.Current, cancellation);

    /// <summary>
    /// Monta a atualizacao incremental numa pasta a parte. Devolve nulo sempre
    /// que algo nao fecha -- e o sinal para o chamador cair no instalador
    /// completo, nunca para aplicar uma atualizacao parcial.
    /// </summary>
    internal async Task<PreparedUpdate?> PrepareIncrementalAsync(
        UpdateCheckResult check,
        IProgress<int>? progress = null,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(check);

        if (!check.SupportsIncremental || check.PublishedVersion is not { } version)
        {
            return null;
        }

        try
        {
            var json = await _http.GetStringAsync(check.ManifestUrl!, cancellation).ConfigureAwait(false);

            if (UpdateManifest.Parse(json) is not { } published)
            {
                return null;
            }

            // Hash dos 229 arquivos instalados: ~0,3 s, e so acontece depois de
            // ja sabermos que existe versao nova.
            var installed = UpdateManifest.Scan(AppPaths.InstallFolder, AppVersion.CurrentDisplay);
            var plan = UpdatePlan.Compute(installed.Files, published);

            if (plan.IsEmpty)
            {
                return null;
            }

            var staging = Path.Combine(_paths.UpdateFolder, version.ToString(3));
            ResetFolder(staging);

            var package = Path.Combine(_paths.UpdateFolder, "delta.zip");
            await DownloadToFileAsync(check.DeltaUrl!, package, progress, cancellation).ConfigureAwait(false);

            Extract(package, staging);
            File.Delete(package);

            // O pacote tem mesmo tudo o que o plano pediu, com o hash certo?
            var staged = UpdateManifest.Scan(staging, version.ToString(3));

            if (!plan.IsSatisfiedBy(staged.Files))
            {
                ResetFolder(staging);
                return null;
            }

            return new PreparedUpdate
            {
                Plan = plan,
                StagingFolder = staging,
                Version = version,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or
                                   UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>
    /// Troca os arquivos. O navegador segue aberto com o codigo antigo em
    /// memoria; a versao nova vale na proxima abertura.
    /// </summary>
    internal static UpdateApplyResult ApplyIncremental(PreparedUpdate prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);

        var result = UpdateApplier.Apply(AppPaths.InstallFolder, prepared.StagingFolder, prepared.Plan);

        if (!result.Success)
        {
            return result;
        }

        RecordInstalledVersion(prepared.Version);

        try
        {
            Directory.Delete(prepared.StagingFolder, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return result;
    }

    /// <summary>
    /// Fecha o navegador e abre a versao recem-instalada. Processo separado: o
    /// executavel no disco ja e o novo.
    /// </summary>
    internal static void RestartNow()
    {
        var exe = Path.Combine(AppPaths.InstallFolder, "Levhrium.exe");

        if (File.Exists(exe))
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        }

        Application.Exit();
    }

    /// <summary>
    /// Baixa o instalador completo para a pasta temporaria. Grava num arquivo
    /// com nome aleatorio e so entao renomeia: um download interrompido nunca
    /// vira um instalador pela metade pronto para rodar.
    /// </summary>
    internal async Task<string> DownloadAsync(
        string installerUrl,
        IProgress<int>? progress = null,
        CancellationToken cancellation = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installerUrl);

        var folder = Path.Combine(Path.GetTempPath(), "Levhrium-update");
        Directory.CreateDirectory(folder);

        var partial = Path.Combine(folder, $"setup-{Guid.NewGuid():n}.part");
        var final = Path.Combine(folder, "Levhrium-Setup.exe");

        await DownloadToFileAsync(installerUrl, partial, progress, cancellation).ConfigureAwait(false);

        File.Move(partial, final, overwrite: true);
        return final;
    }

    /// <summary>
    /// Inicia o instalador e devolve o controle: quem chamou precisa fechar o
    /// navegador em seguida, senao o instalador nao consegue substituir os
    /// arquivos em uso.
    /// </summary>
    internal static void LaunchInstaller(string installerPath)
    {
        Process.Start(new ProcessStartInfo(installerPath)
        {
            UseShellExecute = true,

            // /SILENT mostra a barra de progresso mas nao faz perguntas, e
            // /NOCANCEL impede parar no meio da troca dos arquivos.
            Arguments = "/SILENT /NOCANCEL /NORESTART",
        });
    }

    public void Dispose() => _http.Dispose();

    private async Task DownloadToFileAsync(
        string url,
        string destinationPath,
        IProgress<int>? progress,
        CancellationToken cancellation)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        using var response = await _http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1;
        var downloaded = 0L;

        await using var source = await response.Content
            .ReadAsStreamAsync(cancellation)
            .ConfigureAwait(false);

        await using var destination = File.Create(destinationPath);

        var buffer = new byte[81920];
        int read;

        while ((read = await source.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellation).ConfigureAwait(false);
            downloaded += read;

            if (total > 0)
            {
                progress?.Report((int)(downloaded * 100 / total));
            }
        }
    }

    /// <summary>
    /// Extrai entrada por entrada, conferindo o caminho de cada uma. Um zip pode
    /// trazer "../.." no nome de uma entrada e escrever fora da pasta de destino.
    /// </summary>
    private static void Extract(string packagePath, string destinationFolder)
    {
        using var archive = ZipFile.OpenRead(packagePath);

        var root = Path.GetFullPath(destinationFolder);

        foreach (var entry in archive.Entries)
        {
            // Entrada de pasta: nome vazio depois da ultima barra.
            if (entry.Name.Length == 0)
            {
                continue;
            }

            if (!UpdateManifest.IsSafeRelativePath(entry.FullName))
            {
                throw new InvalidDataException($"Entrada recusada no pacote: {entry.FullName}");
            }

            var target = Path.GetFullPath(Path.Combine(root, entry.FullName));

            if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Entrada recusada no pacote: {entry.FullName}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static void ResetFolder(string folder)
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        Directory.CreateDirectory(folder);
    }

    /// <summary>
    /// Acerta a versao em "Aplicativos e recursos". Sem isto o Windows seguiria
    /// mostrando a versao de quando o instalador rodou pela ultima vez, porque a
    /// atualizacao incremental nao passa por ele. Falhar aqui nao impede nada.
    /// </summary>
    private static void RecordInstalledVersion(Version version)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKey, writable: true);
            key?.SetValue("DisplayVersion", version.ToString(3));
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
    }
}
