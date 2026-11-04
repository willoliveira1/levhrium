using System.Diagnostics;
using Levhrium.Core;

namespace Levhrium.App;

/// <summary>
/// Verifica, baixa e inicia a atualizacao. A decisao de "e mais nova?" mora no
/// nucleo e e testada sem rede; aqui fica so o que precisa de disco e processo.
/// </summary>
internal sealed class UpdateService : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly UpdateChecker _checker;

    internal UpdateService(string repository = UpdateChecker.DefaultRepository)
    {
        _checker = new UpdateChecker(_http, repository);
    }

    internal Task<UpdateCheckResult> CheckAsync(CancellationToken cancellation = default) =>
        _checker.CheckAsync(AppVersion.Current, cancellation);

    /// <summary>
    /// Baixa o instalador para a pasta temporaria. Grava num arquivo com nome
    /// aleatorio e so entao renomeia: um download interrompido nunca vira um
    /// instalador pela metade pronto para rodar.
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

        using (var response = await _http
            .GetAsync(installerUrl, HttpCompletionOption.ResponseHeadersRead, cancellation)
            .ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? -1;
            var downloaded = 0L;

            await using var source = await response.Content
                .ReadAsStreamAsync(cancellation)
                .ConfigureAwait(false);

            await using var destination = File.Create(partial);

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
}
