using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace ReSkateTrainerUpdater.Core;

/// <summary>The product name and version from a file's Windows file properties.</summary>
internal sealed record FileVersionDetails(string? ProductName, string? ProductVersion);

/// <summary>Reads version information out of ReSkate.dll and ReSkateLauncher.exe.</summary>
public static class ReSkateScanner
{
    private const string TrainerMarker = "ReSkate_Trainer";
    private const int MaxNameLength = 64;
    private const int VersionSearchWindow = 64;

    private static readonly Regex VersionPattern = new(
        @"(?<![0-9A-Za-z.])(\d{1,4}\.\d{1,4}\.\d{1,4})(?:\+([0-9a-f]{5,40}))?(?![0-9A-Za-z.+])",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Reads the file properties of a Windows file. Tests replace this so they don't need real Windows binaries.
    /// </summary>
    internal static Func<string, FileVersionDetails> FileVersionReader { get; set; } = ReadWindowsFileProperties;

    private static FileVersionDetails ReadWindowsFileProperties(string path)
    {
        var info = FileVersionInfo.GetVersionInfo(path);
        return new FileVersionDetails(
            NullIfBlank(info.ProductName),
            NullIfBlank(info.ProductVersion) ?? NullIfBlank(info.FileVersion));
    }

    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>Reads the ReSkate version and, if there is one, the Trainer version baked into the file.</summary>
    public static ReSkateVersion Read(string path)
    {
        var properties = FileVersionReader(path);
        var (version, build, edition) = FindTrainer(path);
        return new ReSkateVersion(properties.ProductVersion, properties.ProductName, version, build, edition);
    }

    /// <summary>
    /// The Trainer stamps its name and version into the binary as two strings stored close together,
    /// for example "ReSkate_Trainer_Tournament" followed a few bytes later by "0.9.2+578f597".
    /// </summary>
    private static (string? Version, string? Build, string? Edition) FindTrainer(string path)
    {
        var data = ReadAllBytesShared(path);
        ReadOnlySpan<byte> marker = Encoding.ASCII.GetBytes(TrainerMarker);

        int position = 0;
        while (position < data.Length - marker.Length)
        {
            int offset = data.AsSpan(position).IndexOf(marker);
            if (offset < 0) break;

            int start = position + offset;
            position = start + marker.Length;

            // The whole name, which must end with a NUL byte.
            int end = start;
            while (end < data.Length && end - start < MaxNameLength && IsNameByte(data[end])) end++;
            if (end >= data.Length || data[end] != 0) continue;
            string name = Encoding.ASCII.GetString(data, start, end - start);

            // The version string follows shortly after.
            int windowLength = Math.Min(VersionSearchWindow, data.Length - end);
            var match = VersionPattern.Match(Encoding.Latin1.GetString(data, end, windowLength));
            if (!match.Success) continue;

            string edition = name[TrainerMarker.Length..].Trim('_').Replace('_', ' ');
            return (
                match.Groups[1].Value,
                match.Groups[2].Success ? match.Groups[2].Value : null,
                edition.Length > 0 ? edition : null);
        }

        return (null, null, null);
    }

    private static bool IsNameByte(byte b) =>
        b is (>= (byte)'A' and <= (byte)'Z') or (>= (byte)'a' and <= (byte)'z') or (>= (byte)'0' and <= (byte)'9') or (byte)'_';

    // Read even if the game or launcher currently has the file open.
    private static byte[] ReadAllBytesShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buffer = new byte[stream.Length];
        int total = 0;
        while (total < buffer.Length)
        {
            int read = stream.Read(buffer, total, buffer.Length - total);
            if (read <= 0) break;
            total += read;
        }
        return total == buffer.Length ? buffer : buffer[..total];
    }

    /// <summary>True if the file is a 64-bit (x64) Windows executable or DLL.</summary>
    public static bool IsX64WindowsProgram(string path)
    {
        const ushort MachineX64 = 0x8664;
        const int PeOffsetLocation = 0x3C;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            var dosHeader = new byte[64];
            if (stream.Read(dosHeader, 0, dosHeader.Length) < dosHeader.Length) return false;
            if (dosHeader[0] != 'M' || dosHeader[1] != 'Z') return false;

            int peOffset = BitConverter.ToInt32(dosHeader, PeOffsetLocation);
            if (peOffset < 0 || peOffset > stream.Length - 6) return false;

            stream.Position = peOffset;
            var peHeader = new byte[6];
            if (stream.Read(peHeader, 0, peHeader.Length) < peHeader.Length) return false;

            return peHeader[0] == 'P' && peHeader[1] == 'E' && peHeader[2] == 0 && peHeader[3] == 0
                && BitConverter.ToUInt16(peHeader, 4) == MachineX64;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
