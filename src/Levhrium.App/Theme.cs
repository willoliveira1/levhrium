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

    /// <summary>Fundo da faixa de abas: mais escuro que a toolbar, para a aba
    /// ativa parecer erguida e fundir com a toolbar logo abaixo dela.</summary>
    internal static readonly Color StripBackground = Color.FromArgb(0x17, 0x17, 0x17);

    /// <summary>Fundo de aba nao selecionada.</summary>
    internal static readonly Color TabInactive = Color.FromArgb(0x24, 0x24, 0x24);

    /// <summary>Fundo de aba sob o cursor.</summary>
    internal static readonly Color TabHover = Color.FromArgb(0x32, 0x32, 0x32);

    internal static readonly Color CloseHover = Color.FromArgb(0x55, 0x55, 0x55);
}
