using System.Text.Json;

namespace ReSkateTrainerUpdater.Core;

/// <summary>Remembers the last ReSkate folder and the backup switch between runs.</summary>
public sealed class UpdaterSettings
{
    private string _path = DefaultPath;

    public string? LastFolder { get; set; }
    public bool BackupReplacedFiles { get; set; } = true;

    /// <summary>Where the window's top-left corner was when it was last closed. Null the first time.</summary>
    public int? WindowLeft { get; set; }
    public int? WindowTop { get; set; }

    /// <summary>%AppData%\ReSkateTrainerUpdater\settings.json</summary>
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ReSkateTrainerUpdater",
        "settings.json");

    /// <summary>Loads the settings, or defaults if the file is missing or unreadable. Never throws.</summary>
    public static UpdaterSettings Load(string? path = null)
    {
        path ??= DefaultPath;

        UpdaterSettings settings;
        try
        {
            settings = File.Exists(path)
                ? JsonSerializer.Deserialize<UpdaterSettings>(File.ReadAllText(path)) ?? new UpdaterSettings()
                : new UpdaterSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            settings = new UpdaterSettings();
        }

        settings._path = path;
        return settings;
    }

    /// <summary>Saves the settings. Failing to save is not worth bothering the user about.</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience only.
        }
    }
}
