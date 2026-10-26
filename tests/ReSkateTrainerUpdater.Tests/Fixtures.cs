using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace ReSkateTrainerUpdater.Tests;

/// <summary>A scratch folder that is deleted when the test run ends.</summary>
internal sealed class Workspace : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "ReSkateUpdaterTests_" + Guid.NewGuid().ToString("N"));

    public Workspace() => Directory.CreateDirectory(Root);

    public string NewFolder(string name)
    {
        var folder = Path.Combine(Root, name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>Zips up a folder that <paramref name="fill"/> populates.</summary>
    public string NewZip(string name, Action<string> fill)
    {
        var source = NewFolder("zipsource_" + name);
        fill(source);
        var zip = Path.Combine(Root, name + ".zip");
        ZipFile.CreateFromDirectory(source, zip);
        return zip;
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>Builds small stand-ins for the real ReSkate binaries.</summary>
internal static class FakeBinary
{
    /// <summary>Put this in a fake binary's salt and the test setup reports it as a different product.</summary>
    public const string OtherProductMarker = "ThisIsNotReSkate";

    /// <summary>
    /// The smallest file the validator accepts as a 64-bit Windows program (a DOS header pointing at "PE\0\0" and the x64
    /// machine type), followed by the Trainer's name and version strings placed the way the real binary stores them.
    /// </summary>
    /// <param name="trainerName">For example "ReSkate_Trainer_Tournament". Null for plain ReSkate.</param>
    /// <param name="trainerVersion">For example "0.9.2+578f597".</param>
    /// <param name="salt">Changes the file's contents so two otherwise equal files differ.</param>
    public static byte[] Pe(string? trainerName = "ReSkate_Trainer_Tournament", string? trainerVersion = "0.9.2+578f597", string salt = "")
    {
        var bytes = new List<byte>();

        var dos = new byte[64];
        dos[0] = (byte)'M';
        dos[1] = (byte)'Z';
        BitConverter.GetBytes(64).CopyTo(dos, 0x3C);
        bytes.AddRange(dos);

        bytes.AddRange(new byte[] { (byte)'P', (byte)'E', 0, 0 });
        bytes.AddRange(BitConverter.GetBytes((ushort)0x8664));
        bytes.AddRange(new byte[256]);

        if (trainerName is not null)
        {
            bytes.AddRange(Encoding.ASCII.GetBytes(trainerName));
            bytes.Add(0);
            bytes.AddRange(new byte[] { 0, 0, 0, 0, 0, 0, 13, 0, 0, 0, 0, 0, 0, 0 });
            bytes.AddRange(Encoding.ASCII.GetBytes(trainerVersion ?? ""));
            bytes.Add(0);
        }

        bytes.AddRange(new byte[100]);
        bytes.AddRange(Encoding.ASCII.GetBytes(salt));
        return bytes.ToArray();
    }

    /// <summary>Writes a Trainer release (ReSkate.dll, ReSkateLauncher.exe, README.txt) into a folder.</summary>
    public static void WriteRelease(string folder, string? trainerName = "ReSkate_Trainer_Tournament", string? trainerVersion = "0.9.2+578f597", string salt = "")
    {
        File.WriteAllBytes(Path.Combine(folder, "ReSkate.dll"), Pe(trainerName, trainerVersion, salt));
        File.WriteAllBytes(Path.Combine(folder, "ReSkateLauncher.exe"), Pe(trainerName, trainerVersion, salt + "launcher"));
        File.WriteAllText(Path.Combine(folder, "README.txt"), "Copy these files over your ReSkate folder.");
    }
}

internal static class Hashing
{
    public static string Of(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
