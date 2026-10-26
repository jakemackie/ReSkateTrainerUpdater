namespace ReSkateTrainerUpdater.Core;

/// <summary>
/// The one place that says where the app looks for Trainer updates online.
/// The app trusts exactly one GitHub repository: it reads latest.json from that repository's main branch and only
/// downloads zips that are Release assets of that same repository. See docs/ONLINE-UPDATES.md.
/// </summary>
public static class OnlineSource
{
    /// <summary>The GitHub account that owns the repository.</summary>
    public const string Owner = "jakemackie";

    /// <summary>The GitHub repository that hosts latest.json and the Trainer zips.</summary>
    public const string Repository = "ReSkateTrainerUpdater";

    /// <summary>The branch latest.json is read from.</summary>
    public const string Branch = "main";

    /// <summary>The path of the manifest inside the repository.</summary>
    public const string ManifestFileName = "latest.json";

    /// <summary>The edition offered when the chosen folder has no ReSkate in it yet.</summary>
    public const string FreshInstallEdition = "tournament";

    /// <summary>
    /// Where latest.json is downloaded from. raw.githubusercontent.com is not the GitHub API, so the API's
    /// 60-requests-per-hour limit does not apply, but GitHub caches it for up to about five minutes.
    /// </summary>
    public static Uri ManifestUrl { get; } =
        new($"https://raw.githubusercontent.com/{Owner}/{Repository}/{Branch}/{ManifestFileName}");

    /// <summary>Every accepted download URL starts with this path on https://github.com.</summary>
    public static string ReleaseDownloadPathPrefix { get; } = $"/{Owner}/{Repository}/releases/download/";

    /// <summary>The download URL for a Release asset with this tag and file name.</summary>
    public static string ReleaseAssetUrl(string tag, string fileName) =>
        $"https://github.com{ReleaseDownloadPathPrefix}{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(fileName)}";

    /// <summary>
    /// True only for https://github.com/{Owner}/{Repository}/releases/download/{tag}/{file}.zip.
    /// Anything else, including other repositories, other hosts, plain http, credentials, ports, queries and
    /// paths that climb out with "..", is refused.
    /// </summary>
    public static bool IsTrustedDownloadUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (url.Contains("..", StringComparison.Ordinal) || url.Contains('\\')) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;

        if (uri.Scheme != Uri.UriSchemeHttps) return false;
        if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)) return false;
        if (!uri.IsDefaultPort || uri.UserInfo.Length > 0) return false;
        if (uri.Query.Length > 0 || uri.Fragment.Length > 0) return false;

        var path = uri.AbsolutePath;
        if (!path.StartsWith(ReleaseDownloadPathPrefix, StringComparison.OrdinalIgnoreCase)) return false;

        // Exactly "<tag>/<file>.zip" after the prefix.
        var rest = path[ReleaseDownloadPathPrefix.Length..].Split('/');
        return rest.Length == 2
            && rest[0].Length > 0
            && rest[1].Length > ".zip".Length
            && rest[1].EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The manifest key for a Trainer whose binary names no edition.</summary>
    public const string StandardEdition = "standard";

    /// <summary>The tag of an app release, for example v1.2.0. Tags starting with v are app releases only.</summary>
    public static string AppReleaseTag(string version) => "v" + version;

    /// <summary>The file name of an app release zip, for example ReSkateTrainerUpdater-1.2.0.zip.</summary>
    public static string AppZipFileName(string version) => $"ReSkateTrainerUpdater-{version}.zip";

    /// <summary>The download URL of an app release zip.</summary>
    public static string AppAssetUrl(string version) => ReleaseAssetUrl(AppReleaseTag(version), AppZipFileName(version));

    /// <summary>
    /// True only for this app's own release asset of that version: the trusted download URL, with tag
    /// v{version} and file ReSkateTrainerUpdater-{version}.zip. A self-update installs code, so it is held to a
    /// stricter rule than a Trainer zip, which may use any tag.
    /// </summary>
    public static bool IsTrustedAppUrl(string? url, string? version)
    {
        if (string.IsNullOrEmpty(version) || !IsTrustedDownloadUrl(url)) return false;
        var rest = new Uri(url!).AbsolutePath[ReleaseDownloadPathPrefix.Length..].Split('/');
        return rest.Length == 2
            && string.Equals(Uri.UnescapeDataString(rest[0]), AppReleaseTag(version), StringComparison.Ordinal)
            && string.Equals(Uri.UnescapeDataString(rest[1]), AppZipFileName(version), StringComparison.Ordinal);
    }

    /// <summary>
    /// The manifest key for an edition: lower case, spaces as dashes. The standard edition (no edition name in the
    /// binary) is "standard". "Tournament" becomes "tournament".
    /// </summary>
    public static string EditionKey(string? edition) =>
        string.IsNullOrWhiteSpace(edition) ? StandardEdition : edition.Trim().ToLowerInvariant().Replace(' ', '-');

    /// <summary>The display name for a manifest key: "tournament" becomes "Tournament".</summary>
    public static string EditionDisplayName(string key)
    {
        var words = key.Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..]);
        return string.Join(' ', words);
    }
}
