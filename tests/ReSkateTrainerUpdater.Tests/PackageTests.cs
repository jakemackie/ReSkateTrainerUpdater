using ReSkateTrainerUpdater.Core;
using static ReSkateTrainerUpdater.Tests.TestKit;

namespace ReSkateTrainerUpdater.Tests;

internal static class PackageTests
{
    public static void Run(Workspace workspace)
    {
        Section("Unpacking and checking zips");

        var inner = workspace.NewZip("inner", folder => FakeBinary.WriteRelease(folder));

        using (var flat = TrainerPackageLoader.Load(inner))
        {
            Check("A normal zip is valid", flat.IsValid, string.Join("; ", flat.Errors));
            Check("It reports the Trainer version", flat.Version is { TrainerVersion: "0.9.2", Edition: "Tournament" });
            Check("It lists its files", flat.Files.Count == 3, string.Join(",", flat.Files));
            Check("It needs no unpacking notes", flat.Notes.Count == 0);
            Check("A clean zip has no warnings", flat.Warnings.Count == 0, string.Join("; ", flat.Warnings));
        }

        using (var nested = TrainerPackageLoader.Load(workspace.NewZip("nested", f => File.Copy(inner, Path.Combine(f, "Release.zip")))))
        {
            Check("A zip inside a zip is unpacked", nested.IsValid && nested.Notes.Any(n => n.Contains("zip inside the zip")), string.Join("; ", nested.Errors.Concat(nested.Notes)));
        }

        using (var wrapped = TrainerPackageLoader.Load(workspace.NewZip("wrapped", f =>
        {
            var sub = Directory.CreateDirectory(Path.Combine(f, "ReSkate_Trainer")).FullName;
            FakeBinary.WriteRelease(sub);
        })))
        {
            Check("A wrapper folder is skipped", wrapped.IsValid && wrapped.Notes.Any(n => n.Contains("top-level folder")), string.Join("; ", wrapped.Errors.Concat(wrapped.Notes)));
        }

        using (var deep = TrainerPackageLoader.Load(workspace.NewZip("deep", f =>
        {
            var sub = Directory.CreateDirectory(Path.Combine(f, "outer", "deeper")).FullName;
            File.Copy(inner, Path.Combine(sub, "Release.zip"));
        })))
        {
            Check("A wrapper folder around a zip is handled", deep.IsValid, string.Join("; ", deep.Errors));
        }

        Section("Zips that can't be installed");

        using (var noDll = TrainerPackageLoader.Load(workspace.NewZip("nodll", f =>
            File.WriteAllBytes(Path.Combine(f, "ReSkateLauncher.exe"), FakeBinary.Pe()))))
        {
            Check("A missing ReSkate.dll is an error", !noDll.IsValid && noDll.Errors[0].Contains("ReSkate.dll"), string.Join("; ", noDll.Errors));
        }

        using (var noLauncher = TrainerPackageLoader.Load(workspace.NewZip("nolauncher", f =>
            File.WriteAllBytes(Path.Combine(f, "ReSkate.dll"), FakeBinary.Pe()))))
        {
            Check("A missing launcher is an error", !noLauncher.IsValid && noLauncher.Errors[0].Contains("ReSkateLauncher.exe"), string.Join("; ", noLauncher.Errors));
        }

        using (var unrelated = TrainerPackageLoader.Load(workspace.NewZip("unrelated", f =>
            File.WriteAllText(Path.Combine(f, "notes.txt"), "hello"))))
        {
            Check("An unrelated zip is an error", !unrelated.IsValid && unrelated.Errors[0].Contains("doesn't look like"), string.Join("; ", unrelated.Errors));
        }

        using (var notPrograms = TrainerPackageLoader.Load(workspace.NewZip("notpe", f =>
        {
            File.WriteAllText(Path.Combine(f, "ReSkate.dll"), "not a program");
            File.WriteAllText(Path.Combine(f, "ReSkateLauncher.exe"), "nope");
        })))
        {
            Check("Files that aren't Windows programs are an error", !notPrograms.IsValid && notPrograms.Errors.Any(e => e.Contains("64-bit Windows")), string.Join("; ", notPrograms.Errors));
        }

        using (var wrongProduct = TrainerPackageLoader.Load(workspace.NewZip("wrongproduct", f =>
        {
            File.WriteAllBytes(Path.Combine(f, "ReSkate.dll"), FakeBinary.Pe(salt: FakeBinary.OtherProductMarker));
            File.WriteAllBytes(Path.Combine(f, "ReSkateLauncher.exe"), FakeBinary.Pe());
        })))
        {
            Check("A file from another product is an error", !wrongProduct.IsValid && wrongProduct.Errors.Any(e => e.Contains("wrong download")), string.Join("; ", wrongProduct.Errors));
        }

        using (var noTrainer = TrainerPackageLoader.Load(workspace.NewZip("plainreskate", f => FakeBinary.WriteRelease(f, trainerName: null))))
        {
            Check("Plain ReSkate loads but warns", noTrainer.IsValid && noTrainer.Warnings.Count == 1 && !noTrainer.Version!.HasTrainer, string.Join("; ", noTrainer.Warnings));
        }

        using (var mixed = TrainerPackageLoader.Load(workspace.NewZip("mixed", f =>
        {
            File.WriteAllBytes(Path.Combine(f, "ReSkate.dll"), FakeBinary.Pe(trainerVersion: "0.9.2+578f597"));
            File.WriteAllBytes(Path.Combine(f, "ReSkateLauncher.exe"), FakeBinary.Pe(trainerVersion: "0.9.1+aaaaaaa"));
        })))
        {
            Check("Mismatched Trainer versions warn", mixed.IsValid && mixed.Warnings.Any(w => w.Contains("different Trainer versions")), string.Join("; ", mixed.Warnings));
        }

        Section("Cleaning up");

        var damaged = Path.Combine(workspace.Root, "damaged.zip");
        File.WriteAllText(damaged, "this is not a zip");
        int before = Directory.GetDirectories(Path.GetTempPath(), TrainerPackageLoader.TempPrefix + "*").Length;

        bool threw = false;
        try { TrainerPackageLoader.Load(damaged).Dispose(); }
        catch (InvalidDataException) { threw = true; }
        Check("A damaged zip throws InvalidDataException", threw);
        Check("A failed load leaves no temp folder behind", Directory.GetDirectories(Path.GetTempPath(), TrainerPackageLoader.TempPrefix + "*").Length == before);

        string tempFolder;
        using (var package = TrainerPackageLoader.Load(inner)) tempFolder = package.RootFolder;
        Check("Disposing a package deletes its temp folder", !Directory.Exists(tempFolder));

        TrainerPackageLoader.CleanUpStaleTempFolders();
        Check("Stale-folder cleanup runs without error", true);
    }
}
