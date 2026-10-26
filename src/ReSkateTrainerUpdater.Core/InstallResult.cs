namespace ReSkateTrainerUpdater.Core;

public enum InstallOutcome
{
    /// <summary>Files were copied.</summary>
    Installed,

    /// <summary>Every file already matched, so nothing was copied.</summary>
    AlreadyUpToDate,

    /// <summary>Something stopped the install before it started. Nothing was changed.</summary>
    Blocked,

    /// <summary>Something failed part-way and the original files were put back.</summary>
    RolledBack,

    /// <summary>Something failed part-way and putting the original files back failed too.</summary>
    RestoreFailed,
}

public enum BlockReason
{
    None,
    FolderNotWritable,
    FilesInUse,
}

/// <summary>The result of <see cref="TrainerInstaller.Install"/>.</summary>
public sealed record InstallResult(
    InstallOutcome Outcome,
    int Added,
    int Replaced,
    int Unchanged,
    IReadOnlyList<string> Problems,
    string? BackupFolder = null,
    string? RescueFolder = null,
    BlockReason BlockedBy = BlockReason.None);
