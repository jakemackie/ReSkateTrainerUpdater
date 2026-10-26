namespace ReSkateTrainerUpdater.Core;

/// <summary>What is in the folder the user wants to update.</summary>
/// <param name="Folder">The folder that was inspected.</param>
/// <param name="Exists">False if the folder is gone.</param>
/// <param name="IsEmpty">True for an empty folder, which is a valid place for a fresh install.</param>
/// <param name="HasLauncher">ReSkateLauncher.exe is in the folder.</param>
/// <param name="HasRuntime">ReSkate.dll is in the folder.</param>
/// <param name="Installed">The version of the installed ReSkate.dll, if it could be read.</param>
/// <param name="IsSteamSkateFolder">The folder is Steam's own "skate." game folder, which ReSkate can't run from.</param>
/// <param name="HasSpecialCharacters">The path has non-ASCII characters, which the launcher may not cope with.</param>
public sealed record InstallFolderInfo(
    string Folder,
    bool Exists,
    bool IsEmpty,
    bool HasLauncher,
    bool HasRuntime,
    ReSkateVersion? Installed,
    bool IsSteamSkateFolder,
    bool HasSpecialCharacters);
