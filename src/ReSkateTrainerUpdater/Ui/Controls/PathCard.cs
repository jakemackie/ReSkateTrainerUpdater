using ReSkateTrainerUpdater.Ui;

namespace ReSkateTrainerUpdater.Ui.Controls;

/// <summary>Shows the chosen folder (name and full path) and which ReSkate Trainer version is installed in it.</summary>
internal sealed class PathCard : BufferedControl
{
    private string? _folder;
    private string? _installed;
    private Color _installedColor = Theme.Muted;

    public PathCard()
    {
        TabStop = true;
        Cursor = Cursors.Hand;
    }

    public string? FolderPath
    {
        get => _folder;
        set { _folder = value; Invalidate(); }
    }

    /// <summary>The third line: what is installed in the folder. Pass null to hide it.</summary>
    public void SetInstalled(string? text, Color color)
    {
        _installed = text;
        _installedColor = color;
        Invalidate();
    }

    private static string FolderName(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrEmpty(name) ? path : name;
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

        bool has = !string.IsNullOrEmpty(_folder);

        // Little folder icon.
        float gx = S(18);
        float gy = (Height - S(18)) / 2f;
        using (var brush = new SolidBrush(has ? Theme.Accent : Color.FromArgb(98, 106, 122)))
        {
            using (var tab = Theme.RoundedRect(new RectangleF(gx, gy, S(9), S(6)), S(2)))
                g.FillPath(brush, tab);
            using (var body = Theme.RoundedRect(new RectangleF(gx, gy + S(3), S(22), S(15)), S(3)))
                g.FillPath(brush, body);
        }

        int tx = (int)S(54);
        int tw = Math.Max(0, Width - tx - (int)S(14));
        string name = has ? FolderName(_folder!) : "Choose your ReSkate folder";
        string sub = has ? _folder! : "The folder where ReSkateLauncher.exe lives (or a new empty one)";

        bool three = has && _installed != null;
        int nameY = three ? (int)S(8) : (int)S(14);
        int subY = three ? (int)S(28) : (int)S(36);

        TextRenderer.DrawText(g, name, Theme.Semibold,
            new Rectangle(tx, nameY, tw, (int)S(20)), Theme.Text, LeftFlags);
        TextRenderer.DrawText(g, sub, Theme.Small,
            new Rectangle(tx, subY, tw, (int)S(18)), Theme.Muted, LeftPathFlags);

        if (three)
        {
            TextRenderer.DrawText(g, _installed!, Theme.Small,
                new Rectangle(tx, (int)S(46), tw, (int)S(18)), _installedColor, LeftFlags);
        }
    }
}
