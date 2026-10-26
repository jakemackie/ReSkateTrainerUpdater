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
public sealed record UpdateContext(
    bool CheckingZip,
    string? ZipPath,
    string? ZipProblem,
    TrainerPackage? Package,
    string? Folder,
    bool CheckingFolder,
    InstallFolderInfo? FolderInfo);
