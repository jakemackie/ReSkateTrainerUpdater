using System.Drawing.Drawing2D;
using ReSkateTrainerUpdater.Ui;

namespace ReSkateTrainerUpdater.Ui.Controls;

/// <summary>
/// The minimise and close buttons in the top-right corner, drawn like the launcher's: two flush 46 by 33 buttons with
/// thin white icons, a faint wash on hover, and Windows' red for close. There is no maximise button, because the
/// window has a fixed size, as in the launcher.
/// </summary>
internal sealed class WindowButtons : BufferedControl
{
    public const int ButtonWidth = 46;
    public const int ButtonHeight = 33;

    private int _hot = -1;
    private int _down = -1;

    public event EventHandler? MinimizeClick;
    public event EventHandler? CloseClick;

    public WindowButtons()
    {
        Size = new Size(ButtonWidth * 2, ButtonHeight);
        Cursor = Cursors.Default;
        // The two corner buttons are one drawn control and are mouse only. Windows' own Alt+Space menu and Alt+F4 still minimise and close.
        Describe("Window buttons: minimise and close. Alt+F4 also closes the app.", AccessibleRole.Grouping);
    }

    private int ButtonAt(Point p) => p.X < 0 || p.X >= Width || p.Y < 0 || p.Y >= Height ? -1 : p.X < Width / 2 ? 0 : 1;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int hot = ButtonAt(e.Location);
        if (hot != _hot) { _hot = hot; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hot = -1;
        _down = -1;
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) _down = ButtonAt(e.Location);
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        int released = ButtonAt(e.Location);
        int pressed = _down;
        _down = -1;
        Invalidate();
        base.OnMouseUp(e);

        if (e.Button != MouseButtons.Left || released != pressed) return;
        if (released == 0) MinimizeClick?.Invoke(this, EventArgs.Empty);
        else if (released == 1) CloseClick?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prepare(e);
        float half = Width / 2f;
        var minimise = new RectangleF(0, 0, half, Height);
        var close = new RectangleF(half, 0, half, Height);

        // Hover and press backgrounds.
        if (_hot == 0)
        {
            using var brush = new SolidBrush(Color.FromArgb(_down == 0 ? 44 : 28, 255, 255, 255));
            g.FillRectangle(brush, minimise);
        }
        if (_hot == 1)
        {
            using var brush = new SolidBrush(_down == 1 ? Color.FromArgb(241, 112, 122) : Color.FromArgb(232, 17, 35));
            if (NativeMethods.HasRoundedCorners)
            {
                // The window's top-right corner is rounded, so the red fill follows it.
                float r = S(8);
                using var path = new GraphicsPath();
                path.AddLine(close.Left, close.Top, close.Right - r, close.Top);
                path.AddArc(close.Right - 2 * r, close.Top, 2 * r, 2 * r, 270, 90);
                path.AddLine(close.Right, close.Top + r, close.Right, close.Bottom);
                path.AddLine(close.Right, close.Bottom, close.Left, close.Bottom);
                path.CloseFigure();
                g.FillPath(brush, path);
            }
            else
            {
                g.FillRectangle(brush, close);
            }
        }

        // Thin white icons.
        using var pen = new Pen(Color.White, Math.Max(1f, S(1.1f))) { StartCap = LineCap.Flat, EndCap = LineCap.Flat };
        float cy = Height / 2f;

        float mx = minimise.Left + half / 2f;
        float arm = S(5);
        g.SmoothingMode = SmoothingMode.None;
        g.DrawLine(pen, mx - arm, (float)Math.Round(cy), mx + arm, (float)Math.Round(cy));

        g.SmoothingMode = SmoothingMode.AntiAlias;
        float cx = close.Left + half / 2f;
        g.DrawLine(pen, cx - arm, cy - arm, cx + arm, cy + arm);
        g.DrawLine(pen, cx + arm, cy - arm, cx - arm, cy + arm);
    }
}
