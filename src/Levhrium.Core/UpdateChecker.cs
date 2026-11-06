using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Levhrium.Core;

/// <summary>Resultado de uma verificacao de atualizacao.</summary>
public sealed record UpdateCheckResult
{
    public required bool HasUpdate { get; init; }

    public Version? PublishedVersion { get; init; }

    /// <summary>URL direta do instalador publicado. Sempre presente numa release valida.</summary>
    public string? InstallerUrl { get; init; }

    /// <summary>
    /// Manifesto com o hash de cada arquivo da versao publicada. Quando existe,
    /// o navegador consegue descobrir que so tres arquivos mudaram e baixar so
    /// eles, em vez dos 35 MB do instalador.
    /// </summary>
    public string? ManifestUrl { get; init; }

    /// <summary>Pacote com os arquivos que mudaram desde a release anterior.</summary>
    public string? DeltaUrl { get; init; }

    /// <summary>Verdadeiro quando da para tentar a atualizacao incremental.</summary>
    public bool SupportsIncremental =>
        HasUpdate && ManifestUrl is { Length: > 0 } && DeltaUrl is { Length: > 0 };

    public string? ReleaseNotes { get; init; }

    /// <summary>Preenchido quando a verificacao falhou (sem rede, API fora).</summary>
    public string? Error { get; init; }

    public static UpdateCheckResult UpToDate(Version published) =>
        new() { HasUpdate = false, PublishedVersion = published };

    public static UpdateCheckResult Failed(string message) =>
        new() { HasUpdate = false, Error = message };
}

/// <summary>
/// Pergunta ao GitHub qual e a ultima versao publicada. Fica no nucleo com a rede
/// injetada por <see cref="HttpMessageHandler"/>, para que os testes rodem sem
/// internet e sem depender da API estar de pe.
/// </summary>
public sealed class UpdateChecker
{
    /// <summary>Repositorio publico que hospeda as releases.</summary>
    public const string DefaultRepository = "willoliveira1/levhrium";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly string _repository;

    public UpdateChecker(HttpClient http, string repository = DefaultRepository)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);

        _http = http;
        _repository = repository;

        // A API do GitHub recusa requisicao sem User-Agent.
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
        {
            _http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("Levhrium", AppVersion.CurrentDisplay));
        }
    }

    public string LatestReleaseUrl => $"https://api.github.com/repos/{_repository}/releases/latest";

    public async Task<UpdateCheckResult> CheckAsync(
        Version current,
        CancellationToken cancellation = default)
    {
        try
        {
            using var response = await _http
                .GetAsync(LatestReleaseUrl, cancellation)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // 404 e o caso comum e util: ainda nao ha nenhuma release publicada.
                return UpdateCheckResult.Failed(
                    response.StatusCode == System.Net.HttpStatusCode.NotFound
                        ? "Nenhuma versao publicada ainda."
                        : $"O GitHub respondeu {(int)response.StatusCode}.");
            }

            var json = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
            return Evaluate(json, current);
        }
        catch (HttpRequestException ex)
        {
            return UpdateCheckResult.Failed("Nao foi possivel falar com o GitHub: " + ex.Message);
        }
        catch (TaskCanceledException)
        {
            return UpdateCheckResult.Failed("A verificacao demorou demais e foi cancelada.");
        }
    }

    /// <summary>Decide a partir do JSON da release. Separado para ser testavel sem rede.</summary>
    public static UpdateCheckResult Evaluate(string json, Version current)
    {
        GitHubRelease? release;

        try
        {
            release = JsonSerializer.Deserialize<GitHubRelease>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return UpdateCheckResult.Failed("A resposta do GitHub veio num formato inesperado.");
        }

        if (release is null || AppVersion.ParseTag(release.TagName) is not { } published)
        {
            return UpdateCheckResult.Failed("A ultima release nao tem uma tag de versao valida.");
        }

        if (!AppVersion.IsNewer(published, current))
        {
            return UpdateCheckResult.UpToDate(published);
        }

        // So instaladores: baixar um .zip de codigo-fonte nao atualiza nada.
        var installer = release.Assets?.FirstOrDefault(asset =>
            asset.Name is { Length: > 0 } name &&
            name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

        if (installer?.DownloadUrl is not { Length: > 0 } url)
        {
            return UpdateCheckResult.Failed(
                $"A versao {published.ToString(3)} existe, mas sem instalador anexado.");
        }

        return new UpdateCheckResult
        {
            HasUpdate = true,
            PublishedVersion = published,
            InstallerUrl = url,
            ManifestUrl = FindAsset(release, name =>
                name.StartsWith("manifest-", StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)),
            DeltaUrl = FindAsset(release, name =>
                name.EndsWith("-delta.zip", StringComparison.OrdinalIgnoreCase)),
            ReleaseNotes = release.Body,
        };
    }

    /// <summary>
    /// Devolve nulo quando o anexo nao existe -- releases antigas nao tem
    /// manifesto, e nesse caso o navegador cai para o instalador completo.
    /// </summary>
    private static string? FindAsset(GitHubRelease release, Func<string, bool> matches)
    {
        var asset = release.Assets?.FirstOrDefault(candidate =>
            candidate.Name is { Length: > 0 } name && matches(name));

        return asset?.DownloadUrl is { Length: > 0 } url ? url : null;
    }

    private sealed record GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; init; }

        public string? Body { get; init; }

        public IReadOnlyList<GitHubAsset>? Assets { get; init; }
    }

    private sealed record GitHubAsset
    {
        public string? Name { get; init; }

        [JsonPropertyName("browser_download_url")]
        public string? DownloadUrl { get; init; }
    }
}
