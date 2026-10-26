using System.Drawing.Drawing2D;
using ReSkateTrainerUpdater.Ui;

namespace ReSkateTrainerUpdater.Ui.Controls;

/// <summary>App icon, title and tagline at the top of the window.</summary>
internal sealed class HeaderBar : BufferedControl
{
    private Bitmap? _logo;
    private int _logoSize;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prepare(e);

        int size = (int)S(48);
        if (_logo == null || _logoSize != size)
        {
            _logo?.Dispose();
            _logo = AppIcon.Render(size);
            _logoSize = size;
        }

        var logoRect = new RectangleF(0, S(12), size, size);
        using (var clip = Theme.RoundedRect(logoRect, S(10)))
        {
            if (_logo != null)
            {
                g.SetClip(clip);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(_logo, logoRect);
                g.ResetClip();
            }
            else
            {
                using var brush = new SolidBrush(Theme.Accent);
                g.FillPath(brush, clip);
            }
        }

        int tx = (int)(size + S(14));
        TextRenderer.DrawText(g, AppInfo.Name, Theme.Title,
            new Rectangle(tx, (int)S(10), Math.Max(0, Width - tx), (int)S(30)), Theme.Text, LeftFlags);
        TextRenderer.DrawText(g, AppInfo.Tagline, Theme.Body,
            new Rectangle(tx, (int)S(41), Math.Max(0, Width - tx), (int)S(22)), Theme.Muted, LeftFlags);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _logo?.Dispose();
        base.Dispose(disposing);
    }
}
