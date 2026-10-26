namespace ReSkateTrainerUpdater.Ui;

/// <summary>Loads the app icon that is embedded in the exe (the same file used for the exe's own icon).</summary>
internal static class AppIcon
{
    // Must match the LogicalName of the EmbeddedResource in the project file.
    private const string ResourceName = "app.ico";

    /// <summary>The icon drawn at a specific size, or null if it can't be loaded.</summary>
    public static Bitmap? Render(int size)
    {
        try
        {
            using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream(ResourceName);
            if (stream is null) return null;
            using var icon = new Icon(stream, new Size(size, size));
            return icon.ToBitmap();
        }
        catch (ArgumentException) { return null; }
    }

    /// <summary>The full icon for the window's title bar and taskbar, or null if it can't be loaded.</summary>
    public static Icon? Load()
    {
        try
        {
            using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream(ResourceName);
            return stream is null ? null : new Icon(stream);
        }
        catch (ArgumentException) { return null; }
    }
}
