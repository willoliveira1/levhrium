using System.Drawing.Drawing2D;
using Levhrium.Core;

namespace Levhrium.App;

/// <summary>
/// Faixa fina no rodapé com a memória e a CPU do navegador inteiro.
///
/// Escolhida entre cinco posições possíveis por um motivo concreto: é a única
/// que nunca encosta na página nem encolhe quando há muitas abas. Custa 22px
/// de altura, e é por isso que vem desligada e é ligada nas configurações —
/// ninguém deve pagar pixels por algo que não pediu.
///
/// Teve um chip de "modo econômico" na v1.0.10, removido na seguinte: ele
/// ligava e desligava a hibernação, que congela o renderer sem descartá-lo.
/// Congelar economiza CPU e quase nada de memória, então o rótulo prometia
/// uma coisa e entregava outra. A hibernação continua nas configurações, com
/// o nome certo.
/// </summary>
internal sealed class StatusBar : Control
{
    internal const int BarHeight = 22;

    private ResourceSample _sample;

    internal StatusBar()
    {
        Dock = DockStyle.Bottom;
        Height = BarHeight;
        TabStop = false;
        BackColor = Theme.StripBackground;
        Font = new Font("Segoe UI", 8f);

        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw,
            true);
    }

    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal ResourceSample Sample
    {
        get => _sample;
        set
        {
            _sample = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.StripBackground);

        using (var pen = new Pen(Theme.CardBorder))
        {
            g.DrawLine(pen, 0, 0, Width, 0);
        }

        if (_sample.IsEmpty)
        {
            return;
        }

        var texto = $"{ResourceMath.FormatBytes(_sample.Bytes)} · {ResourceMath.FormatCpu(_sample.CpuPercent)}";

        TextRenderer.DrawText(
            g,
            texto,
            Font,
            new Rectangle(0, 0, Width - 12, Height),
            Theme.ForegroundDim,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

}
