using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ReSkateTrainerUpdater.Ui;

/// <summary>
/// Loads the fonts the ReSkate launcher uses, straight from the exe, so the updater looks like part of the same family:
/// Permanent Marker for the big marker-pen headings, and Montserrat for everything else.
/// Both are free fonts (Apache 2.0 and the SIL Open Font License), credited in THIRD-PARTY-NOTICES.md.
/// If a font can't be loaded, <see cref="Create"/> quietly falls back to a Windows font.
/// </summary>
internal static class GameFonts
{
    public const string Marker = "Permanent Marker";
    public const string MontserratExtraBold = "Montserrat ExtraBold";
    public const string MontserratSemiBold = "Montserrat SemiBold";

    private static readonly PrivateFontCollection Collection = new();

    // The collection needs the font bytes to stay in memory for as long as the app runs.
    private static readonly List<IntPtr> Pinned = new();
    private static bool _loaded;

    /// <summary>Call once at start-up, before any window is created.</summary>
    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;

        var assembly = Assembly.GetExecutingAssembly();
        foreach (var resource in new[] { "font.permanentmarker", "font.montserrat-extrabold", "font.montserrat-semibold" })
        {
            try
            {
                using var stream = assembly.GetManifestResourceStream(resource);
                if (stream is null) continue;

                var bytes = new byte[stream.Length];
                stream.ReadExactly(bytes);

                IntPtr memory = Marshal.AllocCoTaskMem(bytes.Length);
                Marshal.Copy(bytes, 0, memory, bytes.Length);
                Collection.AddMemoryFont(memory, bytes.Length);
                Pinned.Add(memory);
            }
            catch (Exception ex) when (ex is IOException or ExternalException or ArgumentException)
            {
                // A missing font only changes how the window looks.
            }
        }
    }

    /// <summary>True if the font was loaded from the exe, false if <see cref="Create"/> will use the fallback.</summary>
    public static bool IsLoaded(string family) => Collection.Families.Any(f => f.Name == family);

    /// <summary>A font in points. Uses the embedded one if it loaded, otherwise the named Windows fallback.</summary>
    public static Font Create(string family, string fallback, float sizeInPoints)
    {
        var loaded = Collection.Families.FirstOrDefault(f => f.Name == family);
        return loaded is not null && loaded.IsStyleAvailable(FontStyle.Regular)
            ? new Font(loaded, sizeInPoints, FontStyle.Regular, GraphicsUnit.Point)
            : new Font(fallback, sizeInPoints, FontStyle.Regular, GraphicsUnit.Point);
    }
}
