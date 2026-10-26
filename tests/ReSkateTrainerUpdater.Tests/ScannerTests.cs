using ReSkateTrainerUpdater.Core;
using static ReSkateTrainerUpdater.Tests.TestKit;

namespace ReSkateTrainerUpdater.Tests;

internal static class ScannerTests
{
    public static void Run(Workspace workspace)
    {
        Section("Reading versions");

        string Write(string name, byte[] content)
        {
            var path = Path.Combine(workspace.Root, name);
            File.WriteAllBytes(path, content);
            return path;
        }

        var tournament = ReSkateScanner.Read(Write("tournament.dll", FakeBinary.Pe()));
        Check("Trainer version, build and edition are read",
            tournament is { TrainerVersion: "0.9.2", TrainerBuild: "578f597", Edition: "Tournament" },
            tournament.ToString());
        Check("Display text includes the edition", tournament.TrainerText == "0.9.2 (Tournament)", tournament.TrainerText);
        Check("Display name is readable", tournament.DisplayName == "ReSkate Trainer 0.9.2 (Tournament)", tournament.DisplayName);

        var standard = ReSkateScanner.Read(Write("standard.dll", FakeBinary.Pe("ReSkate_Trainer", "1.2.3+abcdef1")));
        Check("Standard edition has no edition", standard is { TrainerVersion: "1.2.3", Edition: null } && standard.TrainerText == "1.2.3", standard.ToString());

        var noBuild = ReSkateScanner.Read(Write("nobuild.dll", FakeBinary.Pe("ReSkate_Trainer", "1.2.3")));
        Check("A version without a build is fine", noBuild is { TrainerVersion: "1.2.3", TrainerBuild: null }, noBuild.ToString());

        Check("Plain ReSkate has no Trainer", !ReSkateScanner.Read(Write("plain.dll", FakeBinary.Pe(null))).HasTrainer);
        Check("A Trainer name with no version is ignored", !ReSkateScanner.Read(Write("noversion.dll", FakeBinary.Pe("ReSkate_Trainer", "nothing here"))).HasTrainer);
        Check("An empty file is handled", !ReSkateScanner.Read(Write("empty.dll", Array.Empty<byte>())).HasTrainer);

        Section("Version comparison");
        Check("0.9.2 is older than 0.10.0 (numbers, not text)", VersionCompare.Compare("0.9.2", "0.10.0") < 0);
        Check("Equal versions compare equal", VersionCompare.Compare("0.9.2", "0.9.2") == 0);
        Check("1.0.0 is newer than 0.9.9", VersionCompare.Compare("1.0.0", "0.9.9") > 0);

        Section("Recognising 64-bit Windows programs");
        Check("A 64-bit program is accepted", ReSkateScanner.IsX64WindowsProgram(Write("x64.dll", FakeBinary.Pe())));
        Check("A text file is rejected", !ReSkateScanner.IsX64WindowsProgram(Write("text.dll", "not a program"u8.ToArray())));
        Check("A missing file is rejected", !ReSkateScanner.IsX64WindowsProgram(Path.Combine(workspace.Root, "missing.dll")));

        var x86 = FakeBinary.Pe();
        BitConverter.GetBytes((ushort)0x014C).CopyTo(x86, 64 + 4);
        Check("A 32-bit program is rejected", !ReSkateScanner.IsX64WindowsProgram(Write("x86.dll", x86)));
    }
}
