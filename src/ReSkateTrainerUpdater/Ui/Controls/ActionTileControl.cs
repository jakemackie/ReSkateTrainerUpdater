using System.Drawing.Drawing2D;
using ReSkateTrainerUpdater.Core;
using ReSkateTrainerUpdater.Ui;

namespace ReSkateTrainerUpdater.Ui.Controls;

/// <summary>
/// The big tile, in the spirit of the launcher's PLAY button: bright blue with a huge marker-pen word and a rough
/// brushed edge. While something is running it fills like a progress bar. When there is nothing to do it goes dark.
/// It draws an <see cref="ActionTile"/> and decides nothing itself.
/// </summary>
internal sealed class ActionTileControl : BufferedControl
{
    private readonly System.Windows.Forms.Timer _animation = new() { Interval = 30 };
    private ActionTile? _tile;
    private float _phase;
    private Font? _fitted;
    private string _fittedKey = "";

    public ActionTileControl()
    {
        _animation.Tick += (_, _) =>
        {
            _phase = (_phase + 0.018f) % 1f;
            Invalidate();
        };
    }

    public void Show(ActionTile tile)
    {
        _tile = tile;
        Enabled = tile.Enabled;
        TabStop = tile.Enabled;
        Cursor = tile.Enabled ? Cursors.Hand : Cursors.Default;
        if (tile.Working) _animation.Start();
        else _animation.Stop();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animation.Dispose();
            _fitted?.Dispose();
        }
        base.Dispose(disposing);
    }

    // The marker font is huge, so shrink it until a long word such as DOWNLOADING fits the tile.
    private Font FitTitle(Graphics g, string text, float maxWidth)
    {
        string key = $"{text}|{(int)maxWidth}|{DeviceDpi}";
        if (_fitted is not null && key == _fittedKey) return _fitted;

        _fitted?.Dispose();
        float size = Theme.Tile64.SizeInPoints;
        Font font = new(Theme.Tile64.FontFamily, size, FontStyle.Regular, GraphicsUnit.Point);
        while (size > 18f && TextPaint.Measure(g, text, font).Width > maxWidth)
        {
            font.Dispose();
            size -= 2f;
            font = new Font(Theme.Tile64.FontFamily, size, FontStyle.Regular, GraphicsUnit.Point);
        }
        _fitted = font;
        _fittedKey = key;
        return font;
    }

    // A rectangle whose right and bottom edges are slightly rough, like a brush stroke.
    private PointF[] BrushedRect(RectangleF r)
    {
        var rough = new Random(11);
        float jitter = S(5);
        float step = S(16);
        var points = new List<PointF> { new(r.Left, r.Top), new(r.Right, r.Top) };

        for (float y = r.Top + step; y < r.Bottom - step; y += step)
            points.Add(new PointF(r.Right - (float)rough.NextDouble() * jitter, y));
        points.Add(new PointF(r.Right - (float)rough.NextDouble() * jitter, r.Bottom));
        for (float x = r.Right - step; x > r.Left + step; x -= step)
            points.Add(new PointF(x, r.Bottom - (float)rough.NextDouble() * jitter));
        points.Add(new PointF(r.Left, r.Bottom));
        return points.ToArray();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prepare(e);
        if (_tile is not { } tile) return;

        var area = new RectangleF(0, 0, Width, Height);
        bool primary = tile.Style == TileStyle.Primary;
        bool working = tile.Working;

        Color ink, subInk;
        if (primary && working)
        {
            ink = Color.White;
            subInk = Color.FromArgb(230, 240, 255);
        }
        else if (primary)
        {
            ink = Theme.Ink;
            subInk = Theme.Ink;
        }
        else
        {
            ink = tile.Style == TileStyle.Success ? Theme.Success : Theme.Faint;
            subInk = Theme.Faint;
        }

        if (primary)
        {
            Color fill = working ? Theme.AccentDeep
                       : Pressed ? Theme.AccentPressed
                       : Hover ? Theme.AccentHover
                       : Theme.Accent;
            var shape = BrushedRect(area);
            using (var brush = new SolidBrush(fill))
            {
                g.FillPolygon(brush, shape);
            }

            if (working)
            {
                using var clip = new GraphicsPath();
                clip.AddPolygon(shape);
                var state = g.Save();
                g.SetClip(clip);

                if (tile.Progress is { } progress)
                {
                    using var fillBrush = new SolidBrush(Theme.Accent);
                    g.FillRectangle(fillBrush, 0, 0, (float)(Width * Math.Clamp(progress, 0, 1)), Height);
                }
                else
                {
                    // No known progress: a lighter band sweeps across.
                    float band = Width * 0.4f;
                    float x = -band + _phase * (Width + band);
                    using var bandBrush = new LinearGradientBrush(new RectangleF(x, 0, band, Height),
                        Color.FromArgb(0, Theme.Accent), Theme.Accent, LinearGradientMode.Horizontal);
                    var blend = new ColorBlend
                    {
                        Colors = new[] { Color.FromArgb(0, Theme.Accent), Color.FromArgb(220, Theme.Accent), Color.FromArgb(0, Theme.Accent) },
                        Positions = new[] { 0f, 0.5f, 1f },
                    };
                    bandBrush.InterpolationColors = blend;
                    g.FillRectangle(bandBrush, x, 0, band, Height);
                }
                g.Restore(state);
            }
        }
        else
        {
            using var brush = new SolidBrush(Theme.Tile);
            g.FillRectangle(brush, area);
        }

        // Where the launcher puts the word on its PLAY tile: 20 pixels in, near the top, with a small line at the bottom.
        float pad = S(20);
        var titleFont = FitTitle(g, tile.Title, Width - pad * 2 - (tile.Style == TileStyle.Success ? S(50) : 0));
        TextPaint.Draw(g, tile.Title, titleFont, new RectangleF(pad, S(6), Width - pad * 1.4f, S(84)), ink, HAlign.Left, VAlign.Top);

        TextPaint.Draw(g, tile.Subtitle, Theme.ChipTitle, new RectangleF(pad, Height - S(54), Width - pad * 2, S(32)), subInk,
            HAlign.Left, VAlign.Bottom);

        if (tile.Style == TileStyle.Success)
            DrawStatusIcon(g, Width - pad - S(16), pad + S(18), S(16), Severity.Good);

        DrawFocusRing(g, area);
    }
}
