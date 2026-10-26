namespace ReSkateTrainerUpdater.Ui;

internal enum HAlign { Left, Center, Right }

internal enum VAlign { Top, Middle, Bottom }

/// <summary>
/// Draws and measures text with GDI+. The launcher's embedded fonts are only visible to GDI+ (the older TextRenderer
/// can't see them), so every control draws its text through here.
/// </summary>
internal static class TextPaint
{
    private static StringFormat MakeFormat(HAlign h, VAlign v, bool wrap, bool pathEllipsis)
    {
        var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = h switch { HAlign.Center => StringAlignment.Center, HAlign.Right => StringAlignment.Far, _ => StringAlignment.Near },
            LineAlignment = v switch { VAlign.Middle => StringAlignment.Center, VAlign.Bottom => StringAlignment.Far, _ => StringAlignment.Near },
            Trimming = pathEllipsis ? StringTrimming.EllipsisPath : StringTrimming.EllipsisCharacter,
            // No LineLimit: the marker font's lines are taller than its glyphs, and LineLimit would skip text that
            // doesn't fit whole lines in the box.
            FormatFlags = wrap ? 0 : StringFormatFlags.NoWrap,
        };
        return format;
    }

    /// <summary>Draws <paramref name="text"/> inside <paramref name="rect"/>, shortened with "..." if it doesn't fit.</summary>
    public static void Draw(Graphics g, string text, Font font, RectangleF rect, Color color,
        HAlign h = HAlign.Left, VAlign v = VAlign.Middle, bool wrap = false, bool pathEllipsis = false)
    {
        if (string.IsNullOrEmpty(text) || rect.Width <= 1 || rect.Height <= 1) return;
        using var format = MakeFormat(h, v, wrap, pathEllipsis);
        using var brush = new SolidBrush(color);
        g.DrawString(text, font, brush, rect, format);
    }

    /// <summary>How big the text is. With <paramref name="wrap"/> it wraps at <paramref name="maxWidth"/>.</summary>
    public static SizeF Measure(Graphics g, string text, Font font, float maxWidth = 100000f, bool wrap = false)
    {
        if (string.IsNullOrEmpty(text)) return SizeF.Empty;
        using var format = MakeFormat(HAlign.Left, VAlign.Top, wrap, pathEllipsis: false);
        return g.MeasureString(text, font, new SizeF(maxWidth, 100000f), format);
    }
}
