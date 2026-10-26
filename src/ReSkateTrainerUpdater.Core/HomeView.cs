namespace ReSkateTrainerUpdater.Core;

/// <summary>Whether the start-up check of latest.json has finished, and whether it worked.</summary>
public enum OnlineStatus
{
    /// <summary>The check hasn't finished yet.</summary>
    Checking,

    /// <summary>latest.json was read.</summary>
    Reachable,

    /// <summary>GitHub couldn't be reached or the file couldn't be used. Dropping a zip still works.</summary>
    Unavailable,
}

/// <summary>What clicking the big action tile does.</summary>
public enum TileAction
{
    /// <summary>The tile is greyed out.</summary>
    None,

    /// <summary>Download the offered Trainer from GitHub, verify it, and install it.</summary>
    DownloadAndInstall,

    /// <summary>Install the zip the user chose.</summary>
    InstallZip,
}

/// <summary>The look of the big action tile.</summary>
public enum TileStyle
{
    /// <summary>Bright blue: there is something to do.</summary>
    Primary,

    /// <summary>Dark: nothing to do right now.</summary>
    Muted,

    /// <summary>Dark with a green title: everything is done and you're up to date.</summary>
    Success,
}

/// <summary>One line in the status card, with a coloured icon.</summary>
public sealed record StatusRow(string Text, Severity Severity);

/// <summary>The big tile that starts the install, and what it says.</summary>
/// <param name="Title">One or two words in the big marker font, for example UPDATE.</param>
/// <param name="Subtitle">A small line under it, for example "0.9.2 → 0.9.3".</param>
/// <param name="Style">Blue or dark.</param>
/// <param name="Action">What a click does. None means it is greyed out.</param>
/// <param name="Progress">0 to 1 fills the tile like a progress bar. Null means no bar.</param>
/// <param name="Working">Something is running, so the tile shows activity even without a known progress.</param>
public sealed record ActionTile(
    string Title,
    string Subtitle,
    TileStyle Style,
    TileAction Action,
    double? Progress = null,
    bool Working = false)
{
    public bool Enabled => Action != TileAction.None;
}

/// <summary>
/// Everything the main window shows, worked out in one place so it can be tested without a window.
/// Built by <see cref="UpdateAdvisor.DescribeHome"/>.
/// </summary>
/// <param name="Pill">The one or two upper-case words in the pill, for example READY or UPDATE.</param>
/// <param name="Severity">Colours the pill, the card's side bar and the headline icon.</param>
/// <param name="Headline">The big line, for example "Ready to skate".</param>
/// <param name="Subline">The smaller line under it.</param>
/// <param name="Rows">Facts below the divider, each with an icon.</param>
/// <param name="Tile">The big action tile.</param>
public sealed record HomeView(
    string Pill,
    Severity Severity,
    string Headline,
    string Subline,
    IReadOnlyList<StatusRow> Rows,
    ActionTile Tile);

/// <summary>The small chip in the top-left corner, like the launcher's "Welcome" chip: a dot, a title and a line under it.</summary>
/// <param name="Title">For example "Connected to GitHub".</param>
/// <param name="Subtitle">For example "v1.1.2 · Unofficial helper".</param>
/// <param name="Severity">Colours the dot: green when connected, grey when not.</param>
public sealed record ChipView(string Title, string Subtitle, Severity Severity);

/// <summary>What the app is busy doing, for <see cref="UpdateAdvisor.DescribeBusy"/>.</summary>
public enum BusyStage
{
    Downloading,
    Unpacking,
    Installing,
}
