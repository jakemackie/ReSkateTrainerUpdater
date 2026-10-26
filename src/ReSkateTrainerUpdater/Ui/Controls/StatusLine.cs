using ReSkateTrainerUpdater.Core;
using ReSkateTrainerUpdater.Ui;

namespace ReSkateTrainerUpdater.Ui.Controls;

/// <summary>
/// The status message under the Install button: a coloured pill naming how things stand ("All good", "Heads up",
/// "Problem", ...) with the message beside it in normal text, wrapping onto a second line if needed.
/// </summary>
internal sealed class StatusLine : BufferedControl
{
    private string _message = "";
    private string _pill = "";
    private Color _color = Theme.Muted;
    private bool _muted = true;

    public void Show(Advice advice)
    {
        _message = advice.Message;
        _pill = UpdateAdvisor.PillLabel(advice.Severity);
        _color = Theme.ForSeverity(advice.Severity);
        _muted = advice.Severity == Severity.Neutral;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prepare(e);
        if (_message.Length == 0) return;

        var pill = DrawPill(g, (int)S(2), (int)S(2), _pill, _color, Math.Max(0, Width - (int)S(4)));

        int textX = pill.Right + (int)S(10);
        var textRect = new Rectangle(textX, 0, Math.Max(0, Width - textX), Height);
        TextRenderer.DrawText(g, _message, Theme.Body, textRect, _muted ? Theme.Muted : Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPadding);
    }
}
