using ReSkateTrainerUpdater.Ui;

namespace ReSkateTrainerUpdater.Ui.Controls;

internal enum TileGlyph { Folder, Zip }

/// <summary>
/// A flat dark tile like SETTINGS or MOD MANAGER in the launcher: a big title, a small muted line under it, and an icon on
/// the right. Used for the ReSkate folder and the Trainer zip. It can also take a dragged file (outlined in blue) and
/// swap its icon for a cross that clears what was chosen.
/// </summary>
internal sealed class InfoTile : BufferedControl
{
    private Rectangle _clearBounds = Rectangle.Empty;

    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public TileGlyph Glyph { get; set; }
    public bool PathSubtitle { get; set; }

    /// <summary>True while a file is being dragged over the window, to show it can be dropped here.</summary>
    public bool Highlight { get; set; }

    /// <summary>Shows a cross in place of the icon that raises <see cref="ClearClick"/> instead of <see cref="Control.Click"/>.</summary>
    public bool Clearable { get; set; }

    public event EventHandler? ClearClick;

    public InfoTile()
    {
        TabStop = true;
        Cursor = Cursors.Hand;
    }

    public void Set(string title, string subtitle, bool clearable = false)
    {
        Title = title;
        Subtitle = subtitle;
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

        // The icon, or a cross, on the right.
        float iconCx = Width - S(44);
        float cy = Height / 2f;
        var iconColour = Highlight ? Theme.Accent : Color.FromArgb(120, 121, 128);
        if (Clearable)
        {
            _clearBounds = new Rectangle((int)(iconCx - S(20)), (int)(cy - S(20)), (int)S(40), (int)S(40));
            if (_clearBounds.Contains(PointToClient(Cursor.Position)))
            {
                using var wash = new SolidBrush(Theme.ControlHover);
                g.FillRectangle(wash, _clearBounds);
            }
            DrawCrossGlyph(g, iconCx, cy, Theme.Text, S(7));
        }
        else
        {
            _clearBounds = Rectangle.Empty;
            if (Glyph == TileGlyph.Folder) DrawFolderGlyph(g, iconCx, cy, iconColour);
            else DrawZipGlyph(g, iconCx, cy, iconColour);
        }

        // The words.
        float textX = S(24);
        float textWidth = Math.Max(0, iconCx - S(34) - textX);
        TextPaint.Draw(g, Title, Theme.Title, new RectangleF(textX, cy - S(31), textWidth, S(30)), Theme.Text, HAlign.Left, VAlign.Bottom);
        TextPaint.Draw(g, Subtitle, Theme.Small, new RectangleF(textX, cy + S(1), textWidth, S(24)), Theme.Muted,
            HAlign.Left, VAlign.Top, pathEllipsis: PathSubtitle);

        DrawFocusRing(g, area);
    }
}
