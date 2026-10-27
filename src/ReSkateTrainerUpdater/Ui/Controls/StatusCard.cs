using ReSkateTrainerUpdater.Core;
using ReSkateTrainerUpdater.Ui;

namespace ReSkateTrainerUpdater.Ui.Controls;

/// <summary>
/// The STATUS card, laid out like the one in the ReSkate launcher (the measurements were taken from it): a coloured bar
/// down the left edge, the title and a small pill at the top, a headline with an icon, a smaller line under it, and a few
/// facts below a divider. It is only as tall as its content needs. It draws a <see cref="HomeView"/> and decides nothing.
/// </summary>
internal sealed class StatusCard : BufferedControl
{
    // Measurements in 96-dpi pixels, from the launcher's card at its default size.
    private const float Pad = 22f;
    private const float TitleCenter = 29f;
    private const float HeadCenter = 75f;
    private const float IconRadius = 9.5f;
    private const float SubTop = 88f;
    private const float RowPitch = 30f;
    private const float MinHeight = 150f;

    private readonly System.Windows.Forms.Timer _spinner = new() { Interval = 30 };
    private HomeView? _view;
    private float _spin;

    public StatusCard()
    {
        _spinner.Tick += (_, _) =>
        {
            _spin = (_spin + 12f) % 360f;
            Invalidate();
        };
    }

    public void Show(HomeView view)
    {
        _view = view;
        if (view.Severity == Severity.Working) _spinner.Start();
        else _spinner.Stop();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _spinner.Dispose();
        base.Dispose(disposing);
    }

    // Where the headline and the lines under it start, from the card's left edge. The icons are centred at IconX.
    private const float TextX = 48f;
    private const float IconX = 29f;

    /// <summary>How tall the card needs to be for this view at this width (in device pixels).</summary>
    public int PreferredHeight(HomeView view, int width)
    {
        using var g = Graphics.FromHwnd(IntPtr.Zero);
        return (int)Math.Ceiling(MeasureLayout(g, view, width).Height);
    }

    private readonly record struct CardLayout(float SubHeight, float DividerY, float FirstRowCenter, float Height);

    private CardLayout MeasureLayout(Graphics g, HomeView view, float width)
    {
        float textWidth = Math.Max(10, width - S(TextX) - S(Pad));
        float subHeight = 0;
        if (view.Subline.Length > 0)
            subHeight = Math.Min(TextPaint.Measure(g, view.Subline, Theme.Body, textWidth, wrap: true).Height, S(70));

        if (view.Rows.Count == 0)
            return new CardLayout(subHeight, 0, 0, Math.Max(S(MinHeight), S(SubTop) + subHeight + S(24)));

        float dividerY = S(SubTop) + Math.Max(subHeight, S(17)) + S(14);
        float firstRow = dividerY + S(17);
        float height = firstRow + (view.Rows.Count - 1) * S(RowPitch) + S(24);
        return new CardLayout(subHeight, dividerY, firstRow, Math.Max(S(MinHeight), height));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prepare(e);
        using (var brush = new SolidBrush(Theme.Card))
        {
            g.FillRectangle(brush, 0, 0, Width, Height);
        }
        if (_view is not { } view) return;

        // The bar down the left edge takes the colour of how things stand.
        using (var brush = new SolidBrush(Theme.ForSeverity(view.Severity)))
        {
            g.FillRectangle(brush, 0, 0, (int)S(4), Height);
        }

        float pad = S(Pad);
        float right = Width - S(20);
        var layout = MeasureLayout(g, view, Width);

        // Title and pill.
        TextPaint.Draw(g, "STATUS", Theme.Title, new RectangleF(pad, S(TitleCenter) - S(16), right - pad, S(32)), Theme.Text);
        string pillText = view.Pill;
        int pillWidth = PillWidth(g, pillText);
        var pillColour = view.Severity == Severity.Neutral ? Color.FromArgb(70, 72, 80) : Theme.ForSeverity(view.Severity);
        DrawPill(g, (int)(right - pillWidth), (int)(S(TitleCenter) - S(10.5f)), pillText, pillColour);

        // Headline.
        float iconR = S(IconRadius);
        DrawStatusIcon(g, S(IconX), S(HeadCenter), iconR, view.Severity, _spin);
        float textX = S(TextX);
        float textWidth = Math.Max(0, right - textX);
        TextPaint.Draw(g, view.Headline, Theme.Heading, new RectangleF(textX, S(HeadCenter) - S(14), textWidth, S(28)), Theme.Text);

        // The line under it, which may wrap.
        if (view.Subline.Length > 0)
        {
            TextPaint.Draw(g, view.Subline, Theme.Body, new RectangleF(textX, S(SubTop), textWidth, layout.SubHeight + S(3)), Theme.Muted,
                HAlign.Left, VAlign.Top, wrap: true);
        }

        // Facts.
        if (view.Rows.Count == 0) return;
        using (var pen = new Pen(Theme.Divider, 1f))
        {
            g.DrawLine(pen, pad, layout.DividerY, right, layout.DividerY);
        }

        float rowY = layout.FirstRowCenter;
        foreach (var row in view.Rows)
        {
            if (rowY + S(14) > Height) break;
            DrawStatusIcon(g, S(IconX), rowY, iconR, row.Severity, _spin);
            TextPaint.Draw(g, row.Text, Theme.Body, new RectangleF(textX, rowY - S(13), textWidth, S(26)), Theme.TextSoft);
            rowY += S(RowPitch);
        }
    }
}
