using ReSkateTrainerUpdater.Ui;

namespace ReSkateTrainerUpdater.Ui.Controls;

/// <summary>Flat rounded button. Set Primary for the accent-coloured call-to-action style.</summary>
internal sealed class RoundedButton : BufferedControl
{
    public bool Primary { get; set; }

    public RoundedButton()
    {
        TabStop = true;
        Cursor = Cursors.Hand;
        Font = Theme.ButtonFont;
        Size = new Size(110, 44);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prepare(e);
        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);

        Color fill, border, text;
        if (!Enabled)
        {
            fill = Primary ? Color.FromArgb(38, 44, 60) : Theme.Card;
            border = Primary ? fill : Theme.Border;
            text = Color.FromArgb(98, 106, 122);
        }
        else if (Primary)
        {
            fill = Pressed ? Theme.AccentPressed : Hover ? Theme.AccentHover : Theme.Accent;
            border = fill;
            text = Color.White;
        }
        else
        {
            fill = Pressed ? Theme.Border : Hover ? Theme.CardHover : Theme.Card;
            border = Hover ? Color.FromArgb(70, 77, 92) : Theme.Border;
            text = Theme.Text;
        }

        using (var path = Theme.RoundedRect(rect, S(11)))
        using (var brush = new SolidBrush(fill))
        using (var pen = new Pen(border))
        {
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }
        DrawFocusRing(g, rect, S(11));

        TextRenderer.DrawText(g, Text, Font, ClientRectangle, text, CenterFlags);
    }
}
