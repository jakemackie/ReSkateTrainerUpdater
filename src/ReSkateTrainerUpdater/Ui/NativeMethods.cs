using System.Runtime.InteropServices;

namespace ReSkateTrainerUpdater.Ui;

/// <summary>The few Windows calls WinForms has no wrapper for. All of them are purely cosmetic.</summary>
internal static class NativeMethods
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwcpRound = 2;

    private const int WmNcLButtonDown = 0xA1;
    private const int HtCaption = 2;

    /// <summary>Windows 11 (build 22000) and later round the corners of top-level windows.</summary>
    public static bool HasRoundedCorners => Environment.OSVersion.Version.Build >= 22000;

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left, Right, Top, Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string? pszSubIdList);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Gives a window with no title bar what a normal one has from Windows: rounded corners (Windows 11), a thin border in
    /// the given colour, and a soft shadow. On older Windows the corners stay square and the rest is ignored.
    /// </summary>
    public static void ApplyWindowFrame(IntPtr handle, Color border)
    {
        try
        {
            int round = DwmwcpRound;
            DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref round, sizeof(int));

            int colour = border.R | (border.G << 8) | (border.B << 16); // a COLORREF is 0x00BBGGRR
            DwmSetWindowAttribute(handle, DwmwaBorderColor, ref colour, sizeof(int));

            // A one-pixel frame is what makes Windows draw the shadow around a borderless window.
            var margins = new Margins { Left = 1, Right = 1, Top = 1, Bottom = 1 };
            DwmExtendFrameIntoClientArea(handle, ref margins);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    /// <summary>Starts dragging the window, as if the mouse had grabbed its title bar. Call from a left mouse-down.</summary>
    public static void DragWindow(IntPtr handle)
    {
        ReleaseCapture();
        SendMessage(handle, WmNcLButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
    }

    /// <summary>Gives a native control, such as a text box, dark scrollbars.</summary>
    public static void UseDarkScrollbars(IntPtr handle)
    {
        try { SetWindowTheme(handle, "DarkMode_Explorer", null); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }
}
