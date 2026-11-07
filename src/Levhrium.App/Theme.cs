using System.Drawing.Drawing2D;

namespace Levhrium.App;

/// <summary>
/// Paleta escura. Centralizada porque a faixa de abas, a toolbar e a barra de
/// endereco sao desenhadas a mao e precisam das mesmas cores.
///
/// O desenho e o meio-termo entre "compacto" e "elevado": abas presas a faixa,
/// com a ativa fundindo na toolbar, mas com cantos macios e botoes circulares.
/// </summary>
internal static class Theme
{
    /// <summary>Fundo da janela, atras do conteudo.</summary>
    internal static readonly Color WindowBackground = Color.FromArgb(0x0F, 0x0F, 0x12);

    /// <summary>Fundo da faixa de abas: mais escuro, para a aba parecer erguida.</summary>
    internal static readonly Color StripBackground = Color.FromArgb(0x14, 0x14, 0x17);

    /// <summary>
    /// Toolbar. A aba ativa usa esta mesma cor: as duas se fundem e a aba parece
    /// continuar na barra de baixo.
    /// </summary>
    internal static readonly Color ToolbarBackground = Color.FromArgb(0x22, 0x22, 0x2A);

    internal static readonly Color TabInactive = Color.FromArgb(0x1A, 0x1A, 0x1F);
    internal static readonly Color TabHover = Color.FromArgb(0x26, 0x26, 0x2E);

    /// <summary>Interior da pilula de endereco.</summary>
    internal static readonly Color FieldBackground = Color.FromArgb(0x14, 0x14, 0x17);
    internal static readonly Color FieldBorder = Color.FromArgb(0x2E, 0x2E, 0x36);

    internal static readonly Color Foreground = Color.FromArgb(0xF1, 0xF1, 0xF7);
    internal static readonly Color ForegroundDim = Color.FromArgb(0x9C, 0x9C, 0xA6);

    /// <summary>Texto e icone desabilitados; so para elementos nao essenciais.</summary>
    internal static readonly Color ForegroundFaint = Color.FromArgb(0x5F, 0x5F, 0x69);

    internal static readonly Color Accent = Color.FromArgb(0x4C, 0x9A, 0xFF);

    /// <summary>Realce circular dos botoes de icone sob o cursor.</summary>
    internal static readonly Color ButtonHover = Color.FromArgb(0x2E, 0x2E, 0x38);
    internal static readonly Color ButtonActive = Color.FromArgb(0x38, 0x38, 0x44);

    internal static readonly Color CloseBackground = Color.FromArgb(0x32, 0x32, 0x3C);
    internal static readonly Color CloseHover = Color.FromArgb(0x4A, 0x4A, 0x58);

    /// <summary>Verde do escudo de bloqueios, e o fundo da sua pilula.</summary>
    internal static readonly Color Shield = Color.FromArgb(0x7F, 0xDC, 0xA8);
    internal static readonly Color ShieldBackground = Color.FromArgb(0x1C, 0x2E, 0x25);

    /// <summary>Cartao das configuracoes: um degrau acima do fundo da janela.</summary>
    internal static readonly Color CardBackground = Color.FromArgb(0x1A, 0x1A, 0x1E);
    internal static readonly Color CardBorder = Color.FromArgb(0x26, 0x26, 0x2C);

    /// <summary>Linha dentro de um cartao -- mais funda que o cartao, de proposito.</summary>
    internal static readonly Color RowBackground = Color.FromArgb(0x0E, 0x0E, 0x10);

    /// <summary>Contorno dos botoes-pilula, que nao tem preenchimento.</summary>
    internal static readonly Color PillBorder = Color.FromArgb(0x35, 0x35, 0x3E);
    internal static readonly Color PillForeground = Color.FromArgb(0xD7, 0xD7, 0xDF);

    /// <summary>Texto secundario dentro dos cartoes, mais apagado que o da toolbar.</summary>
    internal static readonly Color CardSubtitle = Color.FromArgb(0x83, 0x83, 0x8D);

    /// <summary>Verde de "esta tudo certo", usado na linha de versao.</summary>
    internal static readonly Color Success = Color.FromArgb(0x6F, 0xCF, 0x97);

    internal static readonly Color Purple = Color.FromArgb(0xB4, 0x7B, 0xFF);

    /// <summary>Fundo do ladrilho de icone: a propria cor, bem diluida.</summary>
    internal static Color Tint(Color color) => Color.FromArgb(0x22, color);

    /// <summary>Fonte de icones do Windows; glifos em <see cref="Glyphs"/>.</summary>
    internal const string IconFontName = "Segoe MDL2 Assets";

    /// <summary>Retangulo de cantos arredondados, para fundo de aba e pilula.</summary>
    internal static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));

        if (diameter <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }

    /// <summary>Retangulo com so os dois cantos de cima arredondados: a aba.</summary>
    internal static GraphicsPath RoundedTop(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));

        if (diameter <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddLine(bounds.Right, bounds.Bottom, bounds.Left, bounds.Bottom);
        path.CloseFigure();

        return path;
    }
}

/// <summary>
/// Glifos do Segoe MDL2 Assets, a fonte de icones que acompanha o Windows 10 e
/// 11. Usar a fonte do sistema em vez de setas de texto deixa os icones do mesmo
/// peso e alinhados entre si -- e sem baixar nada.
/// </summary>
internal static class Glyphs
{
    internal const string Back = "";
    internal const string Forward = "";
    internal const string Reload = "";
    internal const string Stop = "";
    internal const string Menu = "";
    internal const string Add = "";
    internal const string ChevronDown = "";
    internal const string Close = "";
    internal const string Lock = "";
    internal const string Shield = "";
    internal const string Star = "";
    internal const string StarFilled = "";
    internal const string Pin = "";
    internal const string Moon = "";
    internal const string Settings = "";
    internal const string Gauge = "";
}
