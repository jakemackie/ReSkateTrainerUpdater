using ReSkateTrainerUpdater.Core;
using ReSkateTrainerUpdater.Ui;

namespace ReSkateTrainerUpdater.Ui.Controls;

/// <summary>
/// The big STATUS card, laid out like the one in the ReSkate launcher: a coloured bar down the left edge, the title and
/// a pill at the top, a headline with an icon, a smaller line, and a few facts below a divider.
/// It draws a <see cref="HomeView"/> and decides nothing itself.
/// </summary>
internal sealed class StatusCard : BufferedControl
{
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

        float pad = S(30);
        float right = Width - pad;

        // Title and pill.
        TextPaint.Draw(g, "STATUS", Theme.Title, new RectangleF(pad, S(22), right - pad, S(30)), Theme.Text);
        string pillText = view.Pill;
        int pillWidth = PillWidth(g, pillText);
        var pillColour = view.Severity == Severity.Neutral ? Color.FromArgb(70, 72, 80) : Theme.ForSeverity(view.Severity);
        DrawPill(g, (int)(right - pillWidth), (int)S(25), pillText, pillColour);

        // Headline.
        float y = S(86);
        float iconRadius = S(16);
        DrawStatusIcon(g, pad + iconRadius, y + S(17), iconRadius, view.Severity, _spin);
        float textX = pad + iconRadius * 2 + S(14);
        float textWidth = Math.Max(0, right - textX);
        TextPaint.Draw(g, view.Headline, Theme.Heading, new RectangleF(textX, y, textWidth, S(34)), Theme.Text);

        // The line under it, which may wrap.
        float subTop = y + S(40);
        float subHeight = 0;
        if (view.Subline.Length > 0)
        {
            var measured = TextPaint.Measure(g, view.Subline, Theme.Body, textWidth, wrap: true);
            subHeight = Math.Min(measured.Height, S(86));
            TextPaint.Draw(g, view.Subline, Theme.Body, new RectangleF(textX, subTop, textWidth, subHeight + S(2)), Theme.Muted,
                HAlign.Left, VAlign.Top, wrap: true);
        }

        // Facts.
        if (view.Rows.Count == 0) return;
        float dividerY = subTop + subHeight + S(26);
        using (var pen = new Pen(Theme.Divider, 1f))
        {
            g.DrawLine(pen, pad, dividerY, right, dividerY);
        }

        float rowY = dividerY + S(16);
        float rowHeight = S(36);
        foreach (var row in view.Rows)
        {
            if (rowY + rowHeight > Height - S(8)) break;
            float rowRadius = S(9);
            DrawStatusIcon(g, pad + rowRadius, rowY + rowHeight / 2f, rowRadius, row.Severity, _spin);
            TextPaint.Draw(g, row.Text, Theme.Body, new RectangleF(pad + rowRadius * 2 + S(12), rowY, right - pad - rowRadius * 2 - S(12), rowHeight),
                Theme.TextSoft);
            rowY += rowHeight;
        }
    }
}
