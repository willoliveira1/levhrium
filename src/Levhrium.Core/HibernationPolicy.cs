namespace Levhrium.Core;

/// <summary>O que a politica precisa saber de uma aba para decidir.</summary>
public readonly record struct TabHibernationInfo
{
    public required bool IsActive { get; init; }
    public required bool Pinned { get; init; }
    public required bool IsSuspended { get; init; }
    public required string Url { get; init; }

    /// <summary>Ultima vez que a aba esteve ativa.</summary>
    public required DateTimeOffset LastActiveAt { get; init; }
}

/// <summary>O que fazer com uma aba nesta rodada da politica.</summary>
public enum HibernationAction
{
    /// <summary>Nada a fazer.</summary>
    None,

    /// <summary>Congelar o renderer (TrySuspend).</summary>
    Suspend,

    /// <summary>Descongelar, porque a aba voltou a ser a ativa.</summary>
    Resume,
}

/// <summary>
/// Decide quais abas hibernar. Fica no nucleo porque e a regra onde um erro
/// silencioso custaria progresso de jogo: hibernar a aba errada trava um idle.
///
/// Nunca descarta, so congela (TrySuspend): retomar e instantaneo e o estado da
/// pagina continua vivo.
/// </summary>
public sealed class HibernationPolicy
{
    public static readonly TimeSpan DefaultIdleThreshold = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Dominios que nunca hibernam. Abas destes dominios tambem nascem fixadas:
    /// um jogo idle congelado para de progredir.
    /// </summary>
    public static readonly IReadOnlySet<string> DefaultNeverHibernate =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "poke.sigmaproject.online",
        };

    private readonly HashSet<string> _neverHibernate;

    public HibernationPolicy(TimeSpan? idleThreshold = null, IEnumerable<string>? neverHibernate = null)
    {
        IdleThreshold = idleThreshold ?? DefaultIdleThreshold;
        _neverHibernate = new HashSet<string>(
            neverHibernate ?? DefaultNeverHibernate,
            StringComparer.OrdinalIgnoreCase);
    }

    public TimeSpan IdleThreshold { get; }

    public IReadOnlyCollection<string> NeverHibernateDomains => _neverHibernate;

    /// <summary>Habilitada quando o limiar e positivo; zero ou negativo desliga tudo.</summary>
    public bool Enabled => IdleThreshold > TimeSpan.Zero;

    public HibernationAction Decide(TabHibernationInfo tab, DateTimeOffset now)
    {
        // A aba ativa sempre volta, mesmo que a hibernacao esteja desligada.
        if (tab.IsActive)
        {
            return tab.IsSuspended ? HibernationAction.Resume : HibernationAction.None;
        }

        if (!Enabled || tab.IsSuspended || tab.Pinned || IsNeverHibernate(tab.Url))
        {
            return HibernationAction.None;
        }

        return now - tab.LastActiveAt >= IdleThreshold
            ? HibernationAction.Suspend
            : HibernationAction.None;
    }

    /// <summary>
    /// Abas de dominio protegido nascem fixadas, para que a protecao sobreviva a
    /// uma mudanca da lista e fique visivel na faixa de abas.
    /// </summary>
    public bool ShouldPinOnOpen(string url) => IsNeverHibernate(url);

    /// <summary>Compara host e tambem subdominios: "a.jogo.com" casa com "jogo.com".</summary>
    public bool IsNeverHibernate(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        var host = parsed.Host;
        if (_neverHibernate.Contains(host))
        {
            return true;
        }

        return _neverHibernate.Any(domain =>
            host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));
    }
}
