namespace Levhrium.App;

/// <summary>
/// Paleta escura. Centralizada porque a faixa de abas do M2 vai ser desenhada
/// a mao e precisa das mesmas cores da toolbar.
/// </summary>
internal static class Theme
{
    internal static readonly Color WindowBackground = Color.FromArgb(0x1E, 0x1E, 0x1E);
    internal static readonly Color ToolbarBackground = Color.FromArgb(0x2B, 0x2B, 0x2B);
    internal static readonly Color FieldBackground = Color.FromArgb(0x3A, 0x3A, 0x3A);
    internal static readonly Color Foreground = Color.FromArgb(0xE6, 0xE6, 0xE6);
    internal static readonly Color ForegroundDim = Color.FromArgb(0x8A, 0x8A, 0x8A);
    internal static readonly Color Accent = Color.FromArgb(0x4C, 0x9A, 0xFF);
}
