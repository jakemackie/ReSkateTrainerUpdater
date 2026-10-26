using ReSkateTrainerUpdater.Ui;

namespace ReSkateTrainerUpdater.Ui.Controls;

/// <summary>Thin indeterminate progress bar; only visible while Active.</summary>
internal sealed class ProgressStrip : BufferedControl
{
    private const float Start = -0.35f;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private float _x = Start;
    private bool _active;

    public ProgressStrip()
    {
        _timer.Tick += (_, _) =>
        {
            _x += 0.012f;
            if (_x > 1f) _x = Start;
            Invalidate();
        };
    }

    public bool Active
    {
        get => _active;
        set
        {
            if (_active == value) return;
            _active = value;
            if (value) _timer.Start();
            else { _timer.Stop(); _x = Start; }
            Invalidate();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prepare(e);
        if (!_active) return;

        float h = S(4);
        float y = (Height - h) / 2f;
        var track = new RectangleF(0, y, Width, h);

        using var path = Theme.RoundedRect(track, h / 2f);
        using (var trackBrush = new SolidBrush(Theme.Border))
        {
            g.FillPath(trackBrush, path);
        }

        g.SetClip(path);
        using (var segBrush = new SolidBrush(Theme.Accent))
        {
            g.FillRectangle(segBrush, _x * Width, y, Width * 0.35f, h);
        }
        g.ResetClip();
    }
}
