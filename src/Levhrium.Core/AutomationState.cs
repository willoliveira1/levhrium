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

    /// <summary>Quantos dominios a lista de bloqueio carregou.</summary>
    public required int BlocklistSize { get; init; }

    /// <summary>Favoritos, historico e downloads, como a biblioteca os ve.</summary>
    public required AutomationLibrary Library { get; init; }

    /// <summary>
    /// Quantas teclas chegaram vindas de dentro da pagina, e qual foi a ultima.
    /// Existe para diagnosticar atalhos: zero aqui depois de apertar uma tecla
    /// significa que ela nem chega ao host.
    /// </summary>
    public required int KeysFromPage { get; init; }

    public required string LastKeyFromPage { get; init; }
}

/// <summary>Resumo da biblioteca para os testes.</summary>
public sealed record AutomationLibrary
{
    public required int BookmarkCount { get; init; }
    public required int HistoryCount { get; init; }
    public required int DownloadCount { get; init; }

    /// <summary>Verdadeiro se a URL da aba ativa esta nos favoritos.</summary>
    public required bool ActiveIsBookmarked { get; init; }

    /// <summary>Zoom aplicado a aba ativa.</summary>
    public required double ActiveZoom { get; init; }

    /// <summary>Titulos dos favoritos, do primeiro ao ultimo.</summary>
    public required IReadOnlyList<string> BookmarkTitles { get; init; }

    /// <summary>URLs do historico, do mais recente para o mais antigo.</summary>
    public required IReadOnlyList<string> HistoryUrls { get; init; }
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

    /// <summary>Requisicoes recusadas pela lista de dominios nesta aba.</summary>
    public required int BlockedRequests { get; init; }
}
