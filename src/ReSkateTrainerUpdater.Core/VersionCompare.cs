namespace ReSkateTrainerUpdater.Core;

public static class VersionCompare
{
    /// <summary>Negative if <paramref name="a"/> is older than <paramref name="b"/>, zero if equal, positive if newer.</summary>
    public static int Compare(string? a, string? b)
    {
        if (Version.TryParse(a, out var versionA) && Version.TryParse(b, out var versionB))
            return versionA.CompareTo(versionB);

        return string.CompareOrdinal(a, b);
    }
}
