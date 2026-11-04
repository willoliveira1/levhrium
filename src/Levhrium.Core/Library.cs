namespace Levhrium.Core;

/// <summary>
/// Historico, favoritos, downloads e zoom por site. Toda a regra fica aqui, sem
/// janela, para que a deduplicacao e os limites sejam testaveis direto.
/// </summary>
public sealed class Library
{
    private readonly List<Bookmark> _bookmarks = [];
    private readonly List<HistoryEntry> _history = [];
    private readonly List<DownloadEntry> _downloads = [];
    private readonly Dictionary<string, double> _zoom = new(StringComparer.OrdinalIgnoreCase);

    public Library(BrowserState? state = null)
    {
        if (state is null)
        {
            return;
        }

        _bookmarks.AddRange(state.Bookmarks);
        _history.AddRange(state.History);
        _downloads.AddRange(state.Downloads);

        foreach (var (host, factor) in state.ZoomByHost)
        {
            _zoom[host] = factor;
        }
    }

    /// <summary>Favoritos na ordem em que foram adicionados.</summary>
    public IReadOnlyList<Bookmark> Bookmarks => _bookmarks;

    /// <summary>Historico do mais recente para o mais antigo.</summary>
    public IReadOnlyList<HistoryEntry> History => _history;

    /// <summary>Downloads do mais recente para o mais antigo.</summary>
    public IReadOnlyList<DownloadEntry> Downloads => _downloads;

    public IReadOnlyDictionary<string, double> ZoomByHost => _zoom;

    // ---- Favoritos ----

    /// <summary>
    /// Alterna o favorito da URL e devolve se ficou favoritada. Ctrl+D numa
    /// pagina ja favoritada desfavorita, como em qualquer navegador.
    /// </summary>
    public bool ToggleBookmark(string url, string title)
    {
        if (!IsRecordable(url))
        {
            return false;
        }

        var existing = _bookmarks.FindIndex(b => UrlEquals(b.Url, url));
        if (existing >= 0)
        {
            _bookmarks.RemoveAt(existing);
            return false;
        }

        _bookmarks.Add(new Bookmark
        {
            Url = url,
            Title = title is { Length: > 0 } ? title : url,
        });

        return true;
    }

    public bool IsBookmarked(string url) => _bookmarks.Any(b => UrlEquals(b.Url, url));

    // ---- Historico ----

    /// <summary>
    /// Registra uma visita. Revisitar a mesma URL nao cria entrada nova: sobe a
    /// contagem e move para o topo, senao recarregar uma pagina enterraria o
    /// historico inteiro.
    /// </summary>
    public void RecordVisit(string url, string title, DateTimeOffset? at = null)
    {
        if (!IsRecordable(url))
        {
            return;
        }

        var when = at ?? DateTimeOffset.UtcNow;
        var existing = _history.FindIndex(h => UrlEquals(h.Url, url));

        if (existing >= 0)
        {
            var previous = _history[existing];
            _history.RemoveAt(existing);
            _history.Insert(0, previous with
            {
                Title = title is { Length: > 0 } ? title : previous.Title,
                VisitedAt = when,
                VisitCount = previous.VisitCount + 1,
            });
        }
        else
        {
            _history.Insert(0, new HistoryEntry
            {
                Url = url,
                Title = title is { Length: > 0 } ? title : url,
                VisitedAt = when,
            });
        }

        if (_history.Count > BrowserState.HistoryCapacity)
        {
            _history.RemoveRange(
                BrowserState.HistoryCapacity,
                _history.Count - BrowserState.HistoryCapacity);
        }
    }

    public void ClearHistory() => _history.Clear();

    // ---- Downloads ----

    public void RecordDownload(string url, string filePath, DateTimeOffset? at = null)
    {
        _downloads.Insert(0, new DownloadEntry
        {
            Url = url,
            FilePath = filePath,
            FileName = Path.GetFileName(filePath) is { Length: > 0 } name ? name : filePath,
            StartedAt = at ?? DateTimeOffset.UtcNow,
        });
    }

    public void MarkDownloadCompleted(string filePath)
    {
        var index = _downloads.FindIndex(d =>
            string.Equals(d.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
        {
            _downloads[index] = _downloads[index] with { Completed = true };
        }
    }

    // ---- Zoom por site ----

    /// <summary>
    /// Zoom do host, ou 1.0. Por host e nao por aba: ajustar o zoom de um site
    /// uma vez tem de valer da proxima vez que ele abrir.
    /// </summary>
    public double ZoomFor(string url) =>
        HostOf(url) is { } host && _zoom.TryGetValue(host, out var factor) ? factor : 1.0;

    public void SetZoom(string url, double factor)
    {
        if (HostOf(url) is not { } host)
        {
            return;
        }

        var clamped = Math.Clamp(factor, 0.25, 5.0);

        if (Math.Abs(clamped - 1.0) < 0.001)
        {
            _zoom.Remove(host);
            return;
        }

        _zoom[host] = clamped;
    }

    /// <summary>Degraus de zoom iguais aos do Chrome.</summary>
    public static double NextZoomStep(double current, int direction)
    {
        double[] steps = [0.25, 0.33, 0.5, 0.67, 0.75, 0.8, 0.9, 1.0, 1.1, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0, 4.0, 5.0];

        var index = Array.FindIndex(steps, s => Math.Abs(s - current) < 0.001);
        if (index < 0)
        {
            index = Array.FindLastIndex(steps, s => s <= current);
            if (index < 0)
            {
                index = 0;
            }
        }

        return steps[Math.Clamp(index + direction, 0, steps.Length - 1)];
    }

    /// <summary>Monta o estado para gravacao, somando o que a sessao sabe.</summary>
    public BrowserState ToState(
        IReadOnlyList<PersistedTab> tabs,
        IReadOnlyList<PersistedContainer> containers,
        int activeTabIndex,
        IReadOnlyList<ClosedTabEntry> closedTabs) => new()
        {
            Tabs = tabs,
            Containers = containers,
            ActiveTabIndex = activeTabIndex,
            Bookmarks = _bookmarks,
            History = _history,
            Downloads = _downloads,
            ZoomByHost = _zoom,
            ClosedTabs = closedTabs,
        };

    private static string? HostOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed) && parsed.Host.Length > 0
            ? parsed.Host
            : null;

    /// <summary>about:blank e afins nao entram em historico nem em favoritos.</summary>
    private static bool IsRecordable(string url) =>
        url is { Length: > 0 } &&
        Uri.TryCreate(url, UriKind.Absolute, out var parsed) &&
        (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);

    private static bool UrlEquals(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
