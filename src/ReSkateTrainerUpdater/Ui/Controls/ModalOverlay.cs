using ReSkateTrainerUpdater.Core;
using ReSkateTrainerUpdater.Ui;

namespace ReSkateTrainerUpdater.Ui.Controls;

/// <summary>
/// A dialog over the whole window, like the ReSkate launcher's: the window dims and a dark card in the middle gives a bold
/// title, a few lines of text and one or two square buttons. It appears at once, with no fade or slide. It covers the
/// window, so the mouse can't reach what is under it, and it takes the keyboard too: Enter presses the main button and
/// Escape the other. It draws a <see cref="ModalView"/> and decides nothing.
/// </summary>
internal sealed class ModalOverlay : BufferedControl
{
    // Measurements in 96-dpi pixels, from the launcher's dialogs.
    private const float CardWidth = 440f;
    private const float Pad = 28f;
    private const float ParagraphGap = 14f;
    private const float ButtonHeight = 40f;
    private const float ButtonGap = 10f;
    private const float MinCardHeight = 220f;

    private enum Button { None = -1, Cancel = 0, Confirm = 1 }

    private ModalView? _view;
    private Bitmap? _snapshot;
    private RectangleF _card, _titleRect, _cancelRect, _confirmRect;
    private readonly List<RectangleF> _paragraphRects = new();
    private Button _hot = Button.None;
    private Button _down = Button.None;

    /// <summary>The main button was pressed (or Enter).</summary>
    public event EventHandler? Confirmed;

    /// <summary>The other button was pressed (or Escape).</summary>
    public event EventHandler? Dismissed;

    public ModalOverlay()
    {
        TabStop = true;
        Visible = false;
    }

    /// <summary>Shows the dialog over <paramref name="snapshot"/>, a picture of the window as it was (this control takes it over).</summary>
    public void Open(ModalView view, Bitmap snapshot)
    {
        _view = view;
        _snapshot?.Dispose();
        _snapshot = snapshot;
        _hot = _down = Button.None;
        Describe($"{view.Title}. {string.Join(" ", view.Paragraphs)} Press Enter for {view.ConfirmText}, or Escape for {view.CancelText}.",
            AccessibleRole.Dialog);
        MeasureLayout();
        Visible = true;
        BringToFront();
        Focus();
        Invalidate();
    }

    public void Close()
    {
        Visible = false;
        _view = null;
        _snapshot?.Dispose();
        _snapshot = null;
    }

    public bool IsOpen => Visible && _view is not null;

    /// <summary>
    /// An area (in this control's coordinates) where the mouse goes straight through to what is underneath, so the window's
    /// minimise and close buttons keep working while a dialog is open.
    /// </summary>
    public Rectangle PassThrough { get; set; }

    protected override void WndProc(ref Message m)
    {
        const int WmNcHitTest = 0x0084;
        const int HtTransparent = -1;
        if (m.Msg == WmNcHitTest && !PassThrough.IsEmpty)
        {
            long lParam = m.LParam.ToInt64();
            var screen = new Point((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF));
            if (PassThrough.Contains(PointToClient(screen)))
            {
                m.Result = (IntPtr)HtTransparent;
                return;
            }
        }
        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _snapshot?.Dispose();
        base.Dispose(disposing);
    }

    // Works out where everything goes, once, when the dialog opens.
    private void MeasureLayout()
    {
        if (_view is not { } view) return;
        using var g = Graphics.FromHwnd(IntPtr.Zero);

        float cardWidth = S(CardWidth);
        float pad = S(Pad);
        float textWidth = cardWidth - pad * 2;

        float y = pad;
        float titleHeight = TextPaint.Measure(g, view.Title, Theme.Title, textWidth, wrap: true).Height;
        var titleRect = new RectangleF(pad, y, textWidth, titleHeight);
        y += titleHeight + S(14);

        var paragraphs = new List<RectangleF>();
        foreach (var text in view.Paragraphs)
        {
            float height = TextPaint.Measure(g, text, Theme.Body, textWidth, wrap: true).Height + S(3);
            paragraphs.Add(new RectangleF(pad, y, textWidth, height));
            y += height + S(ParagraphGap);
        }

        float cancelWidth = Math.Max(S(110), TextPaint.Measure(g, view.CancelText, Theme.ChipTitle).Width + S(44));
        float confirmWidth = Math.Max(S(110), TextPaint.Measure(g, view.ConfirmText, Theme.ChipTitle).Width + S(44));
        float cardHeight = Math.Max(S(MinCardHeight), y + S(10) + S(ButtonHeight) + pad);

        // Centred in the window. The positions above are inside the card, so move them to the window.
        float left = (Width - cardWidth) / 2f;
        float top = (Height - cardHeight) / 2f;
        _card = new RectangleF(left, top, cardWidth, cardHeight);
        _titleRect = Offset(titleRect, left, top);
        _paragraphRects.Clear();
        foreach (var rect in paragraphs) _paragraphRects.Add(Offset(rect, left, top));

        float buttonTop = top + cardHeight - pad - S(ButtonHeight);
        float right = left + cardWidth - pad;
        _confirmRect = new RectangleF(right - confirmWidth, buttonTop, confirmWidth, S(ButtonHeight));
        _cancelRect = new RectangleF(_confirmRect.Left - S(ButtonGap) - cancelWidth, buttonTop, cancelWidth, S(ButtonHeight));
    }

