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

        string Read(string relative) => File.ReadAllText(Path.Combine(root, relative));
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

        Check("The wizard pictures exist", File.Exists(Path.Combine(root, @"installer\wizard-large.bmp")) && File.Exists(Path.Combine(root, @"installer\wizard-small.bmp")));
    }

    private static string? FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ReSkateTrainerUpdater.sln"))) return dir.FullName;
        return null;
    }
}
