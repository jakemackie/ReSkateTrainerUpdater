using System.Runtime.InteropServices;

namespace ReSkateTrainerUpdater.Ui;

/// <summary>The few Windows calls WinForms has no wrapper for. All of them are purely cosmetic.</summary>
internal static class NativeMethods
{
    // DWMWA_USE_IMMERSIVE_DARK_MODE: 20 on Windows 10 2004 and later, 19 on the earlier builds that had it.
    private const int DarkModeAttribute = 20;
    private const int DarkModeAttributeLegacy = 19;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string? pszSubIdList);

    /// <summary>Makes the title bar dark on Windows 10 (1809 and later) and Windows 11.</summary>
    public static void UseDarkTitleBar(IntPtr handle)
    {
        try
        {
            int on = 1;
            if (DwmSetWindowAttribute(handle, DarkModeAttribute, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, DarkModeAttributeLegacy, ref on, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    /// <summary>Gives a native control, such as a text box, dark scrollbars.</summary>
    public static void UseDarkScrollbars(IntPtr handle)
    {
        try { SetWindowTheme(handle, "DarkMode_Explorer", null); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }
}
