using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using ReSkateTrainerUpdater.Core;
using ReSkateTrainerUpdater.Ui;
using ReSkateTrainerUpdater.Ui.Controls;

namespace ReSkateTrainerUpdater;

/// <summary>
/// <c>ReSkateTrainerUpdater.exe --self-test &lt;report-file&gt;</c>: builds the window out of sight, draws it, and uses the
/// parts of the framework that a smaller build is most likely to have lost (every referenced assembly, drag and drop,
/// accessibility, JSON, signature checks, the embedded fonts and pictures). It makes no network request, touches no game
/// folder, and never writes the user's settings. The exit code is 0 only if every step worked.
///
/// The release exe leaves out framework assemblies nothing uses (the csproj's UnusedFrameworkAssembly list) to keep the
/// self-update small, and a broken build would reach people through the app's own self-update, which would then be unable
/// to fix itself. So the build script runs this on the exe it just made and refuses to continue if it fails.
/// </summary>
internal static class SelfTest
{
    public const string Flag = "--self-test";

    public static int Run(string? reportPath)
    {
        var report = new List<string>();
        int failures = 0;

        void Step(string name, Func<string?> body)
        {
            try
            {
                var detail = body();
                report.Add("ok    " + name + (detail is null ? "" : " - " + detail));
            }
            catch (Exception ex)
            {
                failures++;
                report.Add($"FAIL  {name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        var scratch = Path.Combine(Path.GetTempPath(), "ReSkateTrainerUpdater_selftest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);

        Step("every assembly the app references is in the exe", () =>
        {
            // The release leaves some framework assemblies out. If anything the app or the framework code it uses refers
            // to one of them, loading it fails here instead of in front of a user.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Queue<System.Reflection.Assembly>();
            pending.Enqueue(typeof(SelfTest).Assembly);
            while (pending.Count > 0)
            {
                foreach (var name in pending.Dequeue().GetReferencedAssemblies())
                {
                    if (name.Name is null || !seen.Add(name.Name)) continue;
                    try { pending.Enqueue(System.Reflection.Assembly.Load(name)); }
                    catch (FileNotFoundException) { throw new InvalidOperationException(name.Name + " is referenced but missing from the exe"); }
                }
            }
            return $"{seen.Count} assemblies";
        });

        Step("the embedded fonts load", () =>
        {
            foreach (var family in new[] { GameFonts.Marker, GameFonts.MontserratExtraBold, GameFonts.MontserratSemiBold })
                if (!GameFonts.IsLoaded(family)) throw new InvalidOperationException(family + " did not load from the exe");
            return "3 families";
        });

        Step("the exe's own version information reads", () =>
        {
            var info = FileVersionInfo.GetVersionInfo(Environment.ProcessPath!);
            if (info.ProductName != AppInfo.Name) throw new InvalidOperationException($"product name is \"{info.ProductName}\"");
            return info.ProductVersion;
        });

        Step("settings save and load as JSON", () =>
        {
            var path = Path.Combine(scratch, "settings.json");
            var settings = UpdaterSettings.Load(path);
            settings.LastFolder = scratch;
            settings.BackupReplacedFiles = false;
            settings.WindowLeft = 12;
            settings.Save();
            var back = UpdaterSettings.Load(path);
            if (back.LastFolder != scratch || back.BackupReplacedFiles || back.WindowLeft != 12)
                throw new InvalidOperationException("the saved values did not come back");
            return null;
        });

        Step("a signed manifest verifies and is read", () =>
        {
            var (privateKey, publicKey) = ManifestSignature.GenerateKeyPair();
            using var key = ManifestSignature.LoadPrivateKey(privateKey);
            var entry = new ManifestEntry("tournament", "0.9.2", "578f597", "2.1.1", OnlineSource.ReleaseAssetUrl("trainer-tournament-0.9.2", "t.zip"), 10, new string('a', 64));
            var signed = ManifestSignature.Sign(new TrainerManifest().With(entry).ToJson(), key);
            if (!ManifestSignature.Verify(signed, new[] { publicKey }).IsValid) throw new InvalidOperationException("signature did not verify");
            if (TrainerManifest.Parse(signed).Manifest?.Find("tournament") != entry) throw new InvalidOperationException("manifest did not read back");
            return null;
        });

        Step("the HTTP client and a SHA-256 work", () =>
        {
            using var client = TrainerDownloader.CreateClient();
            if (!client.DefaultRequestHeaders.UserAgent.ToString().StartsWith("ReSkateTrainerUpdater/", StringComparison.Ordinal))
                throw new InvalidOperationException("no User-Agent");
            return Convert.ToHexString(SHA256.HashData(new byte[] { 1 }))[..8];
        });

        Step("a dropped file's data object reads back", () =>
        {
            var data = new DataObject(DataFormats.FileDrop, new[] { Path.Combine(scratch, "x.zip") });
            var files = data.GetData(DataFormats.FileDrop) as string[];
            if (files is not { Length: 1 } || !files[0].EndsWith("x.zip", StringComparison.Ordinal)) throw new InvalidOperationException("file list lost");

            // The same marshalling Windows uses when a file is dragged onto the window: the file list as a native HDROP.
            var native = (System.Runtime.InteropServices.ComTypes.IDataObject)data;
            var format = new System.Runtime.InteropServices.ComTypes.FORMATETC
            {
                cfFormat = 15, // CF_HDROP
                dwAspect = System.Runtime.InteropServices.ComTypes.DVASPECT.DVASPECT_CONTENT,
                lindex = -1,
                tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL,
            };
            native.GetData(ref format, out var medium);
            if (medium.unionmember == IntPtr.Zero) throw new InvalidOperationException("no native file list was produced");
            return null;
        });

        Step("the file and folder dialogs can be created", () =>
        {
            using var file = new OpenFileDialog { Filter = "Zip|*.zip" };
            using var folder = new FolderBrowserDialog();
            return null;
        });

        Step("the window is built, drawn and accessible", () =>
        {
            // Out of sight: fully transparent, off the screen, not in the taskbar. QuietStartup keeps it from checking GitHub.
            using var form = new UpdaterForm(quietStartup: true)
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000),
                ShowInTaskbar = false,
                Opacity = 0,
            };
            form.Show();
            Application.DoEvents();

            using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height, PixelFormat.Format32bppArgb);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.ClientSize));

