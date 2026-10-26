namespace ReSkateTrainerUpdater.Core;

/// <summary>
/// A downloaded Trainer zip that has been unpacked to a temporary folder and checked.
/// Created by <see cref="TrainerPackageLoader"/>. Dispose it to delete the temporary folder.
/// </summary>
public sealed class TrainerPackage : IDisposable
{
    private readonly List<string> _files = new();
    private readonly List<string> _errors = new();
    private readonly List<string> _warnings = new();
    private readonly List<string> _notes = new();

    internal TrainerPackage(string zipPath, string tempFolder, string rootFolder, long zipSize)
    {
        ZipPath = zipPath;
        TempFolder = tempFolder;
        RootFolder = rootFolder;
        ZipSize = zipSize;
    }

    public string ZipPath { get; }
    public long ZipSize { get; }

    /// <summary>The folder holding the real files, after unwrapping nested zips and wrapper folders.</summary>
    public string RootFolder { get; }

    /// <summary>Paths of every file in <see cref="RootFolder"/>, relative to it.</summary>
    public IReadOnlyList<string> Files => _files;

    /// <summary>Problems that make the zip impossible to install.</summary>
    public IReadOnlyList<string> Errors => _errors;

    /// <summary>Things worth telling the user about that don't stop an install.</summary>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>What was done to unpack the zip, for the activity log.</summary>
    public IReadOnlyList<string> Notes => _notes;

    /// <summary>The version information of ReSkate.dll in the zip.</summary>
    public ReSkateVersion? Runtime { get; internal set; }

    /// <summary>The version information of ReSkateLauncher.exe in the zip.</summary>
    public ReSkateVersion? Launcher { get; internal set; }

    /// <summary>The version this zip installs. Always set when <see cref="IsValid"/> is true.</summary>
    public ReSkateVersion? Version => Runtime ?? Launcher;

    public bool IsValid => _errors.Count == 0;

    internal string TempFolder { get; }

    internal void AddFile(string relativePath) => _files.Add(relativePath);
    internal void AddError(string message) => _errors.Add(message);
    internal void AddWarning(string message) => _warnings.Add(message);
    internal void AddNote(string message) => _notes.Add(message);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(TempFolder)) Directory.Delete(TempFolder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temp cleanup is best effort; leftovers are removed on the next start-up.
        }
    }
}
