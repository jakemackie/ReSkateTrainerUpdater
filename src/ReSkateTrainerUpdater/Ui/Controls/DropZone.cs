using System.Drawing.Drawing2D;
using ReSkateTrainerUpdater.Ui;

namespace ReSkateTrainerUpdater.Ui.Controls;

internal enum DropState
{
    Empty,    // waiting for a zip
    Working,  // unpacking and checking it
    Ready,    // looks good
    Problem,  // damaged or not a ReSkate Trainer zip
}

/// <summary>Drag-and-drop target for the zip that also acts as a "browse" button.</summary>
internal sealed class DropZone : BufferedControl
{
    private const string EmptyTitle = "Drop the ReSkate Trainer .zip here";
    private const string EmptySubtitle = "or click to browse";

    private bool _highlight;
    private DropState _state = DropState.Empty;
    private string _title = EmptyTitle;
    private string _sub = EmptySubtitle;
    private float _angle;
    private readonly System.Windows.Forms.Timer _spin = new() { Interval = 30 };

    public DropZone()
    {
        TabStop = true;
        Cursor = Cursors.Hand;
        _spin.Tick += (_, _) =>
        {
            _angle = (_angle + 14f) % 360f;
            Invalidate();
        };
    }

    /// <summary>True while a file is being dragged over the window.</summary>
    public bool Highlight
    {
        get => _highlight;
        set { _highlight = value; Invalidate(); }
    }

    public void SetState(DropState state, string title, string sub)
    {
        _state = state;
        _title = title;
        _sub = sub;
        if (state == DropState.Working) _spin.Start();
        else _spin.Stop();
        Invalidate();
    }

    public void SetEmpty() => SetState(DropState.Empty, EmptyTitle, EmptySubtitle);

    protected override void Dispose(bool disposing)
    {
        if (disposing) _spin.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prepare(e);
        bool empty = _state == DropState.Empty;
        bool ready = _state == DropState.Ready;
        bool problem = _state == DropState.Problem;
        var rect = new RectangleF(1f, 1f, Width - 3f, Height - 3f);

        Color fill = _highlight ? Color.FromArgb(26, 37, 68)
                   : problem ? Color.FromArgb(38, 26, 30)
                   : Hover ? Theme.CardHover
                   : Theme.Card;
        Color border = (_highlight || (Hover && empty)) ? Theme.Accent
                     : ready ? Color.FromArgb(46, 104, 82)
                     : problem ? Color.FromArgb(140, 64, 70)
                     : empty ? Color.FromArgb(66, 73, 88)
                     : Theme.Border;

        using (var path = Theme.RoundedRect(rect, S(14)))
        using (var brush = new SolidBrush(fill))
        using (var pen = new Pen(border, S(1.5f)) { DashStyle = empty ? DashStyle.Dash : DashStyle.Solid })
        {
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }
        DrawFocusRing(g, rect, S(14));

        float iconSize = S(40);
        float blockHeight = iconSize + S(6) + S(22) + S(18);
        float top = Math.Max(S(6), (Height - blockHeight) / 2f);
        float cx = Width / 2f;
        float cy = top + iconSize / 2f;

        Color tint = ready ? Color.FromArgb(28, 66, 54)
                   : problem ? Color.FromArgb(74, 34, 40)
                   : _highlight ? Color.FromArgb(40, 58, 110)
                   : Color.FromArgb(34, 46, 80);
        using (var brush = new SolidBrush(tint))
        {
            g.FillEllipse(brush, cx - iconSize / 2f, top, iconSize, iconSize);
        }

        switch (_state)
        {
            case DropState.Ready:
                DrawCheckGlyph(g, cx, cy, Theme.Success, S(2.2f));
                break;
            case DropState.Problem:
                DrawWarningGlyph(g, cx, cy, Theme.Danger, S(2.4f));
                break;
            case DropState.Working:
                using (var pen = new Pen(Theme.Accent, S(2.4f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    float r = S(9);
                    g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, _angle, 270f);
                }
                break;
            default:
                DrawDownloadGlyph(g, cx, cy, Theme.Accent, S(2.2f));
                break;
        }

        int pad = (int)S(16);
        var titleRect = new Rectangle(pad, (int)(top + iconSize + S(6)), Math.Max(0, Width - pad * 2), (int)S(22));
        var subRect = new Rectangle(pad, (int)(top + iconSize + S(28)), Math.Max(0, Width - pad * 2), (int)S(18));

        TextRenderer.DrawText(g, _title, Theme.Semibold, titleRect, Theme.Text, CenterFlags);
        TextRenderer.DrawText(g, _sub, Theme.Small, subRect, Theme.Muted, CenterFlags);
    }
}
