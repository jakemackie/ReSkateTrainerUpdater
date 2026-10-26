using System.Drawing.Drawing2D;
using ReSkateTrainerUpdater.Core;
using ReSkateTrainerUpdater.Ui;

namespace ReSkateTrainerUpdater.Ui.Controls;

/// <summary>
/// The top-left of the window, as in the launcher: a small chip with the app icon, a status dot and two lines of text
/// (the launcher's says "Welcome"), and under it the name in big, slightly tilted marker-pen lettering with a shadow.
/// </summary>
internal sealed class HeaderBar : BufferedControl
{
    private Bitmap? _logo;
    private int _logoSize;
    private ChipView _chip = new("", "", Severity.Neutral);

    public void SetChip(ChipView chip)
    {
        _chip = chip;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prepare(e);

        // The chip: icon square, text block, and a status dot over the icon's corner.
        int size = (int)S(46);
        float top = S(4);
        if (_logo == null || _logoSize != size)
        {
            _logo?.Dispose();
            _logo = AppIcon.Render(size);
            _logoSize = size;
        }

        var logoRect = new RectangleF(0, top, size, size);
        if (_logo != null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(_logo, logoRect);
        }
        else
        {
            using var brush = new SolidBrush(Theme.Accent);
            g.FillRectangle(brush, logoRect);
        }

        float textHeight = S(40);
        var textBlock = new RectangleF(size, top + (size - textHeight) / 2f, S(268), textHeight);
        using (var brush = new SolidBrush(Theme.Chip))
        {
            g.FillRectangle(brush, textBlock);
        }
        float tx = textBlock.Left + S(14);
        float tw = textBlock.Width - S(22);
        TextPaint.Draw(g, _chip.Title, Theme.Strong, new RectangleF(tx, textBlock.Top + S(2), tw, S(20)), Theme.Text, HAlign.Left, VAlign.Middle);
        TextPaint.Draw(g, _chip.Subtitle, Theme.Small, new RectangleF(tx, textBlock.Top + S(19), tw, S(18)), Theme.Muted, HAlign.Left, VAlign.Middle);

        float dot = S(10);
        var dotColour = _chip.Severity switch
        {
            Severity.Good => Theme.Success,
            Severity.Working => Theme.Accent,
            _ => Theme.Faint,
        };
        using (var ring = new SolidBrush(Theme.Bg))
        using (var brush = new SolidBrush(dotColour))
        {
            g.FillEllipse(ring, S(2) - S(2), top - S(4) - S(2), dot + S(4), dot + S(4));
            g.FillEllipse(brush, S(2), top - S(4), dot, dot);
        }

        // The wordmark: tilted a little, with a soft shadow, like the launcher's.
        var state = g.Save();
        float baseline = top + size + S(8);
        float wordHeight = S(64);
        g.TranslateTransform(S(8), baseline + wordHeight / 2f);
        g.RotateTransform(-3.5f);
        string name = "TRAINER UPDATER";
        var rect = new RectangleF(0, -wordHeight / 2f, Width, wordHeight);
        TextPaint.Draw(g, name, Theme.Wordmark, new RectangleF(rect.X + S(2), rect.Y + S(3), rect.Width, rect.Height), Color.FromArgb(150, 0, 0, 0));
        TextPaint.Draw(g, name, Theme.Wordmark, rect, Theme.Text);
        g.Restore(state);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _logo?.Dispose();
        base.Dispose(disposing);
    }
}
