using System.Drawing.Drawing2D;
using ReSkateTrainerUpdater.Core;
using ReSkateTrainerUpdater.Ui;

namespace ReSkateTrainerUpdater.Ui.Controls;

/// <summary>
/// The top-left of the window, as in the launcher. A row of two small cards, each a square picture and a block of two
/// lines of text, like the launcher's "Welcome" card: the creator's credit (with their profile picture) and the GitHub
/// status (with the app icon and a status dot). Under them is the name in big, slightly tilted marker-pen lettering.
/// </summary>
internal sealed class HeaderBar : BufferedControl
{
    // Measurements in 96-dpi pixels, from the launcher's "Welcome" card.
    private const float PictureSize = 46f;
    private const float TextBlockWidth = 254f;
    private const float TextBlockHeight = 40f;
    private const float ChipGap = 16f;

    private Bitmap? _appIcon;
    private int _appIconSize;
    private ChipView _creator = new("", "", Severity.Neutral, ShowDot: false);
    private ChipView _status = new("", "", Severity.Neutral);

    public void SetCreator(ChipView chip)
    {
        _creator = chip;
        Invalidate();
    }

    public void SetStatus(ChipView chip)
    {
        _status = chip;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prepare(e);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        float chipWidth = S(PictureSize + TextBlockWidth);
        int picture = (int)S(PictureSize);

        // The creator card, with the creator's own picture.
        DrawChip(g, 0, _creator, AppImages.Profile);

        // The status card, with the app icon.
        if (_appIcon == null || _appIconSize != picture)
        {
            _appIcon?.Dispose();
            _appIcon = AppIcon.Render(picture);
            _appIconSize = picture;
        }
        DrawChip(g, chipWidth + S(ChipGap), _status, _appIcon);

        // The wordmark: tilted a little, with a soft shadow, like the launcher's.
        var state = g.Save();
        float wordHeight = S(80);
        float top = S(PictureSize) + S(22);
        g.TranslateTransform(S(8), top + wordHeight / 2f);
        g.RotateTransform(-3.5f);
        const string name = "TRAINER UPDATER";
        var rect = new RectangleF(0, -wordHeight / 2f, Width, wordHeight);
        TextPaint.Draw(g, name, Theme.Wordmark, new RectangleF(rect.X + S(2), rect.Y + S(3), rect.Width, rect.Height), Color.FromArgb(150, 0, 0, 0));
        TextPaint.Draw(g, name, Theme.Wordmark, rect, Theme.Text);
        g.Restore(state);
    }

    // One card: a square picture on the left, a block of text beside it, and (for a status) a dot on the picture's corner.
    private void DrawChip(Graphics g, float x, ChipView chip, Image? picture)
    {
        float size = S(PictureSize);
        var pictureRect = new RectangleF(x, 0, size, size);
        if (picture != null)
        {
            g.DrawImage(picture, pictureRect);
        }
        else
        {
            using var brush = new SolidBrush(Theme.Control);
            g.FillRectangle(brush, pictureRect);
        }

        var block = new RectangleF(x + size, (size - S(TextBlockHeight)) / 2f, S(TextBlockWidth), S(TextBlockHeight));
        using (var brush = new SolidBrush(Theme.Chip))
        {
            g.FillRectangle(brush, block);
        }
        float textX = block.Left + S(12);
        float textWidth = block.Width - S(20);
        TextPaint.Draw(g, chip.Title, Theme.ChipTitle, new RectangleF(textX, block.Top + S(1), textWidth, S(21)), Theme.Text);
        TextPaint.Draw(g, chip.Subtitle, Theme.Small, new RectangleF(textX, block.Top + S(19), textWidth, S(19)), Theme.Muted);

        if (!chip.ShowDot) return;
        var dotColour = chip.Severity switch
        {
            Severity.Good => Theme.Success,
            Severity.Working => Theme.Accent,
            _ => Theme.Faint,
        };
        float dot = S(10);
        using var dotBrush = new SolidBrush(dotColour);
        g.FillEllipse(dotBrush, x - S(2), -S(2), dot, dot);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _appIcon?.Dispose();
        base.Dispose(disposing);
    }
}
