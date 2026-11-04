using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace IconGen;

/// <summary>
/// Gera o icone do Levhrium a partir de um PNG de origem: mesma silhueta e mesmo
/// gradiente da imagem dada, sem os detalhes internos, com um "L" dentro.
///
/// A silhueta e extraida por inundacao a partir das bordas, ficando com a maior
/// regiao conectada -- os cantos do PNG de origem nao sao transparentes e
/// contariam como parte do desenho.
///
/// Uso:
///   IconGen de-origem.png pasta-destino [estilo]
///   IconGen de-origem.png pasta-destino variants   (gera uma amostra de cada estilo)
/// </summary>
internal static class Program
{
    private static void Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Uso: IconGen <origem.png> <pasta-destino> [estilo|variants]");
            Environment.ExitCode = 1;
            return;
        }

        var source = args[0];
        var outputDirectory = args[1];
        var variant = args.Length > 2 ? args[2] : "1-branco-grande";

        Directory.CreateDirectory(outputDirectory);

        using var original = new Bitmap(source);
        var shape = PickShape.Extract(original);

        Console.WriteLine($"palheta: {shape.Width}x{shape.Height}");
        Console.WriteLine($"gradiente: {Hex(shape.TopColor)} -> {Hex(shape.BottomColor)}");

        if (variant.Equals("variants", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var (name, style) in LogoStyle.All(shape))
            {
                using var preview = Render(shape, 512, style);
                preview.Save(Path.Combine(outputDirectory, $"opcao-{name}.png"), ImageFormat.Png);
                Console.WriteLine($"  opcao-{name}.png");
            }

            return;
        }

        var chosen = LogoStyle.ByName(variant, shape);

        foreach (var size in new[] { 256, 512 })
        {
            using var preview = Render(shape, size, chosen);
            preview.Save(Path.Combine(outputDirectory, $"levhrium-{size}.png"), ImageFormat.Png);
        }

        WriteIco(shape, Path.Combine(outputDirectory, "levhrium.ico"), chosen);
        Console.WriteLine($"gerado em {outputDirectory} (estilo: {variant})");
    }

    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>
    /// Desenha o icone no tamanho pedido. Renderiza grande e reduz com
    /// reamostragem: a mascara e binaria, entao o suavizado das bordas vem da
    /// reducao, nao do traco.
    /// </summary>
    private static Bitmap Render(PickShape shape, int size, LogoStyle style)
    {
        const int Supersample = 4;
        var big = size * Supersample;

        using var canvas = new Bitmap(big, big, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(canvas))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            if (style.Background is { } backdrop)
            {
                DrawRoundedSquare(g, big, backdrop);
            }

            // Sem fundo a palheta ocupa quase todo o icone; com fundo ela recua
            // para caber dentro do quadrado.
            var target = (int)(big * (style.Background is null ? 0.94 : 0.64));
            var width = target;
            var height = (int)(target * (double)shape.Height / shape.Width);

            if (height > target)
            {
                height = target;
                width = (int)(target * (double)shape.Width / shape.Height);
            }

            var left = (big - width) / 2;
            var top = (big - height) / 2;

            using (var pick = shape.ToBitmap())
            {
                g.DrawImage(pick, left, top, width, height);
            }

            DrawLetter(g, style, left, top, width, height);
        }

        var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(result))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            g.DrawImage(canvas, new Rectangle(0, 0, size, size));
        }

        return result;
    }

    /// <summary>
    /// O "L" fica na parte larga de cima da palheta: ela afunila para baixo e um
    /// L centralizado no meio geometrico encostaria nas laterais.
    /// </summary>
    private static void DrawLetter(Graphics g, LogoStyle style, int left, int top, int width, int height)
    {
        if (style.Strings > 0)
        {
            DrawStrings(g, style, left, top, width, height);
            return;
        }

        using var path = new GraphicsPath();
        using var family = new FontFamily(style.FontName);

        path.AddString(
            "L",
            family,
            (int)style.FontStyle,
            height * style.LetterScale,
            new PointF(0, 0),
            StringFormat.GenericTypographic);

        var bounds = path.GetBounds();

        // A palheta afunila: uma letra larga demais sai pelas laterais. Encolhe
        // ate caber na largura util antes de posicionar.
        // Faixa util: abaixo de 74% da altura a palheta ja afunilou demais, e
        // acima de 10% a borda e arredondada.
        const float SafeTop = 0.10f;
        const float SafeBottom = 0.74f;

        var maxWidth = width * 0.52f;
        var maxHeight = height * (SafeBottom - SafeTop);
        var scale = Math.Min(maxWidth / bounds.Width, maxHeight / bounds.Height);

        if (scale < 1f)
        {
            using var shrink = new Matrix();
            shrink.Scale(scale, scale);
            path.Transform(shrink);
            bounds = path.GetBounds();
        }

        // Centro pedido, mas empurrado para dentro da faixa util se estourar.
        var centerY = height * style.LetterY;
        centerY = Math.Max(centerY, (height * SafeTop) + (bounds.Height / 2f));
        centerY = Math.Min(centerY, (height * SafeBottom) - (bounds.Height / 2f));

        using var move = new Matrix();

        move.Translate(
            left + ((width - bounds.Width) / 2f) - bounds.X,
            top + centerY - (bounds.Height / 2f) - bounds.Y);

        path.Transform(move);

        if (style.Cutout)
        {
            // Vazado: o L vira buraco na palheta, deixando passar o que esta atras.
            using var erase = new SolidBrush(Color.Transparent);
            g.CompositingMode = CompositingMode.SourceCopy;
            g.FillPath(erase, path);
            g.CompositingMode = CompositingMode.SourceOver;
            return;
        }

        using var brush = new SolidBrush(style.LetterColor);
        g.FillPath(brush, path);
    }

    /// <summary>
    /// Cordas verticais como no logo de origem: comprimentos diferentes, as do
    /// meio mais longas, acompanhando o afunilamento da palheta.
    /// </summary>
    private static void DrawStrings(Graphics g, LogoStyle style, int left, int top, int width, int height)
    {
        var count = style.Strings;
        var thickness = Math.Max(1f, width * 0.030f);
        var spacing = width * 0.145f;
        var centerX = left + (width / 2f);
        var centerY = top + (height * 0.44f);

        using var pen = new Pen(style.LetterColor, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        for (var i = 0; i < count; i++)
        {
            var offset = (i - ((count - 1) / 2f)) * spacing;

            // A do meio e a mais comprida; as das pontas encurtam.
            var distance = Math.Abs(i - ((count - 1) / 2f)) / Math.Max(1f, (count - 1) / 2f);
            var halfLength = height * (0.26f - (0.07f * distance));

            var x = centerX + offset;

            if (style.Cutout)
            {
                using var erase = new Pen(Color.Transparent, thickness)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                };

                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawLine(erase, x, centerY - halfLength, x, centerY + halfLength);
                g.CompositingMode = CompositingMode.SourceOver;
                continue;
            }

            g.DrawLine(pen, x, centerY - halfLength, x, centerY + halfLength);
        }
    }

    /// <summary>Quadrado arredondado de fundo, no estilo de icone de aplicativo.</summary>
    private static void DrawRoundedSquare(Graphics g, int size, Color color)
    {
        var radius = (int)(size * 0.22);
        var diameter = radius * 2;

        using var path = new GraphicsPath();
        path.AddArc(0, 0, diameter, diameter, 180, 90);
        path.AddArc(size - diameter, 0, diameter, diameter, 270, 90);
        path.AddArc(size - diameter, size - diameter, diameter, diameter, 0, 90);
        path.AddArc(0, size - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    /// <summary>
    /// ICO multi-tamanho escrito a mao: cada entrada e um PNG, suportado desde o
    /// Windows Vista e muito menor que bitmaps nao comprimidos.
    /// </summary>
    private static void WriteIco(PickShape shape, string path, LogoStyle style)
    {
        int[] sizes = [16, 24, 32, 48, 64, 128, 256];
        var frames = new List<(int Size, byte[] Data)>();

        foreach (var size in sizes)
        {
            using var bitmap = Render(shape, size, style);
            using var buffer = new MemoryStream();
            bitmap.Save(buffer, ImageFormat.Png);
            frames.Add((size, buffer.ToArray()));
        }

        using var file = File.Create(path);
        using var writer = new BinaryWriter(file);

        writer.Write((ushort)0);              // reservado
        writer.Write((ushort)1);              // tipo: icone
        writer.Write((ushort)frames.Count);

        var offset = 6 + (16 * frames.Count);
        foreach (var (size, data) in frames)
        {
            // 256 e gravado como 0, por limite de um byte no formato.
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);            // cores na paleta
            writer.Write((byte)0);            // reservado
            writer.Write((ushort)1);          // planos
            writer.Write((ushort)32);         // bits por pixel
            writer.Write((uint)data.Length);
            writer.Write((uint)offset);
            offset += data.Length;
        }

        foreach (var (_, data) in frames)
        {
            writer.Write(data);
        }
    }
}

