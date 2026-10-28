using System.Text.RegularExpressions;
using static ReSkateTrainerUpdater.Tests.TestKit;

namespace ReSkateTrainerUpdater.Tests;

/// <summary>
/// Rules about the setup program that other parts of the app depend on. The setup itself can only be built and run on
/// Windows with Inno Setup, so these read its script and the files around it and check the rules hold.
/// </summary>
internal static class InstallerScriptTests
{
    public static void Run()
    {
        Section("The setup program");

        var root = FindRepoRoot();
        if (root is null)
        {
            Skip("The setup script's rules", "not running from inside the repository");
            return;
        }

        string Read(string relative) => File.ReadAllText(Path.Combine(root, relative.Replace('\\', Path.DirectorySeparatorChar)));
        var script = Read(@"installer\ReSkateTrainerUpdater.iss");
        var appInfo = Read(@"src\ReSkateTrainerUpdater\Ui\AppInfo.cs");
        var release = Read(@".github\workflows\release.yml");
        var buildScript = Read(@"scripts\build-release.ps1");

        // The identity: how a newer setup finds an older install, and how the app finds its own uninstall entry.
        var scriptId = Regex.Match(script, @"(?m)^AppId=\{(\{[0-9A-Fa-f-]{36}\})").Groups[1].Value;
        var appId = Regex.Match(appInfo, "InstallerAppId = \"(\\{[0-9A-Fa-f-]{36}\\})\"").Groups[1].Value;
        Check("The setup and the app use the same app identity", scriptId.Length > 0 && string.Equals(scriptId, appId, StringComparison.OrdinalIgnoreCase),
            $"setup: {scriptId}, app: {appId}");

        // Self-update swaps the exe in its own folder, so that folder must be one the user can write to.
        Check("Invariant: the setup installs for the current user, without administrator rights",
            Regex.IsMatch(script, @"(?m)^PrivilegesRequired=lowest\s*$"));
        Check("Invariant: the setup can't be switched to an all-users install, which would break self-update",
            !script.Contains("PrivilegesRequiredOverridesAllowed", StringComparison.Ordinal) ||
            Regex.IsMatch(script, @"(?m)^\s*;.*PrivilegesRequiredOverridesAllowed"));
        Check("It installs to the per-user programs folder", Regex.IsMatch(script, @"(?m)^DefaultDirName=\{autopf\}\\"));

        // The files an update leaves beside the exe must be removed by the uninstaller.
        Check("The uninstaller removes the .old and .new files a self-update leaves",
            Regex.IsMatch(script, @"\[UninstallDelete\][\s\S]*\.old""") && Regex.IsMatch(script, @"\[UninstallDelete\][\s\S]*\.new"""));
        Check("It closes the app if it is running", Regex.IsMatch(script, @"(?m)^CloseApplications=yes\s*$"));
        Check("It adds a Start Menu entry", Regex.IsMatch(script, @"\{autoprograms\}\\"));
        Check("It carries the font licences with the exe", script.Contains("THIRD-PARTY-NOTICES.md", StringComparison.Ordinal));

        // The release plumbing agrees on the file name.
        Check("The setup is named ReSkateTrainerUpdater-Setup-<version>.exe",
            Regex.IsMatch(script, @"(?m)^OutputBaseFilename=ReSkateTrainerUpdater-Setup-\{#AppVersion\}\s*$"));
        Check("The release script builds it and fails if Inno Setup is missing",
            buildScript.Contains("Setup-$version.exe", StringComparison.Ordinal) && buildScript.Contains("Inno Setup 6 was not found", StringComparison.Ordinal));
        Check("The release workflow attaches both the zip and the setup",
            release.Contains("ReSkateTrainerUpdater-$version.zip", StringComparison.Ordinal) && release.Contains("ReSkateTrainerUpdater-Setup-$version.exe", StringComparison.Ordinal));
        Check("Invariant: the self-update still downloads the portable zip, which the setup name can't match",
            release.Contains("release/ReSkateTrainerUpdater-*.zip", StringComparison.Ordinal) && !"ReSkateTrainerUpdater-Setup-1.0.0.exe".EndsWith(".zip", StringComparison.Ordinal));

        // Trimming would make the self-update much smaller, but a trimmed WinForms exe can't take a dropped zip yet. So it is
        // off unless asked for, and the exe is always self-tested after it is built.
        var csproj = Read(@"src\ReSkateTrainerUpdater\ReSkateTrainerUpdater.csproj");
        var program = Read(@"src\ReSkateTrainerUpdater\Program.cs");
        Check("Invariant: a build is only ever trimmed on request, and the script always runs the exe's self-test and stops on failure",
            buildScript.Contains("PublishTrimmed=$trimValue", StringComparison.Ordinal) &&
            buildScript.Contains("$trimValue = if ($Trim) { 'true' } else { 'false' }", StringComparison.Ordinal) &&
            buildScript.Contains("'--self-test'", StringComparison.Ordinal) &&
            Regex.IsMatch(buildScript, @"ExitCode -ne 0\) \{ throw"));
        Check("Invariant: the self-test flag is handled before any window is created", program.IndexOf("SelfTest.Flag", StringComparison.Ordinal) is > 0 &&
            program.IndexOf("SelfTest.Flag", StringComparison.Ordinal) < program.IndexOf("Application.Run", StringComparison.Ordinal));
        Check("Invariant: trimming keeps the WinForms assemblies whole, because trimmed they can't load a dropped file",
            csproj.Contains("TrimmerRootAssembly Include=\"System.Windows.Forms\"", StringComparison.Ordinal) &&
            csproj.Contains("TrimmerRootAssembly Include=\"System.Drawing.Common\"", StringComparison.Ordinal));
        Check("The CI workflow builds the release files, so the self-test runs on every push",
            Read(@".github\workflows\ci.yml").Contains("build-release.ps1", StringComparison.Ordinal));

        // The workflows reuse the runner's .NET SDK instead of installing one on every run (about 30 seconds each time).
        var composite = Read(".github/actions/dotnet-8/action.yml");
        Check("The workflows share one step that uses the installed .NET 8 SDK and only installs it if it is missing",
            composite.Contains("--list-sdks", StringComparison.Ordinal) && composite.Contains("actions/setup-dotnet", StringComparison.Ordinal) &&
            Read(@".github\workflows\ci.yml").Contains("./.github/actions/dotnet-8", StringComparison.Ordinal) &&
            release.Contains("./.github/actions/dotnet-8", StringComparison.Ordinal) &&
            !release.Contains("uses: actions/setup-dotnet", StringComparison.Ordinal));
        Check("global.json keeps every machine on SDK 8", Regex.IsMatch(Read("global.json"), @"""version"":\s*""8\."));
        Check("Downloading every published zip (verify) is not part of every CI run",
            !Read(@".github\workflows\ci.yml").Contains("-- verify", StringComparison.Ordinal) &&
            Read(".github/workflows/verify-manifest.yml").Contains("-- verify", StringComparison.Ordinal));

        Check("The wizard pictures exist", File.Exists(Path.Combine(root, "installer", "wizard-large.bmp")) && File.Exists(Path.Combine(root, "installer", "wizard-small.bmp")));
    }

    private static string? FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ReSkateTrainerUpdater.sln"))) return dir.FullName;
        return null;
    }
}
