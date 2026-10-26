using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReSkateTrainerUpdater.Core;

/// <summary>One edition's newest published Trainer, as listed in latest.json.</summary>
/// <param name="Edition">The manifest key, for example "tournament". See <see cref="OnlineSource.EditionKey"/>.</param>
/// <param name="Version">The Trainer version, for example 0.9.2.</param>
/// <param name="Build">The Trainer build hash, for example 578f597. Optional.</param>
/// <param name="ReSkateVersion">The ReSkate version the Trainer is built on, for example 2.1.1. Optional.</param>
/// <param name="Url">The Release asset download URL. Always passes <see cref="OnlineSource.IsTrustedDownloadUrl"/>.</param>
/// <param name="SizeBytes">The exact size of the zip in bytes.</param>
/// <param name="Sha256">The SHA-256 of the zip, 64 lower-case hex characters.</param>
public sealed record ManifestEntry(
    string Edition,
    string Version,
    string? Build,
    string? ReSkateVersion,
    string Url,
    long SizeBytes,
    string Sha256)
{
    /// <summary>"Tournament" for the key "tournament".</summary>
    public string EditionName => OnlineSource.EditionDisplayName(Edition);

    /// <summary>"0.9.2 (Tournament)", matching <see cref="Core.ReSkateVersion.TrainerText"/>.</summary>
    public string TrainerText => Edition == OnlineSource.StandardEdition ? Version : $"{Version} ({EditionName})";

    /// <summary>The zip's file name, taken from the URL.</summary>
    public string FileName => Uri.UnescapeDataString(Url[(Url.LastIndexOf('/') + 1)..]);
}

/// <summary>The result of reading latest.json. <see cref="Manifest"/> is null if the file couldn't be used at all.</summary>
public sealed record ManifestReadResult(TrainerManifest? Manifest, IReadOnlyList<string> Problems);

/// <summary>
/// latest.json: the newest published Trainer zip for each edition. The format is described in docs/ONLINE-UPDATES.md.
/// Every entry is checked when it is read. An entry that fails a check is left out, never half-trusted.
/// </summary>
public sealed class TrainerManifest
{
    /// <summary>The format version this app understands. A file with a higher number is ignored.</summary>
    public const int SupportedSchemaVersion = 1;

    /// <summary>The largest zip the app will download, as a sanity limit.</summary>
    public const long MaxZipBytes = 500L * 1024 * 1024;

    /// <summary>The largest manifest the app will read.</summary>
    public const int MaxManifestBytes = 64 * 1024;

    private static readonly Regex VersionPattern = new(@"^\d{1,4}\.\d{1,4}\.\d{1,4}$", RegexOptions.CultureInvariant);
    private static readonly Regex BuildPattern = new(@"^[0-9a-f]{5,40}$", RegexOptions.CultureInvariant);
    private static readonly Regex Sha256Pattern = new(@"^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex EditionPattern = new(@"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant);

    private readonly SortedDictionary<string, ManifestEntry> _editions = new(StringComparer.Ordinal);

    public TrainerManifest(IEnumerable<ManifestEntry>? entries = null)
    {
        foreach (var entry in entries ?? Enumerable.Empty<ManifestEntry>()) _editions[entry.Edition] = entry;
    }

    /// <summary>Every valid entry, by manifest key, sorted.</summary>
    public IReadOnlyDictionary<string, ManifestEntry> Editions => _editions;

    /// <summary>The entry for a manifest key such as "tournament", or null.</summary>
    public ManifestEntry? Find(string editionKey) =>
        _editions.TryGetValue(editionKey, out var entry) ? entry : null;

    /// <summary>A copy of this manifest with one edition added or replaced. Throws if the entry fails a check.</summary>
    public TrainerManifest With(ManifestEntry entry)
    {
        var problems = CheckEntry(entry);
        if (problems.Count > 0) throw new ArgumentException(string.Join(" ", problems), nameof(entry));

        var copy = new TrainerManifest(_editions.Values);
        copy._editions[entry.Edition] = entry;
        return copy;
    }

    // ---------- Reading ----------