/// <summary>Um estilo de logo: como o "L" e desenhado e se ha fundo.</summary>
internal sealed record LogoStyle
{
    private static readonly Color NearWhite = Color.FromArgb(255, 245, 249, 255);

    public required Color LetterColor { get; init; }

    /// <summary>Altura da letra como fracao da altura da palheta.</summary>
    public required float LetterScale { get; init; }

    public string FontName { get; init; } = "Segoe UI";

    public FontStyle FontStyle { get; init; } = FontStyle.Bold;

    /// <summary>Quando preenchido, desenha um quadrado arredondado atras.</summary>
    public Color? Background { get; init; }

    /// <summary>Quando verdadeiro, o L e recortado da palheta em vez de pintado.</summary>
    public bool Cutout { get; init; }

    /// <summary>
    /// Centro vertical da letra, como fracao da altura da palheta. A palheta
    /// afunila para baixo, entao letra maior precisa subir para nao encostar.
    /// </summary>
    public float LetterY { get; init; } = 0.38f;

    /// <summary>
    /// Em vez da letra, desenha este numero de cordas verticais, como no logo de
    /// origem. Zero desenha a letra.
    /// </summary>
    public int Strings { get; init; }

    public static IEnumerable<(string Name, LogoStyle Style)> All(PickShape shape) =>
    [
        ("1-branco-grande", new LogoStyle
        {
            LetterColor = NearWhite,
            LetterScale = 0.58f,
        }),
        ("2-branco-fino", new LogoStyle
        {
            LetterColor = NearWhite,
            LetterScale = 0.62f,
            FontStyle = FontStyle.Regular,
        }),
        ("3-com-fundo", new LogoStyle
        {
            LetterColor = NearWhite,
            LetterScale = 0.58f,
            Background = Color.FromArgb(255, 26, 27, 30),
        }),
        ("4-vazado", new LogoStyle
        {
            LetterColor = NearWhite,
            LetterScale = 0.58f,
            Cutout = true,
        }),
        ("5-escuro", new LogoStyle
        {
            LetterColor = shape.DetailColor,
            LetterScale = 0.58f,
        }),

        // Segunda rodada: variacoes do vazado com L maior, e o estilo de origem
        // com cordas no lugar da letra.
        ("6-vazado-maior", new LogoStyle
        {
            LetterColor = NearWhite,
            LetterScale = 0.72f,
            LetterY = 0.36f,
            Cutout = true,
        }),
        ("7-vazado-enorme", new LogoStyle
        {
            LetterColor = NearWhite,
            LetterScale = 0.86f,
            LetterY = 0.33f,
            Cutout = true,
        }),
        ("8-branco-maior", new LogoStyle
        {
            LetterColor = NearWhite,
            LetterScale = 0.72f,
            LetterY = 0.36f,
        }),
        ("9-branco-enorme", new LogoStyle
        {
            LetterColor = NearWhite,
            LetterScale = 0.86f,
            LetterY = 0.33f,
        }),
        ("10-cordas", new LogoStyle
        {
            LetterColor = shape.DetailColor,
            LetterScale = 0f,
            Strings = 5,
        }),
    ];

