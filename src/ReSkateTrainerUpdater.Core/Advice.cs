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
}

/// <summary>What to tell the user, and whether the Install button should be enabled.</summary>
public sealed record Advice(string Message, Severity Severity, bool CanInstall = false);

/// <summary>Everything <see cref="UpdateAdvisor"/> needs to know about the current state of the window.</summary>
/// <param name="CheckingZip">The zip is still being unpacked and checked.</param>
/// <param name="ZipPath">The zip the user chose, if any.</param>
/// <param name="ZipProblem">Why the zip couldn't be read at all (damaged, unreadable).</param>
/// <param name="Package">The checked zip, if it could be read.</param>
/// <param name="Folder">The ReSkate folder the user chose, if any.</param>
/// <param name="CheckingFolder">The folder is still being inspected.</param>
/// <param name="FolderInfo">What was found in the folder, once inspected.</param>
/// <param name="Offer">What the online check offers for this folder, if it has run.</param>
public sealed record UpdateContext(
    bool CheckingZip,
    string? ZipPath,
    string? ZipProblem,
    TrainerPackage? Package,
    string? Folder,
    bool CheckingFolder,
    InstallFolderInfo? FolderInfo,
    OnlineOffer? Offer = null);

/// <summary>What the online check can do for the chosen folder.</summary>
public enum OfferKind
{
    /// <summary>Nothing to show: no manifest, no folder, an edition the manifest doesn't list, or plain ReSkate.</summary>
    None,

    /// <summary>The folder has no ReSkate.dll: offer to download and install the fresh-install edition.</summary>
    FreshInstall,

    /// <summary>The installed edition has a newer version in latest.json.</summary>
    Update,

    /// <summary>The installed edition is already the newest in latest.json, or newer. No banner is shown.</summary>
    UpToDate,
}

/// <summary>The banner text and button for an online install or update. Built by <see cref="UpdateAdvisor.DescribeOnlineOffer"/>.</summary>
/// <param name="Kind">What is on offer.</param>
/// <param name="Entry">The manifest entry that would be downloaded. Set for FreshInstall, Update and UpToDate.</param>
/// <param name="Title">The banner's main line.</param>
/// <param name="Detail">The banner's second line.</param>
/// <param name="ButtonText">The banner button's label.</param>
public sealed record OnlineOffer(OfferKind Kind, ManifestEntry? Entry, string Title, string Detail, string ButtonText)
{
    public static OnlineOffer Nothing { get; } = new(OfferKind.None, null, "", "", "");

    /// <summary>True when the banner should be shown with a button.</summary>
    public bool CanDownload => Kind is OfferKind.FreshInstall or OfferKind.Update && Entry is not null;
}
