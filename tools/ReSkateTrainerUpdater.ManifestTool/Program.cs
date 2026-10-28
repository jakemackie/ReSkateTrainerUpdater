using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ReSkateTrainerUpdater.Core;

// Maintainer tool for latest.json. Usage is printed by running it with no arguments.
// Exit code 0 means success, 1 means a problem was found, 2 means the command line was wrong.

const string Usage = """
    ReSkate Trainer Updater manifest tool

      add <zip> [--tag <tag>] [--notes <text>] [--published <yyyy-MM-dd>] [--manifest <path>]
          Reads the Trainer version, edition and build from the zip itself, hashes it, and writes its entry into
          latest.json. The tag defaults to trainer-<edition>-<version>, for example trainer-tournament-0.9.2.
          Upload the zip to a GitHub Release with exactly that tag BEFORE pushing latest.json.
          --notes is one short line the app shows as "What's new" (at most 90 characters). --published defaults to
          today (UTC) and is shown as "Published 3 days ago".

      set-app <zip> [--notes <text>] [--published <yyyy-MM-dd>] [--manifest <path>]
          Records a new version of the updater app itself in latest.json, so installed copies can update themselves.
          The zip must be named ReSkateTrainerUpdater-<version>.zip and hold ReSkateTrainerUpdater.exe. It is checked
          the same way the app will check it. The Release workflow runs this for you after publishing an app release.

      remove <edition> [--manifest <path>]
          Removes one edition (for example tournament) from latest.json, so the app stops offering it.
          Use this to pull a bad release quickly. Users who already installed it keep it.

      sign [--manifest <path>]
          Signs latest.json again as it is. Use it after keygen, or if the signature is missing or wrong.

      keygen <private-key-file>
          Makes a new signing key pair. The private key is written to the file you name (it must not exist yet) and
          must be kept secret and backed up. The public key is printed, to paste into ManifestSignature.cs.

      check [--manifest <path>]
          Checks latest.json is valid, in the canonical format, and signed by a key the app trusts. No network.

      verify [--manifest <path>]
          Downloads every zip latest.json lists from GitHub, exactly as the app would, and checks its size,
          SHA-256, version, edition and build. Run this after uploading and before (or right after) pushing.

    The manifest defaults to latest.json in the repository root.

    add, set-app, remove and sign always sign what they write, because the app ignores an unsigned latest.json. They read
    the private key from the file named by --key-file, or else from the RESKATE_MANIFEST_KEY environment variable (the
    key's text itself, which is how the Release workflow passes its secret). With neither, they write nothing.
    """;

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Console.WriteLine(Usage);
    return args.Length == 0 ? 2 : 0;
}

string command = args[0];
string? zipArgument = null;
string? tag = null;
string? manifestPath = null;
string? notes = null;
string published = DateTime.UtcNow.ToString("yyyy-MM-dd");

for (int i = 1; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--tag" when i + 1 < args.Length: tag = args[++i]; break;
        case "--manifest" when i + 1 < args.Length: manifestPath = args[++i]; break;
        case "--notes" when i + 1 < args.Length: notes = args[++i].Trim(); break;
        case "--published" when i + 1 < args.Length: published = args[++i]; break;
        case "--key-file" when i + 1 < args.Length: Signing.KeyFile = args[++i]; break;
        default:
            if (args[i].StartsWith("--", StringComparison.Ordinal) || zipArgument is not null)
                return UsageError($"Unexpected argument: {args[i]}");
            zipArgument = args[i];
            break;
    }
}

manifestPath = Path.GetFullPath(manifestPath ?? Path.Combine(FindRepoRoot() ?? Directory.GetCurrentDirectory(), OnlineSource.ManifestFileName));

return command switch
{
    "add" when zipArgument is not null => Add(zipArgument, tag, manifestPath, notes, published),
    "add" => UsageError("add needs the path of a Trainer zip."),
    "set-app" when zipArgument is not null => SetApp(zipArgument, manifestPath, notes, published),
    "set-app" => UsageError("set-app needs the path of the app release zip."),
    "remove" when zipArgument is not null => Remove(zipArgument, manifestPath),
    "remove" => UsageError("remove needs an edition key, for example tournament."),
    "sign" => Sign(manifestPath),
    "keygen" when zipArgument is not null => KeyGen(zipArgument),
    "keygen" => UsageError("keygen needs the path of a file to write the private key to."),
    "check" => Check(manifestPath),
    "verify" => await VerifyAsync(manifestPath),
    _ => UsageError($"Unknown command: {command}"),
};

