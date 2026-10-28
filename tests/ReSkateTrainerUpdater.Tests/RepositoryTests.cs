using System.Text.RegularExpressions;
using ReSkateTrainerUpdater.Core;
using static ReSkateTrainerUpdater.Tests.TestKit;

namespace ReSkateTrainerUpdater.Tests;

/// <summary>Files around the code that other things rely on: the licence and the issue templates.</summary>
internal static class RepositoryTests
{
    public static void Run()
    {
        Section("Licence and issue templates");

        var root = FindRepoRoot();
        if (root is null)
        {
            Skip("The licence and issue templates", "not running from inside the repository");
            return;
        }

        string Read(string relative) => File.ReadAllText(Path.Combine(root, relative.Replace('\\', Path.DirectorySeparatorChar)));

        var license = Read("LICENSE");
        Check("The repository has an MIT licence naming a copyright holder",
            license.StartsWith("MIT License", StringComparison.Ordinal) && Regex.IsMatch(license, @"Copyright \(c\) \d{4} \S"));
        Check("The README says what the licence is", Read("README.md").Contains("[MIT licence](LICENSE)", StringComparison.Ordinal));

        // The REPORT A PROBLEM button opens a prefilled issue. The template is what people get from the Issues tab, so
        // both must ask for the same things in the same format.
        var template = Read(@".github\ISSUE_TEMPLATE\problem.md").Replace("\r\n", "\n");
        var prefilled = Uri.UnescapeDataString(UpdateAdvisor.DescribeProblemReportUrl("1.2.3", "Windows 11"));
        var prefilledBody = prefilled[(prefilled.IndexOf("&body=", StringComparison.Ordinal) + "&body=".Length)..];
        var headings = Regex.Matches(prefilledBody, @"\*\*[^*]+\*\*").Select(m => m.Value).ToList();
        var missing = headings.Where(h => !template.Contains(h, StringComparison.Ordinal)).ToList();
        Check("The problem template asks for everything the REPORT A PROBLEM link prefills", headings.Count >= 4 && missing.Count == 0, string.Join(", ", missing));
        Check("The problem template has a fenced block for the log", template.Contains("```\n\n```", StringComparison.Ordinal));
        Check("The problem template sets the title the link uses",
            template.Contains("title: Problem with the updater", StringComparison.Ordinal));
        Check("The idea form has a name, a description and a body",
            Regex.IsMatch(Read(@".github\ISSUE_TEMPLATE\idea.yml"), @"(?s)^name: .+\ndescription: .+\n.*\nbody:"));

        // The Scoop manifest must point at this app's own release zip, like the app's self-update does.
        using var scoop = System.Text.Json.JsonDocument.Parse(Read("bucket/reskate-trainer-updater.json"));
        var scoopRoot = scoop.RootElement;
        var scoopVersion = scoopRoot.GetProperty("version").GetString();
        var scoopArch = scoopRoot.GetProperty("architecture").GetProperty("64bit");
        Check("The Scoop manifest downloads this app's own release zip for its version",
            OnlineSource.IsTrustedAppUrl(scoopArch.GetProperty("url").GetString(), scoopVersion), scoopArch.GetProperty("url").GetString());
        Check("The Scoop manifest has a SHA-256 and the MIT licence",
            Regex.IsMatch(scoopArch.GetProperty("hash").GetString() ?? "", "^[0-9a-f]{64}$") && scoopRoot.GetProperty("license").GetString() == "MIT");
        var published = TrainerManifest.Parse(Read("latest.json")).Manifest?.App;
        Check("When the Scoop manifest is for the published app version, its hash is the one latest.json has",
            published is null || published.Version != scoopVersion || published.Sha256 == scoopArch.GetProperty("hash").GetString());

        // A path like 'bucket\reskate...' once had its \r turned into a real carriage return, which broke the script.
        var scripts = new[] { "scripts", "installer" }.SelectMany(d => Directory.GetFiles(Path.Combine(root, d), "*.ps1")).ToList();
        var mangled = scripts.Where(f => Regex.IsMatch(File.ReadAllText(f), @"\r(?!\n)|[\x00-\x08\x0B\x0C\x0E-\x1F]"))
            .Select(Path.GetFileName).ToList();
        Check("No PowerShell script has a stray control character in it", scripts.Count > 0 && mangled.Count == 0, string.Join(", ", mangled));
    }

    private static string? FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ReSkateTrainerUpdater.sln"))) return dir.FullName;
        return null;
    }
}
