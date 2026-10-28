using ReSkateTrainerUpdater.Ui;

namespace ReSkateTrainerUpdater.Ui.Controls;

/// <summary>A flat dark square button like "Open folder" in the launcher's settings.</summary>
internal sealed class SquareButton : BufferedControl
{
    /// <summary>A darker button, like CHANGELOGS in the launcher, for secondary actions.</summary>
    public bool Dark { get; set; }

    /// <summary>A smaller label, for the little buttons along the bottom of the status card.</summary>
    public bool Compact { get; set; }

    public SquareButton()
    {
        TabStop = true;
        Cursor = Cursors.Hand;
        Size = new Size(150, 42);
    }

    /// <summary>The width this button needs for its text (in device pixels).</summary>
    public int PreferredWidth()
    {
        using var g = Graphics.FromHwnd(IntPtr.Zero);
        return (int)Math.Ceiling(TextPaint.Measure(g, Text, Compact ? Theme.Small : Theme.Strong).Width + S(Compact ? 28 : 40));
    }

    protected override void OnTextChanged(EventArgs e)
    {
        Describe(Text, AccessibleRole.PushButton);
        base.OnTextChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prepare(e);
        var area = new RectangleF(0, 0, Width, Height);

        Color fill = !Enabled ? Theme.Tile
                   : Dark ? (Pressed ? Theme.Control : Hover ? Theme.TileHover : Theme.Tile)
                   : Pressed ? Theme.Control : Hover ? Theme.ControlHover : Theme.Control;
        using (var brush = new SolidBrush(fill))
        {
            g.FillRectangle(brush, area);
        }
        TextPaint.Draw(g, Text, Compact ? Theme.Small : Theme.Strong, area, Enabled ? Theme.Text : Theme.Faint, HAlign.Center, VAlign.Middle);
        DrawFocusRing(g, area);
    }
}