    private static RectangleF Offset(RectangleF r, float dx, float dy) => new(r.X + dx, r.Y + dy, r.Width, r.Height);

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (_view is not null) MeasureLayout();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prepare(e);
        if (_view is not { } view) return;

        // The window as it was, dimmed.
        if (_snapshot is not null) g.DrawImageUnscaled(_snapshot, 0, 0);
        using (var dim = new SolidBrush(Color.FromArgb(170, 0, 0, 0)))
        {
            g.FillRectangle(dim, 0, 0, Width, Height);
        }

        using (var brush = new SolidBrush(Theme.CardSolid))
        {
            g.FillRectangle(brush, _card);
        }

        TextPaint.Draw(g, view.Title, Theme.Title, _titleRect, Theme.Text, HAlign.Left, VAlign.Top, wrap: true);
        for (int i = 0; i < view.Paragraphs.Count && i < _paragraphRects.Count; i++)
            TextPaint.Draw(g, view.Paragraphs[i], Theme.Body, _paragraphRects[i], Theme.TextSoft, HAlign.Left, VAlign.Top, wrap: true);

        DrawButton(g, _cancelRect, view.CancelText, Button.Cancel, accent: false);
        DrawButton(g, _confirmRect, view.ConfirmText, Button.Confirm, accent: true);
    }

    private void DrawButton(Graphics g, RectangleF rect, string text, Button which, bool accent)
    {
        bool hot = _hot == which;
        bool down = _down == which && hot;
        Color fill = accent
            ? (down ? Theme.AccentPressed : hot ? Theme.AccentHover : Theme.Accent)
            : (down ? Theme.Control : hot ? Theme.ControlHover : Theme.Control);
        using (var brush = new SolidBrush(fill))
        {
            g.FillRectangle(brush, rect);
        }
        TextPaint.Draw(g, text, Theme.ChipTitle, rect, accent ? Theme.Ink : Theme.Text, HAlign.Center, VAlign.Middle);
    }

    private Button ButtonAt(Point p) =>
        _confirmRect.Contains(p) ? Button.Confirm : _cancelRect.Contains(p) ? Button.Cancel : Button.None;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hot = ButtonAt(e.Location);
        if (hot != _hot)
        {
            _hot = hot;
            Cursor = hot == Button.None ? Cursors.Default : Cursors.Hand;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hot = Button.None;
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _hot = ButtonAt(e.Location);
            _down = _hot;
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        var released = e.Button == MouseButtons.Left ? ButtonAt(e.Location) : Button.None;
        bool pressed = released != Button.None && released == _down;
        _down = Button.None;
        base.OnMouseUp(e);

        if (!pressed) return;
        if (released == Button.Confirm) Confirmed?.Invoke(this, EventArgs.Empty);
        else Dismissed?.Invoke(this, EventArgs.Empty);
    }

    // The dialog owns the keyboard while it is open, so Tab can't wander to what is behind it.
    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Tab or Keys.Enter or Keys.Escape || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Escape or Keys.Tab) e.Handled = true;
        base.OnKeyDown(e);
    }

    // Acting on the key going up, not down: the dialog closes, focus moves on, and the key-up must not land on another button.
    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter) { e.Handled = true; Confirmed?.Invoke(this, EventArgs.Empty); return; }
        if (e.KeyCode == Keys.Escape) { e.Handled = true; Dismissed?.Invoke(this, EventArgs.Empty); return; }
        base.OnKeyUp(e);
    }
}