    /// <summary>Reads latest.json text. Never throws: problems are returned instead.</summary>
    public static ManifestReadResult Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
        }
        catch (JsonException ex)
        {
            return Fail("latest.json is not valid JSON: " + ex.Message);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Fail("latest.json must be a JSON object.");

            if (!root.TryGetProperty("schemaVersion", out var schema) ||
                schema.ValueKind != JsonValueKind.Number ||
                !schema.TryGetInt32(out int schemaVersion))
            {
                return Fail("latest.json has no whole-number \"schemaVersion\".");
            }

            if (schemaVersion > SupportedSchemaVersion)
            {
                return Fail($"latest.json uses format {schemaVersion}, but this app only understands format " +
                            $"{SupportedSchemaVersion}. A newer version of the app is needed.");
            }
            if (schemaVersion < 1) return Fail($"latest.json has an invalid schemaVersion ({schemaVersion}).");

            if (!root.TryGetProperty("editions", out var editions) || editions.ValueKind != JsonValueKind.Object)
                return Fail("latest.json has no \"editions\" object.");

            var problems = new List<string>();
            var entries = new List<ManifestEntry>();
            foreach (var property in editions.EnumerateObject())
            {
                var entry = ReadEntry(property, problems);
                if (entry is null) continue;

                var entryProblems = CheckEntry(entry);
                if (entryProblems.Count > 0)
                {
                    problems.Add($"Edition \"{property.Name}\" was ignored: " + string.Join(" ", entryProblems));
                    continue;
                }
                if (entries.Any(e => e.Edition == entry.Edition))
                {
                    problems.Add($"Edition \"{property.Name}\" is listed twice, so only the first one is used.");
                    continue;
                }
                entries.Add(entry);
            }

            return new ManifestReadResult(new TrainerManifest(entries), problems);
        }

        static ManifestReadResult Fail(string problem) => new(null, new[] { problem });
    }

    private static ManifestEntry? ReadEntry(JsonProperty property, List<string> problems)
    {
        var value = property.Value;
        if (value.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"Edition \"{property.Name}\" was ignored: it must be an object.");
            return null;
        }

        string? Text(string name) =>
            value.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

        long size = value.TryGetProperty("sizeBytes", out var s) &&
                    s.ValueKind == JsonValueKind.Number &&
                    s.TryGetInt64(out long n) ? n : -1;

        return new ManifestEntry(
            Edition: property.Name,
            Version: Text("version") ?? "",
            Build: Text("build"),
            ReSkateVersion: Text("reskateVersion"),
            Url: Text("url") ?? "",
            SizeBytes: size,
            Sha256: (Text("sha256") ?? "").ToLowerInvariant());
    }

    /// <summary>Every reason an entry can't be trusted. Empty means it is fine.</summary>
    public static IReadOnlyList<string> CheckEntry(ManifestEntry entry)
    {
        var problems = new List<string>();

        if (!EditionPattern.IsMatch(entry.Edition))
            problems.Add($"The edition key \"{entry.Edition}\" must be lower-case letters, digits and dashes, for example \"tournament\".");
        if (!VersionPattern.IsMatch(entry.Version))
            problems.Add($"\"version\" must look like 0.9.2 (it is \"{entry.Version}\").");
        if (entry.Build is not null && !BuildPattern.IsMatch(entry.Build))
            problems.Add($"\"build\" must be 5 to 40 lower-case hex characters (it is \"{entry.Build}\").");
        if (entry.ReSkateVersion is not null && !VersionPattern.IsMatch(entry.ReSkateVersion))
            problems.Add($"\"reskateVersion\" must look like 2.1.1 (it is \"{entry.ReSkateVersion}\").");
        if (!OnlineSource.IsTrustedDownloadUrl(entry.Url))
            problems.Add($"\"url\" must be a .zip Release asset of github.com/{OnlineSource.Owner}/{OnlineSource.Repository} (it is \"{entry.Url}\").");
        if (entry.SizeBytes <= 0 || entry.SizeBytes > MaxZipBytes)
            problems.Add($"\"sizeBytes\" must be a positive whole number no larger than {MaxZipBytes} (it is {entry.SizeBytes}).");
        if (!Sha256Pattern.IsMatch(entry.Sha256))
            problems.Add("\"sha256\" must be exactly 64 lower-case hex characters.");

        return problems;
    }

    // ---------- Writing ----------

    /// <summary>
    /// The manifest as latest.json text: two-space indents, editions sorted by key, fields in a fixed order,
    /// LF line endings and a final newline, so a diff only ever shows real changes.
    /// </summary>
    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SupportedSchemaVersion);
            writer.WriteStartObject("editions");
            foreach (var entry in _editions.Values)
            {
                writer.WriteStartObject(entry.Edition);
                writer.WriteString("version", entry.Version);
                if (entry.Build is not null) writer.WriteString("build", entry.Build);
                if (entry.ReSkateVersion is not null) writer.WriteString("reskateVersion", entry.ReSkateVersion);
                writer.WriteString("url", entry.Url);
                writer.WriteNumber("sizeBytes", entry.SizeBytes);
                writer.WriteString("sha256", entry.Sha256);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n") + "\n";
    }
}
