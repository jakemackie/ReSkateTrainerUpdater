using ReSkateTrainerUpdater.Ui;

namespace ReSkateTrainerUpdater.Ui.Controls;

/// <summary>A card with a title, a one-line description and an animated on/off switch.</summary>
internal sealed class OptionRow : BufferedControl
{
    private bool _checked;
    private float _pos; // 0 = off, 1 = on (animated)
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };

    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public event EventHandler? CheckedChanged;

    public OptionRow()
    {
        TabStop = true;
        Cursor = Cursors.Hand;

        _timer.Tick += (_, _) =>
        {
            const float step = 0.18f;
            float target = _checked ? 1f : 0f;
            if (Math.Abs(target - _pos) <= step)
            {
                _pos = target;
                _timer.Stop();
            }
            else
            {
                _pos += target > _pos ? step : -step;
            }
            Invalidate();
        };
    }

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            _timer.Start();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Sets the state without animating or raising CheckedChanged (for start-up).</summary>
    public void SetCheckedImmediate(bool value)
    {
        _checked = value;
        _pos = value ? 1f : 0f;
        Invalidate();
    }

    protected override void OnClick(EventArgs e)
    {
        Checked = !Checked;
        base.OnClick(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prepare(e);
        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);

        using (var path = Theme.RoundedRect(rect, S(12)))
        using (var brush = new SolidBrush(Hover ? Theme.CardHover : Theme.Card))
        using (var pen = new Pen(Theme.Border))
        {
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }
        DrawFocusRing(g, rect, S(12));

        // The switch.
        float trackW = S(40);
        float trackH = S(22);
        float trackX = Width - S(18) - trackW;
        float trackY = (Height - trackH) / 2f;
        var trackRect = new RectangleF(trackX, trackY, trackW, trackH);

        using (var path = Theme.RoundedRect(trackRect, trackH / 2f))
        using (var brush = new SolidBrush(Theme.Lerp(Color.FromArgb(63, 69, 84), Theme.Accent, _pos)))
        {
            g.FillPath(brush, path);
        }

        float knob = S(16);
        float knobX = trackX + S(3) + _pos * (trackW - knob - S(6));
        using (var brush = new SolidBrush(Theme.Lerp(Color.FromArgb(190, 196, 208), Color.White, _pos)))
        {
            g.FillEllipse(brush, knobX, trackY + S(3), knob, knob);
        }

        // The text.
        int left = (int)S(18);
        int width = Math.Max(0, (int)(trackX - S(14)) - left);
        TextRenderer.DrawText(g, Title, Theme.Semibold,
            new Rectangle(left, (int)S(9), width, (int)S(20)), Theme.Text, LeftFlags);
        TextRenderer.DrawText(g, Description, Theme.Small,
            new Rectangle(left, (int)S(30), width, (int)S(18)), Theme.Muted, LeftFlags);
    }
}