            var colours = new HashSet<int>();
            for (int y = 0; y < bitmap.Height; y += Math.Max(1, bitmap.Height / 24))
                for (int x = 0; x < bitmap.Width; x += Math.Max(1, bitmap.Width / 40))
                    colours.Add(bitmap.GetPixel(x, y).ToArgb());
            if (colours.Count < 12) throw new InvalidOperationException($"the window drew only {colours.Count} different colours, so it looks blank");

            int controls = 0, named = 0;
            void Walk(Control parent)
            {
                foreach (Control child in parent.Controls)
                {
                    controls++;
                    var accessible = child.AccessibilityObject;
                    if (!string.IsNullOrEmpty(accessible.Name) && accessible.Role != AccessibleRole.None) named++;
                    Walk(child);
                }
            }
            Walk(form);

            // A file dragged from Explorer reaches the window as the shell's own data object, through the drop target
            // WinForms registered with Windows. Make that same kind of data object for a real zip, call the drop target
            // the way Explorer does, and expect the zip tile to show the file.
            var zipPath = Path.Combine(scratch, "dropped.zip");
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create)) zip.CreateEntry("empty.txt").Open().Dispose();
            DropLikeExplorer(form.Handle, zipPath, new Point(form.Width / 2, form.Height / 2));
            Application.DoEvents();
            if (!FindControls<InfoTile>(form).Any(tile => tile.Subtitle == Path.GetFileName(zipPath)))
                throw new InvalidOperationException("the window accepted the drop but the zip tile doesn't show the file");
            if (controls < 8) throw new InvalidOperationException($"only {controls} controls were created");
            if (named == 0) throw new InvalidOperationException("no control has an accessible name");

            form.Dispose();
            return $"{controls} controls, {colours.Count} colours drawn, {named} with accessible names";
        });

        try { Directory.Delete(scratch, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }

        report.Add(failures == 0 ? "All steps passed." : $"{failures} step(s) FAILED.");
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            try { File.WriteAllLines(reportPath, report); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return failures == 0 ? 0 : 1;
    }

    private static IEnumerable<T> FindControls<T>(Control parent) where T : Control
    {
        foreach (Control child in parent.Controls)
        {
            if (child is T match) yield return match;
            foreach (var inner in FindControls<T>(child)) yield return inner;
        }
    }

    // Drags a file onto the window and drops it, as Explorer does: a shell data object for the file, handed to the drop
    // target WinForms registered for the window, called through its vtable (IDropTarget: DragEnter is slot 3, DragOver 4,
    // Drop 6). No managed COM interface stands in between. An earlier version declared one, and trimming removed its unused
    // methods, which moved Drop into DragOver's slot and crashed the exe.
    private static unsafe void DropLikeExplorer(IntPtr window, string path, Point point)
    {
        var target = GetProp(window, "OleDropTargetInterface");
        if (target == IntPtr.Zero) throw new InvalidOperationException("the window has no registered drop target");

        var data = CreateShellDataObject(path);
        try
        {
            var vtable = *(IntPtr**)target;
            var dragEnter = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, Point, int*, int>)vtable[3];
            var dragOver = (delegate* unmanaged[Stdcall]<IntPtr, int, Point, int*, int>)vtable[4];
            var drop = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, Point, int*, int>)vtable[6];

            int effect = DropEffectCopy;
            int hr = dragEnter(target, data, 0, point, &effect);
            if (hr != 0 || effect != DropEffectCopy) throw new InvalidOperationException($"the window did not accept a dragged zip (HRESULT 0x{hr:X8}, effect {effect})");
            effect = DropEffectCopy;
            hr = dragOver(target, 0, point, &effect);
            if (hr != 0 || effect != DropEffectCopy) throw new InvalidOperationException($"the window stopped accepting the zip while it moved (HRESULT 0x{hr:X8}, effect {effect})");
            effect = DropEffectCopy;
            hr = drop(target, data, 0, point, &effect);
            if (hr != 0) throw new InvalidOperationException($"dropping a zip on the window failed (HRESULT 0x{hr:X8})");
        }
        finally
        {
            Marshal.Release(data);
        }
    }

    // The data object Explorer makes for a file: the shell's own, which offers the file as a native HDROP.
    private static IntPtr CreateShellDataObject(string path)
    {
        int hr = SHParseDisplayName(path, IntPtr.Zero, out var item, 0, out _);
        if (hr != 0) throw new InvalidOperationException($"the shell couldn't find the test zip (HRESULT 0x{hr:X8})");
        var folder = IntPtr.Zero;
        try
        {
            folder = ILClone(item);
            ILRemoveLastID(folder);
            var iid = IidDataObject;
            hr = SHCreateDataObject(folder, 1, new[] { ILFindLastID(item) }, IntPtr.Zero, ref iid, out var data);
            if (hr != 0 || data == IntPtr.Zero) throw new InvalidOperationException($"the shell couldn't make a data object (HRESULT 0x{hr:X8})");
            return data;
        }
        finally
        {
            Marshal.FreeCoTaskMem(folder);
            Marshal.FreeCoTaskMem(item);
        }
    }

    private const int DropEffectCopy = 1;
    private static readonly Guid IidDataObject = new("0000010e-0000-0000-C000-000000000046");

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetProp(IntPtr window, string name);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindContext, out IntPtr pidl, uint attributesIn, out uint attributesOut);

    [DllImport("shell32.dll")]
    private static extern int SHCreateDataObject(IntPtr folder, uint count, IntPtr[] children, IntPtr inner, ref Guid iid, out IntPtr dataObject);

    [DllImport("shell32.dll")]
    private static extern IntPtr ILClone(IntPtr pidl);

    [DllImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ILRemoveLastID(IntPtr pidl);

    [DllImport("shell32.dll")]
    private static extern IntPtr ILFindLastID(IntPtr pidl);
}