    public static LogoStyle ByName(string name, PickShape shape)
    {
        foreach (var (candidate, style) in All(shape))
        {
            if (candidate.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith(name + "-", StringComparison.OrdinalIgnoreCase))
            {
                return style;
            }
        }

        return All(shape).First().Style;
    }
}

/// <summary>Silhueta da palheta e as cores amostradas do PNG de origem.</summary>
internal sealed class PickShape
{
    private readonly bool[,] _mask;

    private PickShape(bool[,] mask, int width, int height, Color top, Color bottom, Color detail)
    {
        _mask = mask;
        Width = width;
        Height = height;
        TopColor = top;
        BottomColor = bottom;
        DetailColor = detail;
    }

    public int Width { get; }

    public int Height { get; }

    public Color TopColor { get; }

    public Color BottomColor { get; }

    /// <summary>Cor escura dos detalhes internos do PNG de origem.</summary>
    public Color DetailColor { get; }

    public static PickShape Extract(Bitmap source)
    {
        var width = source.Width;
        var height = source.Height;
        var pixels = ReadPixels(source, out var stride);

        // Amostra no meio da borda esquerda, nao no canto: o canto e arredondado
        // e amostrar ali faria a inundacao parar na borda do quadrado escuro.
        var background = GetColor(pixels, stride, width / 20, height / 2);
        var isBackground = FloodFillFromBorders(pixels, stride, width, height, background);

        // Fora do quadrado arredondado o PNG tem cantos de outra cor, que a
        // inundacao nao alcanca e contariam como parte do desenho. Ficar com a
        // maior regiao conectada descarta os cantos sem depender da cor deles.
        var pick = LargestRegion(isBackground, width, height);

        var (minX, minY, maxX, maxY) = BoundingBox(pick, width, height);
        var pickWidth = maxX - minX + 1;
        var pickHeight = maxY - minY + 1;

        var mask = new bool[pickWidth, pickHeight];
        for (var y = 0; y < pickHeight; y++)
        {
            for (var x = 0; x < pickWidth; x++)
            {
                mask[x, y] = pick[minX + x, minY + y];
            }
        }

        // Media dos pixels azuis de cada faixa, nao um pixel so: os detalhes
        // escuros correm pelo meio e uma amostra pontual cairia neles.
        var top = AverageBlue(pixels, stride, pick, minX, minY, pickWidth, pickHeight, 0.06, 0.16);
        var bottom = AverageBlue(pixels, stride, pick, minX, minY, pickWidth, pickHeight, 0.80, 0.90);
        var detail = FindDarkestInside(pixels, stride, pick, minX, minY, pickWidth, pickHeight);

        return new PickShape(mask, pickWidth, pickHeight, top, bottom, detail);
    }

