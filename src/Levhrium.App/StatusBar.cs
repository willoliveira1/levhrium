using System.Drawing.Drawing2D;
using Levhrium.Core;

namespace Levhrium.App;

/// <summary>
/// Faixa fina no rodapé com o consumo e o estado do modo econômico.
///
/// Escolhida entre cinco posições possíveis por um motivo concreto: é a única
/// que nunca encosta na página nem encolhe quando há muitas abas. Custa 22px
/// de altura, e é por isso que vem desligada e é ligada nas configurações —
/// ninguém deve pagar pixels por algo que não pediu.
///
/// O chip da direita não é enfeite: clicar nele liga e desliga a hibernação,
/// que é o que "modo econômico" significa aqui. Um indicador que mostra um
/// estado mas não deixa mudá-lo obriga a abrir as configurações para algo que
/// se quer alternar depressa.
/// </summary>
internal sealed class StatusBar : Control
{
    internal const int BarHeight = 22;

    private ResourceSample _sample;
    private bool _economy;
    private bool _hoverChip;

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

    /// <summary>O chip foi clicado: ligar ou desligar a hibernação.</summary>
    internal event EventHandler? EconomyToggled;

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

    /// <summary>Verdadeiro quando a hibernação está ligada.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool Economy
    {
        get => _economy;
        set
        {
            _economy = value;
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

        var chip = ChipBounds;

        if (_hoverChip)
        {
            using var path = Theme.RoundedRect(chip, chip.Height / 2);
            using var brush = new SolidBrush(Theme.ButtonHover);
            g.FillPath(brush, path);
        }

        var cor = _economy ? Theme.Success : Theme.ForegroundFaint;

        using (var ponto = new SolidBrush(cor))
        {
            g.FillEllipse(ponto, chip.Left + 8, chip.Top + ((chip.Height - 6) / 2), 6, 6);
        }

        TextRenderer.DrawText(
            g,
            _economy ? "modo econômico" : "econômico desligado",
            Font,
            new Rectangle(chip.Left + 18, chip.Top, chip.Width - 24, chip.Height),
            cor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        if (_sample.IsEmpty)
        {
            return;
        }

        var texto = $"{ResourceMath.FormatBytes(_sample.Bytes)} · {ResourceMath.FormatCpu(_sample.CpuPercent)}";

        TextRenderer.DrawText(
            g,
            texto,
            Font,
            new Rectangle(0, 0, chip.Left - 10, Height),
            Theme.ForegroundDim,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    /// <summary>O chip encostado na direita; largura fixa para não dançar.</summary>
    private Rectangle ChipBounds => new(Width - 140, 2, 134, Height - 4);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var dentro = ChipBounds.Contains(e.Location);

        if (dentro != _hoverChip)
        {
            _hoverChip = dentro;
            Cursor = dentro ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverChip = false;
        Cursor = Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button == MouseButtons.Left && ChipBounds.Contains(e.Location))
        {
            EconomyToggled?.Invoke(this, EventArgs.Empty);
        }
    }
}
