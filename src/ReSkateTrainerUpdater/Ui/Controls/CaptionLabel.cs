using ReSkateTrainerUpdater.Ui;

namespace ReSkateTrainerUpdater.Ui.Controls;

/// <summary>A line of small muted text. A normal Label can't draw the launcher's embedded fonts, so this draws it itself.</summary>
internal sealed class CaptionLabel : BufferedControl
{
    public HAlign Align { get; set; } = HAlign.Right;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prepare(e);
        TextPaint.Draw(g, Text, Theme.Small, new RectangleF(0, 0, Width, Height), Theme.Faint, Align, VAlign.Middle);
    }
}