    /// <summary>A silhueta no tamanho original, preenchida com o gradiente vertical.</summary>
    public Bitmap ToBitmap()
    {
        var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(
            new Rectangle(0, 0, Width, Height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);

        var buffer = new byte[data.Stride * Height];

        for (var y = 0; y < Height; y++)
        {
            var t = Height == 1 ? 0 : y / (double)(Height - 1);
            var r = (byte)(TopColor.R + ((BottomColor.R - TopColor.R) * t));
            var g = (byte)(TopColor.G + ((BottomColor.G - TopColor.G) * t));
            var b = (byte)(TopColor.B + ((BottomColor.B - TopColor.B) * t));

            for (var x = 0; x < Width; x++)
            {
                if (!_mask[x, y])
                {
                    continue;
                }

                var i = (y * data.Stride) + (x * 4);
                buffer[i] = b;
                buffer[i + 1] = g;
                buffer[i + 2] = r;
                buffer[i + 3] = 255;
            }
        }

        System.Runtime.InteropServices.Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
        bitmap.UnlockBits(data);
        return bitmap;
    }

    private static byte[] ReadPixels(Bitmap source, out int stride)
    {
        var data = source.LockBits(
            new Rectangle(0, 0, source.Width, source.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);

        stride = data.Stride;
        var buffer = new byte[data.Stride * source.Height];
        System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
        source.UnlockBits(data);
        return buffer;
    }

    private static Color GetColor(byte[] pixels, int stride, int x, int y)
    {
        var i = (y * stride) + (x * 4);
        return Color.FromArgb(pixels[i + 3], pixels[i + 2], pixels[i + 1], pixels[i]);
    }

    private static bool[,] FloodFillFromBorders(
        byte[] pixels,
        int stride,
        int width,
        int height,
        Color background)
    {
        var visited = new bool[width, height];
        var queue = new Queue<(int X, int Y)>();

        for (var x = 0; x < width; x++)
        {
            queue.Enqueue((x, 0));
            queue.Enqueue((x, height - 1));
        }

        for (var y = 0; y < height; y++)
        {
            queue.Enqueue((0, y));
            queue.Enqueue((width - 1, y));
        }

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();

            if (x < 0 || y < 0 || x >= width || y >= height || visited[x, y])
            {
                continue;
            }

            var color = GetColor(pixels, stride, x, y);

            var isBackground = color.A < 32 ||
                (Math.Abs(color.R - background.R) <= 28 &&
                 Math.Abs(color.G - background.G) <= 28 &&
                 Math.Abs(color.B - background.B) <= 28);

            if (!isBackground)
            {
                continue;
            }

            visited[x, y] = true;
            queue.Enqueue((x + 1, y));
            queue.Enqueue((x - 1, y));
            queue.Enqueue((x, y + 1));
            queue.Enqueue((x, y - 1));
        }

        return visited;
    }

    /// <summary>
    /// Maior grupo de pixels nao-fundo ligados entre si. A figura principal e o
    /// maior; os cantos de fora do quadrado arredondado sao grupos pequenos.
    /// </summary>
    private static bool[,] LargestRegion(bool[,] isBackground, int width, int height)
    {
        var visited = new bool[width, height];
        var best = new List<(int X, int Y)>();

        for (var startY = 0; startY < height; startY++)
        {
            for (var startX = 0; startX < width; startX++)
            {
                if (isBackground[startX, startY] || visited[startX, startY])
                {
                    continue;
                }

                var region = new List<(int X, int Y)>();
                var queue = new Queue<(int X, int Y)>();
                queue.Enqueue((startX, startY));
                visited[startX, startY] = true;

                while (queue.Count > 0)
                {
                    var (x, y) = queue.Dequeue();
                    region.Add((x, y));

                    foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        var nx = x + dx;
                        var ny = y + dy;

                        if (nx < 0 || ny < 0 || nx >= width || ny >= height ||
                            visited[nx, ny] || isBackground[nx, ny])
                        {
                            continue;
                        }

                        visited[nx, ny] = true;
                        queue.Enqueue((nx, ny));
                    }
                }

                if (region.Count > best.Count)
                {
                    best = region;
                }
            }
        }

        var mask = new bool[width, height];
        foreach (var (x, y) in best)
        {
            mask[x, y] = true;
        }

        return mask;
    }

