namespace ReSkateTrainerUpdater.Core;

/// <summary>How serious or how good a message is. The UI turns this into a colour.</summary>
public enum Severity
{
    /// <summary>Plain information or an instruction.</summary>
    Neutral,

    /// <summary>Something is being checked or done right now.</summary>
    Working,

    /// <summary>Everything is fine.</summary>
    Good,

    /// <summary>Worth knowing, but it doesn't stop the install.</summary>
    Warning,

    /// <summary>It can't go ahead like this.</summary>
    Error,

    /// <summary>Something new is on offer, for example an update. Shown in the accent colour.</summary>
    Highlight,
}

/// <summary>What the big tile should offer once an outcome has been shown, instead of its usual job.</summary>
public enum FollowUp
{
    /// <summary>Nothing special: the tile stays as it was.</summary>
    None,

    /// <summary>The Trainer is in place: the tile starts the game's launcher.</summary>
    Launch,

    /// <summary>The game is in the way: the tile closes it (after asking) and tries the install again.</summary>
    CloseGameAndRetry,
}

/// <summary>What to tell the user, and whether the Install button should be enabled.</summary>
/// <param name="Message">The text.</param>
/// <param name="Severity">How it is coloured.</param>
/// <param name="CanInstall">Whether installing is allowed right now.</param>
/// <param name="Next">What the big tile should offer next.</param>
/// <param name="Links">Small buttons to show with the message, for example "Open backup folder".</param>
public sealed record Advice(
    string Message,
    Severity Severity,
    bool CanInstall = false,
    FollowUp Next = FollowUp.None,
    IReadOnlyList<CardLink>? Links = null);

/// <summary>Everything <see cref="UpdateAdvisor"/> needs to know about the current state of the window.</summary>
/// <param name="CheckingZip">The zip is still being unpacked and checked.</param>
/// <param name="ZipPath">The zip the user chose, if any.</param>
/// <param name="ZipProblem">Why the zip couldn't be read at all (damaged, unreadable).</param>
/// <param name="Package">The checked zip, if it could be read.</param>
/// <param name="Folder">The ReSkate folder the user chose, if any.</param>
/// <param name="CheckingFolder">The folder is still being inspected.</param>
/// <param name="FolderInfo">What was found in the folder, once inspected.</param>
/// <param name="Offer">What the online check offers for this folder, if it has run.</param>
/// <param name="Online">Whether the check of latest.json has finished and worked.</param>
/// <param name="App">A newer version of this app, if one is published.</param>
/// <param name="SuggestedFolder">A folder that looks like a ReSkate install, found on this PC while no folder is chosen.</param>
/// <param name="Now">The current time. Only tests set it.</param>
public sealed record UpdateContext(
    bool CheckingZip,
    string? ZipPath,
    string? ZipProblem,
    TrainerPackage? Package,
    string? Folder,
    bool CheckingFolder,
    InstallFolderInfo? FolderInfo,
    OnlineOffer? Offer = null,
    OnlineStatus Online = OnlineStatus.Checking,
    AppOffer? App = null,
    string? SuggestedFolder = null,
    DateTimeOffset? Now = null);

/// <summary>A newer version of this app is published. Built by <see cref="UpdateAdvisor.DescribeAppOffer"/>.</summary>
/// <param name="Entry">The published version, with its download and hash.</param>
/// <param name="CurrentVersion">The version that is running, for example 1.1.3.</param>
public sealed record AppOffer(AppEntry Entry, string CurrentVersion);

/// <summary>What the online check can do for the chosen folder.</summary>
public enum OfferKind
{
    /// <summary>Nothing to show: no manifest, no folder, an unreadable ReSkate.dll, or nothing published to offer.</summary>
    None,

    /// <summary>The folder has no ReSkate.dll: offer to download and install the fresh-install edition.</summary>
    FreshInstall,

    /// <summary>The installed edition has a newer version in latest.json.</summary>
    Update,

    /// <summary>The installed edition is already the newest in latest.json, or newer. The banner is shown with its button greyed out.</summary>
    UpToDate,

    /// <summary>The folder has plain ReSkate with no Trainer: offer to add the fresh-install edition.</summary>
    AddTrainer,

    /// <summary>The installed Trainer is an edition latest.json doesn't list (such as the standard one), and the fresh-install edition is published: offer it as a deliberate switch.</summary>
    SwitchEdition,
}

/// <summary>What the online check offers for the chosen folder: what kind of offer, and which published Trainer it is.</summary>
/// <param name="Kind">What is on offer.</param>
/// <param name="Entry">The manifest entry that would be downloaded. Set for FreshInstall, Update and UpToDate.</param>
public sealed record OnlineOffer(OfferKind Kind, ManifestEntry? Entry)
{
    public static OnlineOffer Nothing { get; } = new(OfferKind.None, null);

    /// <summary>True when there is a Trainer to download and install.</summary>
    public bool CanDownload =>
        Kind is OfferKind.FreshInstall or OfferKind.Update or OfferKind.AddTrainer or OfferKind.SwitchEdition && Entry is not null;
}
