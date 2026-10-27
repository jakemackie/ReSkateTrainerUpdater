using System.Drawing.Drawing2D;
using ReSkateTrainerUpdater.Ui;

namespace ReSkateTrainerUpdater.Ui.Controls;

/// <summary>A flat tile with a title, a one-line description and a square checkbox, like the options in the launcher's settings.</summary>
internal sealed class OptionRow : BufferedControl
{
    private bool _checked;

    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public event EventHandler? CheckedChanged;

    public OptionRow()
    {
        TabStop = true;
        Cursor = Cursors.Hand;
    }

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Sets the state without raising CheckedChanged (for start-up).</summary>
    public void SetCheckedImmediate(bool value)
    {
        _checked = value;
        Invalidate();
    }

    protected override void OnClick(EventArgs e)
    {
        Checked = !Checked;
        base.OnClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prepare(e);
        var area = new RectangleF(0, 0, Width, Height);

        using (var brush = new SolidBrush(Hover ? Theme.TileHover : Theme.Tile))
        {
            g.FillRectangle(brush, area);
        }

        // The checkbox: a dark square with a blue tick.
        float box = S(28);
        float bx = S(20);
        float by = (Height - box) / 2f;
        using (var brush = new SolidBrush(Theme.Control))
        {
            g.FillRectangle(brush, bx, by, box, box);
        }
        if (_checked)
        {
            using var pen = new Pen(Theme.Accent, S(3.2f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(pen, new[]
            {
                new PointF(bx + box * 0.24f, by + box * 0.54f),
                new PointF(bx + box * 0.43f, by + box * 0.72f),
                new PointF(bx + box * 0.78f, by + box * 0.30f),
            });
        }

        float textX = bx + box + S(18);
        float textWidth = Math.Max(0, Width - textX - S(20));
        TextPaint.Draw(g, Title, Theme.ChipTitle, new RectangleF(textX, Height / 2f - S(25), textWidth, S(24)), Theme.Text, HAlign.Left, VAlign.Bottom);
        TextPaint.Draw(g, Description, Theme.Small, new RectangleF(textX, Height / 2f + S(1), textWidth, S(24)), Theme.Muted, HAlign.Left, VAlign.Top);

        DrawFocusRing(g, area);
    }
}