    private static (int MinX, int MinY, int MaxX, int MaxY) BoundingBox(
        bool[,] pick,
        int width,
        int height)
    {
        int minX = width, minY = height, maxX = 0, maxY = 0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!pick[x, y])
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
        }

        return (minX, minY, maxX, maxY);
    }

    /// <summary>
    /// Cor media dos pixels claramente azuis (B bem acima de R) numa faixa
    /// horizontal da figura, dada em fracao da altura.
    /// </summary>
    private static Color AverageBlue(
        byte[] pixels,
        int stride,
        bool[,] pick,
        int minX,
        int minY,
        int width,
        int height,
        double fromFraction,
        double toFraction)
    {
        long r = 0, g = 0, b = 0, count = 0;
        var fromY = (int)(height * fromFraction);
        var toY = (int)(height * toFraction);

        for (var y = fromY; y <= toY && y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!pick[minX + x, minY + y])
                {
                    continue;
                }

                var color = GetColor(pixels, stride, minX + x, minY + y);

                if (color.B - color.R < 40)
                {
                    continue;
                }

                r += color.R;
                g += color.G;
                b += color.B;
                count++;
            }
        }

        return count == 0
            ? Color.FromArgb(255, 60, 140, 240)
            : Color.FromArgb(255, (int)(r / count), (int)(g / count), (int)(b / count));
    }

    private static Color FindDarkestInside(
        byte[] pixels,
        int stride,
        bool[,] pick,
        int minX,
        int minY,
        int width,
        int height)
    {
        var darkest = Color.FromArgb(255, 31, 41, 55);
        var lowest = double.MaxValue;

        for (var y = 0; y < height; y += 2)
        {
            for (var x = 0; x < width; x += 2)
            {
                if (!pick[minX + x, minY + y])
                {
                    continue;
                }

                var color = GetColor(pixels, stride, minX + x, minY + y);
                var brightness = color.GetBrightness();

                if (brightness < lowest)
                {
                    lowest = brightness;
                    darkest = color;
                }
            }
        }

        return darkest;
    }
}
