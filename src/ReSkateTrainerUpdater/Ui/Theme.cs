using System.Drawing.Drawing2D;
using ReSkateTrainerUpdater.Core;

namespace ReSkateTrainerUpdater.Ui;

/// <summary>The dark colour palette, fonts and drawing helpers shared by every control.</summary>
internal static class Theme
{
    public static readonly Color Bg = Color.FromArgb(17, 19, 24);
    public static readonly Color Card = Color.FromArgb(27, 30, 37);
    public static readonly Color CardHover = Color.FromArgb(34, 38, 47);
    public static readonly Color Border = Color.FromArgb(47, 52, 63);
    public static readonly Color Text = Color.FromArgb(235, 238, 244);
    public static readonly Color Muted = Color.FromArgb(135, 144, 160);
    public static readonly Color Accent = Color.FromArgb(94, 129, 255);
    public static readonly Color AccentHover = Color.FromArgb(120, 151, 255);
    public static readonly Color AccentPressed = Color.FromArgb(72, 106, 230);
    public static readonly Color Success = Color.FromArgb(62, 207, 142);
    public static readonly Color Warning = Color.FromArgb(255, 190, 80);
    public static readonly Color Danger = Color.FromArgb(255, 107, 107);

    public static readonly Font Body = new("Segoe UI", 9.5f);
    public static readonly Font Small = new("Segoe UI", 8.5f);
    public static readonly Font Semibold = new("Segoe UI Semibold", 10f);
    public static readonly Font ButtonFont = new("Segoe UI Semibold", 10.5f);
    public static readonly Font Title = new("Segoe UI Semibold", 17f);
    public static readonly Font Label = new("Segoe UI Semibold", 8f);
    public static readonly Font Mono = new("Consolas", 9f);

    /// <summary>The colour that goes with a message of the given severity.</summary>
    public static Color ForSeverity(Severity severity) => severity switch
    {
        Severity.Working => Accent,
        Severity.Good => Success,
        Severity.Warning => Warning,
        Severity.Error => Danger,
        _ => Muted,
    };

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        radius = Math.Max(0f, Math.Min(radius, Math.Min(r.Width, r.Height) / 2f));
        float d = radius * 2f;
        var path = new GraphicsPath();
        if (d <= 0f)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Color Lerp(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }
}
