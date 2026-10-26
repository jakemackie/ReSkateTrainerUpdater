using System.Drawing.Drawing2D;
using System.Drawing.Text;
using ReSkateTrainerUpdater.Ui;

namespace ReSkateTrainerUpdater.Ui.Controls;

/// <summary>A flat square card with a title that hosts ordinary child controls (used for the activity log).</summary>
internal sealed class CardPanel : Panel
{
    public string Title { get; set; } = "";

    public CardPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Card;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(Theme.Card);

        float scale = DeviceDpi / 96f;
        using (var brush = new SolidBrush(Theme.Accent))
        {
            g.FillRectangle(brush, 0, 0, 4 * scale, Height);
        }
        TextPaint.Draw(g, Title, Theme.Title, new RectangleF(30 * scale, 22 * scale, Width - 60 * scale, 30 * scale), Theme.Text);
    }
}
