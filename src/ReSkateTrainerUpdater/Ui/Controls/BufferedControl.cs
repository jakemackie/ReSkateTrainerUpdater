using System.Drawing.Drawing2D;
using System.Drawing.Text;
using ReSkateTrainerUpdater.Core;
using ReSkateTrainerUpdater.Ui;

namespace ReSkateTrainerUpdater.Ui.Controls;

/// <summary>
/// Base for the custom-painted controls: double buffering, hover/press state, DPI scaling, and the little shapes
/// that more than one control draws (status icons, pills, glyphs).
/// </summary>
internal abstract class BufferedControl : Control
{
    protected BufferedControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        TabStop = false;
    }

    /// <summary>Scales a 96-dpi pixel value to the current display scaling.</summary>
    protected float S(float value) => value * DeviceDpi / 96f;

    protected Color ParentBack => Parent?.BackColor ?? Theme.Bg;
    protected bool Hover { get; private set; }
    protected bool Pressed { get; private set; }
    /// <summary>True when this control has focus and the user is navigating by keyboard (so a focus outline is wanted).</summary>
    protected bool KeyboardFocused => Focused && ShowFocusCues;

    protected Graphics Prepare(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(ParentBack);
        return g;
    }

    /// <summary>A square-cornered focus outline, shown only when focus arrived by keyboard.</summary>
    protected void DrawFocusRing(Graphics g, RectangleF rect)
    {
        if (!KeyboardFocused) return;
        using var pen = new Pen(Theme.Accent, S(2f));
        g.DrawRectangle(pen, rect.X + S(1), rect.Y + S(1), rect.Width - S(2), rect.Height - S(2));
    }

    // ---------- Pills ----------

    /// <summary>The width a pill with this text needs.</summary>
    protected int PillWidth(Graphics g, string text)
    {
        var size = TextPaint.Measure(g, text, Theme.Pill);
        return (int)Math.Ceiling(size.Width + S(26));
    }

    /// <summary>
    /// Draws a pill: a fully rounded capsule filled with <paramref name="fill"/> and a short upper-case word in white,
    /// like the READY pill in the launcher. Returns where it was drawn.
    /// </summary>
    protected Rectangle DrawPill(Graphics g, int x, int y, string text, Color fill)
    {
        int height = (int)S(24);
        var rect = new Rectangle(x, y, PillWidth(g, text), height);

        using (var path = Theme.RoundedRect(rect, height / 2f))
        using (var brush = new SolidBrush(fill))
        {
            g.FillPath(brush, path);
        }
        TextPaint.Draw(g, text, Theme.Pill, rect, Color.White, HAlign.Center, VAlign.Middle);
        return rect;
    }

    // ---------- Icons ----------

    /// <summary>
    /// A filled circle with a white symbol for how things stand: a tick, an up arrow, an exclamation mark, a cross,
    /// a dot, or (for work in progress) a spinning arc. <paramref name="spin"/> is the spinner's angle in degrees.
    /// </summary>
    protected void DrawStatusIcon(Graphics g, float cx, float cy, float radius, Severity severity, float spin = 0f)
    {
        var colour = severity == Severity.Neutral ? Color.FromArgb(86, 88, 96) : Theme.ForSeverity(severity);

        if (severity == Severity.Working)
        {
            using var pen = new Pen(colour, radius * 0.24f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            float r = radius * 0.78f;
            g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, spin, 270f);
            return;
        }

        using (var brush = new SolidBrush(colour))
        {
            g.FillEllipse(brush, cx - radius, cy - radius, radius * 2, radius * 2);
        }

        using var white = new Pen(Color.White, Math.Max(S(1.6f), radius * 0.2f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        float k = radius;
        switch (severity)
        {
            case Severity.Good:
                g.DrawLines(white, new[]
                {
                    new PointF(cx - 0.42f * k, cy + 0.02f * k),
                    new PointF(cx - 0.10f * k, cy + 0.36f * k),
                    new PointF(cx + 0.46f * k, cy - 0.30f * k),
                });
                break;
            case Severity.Highlight:
                g.DrawLine(white, cx, cy + 0.42f * k, cx, cy - 0.38f * k);
                g.DrawLines(white, new[]
                {
                    new PointF(cx - 0.36f * k, cy - 0.04f * k),
                    new PointF(cx, cy - 0.40f * k),
                    new PointF(cx + 0.36f * k, cy - 0.04f * k),
                });
                break;
            case Severity.Warning:
                g.DrawLine(white, cx, cy - 0.42f * k, cx, cy + 0.10f * k);
                using (var dot = new SolidBrush(Color.White))
                {
                    float d = Math.Max(S(1.4f), k * 0.13f);
                    g.FillEllipse(dot, cx - d, cy + 0.40f * k - d, d * 2, d * 2);
                }
                break;
            case Severity.Error:
                g.DrawLine(white, cx - 0.32f * k, cy - 0.32f * k, cx + 0.32f * k, cy + 0.32f * k);
                g.DrawLine(white, cx + 0.32f * k, cy - 0.32f * k, cx - 0.32f * k, cy + 0.32f * k);
                break;
            default:
                using (var dot = new SolidBrush(Color.White))
                {
                    float d = k * 0.2f;
                    g.FillEllipse(dot, cx - d, cy - d, d * 2, d * 2);
                }
                break;
        }
    }

    protected void DrawFolderGlyph(Graphics g, float cx, float cy, Color color)
    {
        using var brush = new SolidBrush(color);
        using (var tab = Theme.RoundedRect(new RectangleF(cx - S(11), cy - S(8), S(10), S(6)), S(1.5f)))
            g.FillPath(brush, tab);
        using (var body = Theme.RoundedRect(new RectangleF(cx - S(11), cy - S(5), S(22), S(15)), S(2.5f)))
            g.FillPath(brush, body);
    }

    protected void DrawZipGlyph(Graphics g, float cx, float cy, Color color)
    {
        using var pen = new Pen(color, S(2f)) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        // A sheet with a folded corner and a zip down the middle.
        g.DrawLines(pen, new[]
        {
            new PointF(cx - S(8), cy - S(11)), new PointF(cx + S(3), cy - S(11)), new PointF(cx + S(9), cy - S(5)),
            new PointF(cx + S(9), cy + S(11)), new PointF(cx - S(8), cy + S(11)), new PointF(cx - S(8), cy - S(11)),
        });
        using var thin = new Pen(color, S(1.6f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        for (int i = 0; i < 4; i++)
        {
            float y = cy - S(7) + i * S(4.4f);
            g.DrawLine(thin, cx - S(1.5f) + (i % 2 == 0 ? 0 : S(3)), y, cx + S(1.5f) + (i % 2 == 0 ? 0 : S(3)) - S(3), y);
        }
    }

    protected void DrawCrossGlyph(Graphics g, float cx, float cy, Color color, float arm)
    {
        using var pen = new Pen(color, S(2f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pen, cx - arm, cy - arm, cx + arm, cy + arm);
        g.DrawLine(pen, cx + arm, cy - arm, cx - arm, cy + arm);
    }

    // ---------- Mouse and keyboard ----------

    protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Hover = false; Pressed = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { Pressed = true; Invalidate(); }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e) { Pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    // The focus outline follows Windows' own rule: hidden until the user presses Tab or an arrow key.
    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (TabStop && Enabled && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter))
        {
            e.Handled = true;
            OnClick(EventArgs.Empty);
        }
        base.OnKeyUp(e);
    }
}
