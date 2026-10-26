using ReSkateTrainerUpdater.Core;
using ReSkateTrainerUpdater.Tests;

// FileVersionInfo only reads Windows file properties on Windows. The tests use stand-in binaries, so they supply the
// product name and version themselves. A binary containing the marker text pretends to be some other product.
ReSkateScanner.FileVersionReader = path =>
{
    bool otherProduct = File.ReadAllText(path, System.Text.Encoding.Latin1).Contains(FakeBinary.OtherProductMarker, StringComparison.Ordinal);
    return new FileVersionDetails(otherProduct ? "SomethingElse" : "ReSkate", "2.1.1");
};

using var workspace = new Workspace();

ScannerTests.Run(workspace);
PackageTests.Run(workspace);
InstallerTests.Run(workspace);
AdvisorTests.Run(workspace);
SettingsTests.Run(workspace);
OnlineTests.Run(workspace);
AppUpdateTests.Run(workspace);

Console.WriteLine();
Console.WriteLine(TestKit.Failures == 0
    ? $"All {TestKit.Total} checks passed."
    : $"{TestKit.Failures} of {TestKit.Total} checks FAILED.");

return TestKit.Failures == 0 ? 0 : 1;
