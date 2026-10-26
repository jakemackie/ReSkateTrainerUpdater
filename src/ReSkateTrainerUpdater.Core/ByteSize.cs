namespace ReSkateTrainerUpdater.Core;

/// <summary>Human-readable file sizes, in the user's number format.</summary>
public static class ByteSize
{
    /// <summary>"17.3 MB", "512 KB", "12 B".</summary>
    public static string Format(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{size:0.#} {units[unit]}";
    }
}
