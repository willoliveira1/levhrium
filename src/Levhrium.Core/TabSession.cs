namespace Levhrium.Core;

/// <summary>Uma aba, do ponto de vista do nucleo: sem janela, sem WebView2.</summary>
public sealed class TabModel
{
    public required Guid Id { get; init; }

    public required Container Container { get; init; }

    public string Url { get; set; } = "about:blank";

    public string Title { get; set; } = string.Empty;

    /// <summary>Aba fixada nunca hiberna (ver politica no M3).</summary>
    public bool Pinned { get; set; }
}

/// <summary>Uma aba fechada, com o bastante para reabri-la onde e como estava.</summary>
public sealed record ClosedTabEntry
{
    public required string ContainerName { get; init; }
    public required string Url { get; init; }

    /// <summary>Posicao que a aba ocupava, para reabrir no mesmo lugar.</summary>
    public required int Index { get; init; }

    public required bool Pinned { get; init; }
}

/// <summary>
/// Dona da lista de abas, da aba ativa e da pilha de fechadas. Fica no nucleo de
/// proposito: e a logica onde um bug silencioso custaria progresso de jogo, entao
/// precisa ser testavel sem abrir janela.
/// </summary>
public sealed class TabSession
{
    /// <summary>Quantas abas fechadas o Ctrl+Shift+T consegue alcancar.</summary>
    public const int ClosedHistoryCapacity = 25;

    private readonly List<TabModel> _tabs = [];
    private readonly List<ClosedTabEntry> _closed = [];

    public TabSession(ContainerRegistry containers)
    {
        ArgumentNullException.ThrowIfNull(containers);
        Containers = containers;
    }

    public event EventHandler<TabModel>? TabOpened;

    public event EventHandler<TabModel>? TabClosed;

    public event EventHandler<TabModel?>? ActiveChanged;

    /// <summary>Disparado quando a ordem muda, para a faixa de abas se redesenhar.</summary>
    public event EventHandler? OrderChanged;

    public ContainerRegistry Containers { get; }

    public IReadOnlyList<TabModel> Tabs => _tabs;

    public int ActiveIndex { get; private set; } = -1;

    public TabModel? Active => ActiveIndex >= 0 && ActiveIndex < _tabs.Count ? _tabs[ActiveIndex] : null;

    public int ClosedCount => _closed.Count;

    public TabModel Open(string containerName, string url, int? atIndex = null, bool activate = true)
    {
        var container = Containers.GetOrCreate(containerName);
        var tab = new TabModel
        {
            Id = Guid.NewGuid(),
            Container = container,
            Url = url,
        };

        var index = atIndex is { } requested
            ? Math.Clamp(requested, 0, _tabs.Count)
            : _tabs.Count;

        _tabs.Insert(index, tab);

        if (ActiveIndex >= index)
        {
            ActiveIndex++;
        }

        TabOpened?.Invoke(this, tab);
        OrderChanged?.Invoke(this, EventArgs.Empty);

        if (activate || ActiveIndex < 0)
        {
            Activate(index);
        }

        return tab;
    }

    /// <summary>
    /// Fecha a aba e empilha o suficiente para o Ctrl+Shift+T reabri-la no mesmo
    /// container -- sem isso, reabrir a Conta 2 a traria dentro da Conta 1.
    /// </summary>
    public ClosedTabEntry? Close(Guid id)
    {
        var index = _tabs.FindIndex(tab => tab.Id == id);
        if (index < 0)
        {
            return null;
        }

        var tab = _tabs[index];
        var entry = new ClosedTabEntry
        {
            ContainerName = tab.Container.Name,
            Url = tab.Url,
            Index = index,
            Pinned = tab.Pinned,
        };

        _tabs.RemoveAt(index);
        PushClosed(entry);

        if (_tabs.Count == 0)
        {
            ActiveIndex = -1;
        }
        else if (index < ActiveIndex)
        {
            ActiveIndex--;
        }
        else if (index == ActiveIndex)
        {
            // Ativa a vizinha a direita; se era a ultima, a da esquerda.
            ActiveIndex = Math.Min(index, _tabs.Count - 1);
        }

        TabClosed?.Invoke(this, tab);
        OrderChanged?.Invoke(this, EventArgs.Empty);
        ActiveChanged?.Invoke(this, Active);

        return entry;
    }

    /// <summary>Reabre a ultima fechada no container, URL e posicao originais.</summary>
    public TabModel? ReopenLastClosed()
    {
        if (_closed.Count == 0)
        {
            return null;
        }

        var entry = _closed[^1];
        _closed.RemoveAt(_closed.Count - 1);

        var tab = Open(entry.ContainerName, entry.Url, entry.Index);
        tab.Pinned = entry.Pinned;
        return tab;
    }

    public void Activate(int index)
    {
        if (_tabs.Count == 0)
        {
            ActiveIndex = -1;
            ActiveChanged?.Invoke(this, null);
            return;
        }

        var clamped = Math.Clamp(index, 0, _tabs.Count - 1);
        if (clamped == ActiveIndex)
        {
            return;
        }

        ActiveIndex = clamped;
        ActiveChanged?.Invoke(this, Active);
    }

    public void Activate(Guid id)
    {
        var index = _tabs.FindIndex(tab => tab.Id == id);
        if (index >= 0)
        {
            Activate(index);
        }
    }

    /// <summary>Ctrl+Tab: proxima aba a direita, em ordem visual, dando a volta.</summary>
    public void ActivateNext() => CycleBy(1);

    /// <summary>Ctrl+Shift+Tab: proxima aba a esquerda, dando a volta.</summary>
    public void ActivatePrevious() => CycleBy(-1);

    /// <summary>Move uma aba de posicao, mantendo a mesma aba ativa.</summary>
    public void Move(int from, int to)
    {
        if (_tabs.Count < 2)
        {
            return;
        }

        from = Math.Clamp(from, 0, _tabs.Count - 1);
        to = Math.Clamp(to, 0, _tabs.Count - 1);

        if (from == to)
        {
            return;
        }

        var active = Active;
        var tab = _tabs[from];
        _tabs.RemoveAt(from);
        _tabs.Insert(to, tab);

        if (active is not null)
        {
            ActiveIndex = _tabs.IndexOf(active);
        }

        OrderChanged?.Invoke(this, EventArgs.Empty);
    }

    public TabModel? Find(Guid id) => _tabs.FirstOrDefault(tab => tab.Id == id);

    private void CycleBy(int delta)
    {
        if (_tabs.Count == 0)
        {
            return;
        }

        var next = (ActiveIndex + delta + _tabs.Count) % _tabs.Count;
        Activate(next);
    }

    private void PushClosed(ClosedTabEntry entry)
    {
        _closed.Add(entry);

        if (_closed.Count > ClosedHistoryCapacity)
        {
            _closed.RemoveAt(0);
        }
    }
}
