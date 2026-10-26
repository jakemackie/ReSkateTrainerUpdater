using ReSkateTrainerUpdater.Ui;

namespace ReSkateTrainerUpdater.Ui.Controls;

/// <summary>A flat dark square button like "Open folder" in the launcher's settings.</summary>
internal sealed class SquareButton : BufferedControl
{
    public SquareButton()
    {
        TabStop = true;
        Cursor = Cursors.Hand;
        Size = new Size(150, 42);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prepare(e);
        var area = new RectangleF(0, 0, Width, Height);

        Color fill = !Enabled ? Theme.Tile : Pressed ? Theme.Control : Hover ? Theme.ControlHover : Theme.Control;
        using (var brush = new SolidBrush(fill))
        {
            g.FillRectangle(brush, area);
        }
        TextPaint.Draw(g, Text, Theme.Strong, area, Enabled ? Theme.Text : Theme.Faint, HAlign.Center, VAlign.Middle);
        DrawFocusRing(g, area);
    }
}
