using System.Drawing.Drawing2D;
using ReSkateTrainerUpdater.Core;

namespace ReSkateTrainerUpdater.Ui;

/// <summary>
/// The look of the ReSkate launcher: near-black background, flat square dark cards, one bright blue, one clear green,
/// and Montserrat text with Permanent Marker for the big headings. The values were sampled from the launcher itself.
/// Every control takes its colours and fonts from here.
/// </summary>
internal static class Theme
{
    public static readonly Color Bg = Color.FromArgb(14, 15, 18);
    public static readonly Color Card = Color.FromArgb(28, 27, 27);
    public static readonly Color Tile = Color.FromArgb(26, 26, 26);
    public static readonly Color TileHover = Color.FromArgb(37, 37, 39);
    public static readonly Color Control = Color.FromArgb(45, 45, 47);
    public static readonly Color ControlHover = Color.FromArgb(60, 60, 63);
    public static readonly Color Divider = Color.FromArgb(48, 48, 50);

    public static readonly Color Text = Color.FromArgb(255, 255, 255);
    public static readonly Color TextSoft = Color.FromArgb(222, 222, 226);
    public static readonly Color Muted = Color.FromArgb(150, 151, 158);
    public static readonly Color Faint = Color.FromArgb(98, 99, 106);

    public static readonly Color Accent = Color.FromArgb(1, 131, 255);
    public static readonly Color AccentHover = Color.FromArgb(52, 158, 255);
    public static readonly Color AccentPressed = Color.FromArgb(0, 104, 212);
    public static readonly Color AccentDeep = Color.FromArgb(0, 78, 158);
    public static readonly Color Success = Color.FromArgb(46, 184, 92);
    public static readonly Color Warning = Color.FromArgb(255, 176, 46);
    public static readonly Color Danger = Color.FromArgb(240, 82, 82);
    public static readonly Color Ink = Color.FromArgb(8, 9, 12);

    // The big marker-pen headings.
    public static readonly Font Display = GameFonts.Create(GameFonts.Marker, "Segoe UI Black", 30f);
    public static readonly Font Tile64 = GameFonts.Create(GameFonts.Marker, "Segoe UI Black", 52f);

    // Montserrat, as in the launcher: ExtraBold for titles and labels, SemiBold for sentences.
    public static readonly Font Heading = GameFonts.Create(GameFonts.MontserratExtraBold, "Segoe UI Black", 18f);
    public static readonly Font Title = GameFonts.Create(GameFonts.MontserratExtraBold, "Segoe UI Black", 14f);
    public static readonly Font Strong = GameFonts.Create(GameFonts.MontserratExtraBold, "Segoe UI Black", 11f);
    public static readonly Font Body = GameFonts.Create(GameFonts.MontserratSemiBold, "Segoe UI Semibold", 11f);
    public static readonly Font Small = GameFonts.Create(GameFonts.MontserratSemiBold, "Segoe UI Semibold", 9.5f);
    public static readonly Font Pill = GameFonts.Create(GameFonts.MontserratExtraBold, "Segoe UI Black", 8.5f);
    public static readonly Font Mono = new("Consolas", 10f);

    /// <summary>The colour that goes with a message of the given severity.</summary>
    public static Color ForSeverity(Severity severity) => severity switch
    {
        Severity.Working => Accent,
        Severity.Good => Success,
        Severity.Warning => Warning,
        Severity.Error => Danger,
        Severity.Highlight => Accent,
        _ => Faint,
    };

    /// <summary>A rectangle with small rounded corners, for pills and the like. The cards themselves are square.</summary>
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
