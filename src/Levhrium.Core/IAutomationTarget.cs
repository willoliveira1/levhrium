namespace Levhrium.Core;

/// <summary>
/// O que o hook de automacao consegue pedir ao navegador. Implementado pela janela
/// principal; mantido no Core para que o servidor de automacao nao conheca a UI.
/// Todas as chamadas chegam de uma thread de fundo: quem implementa e responsavel
/// por marshalar para a thread da UI.
/// </summary>
public interface IAutomationTarget
{
    Task<AutomationState> GetStateAsync();

    Task NavigateAsync(string input);

    Task GoBackAsync();

    Task GoForwardAsync();

    Task ReloadAsync();
}
