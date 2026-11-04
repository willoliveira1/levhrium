using System.Runtime.InteropServices;

namespace Levhrium.App;

/// <summary>
/// A barra de titulo e do Windows (decisao da rodada 4), mas ela nao fica clara
/// num app escuro: o DWM aceita que a janela peca o tema escuro.
/// </summary>
internal static class NativeTheme
{
    /// <summary>DWMWA_USE_IMMERSIVE_DARK_MODE a partir do Windows 10 build 18985.</summary>
    private const int UseImmersiveDarkMode = 20;

    /// <summary>Valor anterior do mesmo atributo, para builds do Windows 10 mais antigas.</summary>
    private const int UseImmersiveDarkModeLegacy = 19;

    internal static void ApplyDarkTitleBar(nint windowHandle)
    {
        var enabled = 1;

        if (DwmSetWindowAttribute(windowHandle, UseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
        {
            // Windows 10 anterior ao 18985 usa o atributo antigo; se este tambem
            // falhar, a janela so fica com a barra clara -- nao e motivo de erro.
            _ = DwmSetWindowAttribute(windowHandle, UseImmersiveDarkModeLegacy, ref enabled, sizeof(int));
        }
    }

    // DllImport e nao LibraryImport de proposito: LibraryImport exigiria
    // AllowUnsafeBlocks no projeto inteiro por causa desta unica chamada.
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint hwnd,
        int attribute,
        ref int value,
        int valueSize);
}
