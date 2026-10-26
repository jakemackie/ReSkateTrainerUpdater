using ReSkateTrainerUpdater.Ui;

namespace ReSkateTrainerUpdater.Ui.Controls;

internal enum TileGlyph { Folder, Zip }

/// <summary>
/// A flat dark tile like SETTINGS or MOD MANAGER in the launcher: a title, a muted line, a glyph in a square, and a small
/// word on the right saying what a click does. Used for the ReSkate folder and the Trainer zip. It can also take a
/// dragged file (highlighted in blue) and show a small cross to clear what was chosen.
/// </summary>
internal sealed class InfoTile : BufferedControl
{
    private Rectangle _clearBounds = Rectangle.Empty;

    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string ActionText { get; set; } = "";
    public TileGlyph Glyph { get; set; }
    public bool PathSubtitle { get; set; }

    /// <summary>True while a file is being dragged over the window, to show it can be dropped here.</summary>
    public bool Highlight { get; set; }

    /// <summary>Shows a small cross that raises <see cref="ClearClick"/> instead of <see cref="Control.Click"/>.</summary>
    public bool Clearable { get; set; }

    public event EventHandler? ClearClick;

    public InfoTile()
    {
        TabStop = true;
        Cursor = Cursors.Hand;
    }

    public void Set(string title, string subtitle, string actionText, bool clearable = false)
    {
        Title = title;
        Subtitle = subtitle;
        ActionText = actionText;
        Clearable = clearable;
        Invalidate();
    }

    public void SetHighlight(bool on)
    {
        if (Highlight == on) return;
        Highlight = on;
        Invalidate();
    }

    protected override void OnClick(EventArgs e)
    {
        if (Clearable && _clearBounds.Contains(PointToClient(Cursor.Position)))
        {
            ClearClick?.Invoke(this, EventArgs.Empty);
            return;
        }
        base.OnClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prepare(e);
        var area = new RectangleF(0, 0, Width, Height);

        using (var brush = new SolidBrush(Highlight ? Color.FromArgb(18, 34, 58) : Hover ? Theme.TileHover : Theme.Tile))
        {
            g.FillRectangle(brush, area);
        }
        if (Highlight)
        {
            using var pen = new Pen(Theme.Accent, S(2f));
            g.DrawRectangle(pen, S(1), S(1), Width - S(2), Height - S(2));
        }

        // The glyph, in a square like the launcher's buttons.
        float box = S(50);
        float bx = S(20);
        float by = (Height - box) / 2f;
        using (var brush = new SolidBrush(Theme.Control))
        {
            g.FillRectangle(brush, bx, by, box, box);
        }
        float cx = bx + box / 2f;
        float cy = by + box / 2f;
        var glyphColour = Highlight ? Theme.Accent : Theme.Muted;
        if (Glyph == TileGlyph.Folder) DrawFolderGlyph(g, cx, cy, glyphColour);
        else DrawZipGlyph(g, cx, cy, glyphColour);

        // The words.
        float textX = bx + box + S(18);
        float right = Width - S(20);

        if (Clearable)
        {
            float arm = S(6);
            _clearBounds = new Rectangle((int)(right - S(28)), (int)((Height - S(28)) / 2f), (int)S(28), (int)S(28));
            DrawCrossGlyph(g, _clearBounds.X + _clearBounds.Width / 2f, _clearBounds.Y + _clearBounds.Height / 2f, Theme.Muted, arm);
            right -= S(36);
        }
        else
        {
            _clearBounds = Rectangle.Empty;
            if (ActionText.Length > 0)
            {
                var size = TextPaint.Measure(g, ActionText, Theme.Small);
                TextPaint.Draw(g, ActionText, Theme.Small, new RectangleF(right - size.Width - 2, 0, size.Width + 4, Height), Theme.Accent,
                    HAlign.Right, VAlign.Middle);
                right -= size.Width + S(16);
            }
        }

        float textWidth = Math.Max(0, right - textX);
        TextPaint.Draw(g, Title, Theme.Strong, new RectangleF(textX, Height / 2f - S(26), textWidth, S(24)), Theme.Text, HAlign.Left, VAlign.Bottom);
        TextPaint.Draw(g, Subtitle, Theme.Small, new RectangleF(textX, Height / 2f + S(3), textWidth, S(24)), Theme.Muted,
            HAlign.Left, VAlign.Top, pathEllipsis: PathSubtitle);

        DrawFocusRing(g, area);
    }
}
