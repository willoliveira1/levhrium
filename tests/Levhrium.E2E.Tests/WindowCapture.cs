using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Levhrium.E2E.Tests;

/// <summary>
/// Captura a janela do Levhrium e compara com uma imagem de referencia.
///
/// Usa PrintWindow e nao captura de tela: PrintWindow funciona com a janela
/// atras de outras, o que importa porque um processo em segundo plano nao
/// consegue trazer janela para a frente no Windows.
/// </summary>
internal static partial class WindowCapture
{
    /// <summary>PW_RENDERFULLCONTENT: necessario para conteudo composto por GPU.</summary>
    private const uint RenderFullContent = 2;

    internal static Bitmap Capture(Process process, Rectangle? crop = null)
    {
        process.Refresh();
        var handle = process.MainWindowHandle;

        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("O processo nao tem janela principal.");
        }

        if (!GetWindowRect(handle, out var rect))
        {
            throw new InvalidOperationException("Nao consegui medir a janela.");
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;

        var full = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        using (var graphics = Graphics.FromImage(full))
        {
            var dc = graphics.GetHdc();

            try
            {
                PrintWindow(handle, dc, RenderFullContent);
            }
            finally
            {
                graphics.ReleaseHdc(dc);
            }
        }

        if (crop is not { } area)
        {
            return full;
        }

        using (full)
        {
            var bounded = Rectangle.Intersect(area, new Rectangle(0, 0, width, height));
            return full.Clone(bounded, full.PixelFormat);
        }
    }

    /// <summary>
    /// Fracao de pixels diferentes entre duas imagens, de 0 a 1. Tamanhos
    /// diferentes contam como totalmente diferentes: nao da para comparar
    /// capturas feitas em DPI diferente.
    /// </summary>
    internal static double Difference(Bitmap a, Bitmap b, int channelTolerance = 12)
    {
        if (a.Width != b.Width || a.Height != b.Height)
        {
            return 1.0;
        }

        var different = 0L;
        var total = (long)a.Width * a.Height;

        for (var y = 0; y < a.Height; y++)
        {
            for (var x = 0; x < a.Width; x++)
            {
                var pa = a.GetPixel(x, y);
                var pb = b.GetPixel(x, y);

                if (Math.Abs(pa.R - pb.R) > channelTolerance ||
                    Math.Abs(pa.G - pb.G) > channelTolerance ||
                    Math.Abs(pa.B - pb.B) > channelTolerance)
                {
                    different++;
                }
            }
        }

        return (double)different / total;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr window, out Rect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PrintWindow(IntPtr window, IntPtr deviceContext, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
