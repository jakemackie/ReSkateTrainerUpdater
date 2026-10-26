using ReSkateTrainerUpdater.Ui;

namespace ReSkateTrainerUpdater.Ui.Controls;

/// <summary>
/// The online card near the top of the window: an icon, a title, a detail line and one button.
/// When there is nothing to download, the button is greyed out and the text says why (for example
/// "You're running the newest version"). It only displays what it is given; the text comes from
/// UpdateAdvisor.DescribeOnlineOffer.
/// </summary>
internal sealed class UpdateBanner : BufferedControl
{
    private readonly RoundedButton _button = new() { Primary = true };
    private string _title = "";
    private string _detail = "";
    private bool _canDownload = true;
    private bool _busy;

    public UpdateBanner()
    {
        Controls.Add(_button);
        _button.Click += (_, _) => ButtonClick?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when the button is clicked. Never raised while it is greyed out.</summary>
    public event EventHandler? ButtonClick;

    /// <summary>Shows what is on offer. With <paramref name="canDownload"/> false the button is greyed out.</summary>
    public void SetOffer(string title, string detail, string buttonText, bool canDownload)
    {
        _title = title;
        _detail = detail;
        _canDownload = canDownload;
        _button.Text = buttonText;
        UpdateButton();
        PerformLayout();
        Invalidate();
    }

    /// <summary>True while something else is happening (checking a folder, downloading, installing).</summary>
    public void SetBusy(bool busy)
    {
        _busy = busy;
        UpdateButton();
    }

    /// <summary>Changes the button's label while a download is running.</summary>
    public void SetButtonText(string text)
    {
        _button.Text = text;
        PerformLayout();
    }

    private void UpdateButton()
    {
        _button.Primary = _canDownload;
        _button.Enabled = _canDownload && !_busy;
        _button.Invalidate();
        Invalidate();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int textWidth = TextRenderer.MeasureText(_button.Text, _button.Font).Width;
        int width = Math.Max((int)S(110), textWidth + (int)S(36));
        int height = (int)S(44);
        int pad = (int)S(16);
        _button.Bounds = new Rectangle(Width - width - pad, (Height - height) / 2, width, height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prepare(e);
        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);

        // Blue and lively when there is something to download, calm when there isn't.
        Color fill = _canDownload ? Color.FromArgb(24, 31, 52) : Theme.Card;
        Color border = _canDownload ? Color.FromArgb(60, 82, 160) : Theme.Border;
        Color iconFill = _canDownload ? Color.FromArgb(34, 46, 80) : Color.FromArgb(28, 66, 54);

        using (var path = Theme.RoundedRect(rect, S(12)))
        using (var brush = new SolidBrush(fill))
        using (var pen = new Pen(border))
        {
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }

        float iconSize = S(36);
        float ix = S(16);
        float iy = (Height - iconSize) / 2f;
        using (var brush = new SolidBrush(iconFill))
        {
            g.FillEllipse(brush, ix, iy, iconSize, iconSize);
        }
        float cx = ix + iconSize / 2f;
        float cy = iy + iconSize / 2f;
        if (_canDownload) DrawDownloadGlyph(g, cx, cy, Theme.Accent, S(2f));
        else DrawCheckGlyph(g, cx, cy, Theme.Success, S(2.2f));

        int tx = (int)(ix + iconSize + S(14));
        int tw = Math.Max(0, _button.Left - tx - (int)S(12));
        TextRenderer.DrawText(g, _title, Theme.Semibold,
            new Rectangle(tx, (int)(Height / 2f - S(24)), tw, (int)S(24)), Theme.Text, LeftFlags);
        TextRenderer.DrawText(g, _detail, Theme.Small,
            new Rectangle(tx, (int)(Height / 2f + S(1)), tw, (int)S(22)), Theme.Muted, LeftFlags);
    }
}
