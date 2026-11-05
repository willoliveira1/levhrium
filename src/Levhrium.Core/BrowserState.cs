namespace Levhrium.Core;

/// <summary>Uma aba, como ela e gravada em disco.</summary>
public sealed record PersistedTab
{
    public required string Container { get; init; }
    public required string Url { get; init; }
    public required string Title { get; init; }
    public required bool Pinned { get; init; }
}

/// <summary>Um container e sua cor, para que o selo sobreviva ao fechamento.</summary>
public sealed record PersistedContainer
{
    public required string Name { get; init; }
    public required int Color { get; init; }
}

public sealed record Bookmark
{
    public required string Title { get; init; }
    public required string Url { get; init; }
    public DateTimeOffset AddedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record HistoryEntry
{
    public required string Title { get; init; }
    public required string Url { get; init; }
    public required DateTimeOffset VisitedAt { get; init; }

    /// <summary>Quantas vezes esta URL ja foi visitada.</summary>
    public int VisitCount { get; init; } = 1;
}

public sealed record DownloadEntry
{
    public required string FileName { get; init; }
    public required string Url { get; init; }
    public required string FilePath { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public bool Completed { get; init; }
}

/// <summary>
/// Tudo que o Levhrium guarda entre execucoes. Um unico documento JSON: sem
/// SQLite, uma dependencia a menos e menos RAM.
/// </summary>
public sealed record BrowserState
{
    /// <summary>Historico guardado; alem disso, as visitas mais antigas somem.</summary>
    public const int HistoryCapacity = 5000;

    public IReadOnlyList<PersistedTab> Tabs { get; init; } = [];

    public IReadOnlyList<PersistedContainer> Containers { get; init; } = [];

    public int ActiveTabIndex { get; init; } = -1;

    public IReadOnlyList<Bookmark> Bookmarks { get; init; } = [];

    public IReadOnlyList<HistoryEntry> History { get; init; } = [];

    public IReadOnlyList<DownloadEntry> Downloads { get; init; } = [];

    /// <summary>Fator de zoom por host, por exemplo "exemplo.com" -> 1.25.</summary>
    public IReadOnlyDictionary<string, double> ZoomByHost { get; init; } =
        new Dictionary<string, double>();

    public IReadOnlyList<ClosedTabEntry> ClosedTabs { get; init; } = [];

    /// <summary>
    /// Minutos de ociosidade antes de hibernar, escolhidos nas configuracoes.
    /// Nulo significa usar o padrao da politica.
    /// </summary>
    public int? HibernateAfterMinutes { get; init; }

    /// <summary>
    /// Dominios que nunca hibernam, escolhidos por quem usa. Subdominios contam:
    /// "jogo.com" protege "mundo.jogo.com".
    /// </summary>
    public IReadOnlyList<string> NeverHibernateDomains { get; init; } = [];
}
