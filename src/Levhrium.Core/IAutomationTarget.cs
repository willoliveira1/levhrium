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

    /// <summary>Abre uma aba no container informado.</summary>
    Task NewTabAsync(string container, string url);

    Task CloseTabAsync(int index);

    Task SelectTabAsync(int index);

    Task PinTabAsync(int index, bool pinned);

    /// <summary>
    /// Dispara um atalho pelo mesmo despacho de teclas que o teclado usa, para que
    /// o teste prove a ligacao real e nao um atalho de codigo.
    /// </summary>
    Task HotkeyAsync(string name);

    /// <summary>Avalia a politica de hibernacao agora, sem esperar o proximo tique.</summary>
    Task HibernateTickAsync();

    /// <summary>Favorita ou desfavorita a aba ativa.</summary>
    Task ToggleBookmarkAsync();

    /// <summary>Zoom da aba ativa: "in", "out" ou "reset".</summary>
    Task ZoomAsync(string direction);

    /// <summary>Grava o estado agora, sem esperar o atraso.</summary>
    Task SaveStateAsync();
}
