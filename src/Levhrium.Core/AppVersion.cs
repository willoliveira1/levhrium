using System.Reflection;

namespace Levhrium.Core;

/// <summary>
/// Versao do Levhrium em execucao e comparacao com a publicada no GitHub.
/// A comparacao fica aqui, sem rede, para ser testavel: errar nela faria o
/// navegador oferecer uma atualizacao para tras.
/// </summary>
public static class AppVersion
{
    /// <summary>Versao deste binario, no formato "1.2.3".</summary>
    public static Version Current { get; } = ReadCurrent();

    public static string CurrentDisplay => Current.ToString(3);

    /// <summary>
    /// Le "v1.2.3" ou "1.2.3" de uma tag do GitHub. Devolve nulo quando a tag
    /// nao tem formato de versao -- tag solta nao pode virar atualizacao.
    /// </summary>
    public static Version? ParseTag(string? tag)
    {
        if (tag is not { Length: > 0 })
        {
            return null;
        }

        var text = tag.Trim();

        if (text.StartsWith('v') || text.StartsWith('V'))
        {
            text = text[1..];
        }

        // Ignora sufixos de pre-lancamento: "1.2.3-beta" e tratado como 1.2.3.
        var dash = text.IndexOf('-');
        if (dash > 0)
        {
            text = text[..dash];
        }

        return Version.TryParse(text, out var parsed) ? Normalize(parsed) : null;
    }

    /// <summary>Verdadeiro quando a versao publicada e estritamente mais nova.</summary>
    public static bool IsNewer(Version published, Version current) =>
        Normalize(published) > Normalize(current);

    /// <summary>
    /// Iguala o numero de componentes: Version trata 1.2 e 1.2.0 como diferentes,
    /// e uma tag "v1.2" nao pode parecer mais velha que a atual 1.2.0.
    /// </summary>
    private static Version Normalize(Version value) => new(
        value.Major,
        value.Minor,
        value.Build < 0 ? 0 : value.Build);

    private static Version ReadCurrent()
    {
        var informational = typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        // O SDK acrescenta "+<hash do commit>" na versao informacional.
        if (informational is { Length: > 0 })
        {
            var plus = informational.IndexOf('+');
            var text = plus > 0 ? informational[..plus] : informational;

            if (ParseTag(text) is { } parsed)
            {
                return parsed;
            }
        }

        return Normalize(typeof(AppVersion).Assembly.GetName().Version ?? new Version(0, 0, 0));
    }
}
