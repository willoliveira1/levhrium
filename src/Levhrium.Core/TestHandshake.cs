namespace Levhrium.Core;

/// <summary>
/// O que o app publica quando sobe em --test-mode. O teste le este arquivo em vez
/// de adivinhar a porta: a porta CDP e sorteada justamente para nao ser previsivel.
/// </summary>
public sealed record TestHandshake
{
    public required int ProcessId { get; init; }

    /// <summary>Porta efemera de depuracao remota onde o Playwright se conecta.</summary>
    public required int CdpPort { get; init; }

    /// <summary>Named pipe que dirige a casca (abas, hotkeys, estado).</summary>
    public required string PipeName { get; init; }

    /// <summary>Pasta descartavel de dados desta instancia de teste.</summary>
    public required string RootFolder { get; init; }
}
