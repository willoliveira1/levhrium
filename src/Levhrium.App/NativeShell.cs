using System.Runtime.InteropServices;

namespace Levhrium.App;

/// <summary>
/// O botao fixado na barra de tarefas mostra o icone do atalho, nao o da
/// janela, e o Explorer serve esse icone de um cache proprio.
///
/// Trocar o icone numa atualizacao nao invalida o cache sozinho: quem ja tinha
/// o Levhrium fixado continua vendo o icone da versao anterior mesmo depois de
/// fechar e abrir -- so o reinicio do Explorer resolveria. Avisar o shell e o
/// que faz ele reler na hora.
/// </summary>
internal static class NativeShell
{
    /// <summary>SHCNE_ASSOCCHANGED: mudou a associacao de um arquivo, releia os icones.</summary>
    private const int AssociationChanged = 0x08000000;

    /// <summary>SHCNF_IDLIST, o formato que SHCNE_ASSOCCHANGED espera.</summary>
    private const uint IdList = 0x0000;

    internal static void RefreshIconCache() =>
        SHChangeNotify(AssociationChanged, IdList, nint.Zero, nint.Zero);

    // DllImport e nao LibraryImport pelo mesmo motivo de NativeTheme: o
    // LibraryImport exigiria AllowUnsafeBlocks no projeto inteiro.
    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);
}
