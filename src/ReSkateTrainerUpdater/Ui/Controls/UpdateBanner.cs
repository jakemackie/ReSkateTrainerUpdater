using ReSkateTrainerUpdater.Ui;

namespace ReSkateTrainerUpdater.Ui.Controls;

/// <summary>
/// The "Update available" card at the top of the window: a title, a detail line and one button.
/// It only displays what it is given; the text comes from UpdateAdvisor.DescribeOnlineOffer.
/// </summary>
internal sealed class UpdateBanner : BufferedControl
{
    private readonly RoundedButton _button = new() { Primary = true };
    private string _title = "";
    private string _detail = "";

    public UpdateBanner()
    {
        Controls.Add(_button);
        _button.Click += (_, _) => ButtonClick?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when the banner's button is clicked.</summary>
    public event EventHandler? ButtonClick;

    public bool ButtonEnabled
    {
        get => _button.Enabled;
        set => _button.Enabled = value;
    }

    public void SetOffer(string title, string detail, string buttonText)
    {
        _title = title;
        _detail = detail;
        _button.Text = buttonText;
        PerformLayout();
        Invalidate();
    }

    public void SetButtonText(string text)
    {
        _button.Text = text;
        PerformLayout();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int textWidth = TextRenderer.MeasureText(_button.Text, _button.Font).Width;
        int width = Math.Max((int)S(96), textWidth + (int)S(32));
        int height = (int)S(40);
        int pad = (int)S(14);
        _button.Bounds = new Rectangle(Width - width - pad, (Height - height) / 2, width, height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prepare(e);
        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);

        using (var path = Theme.RoundedRect(rect, S(12)))
        using (var brush = new SolidBrush(Color.FromArgb(24, 31, 52)))
        using (var pen = new Pen(Color.FromArgb(60, 82, 160)))
        {
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }

        float iconSize = S(32);
        float ix = S(14);
        float iy = (Height - iconSize) / 2f;
        using (var brush = new SolidBrush(Color.FromArgb(34, 46, 80)))
        {
            g.FillEllipse(brush, ix, iy, iconSize, iconSize);
        }
        DrawDownloadGlyph(g, ix + iconSize / 2f, iy + iconSize / 2f, Theme.Accent, S(2f));

        int tx = (int)(ix + iconSize + S(12));
        int tw = Math.Max(0, _button.Left - tx - (int)S(10));
        TextRenderer.DrawText(g, _title, Theme.Semibold,
            new Rectangle(tx, (int)(Height / 2f - S(20)), tw, (int)S(20)), Theme.Text, LeftFlags);
        TextRenderer.DrawText(g, _detail, Theme.Small,
            new Rectangle(tx, (int)(Height / 2f + S(1)), tw, (int)S(18)), Theme.Muted, LeftFlags);
    }
}
