namespace Levhrium.Core;

/// <summary>
/// Retrato do estado do navegador, do jeito que o hook de automacao devolve.
/// Vive no Core (sem UI) para que os testes possam desserializar sem referenciar o app.
/// </summary>
public sealed record AutomationState
{
    public required IReadOnlyList<AutomationTab> Tabs { get; init; }

    /// <summary>Indice da aba ativa, ou -1 se nao houver nenhuma.</summary>
    public required int ActiveTabIndex { get; init; }

    /// <summary>
    /// O que a barra de endereco mostra. Exposto porque e desenho da casca: o
    /// Playwright enxerga o DOM da pagina, nunca a barra.
    /// </summary>
    public required string AddressBarText { get; init; }
}

/// <summary>Uma aba, como a automacao a enxerga.</summary>
public sealed record AutomationTab
{
    public required string Url { get; init; }
    public required string Title { get; init; }

    /// <summary>Container (perfil do WebView2) ao qual a aba pertence.</summary>
    public required string Container { get; init; }

    /// <summary>Cor do selo do container, em ARGB.</summary>
    public required int ContainerColor { get; init; }

    public required bool CanGoBack { get; init; }
    public required bool CanGoForward { get; init; }
    public required bool IsLoading { get; init; }

    /// <summary>Aba fixada: isenta de hibernacao.</summary>
    public required bool Pinned { get; init; }

    /// <summary>Renderer congelado por TrySuspend.</summary>
    public required bool Suspended { get; init; }
}