// ---------- add ----------

static int Add(string zipPath, string? tag, string manifestPath, string? notes, string published)
{
    if (!OperatingSystem.IsWindows())
        return Error("add must run on Windows: the product name in ReSkate.dll is read from Windows file properties.");
    if (!File.Exists(zipPath)) return Error($"No such file: {zipPath}");

    var fileName = Path.GetFileName(zipPath);

    // GitHub renames asset files with spaces and other characters, which would break the URL. Refuse them.
    if (!Regex.IsMatch(fileName, @"^[A-Za-z0-9._-]+\.zip$"))
    {
        return Error($"The file name \"{fileName}\" must use only letters, digits, dots, dashes and underscores, " +
                     "because GitHub renames other characters when it is uploaded. Rename it, for example to " +
                     "ReSkate_Trainer_Tournament-0.9.2-on-v2.1.1.zip, and run this again.");
    }

    Console.WriteLine($"Checking {fileName} ...");
    TrainerPackage package;
    try { package = TrainerPackageLoader.Load(zipPath); }
    catch (InvalidDataException) { return Error("That isn't a valid zip file."); }

    using (package)
    {
        foreach (var note in package.Notes) Console.WriteLine("  " + note);
        foreach (var warning in package.Warnings) Console.WriteLine("  Warning: " + warning);
        if (!package.IsValid) return Error(package.Errors[0]);
        if (package.Version is not { HasTrainer: true } version)
            return Error("The zip has no readable Trainer version, so the app couldn't tell when to offer it.");

        string edition = OnlineSource.EditionKey(version.Edition);
        tag ??= $"trainer-{edition}-{version.TrainerVersion}";

        if (!Regex.IsMatch(tag, @"^[A-Za-z0-9._-]+$"))
            return Error($"The tag \"{tag}\" must use only letters, digits, dots, dashes and underscores.");
        if (tag.StartsWith('v'))
        {
            return Error($"The tag \"{tag}\" starts with \"v\". Tags starting with v trigger the app's own Release workflow. " +
                         "Use a tag like trainer-tournament-0.9.2 for Trainer zips.");
        }

        long size = new FileInfo(zipPath).Length;
        string sha256;
        using (var stream = File.OpenRead(zipPath)) sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();

        var entry = new ManifestEntry(
            Edition: edition,
            Version: version.TrainerVersion!,
            Build: version.TrainerBuild,
            ReSkateVersion: version.BaseVersion is { } b && Regex.IsMatch(b, @"^\d{1,4}\.\d{1,4}\.\d{1,4}$") ? b : null,
            Url: OnlineSource.ReleaseAssetUrl(tag, fileName),
            SizeBytes: size,
            Sha256: sha256,
            Published: published,
            Notes: string.IsNullOrEmpty(notes) ? null : notes);

        var problems = TrainerManifest.CheckEntry(entry);
        if (problems.Count > 0) return Error(string.Join(Environment.NewLine, problems));

        // Start from the existing manifest so other editions are kept.
        var manifest = new TrainerManifest();
        if (File.Exists(manifestPath))
        {
            var existing = TrainerManifest.Parse(File.ReadAllText(manifestPath));
            if (existing.Manifest is null || existing.Problems.Count > 0)
            {
                return Error($"The existing {manifestPath} has problems, so it wasn't changed:{Environment.NewLine}  " +
                             string.Join(Environment.NewLine + "  ", existing.Problems));
            }
            manifest = existing.Manifest;

            if (manifest.Find(edition) is { } previous && VersionCompare.Compare(previous.Version, entry.Version) > 0)
                Console.WriteLine($"  Warning: latest.json had {previous.TrainerText}, which is NEWER than this zip. Apps with {previous.Version} won't be offered this one.");
        }

        manifest = manifest.With(entry);
        if (!WriteSigned(manifestPath, manifest)) return 1;

        Console.WriteLine();
        Console.WriteLine($"Updated {manifestPath}");
        Console.WriteLine($"  edition  {edition}");
        Console.WriteLine($"  version  {entry.Version}{(entry.Build is null ? "" : "+" + entry.Build)} on ReSkate {entry.ReSkateVersion ?? "?"}");
        Console.WriteLine($"  size     {size} bytes");
        Console.WriteLine($"  sha256   {sha256}");
        Console.WriteLine($"  url      {entry.Url}");
        Console.WriteLine($"  shown    published {entry.Published}{(entry.Notes is null ? "" : ", \"" + entry.Notes + "\"")}");
        Console.WriteLine();
        Console.WriteLine("Next steps, in this order:");
        Console.WriteLine($"  1. Create a GitHub Release with tag \"{tag}\" and attach {fileName} unchanged.");
        Console.WriteLine("     Untick \"Set as the latest release\" so the app's own release stays the latest.");
        Console.WriteLine("  2. Run:  dotnet run --project tools/ReSkateTrainerUpdater.ManifestTool -- verify");
        Console.WriteLine("  3. Commit and push latest.json to main.");
        return 0;
    }
}

