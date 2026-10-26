using System.Drawing.Drawing2D;
using ReSkateTrainerUpdater.Ui;

namespace ReSkateTrainerUpdater.Ui.Controls;

/// <summary>Base for the custom-painted controls: double buffering, hover/press state, DPI helper.</summary>
internal abstract class BufferedControl : Control
{
    protected const TextFormatFlags CenterFlags =
        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
        TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;

    protected const TextFormatFlags LeftFlags =
        TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
        TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;

    protected const TextFormatFlags LeftPathFlags =
        TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
        TextFormatFlags.PathEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;

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
    protected bool KeyboardFocused { get; private set; }

    protected Graphics Prepare(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(ParentBack);
        return g;
    }

    protected void DrawFocusRing(Graphics g, RectangleF rect, float radius)
    {
        if (!KeyboardFocused) return;
        using var path = Theme.RoundedRect(rect, radius);
        using var pen = new Pen(Theme.Accent, S(2f));
        g.DrawPath(pen, path);
    }

    protected void DrawDownloadGlyph(Graphics g, float cx, float cy, Color color, float penWidth)
    {
        using var pen = new Pen(color, penWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        g.DrawLine(pen, cx, cy - S(9), cx, cy + S(4));
        g.DrawLines(pen, new[]
        {
            new PointF(cx - S(5.5f), cy - S(1.5f)),
            new PointF(cx, cy + S(4)),
            new PointF(cx + S(5.5f), cy - S(1.5f)),
        });
        g.DrawLines(pen, new[]
        {
            new PointF(cx - S(8), cy + S(6.5f)),
            new PointF(cx - S(8), cy + S(9.5f)),
            new PointF(cx + S(8), cy + S(9.5f)),
            new PointF(cx + S(8), cy + S(6.5f)),
        });
    }

    protected void DrawCheckGlyph(Graphics g, float cx, float cy, Color color, float penWidth)
    {
        using var pen = new Pen(color, penWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        g.DrawLines(pen, new[]
        {
            new PointF(cx - S(7), cy + S(0.5f)),
            new PointF(cx - S(2), cy + S(5.5f)),
            new PointF(cx + S(8), cy - S(5)),
        });
    }

    protected void DrawWarningGlyph(Graphics g, float cx, float cy, Color color, float penWidth)
    {
        using var pen = new Pen(color, penWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        g.DrawLine(pen, cx, cy - S(8), cx, cy + S(2));
        using var brush = new SolidBrush(color);
        float r = S(1.8f);
        g.FillEllipse(brush, cx - r, cy + S(6.5f) - r, r * 2, r * 2);
    }

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

    // Only show the focus ring when focus arrived via the keyboard (Tab), not from a mouse click.
    protected override void OnGotFocus(EventArgs e)
    {
        KeyboardFocused = MouseButtons == MouseButtons.None;
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        KeyboardFocused = false;
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
