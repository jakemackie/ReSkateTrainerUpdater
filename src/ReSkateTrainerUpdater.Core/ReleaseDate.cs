using System.Globalization;

namespace ReSkateTrainerUpdater.Core;

/// <summary>Turns a published date from latest.json into words such as "3 days ago".</summary>
public static class ReleaseDate
{
    /// <summary>
    /// "today", "yesterday", "3 days ago", "2 weeks ago", "5 months ago" or "over a year ago". Null if
    /// <paramref name="published"/> isn't a yyyy-MM-dd date. A date in the future counts as today.
    /// </summary>
    public static string? Describe(string? published, DateTimeOffset now)
    {
        if (published is null ||
            !DateOnly.TryParseExact(published, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return null;
        }

        int days = DateOnly.FromDateTime(now.UtcDateTime).DayNumber - date.DayNumber;
        return days switch
        {
            <= 0 => "today",
            1 => "yesterday",
            < 14 => $"{days} days ago",
            < 60 => $"{days / 7} weeks ago",
            < 365 => $"{Math.Max(2, days / 30)} months ago",
            _ => "over a year ago",
        };
    }
}
