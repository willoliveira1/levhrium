using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace IconGen;

/// <summary>
/// Gera o icone do Levhrium a partir de um PNG de origem: mesma silhueta e mesmo
/// gradiente da imagem dada, sem os detalhes internos, com um "L" dentro.
///
/// A silhueta e extraida do PNG de origem por inundacao a partir das bordas --
/// o que a inundacao nao alcanca e a palheta, e as cordas escuras ficam cercadas
/// de azul, entao contam como parte dela.
///
/// Uso: dotnet run --project tools/IconGen -- &lt;origem.png&gt; &lt;pasta-destino&gt;
/// </summary>
internal static class Program
{
    private static void Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Uso: IconGen <origem.png> <pasta-destino>");
            Environment.ExitCode = 1;
            return;
        }

        var source = args[0];
        var outputDirectory = args[1];

        Directory.CreateDirectory(outputDirectory);

        using var original = new Bitmap(source);
        var shape = PickShape.Extract(original);

        Console.WriteLine($"palheta: {shape.Width}x{shape.Height}");
        Console.WriteLine($"gradiente: {Hex(shape.TopColor)} -> {Hex(shape.BottomColor)}");
        Console.WriteLine($"cordas: {Hex(shape.DetailColor)}");

        // PNG grande para o instalador e para conferencia.
        foreach (var size in new[] { 256, 512 })
        {
            using var preview = Render(shape, size);
            preview.Save(Path.Combine(outputDirectory, $"levhrium-{size}.png"), ImageFormat.Png);
        }

        WriteIco(shape, Path.Combine(outputDirectory, "levhrium.ico"));
        Console.WriteLine($"gerado em {outputDirectory}");
    }

    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>
    /// Desenha o icone no tamanho pedido. Renderiza grande e reduz com
    /// reamostragem: a mascara e binaria, entao o suavizado das bordas vem da
    /// reducao, nao do traco.
    /// </summary>
    private static Bitmap Render(PickShape shape, int size)
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

            // A palheta ocupa quase todo o icone: o pedido foi so o logo azul.
            var target = (int)(big * 0.94);
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

            // Branco levemente azulado: contrasta com o azul da palheta em
            // qualquer tamanho, inclusive nos 16px da barra de tarefas.
            DrawLetter(g, Color.FromArgb(255, 245, 249, 255), left, top, width, height);
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
    private static void DrawLetter(Graphics g, Color color, int left, int top, int width, int height)
    {
        using var path = new GraphicsPath();
        using var family = new FontFamily("Segoe UI");

        path.AddString(
            "L",
            family,
            (int)FontStyle.Bold,
            height * 0.58f,
            new PointF(0, 0),
            StringFormat.GenericTypographic);

        var bounds = path.GetBounds();
        using var move = new Matrix();

        move.Translate(
            left + ((width - bounds.Width) / 2f) - bounds.X,
            top + (height * 0.38f) - (bounds.Height / 2f) - bounds.Y);

        path.Transform(move);

        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    /// <summary>
    /// ICO multi-tamanho escrito a mao: cada entrada e um PNG, suportado desde o
    /// Windows Vista e muito menor que bitmaps nao comprimidos.
    /// </summary>
    private static void WriteIco(PickShape shape, string path)
    {
        int[] sizes = [16, 24, 32, 48, 64, 128, 256];
        var frames = new List<(int Size, byte[] Data)>();

        foreach (var size in sizes)
        {
            using var bitmap = Render(shape, size);
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

    /// <summary>Cor escura das cordas; e com ela que o "L" e desenhado.</summary>
    public Color DetailColor { get; }

    public static PickShape Extract(Bitmap source)
    {
        var width = source.Width;
        var height = source.Height;
        var pixels = ReadPixels(source, out var stride);

        // Amostra no meio da borda esquerda, nao no canto: o canto e arredondado
        // e transparente, e amostrar ali faria a inundacao parar na borda do
        // quadrado escuro em vez de atravessa-lo ate a palheta.
        var background = GetColor(pixels, stride, width / 20, height / 2);
        var isBackground = FloodFillFromBorders(pixels, stride, width, height, background);

        // Fora do quadrado arredondado o PNG tem cantos brancos, que a inundacao
        // nao alcanca e contariam como palheta. Ficar com a maior regiao conectada
        // descarta os cantos sem depender da cor deles.
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

        // Media dos pixels azuis de cada faixa, nao um pixel so: as cordas
        // escuras correm pelo meio da palheta e uma amostra pontual cairia nelas.
        var top = AverageBlue(pixels, stride, pick, minX, minY, pickWidth, pickHeight, 0.06, 0.16);
        var bottom = AverageBlue(pixels, stride, pick, minX, minY, pickWidth, pickHeight, 0.80, 0.90);
        var detail = FindDarkestInside(pixels, stride, pick, minX, minY, pickWidth, pickHeight);

        return new PickShape(mask, pickWidth, pickHeight, top, bottom, detail);
    }

    /// <summary>A palheta no tamanho original, preenchida com o gradiente vertical.</summary>
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

            // Canto arredondado e transparente tambem conta como fundo.
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
    /// Maior grupo de pixels nao-fundo ligados entre si. A palheta e o maior; os
    /// cantos brancos de fora do quadrado arredondado sao grupos pequenos.
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
    /// horizontal da palheta, dada em fracao da altura.
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

                // As cordas sao escuras e pouco saturadas; o azul da palheta tem
                // o canal azul bem acima do vermelho.
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