// ---------- set-app ----------

static int SetApp(string zipPath, string manifestPath, string? notes, string published)
{
    if (!OperatingSystem.IsWindows())
        return Error("set-app must run on Windows: the exe's version is read from Windows file properties.");
    if (!File.Exists(zipPath)) return Error($"No such file: {zipPath}");

    var fileName = Path.GetFileName(zipPath);
    var match = Regex.Match(fileName, @"^ReSkateTrainerUpdater-(\d{1,4}\.\d{1,4}\.\d{1,4})\.zip$");
    if (!match.Success)
        return Error($"The file name \"{fileName}\" must look like ReSkateTrainerUpdater-1.2.0.zip, which is what the build script makes.");
    string version = match.Groups[1].Value;

    // Check it exactly as the app will: the exe must be there, be this app, and be this version.
    var scratch = Path.Combine(Path.GetTempPath(), "ReSkateTrainerUpdater_setapp_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(scratch);
    try
    {
        var staged = AppUpdater.Stage(zipPath, Path.Combine(scratch, AppUpdater.ExeName), version);
        if (!staged.Succeeded) return Error($"The zip isn't a valid {version} release of the app: {staged.Detail}");
    }
    finally
    {
        try { Directory.Delete(scratch, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    long size = new FileInfo(zipPath).Length;
    string sha256;
    using (var stream = File.OpenRead(zipPath)) sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();

    var entry = new AppEntry(version, OnlineSource.AppAssetUrl(version), size, sha256, published, string.IsNullOrEmpty(notes) ? null : notes);
    var problems = TrainerManifest.CheckApp(entry);
    if (problems.Count > 0) return Error(string.Join(Environment.NewLine, problems));

    var manifest = new TrainerManifest();
    if (File.Exists(manifestPath))
    {
        var existing = TrainerManifest.Parse(File.ReadAllText(manifestPath));
        if (existing.Manifest is null || existing.Problems.Count > 0)
        {
            return Error($"The existing {manifestPath} has problems, so it wasn't changed:{Environment.NewLine}  " +
                         string.Join(Environment.NewLine + "  ", existing.Problems));
        }
        manifest = existing.Manifest;

        if (manifest.App is { } previous && VersionCompare.Compare(previous.Version, version) > 0)
            Console.WriteLine($"  Warning: latest.json had app version {previous.Version}, which is NEWER than this zip. Installed copies at {previous.Version} won't be offered this one.");
    }

    if (!WriteSigned(manifestPath, manifest.WithApp(entry))) return 1;

    Console.WriteLine($"Updated {manifestPath}");
    Console.WriteLine($"  app      {version}");
    Console.WriteLine($"  size     {size} bytes");
    Console.WriteLine($"  sha256   {sha256}");
    Console.WriteLine($"  url      {entry.Url}");
    Console.WriteLine($"  shown    published {entry.Published}{(entry.Notes is null ? "" : ", \"" + entry.Notes + "\"")}");
    return 0;
}

// ---------- remove ----------

static int Remove(string edition, string manifestPath)
{
    if (!File.Exists(manifestPath)) return Error($"No such file: {manifestPath}");
    var result = TrainerManifest.Parse(File.ReadAllText(manifestPath));
    if (result.Manifest is null || result.Problems.Count > 0)
        return Error($"{manifestPath} has problems, so it wasn't changed:{Environment.NewLine}  " + string.Join(Environment.NewLine + "  ", result.Problems));
    if (result.Manifest.Find(edition) is null)
        return Error($"latest.json has no edition \"{edition}\". It has: {string.Join(", ", result.Manifest.Editions.Keys.DefaultIfEmpty("(none)"))}");

    var kept = result.Manifest.Without(edition);
    if (!WriteSigned(manifestPath, kept)) return 1;
    Console.WriteLine($"Removed \"{edition}\" from {manifestPath}. Commit and push it to main; apps pick it up on their next start.");
    return 0;
}

// ---------- sign / keygen ----------

static int Sign(string manifestPath)
{
    if (!File.Exists(manifestPath)) return Error($"No such file: {manifestPath}");
    var result = TrainerManifest.Parse(File.ReadAllText(manifestPath));
    if (result.Manifest is null || result.Problems.Count > 0)
        return Error($"{manifestPath} has problems, so it wasn't signed:{Environment.NewLine}  " + string.Join(Environment.NewLine + "  ", result.Problems));
    if (!WriteSigned(manifestPath, result.Manifest)) return 1;
    Console.WriteLine($"Signed {manifestPath}");
    return 0;
}

static int KeyGen(string keyFile)
{
    keyFile = Path.GetFullPath(keyFile);
    if (File.Exists(keyFile)) return Error($"{keyFile} already exists. It is never overwritten, because that would lose the old key.");
    if (FindRepoRoot() is { } root && keyFile.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        return Error("Don't keep the private key inside the repository. Choose a path outside it.");

    var (privateKey, publicKey) = ManifestSignature.GenerateKeyPair();
    Directory.CreateDirectory(Path.GetDirectoryName(keyFile)!);
    File.WriteAllText(keyFile, privateKey + Environment.NewLine);

    Console.WriteLine($"Private key written to {keyFile}");
    Console.WriteLine("  Keep it secret and keep a backup: whoever has it can publish updates, and without it you can't.");
    Console.WriteLine("  The Release workflow reads it from the repository secret MANIFEST_SIGNING_KEY (the file's text).");
    Console.WriteLine();
    Console.WriteLine("Public key, for ManifestSignature.TrustedPublicKeys:");
    Console.WriteLine(publicKey);
    return 0;
}

// Signs the manifest and writes it. Writes nothing at all unless it can sign with a key the app trusts.
static bool WriteSigned(string manifestPath, TrainerManifest manifest)
{
    string? keyText = Signing.KeyFile is { } file
        ? (File.Exists(file) ? File.ReadAllText(file) : null)
        : Environment.GetEnvironmentVariable("RESKATE_MANIFEST_KEY");
    if (string.IsNullOrWhiteSpace(keyText))
    {
        Error(Signing.KeyFile is { } missing
            ? $"The key file {missing} doesn't exist, so latest.json was not changed."
            : "No signing key. Pass --key-file <path> or set RESKATE_MANIFEST_KEY. The app ignores an unsigned latest.json, so nothing was written.");
        return false;
    }

    try
    {
        using var key = ManifestSignature.LoadPrivateKey(keyText);
        if (!ManifestSignature.TrustedPublicKeys.Contains(ManifestSignature.PublicKeyOf(key)))
        {
            Error("That key isn't one the app trusts (see ManifestSignature.TrustedPublicKeys), so apps would ignore the result. Nothing was written.");
            return false;
        }

        var signed = ManifestSignature.Sign(manifest.ToJson(), key);
        if (!ManifestSignature.Verify(signed).IsValid)
        {
            Error("Internal check failed: the signature didn't verify. Nothing was written.");
            return false;
        }
        File.WriteAllText(manifestPath, signed);
        return true;
    }
    catch (Exception ex) when (ex is CryptographicException or FormatException)
    {
        Error("The signing key couldn't be read: " + ex.Message);
        return false;
    }
}

// ---------- check ----------

static int Check(string manifestPath)
{
    if (!File.Exists(manifestPath)) return Error($"No such file: {manifestPath}");

    var text = File.ReadAllText(manifestPath);
    var result = TrainerManifest.Parse(text);
    if (result.Manifest is null || result.Problems.Count > 0)
    {
        return Error($"{manifestPath} has problems:{Environment.NewLine}  " +
                     string.Join(Environment.NewLine + "  ", result.Problems));
    }

    var signature = ManifestSignature.Verify(text);
    if (!signature.IsValid)
        return Error($"{manifestPath}: {signature.Problem} The app would ignore it. Run \"sign\" with the signing key.");

    if (signature.Payload != result.Manifest.ToJson())
        return Error($"{manifestPath} is valid but not in the canonical format. Re-run \"add\" rather than editing it by hand.");

    Console.WriteLine(result.Manifest.Editions.Count == 0
        ? "latest.json is valid and signed. No editions are published, so the app offers no online install."
        : "latest.json is valid and signed: " + string.Join(", ", result.Manifest.Editions.Values.Select(e => e.TrainerText)));
    if (result.Manifest.App is { } app) Console.WriteLine($"  The updater itself: version {app.Version} is published for self-update.");
    return 0;
}

// ---------- verify ----------

static async Task<int> VerifyAsync(string manifestPath)
{
    if (Check(manifestPath) != 0) return 1;
    var manifest = TrainerManifest.Parse(File.ReadAllText(manifestPath)).Manifest!;
    if (manifest.Editions.Count == 0 && manifest.App is null) return 0;

    if (!OperatingSystem.IsWindows())
        return Error("verify must run on Windows: the product name in ReSkate.dll is read from Windows file properties.");

    using var client = TrainerDownloader.CreateClient();
    int failures = 0;

    if (manifest.App is { } app)
    {
        Console.WriteLine();
        Console.WriteLine($"updater {app.Version}: downloading {app.Url}");
        var download = await TrainerDownloader.DownloadAsync(client, app);
        if (!download.Succeeded)
        {
            failures++;
            Console.WriteLine($"  FAILED ({download.Failure}): {download.Detail}");
            if (download.Failure == DownloadFailure.HttpError)
                Console.WriteLine("  Has the app release been published with exactly this tag and file name?");
        }
        else
        {
            var scratch = Path.Combine(Path.GetTempPath(), "ReSkateTrainerUpdater_verify_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            try
            {
                Console.WriteLine("  " + download.Detail);
                var staged = AppUpdater.Stage(download.ZipPath!, Path.Combine(scratch, AppUpdater.ExeName), app.Version);
                if (staged.Succeeded)
                {
                    Console.WriteLine($"  OK: the zip holds {AppUpdater.ExeName} {app.Version}, as latest.json says.");
                }
                else
                {
                    failures++;
                    Console.WriteLine("  FAILED: " + staged.Detail);
                }
            }
            finally
            {
                TrainerDownloader.DeleteDownloadFolder(download.ZipPath!);
                try { Directory.Delete(scratch, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }

    foreach (var entry in manifest.Editions.Values)
    {
        Console.WriteLine();
        Console.WriteLine($"{entry.TrainerText}: downloading {entry.Url}");
        var download = await TrainerDownloader.DownloadAsync(client, entry);
        if (!download.Succeeded)
        {
            failures++;
            Console.WriteLine($"  FAILED ({download.Failure}): {download.Detail}");
            if (download.Failure == DownloadFailure.HttpError)
                Console.WriteLine("  Has the zip been uploaded to a Release with exactly this tag and file name?");
            continue;
        }

        try
        {
            Console.WriteLine("  " + download.Detail);
            using var package = TrainerPackageLoader.Load(download.ZipPath!);
            if (TrainerDownloader.CheckPackageMatches(entry, package) is { } mismatch)
            {
                failures++;
                Console.WriteLine("  FAILED: " + mismatch);
            }
            else
            {
                Console.WriteLine($"  OK: the zip contains {package.Version!.DisplayName}, as latest.json says.");
            }
        }
        catch (InvalidDataException)
        {
            failures++;
            Console.WriteLine("  FAILED: the download isn't a valid zip.");
        }
        finally
        {
            TrainerDownloader.DeleteDownloadFolder(download.ZipPath!);
        }
    }

    Console.WriteLine();
    if (failures > 0) return Error($"{failures} entry(ies) failed verification. The app would refuse them.");
    Console.WriteLine("Every published zip downloads and matches latest.json.");
    return 0;
}

// ---------- helpers ----------

static string? FindRepoRoot()
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "ReSkateTrainerUpdater.sln"))) return dir.FullName;
    return null;
}

static int Error(string message)
{
    Console.Error.WriteLine("Error: " + message);
    return 1;
}

static int UsageError(string message)
{
    Console.Error.WriteLine("Error: " + message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(Usage);
    return 2;
}

static class Signing
{
    /// <summary>The --key-file argument, if given.</summary>
    public static string? KeyFile;
}
