using System.Drawing.Drawing2D;
using ReSkateTrainerUpdater.Ui;

namespace ReSkateTrainerUpdater.Ui.Controls;

/// <summary>The app icon and the name in the launcher's marker-pen lettering.</summary>
internal sealed class HeaderBar : BufferedControl
{
    private Bitmap? _logo;
    private int _logoSize;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prepare(e);

        int size = (int)S(56);
        if (_logo == null || _logoSize != size)
        {
            _logo?.Dispose();
            _logo = AppIcon.Render(size);
            _logoSize = size;
        }

        var logoRect = new RectangleF(0, (Height - size) / 2f, size, size);
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

        float tx = size + S(18);
        float width = Math.Max(0, Width - tx);
        TextPaint.Draw(g, AppInfo.Name.ToUpperInvariant(), Theme.Display, new RectangleF(tx, S(2), width, S(46)), Theme.Text, HAlign.Left, VAlign.Middle);
        TextPaint.Draw(g, AppInfo.Tagline, Theme.Small, new RectangleF(tx, S(50), width, S(24)), Theme.Muted, HAlign.Left, VAlign.Top);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _logo?.Dispose();
        base.Dispose(disposing);
    }
}
