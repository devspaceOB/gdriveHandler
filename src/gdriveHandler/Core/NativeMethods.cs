using System.Runtime.InteropServices;

namespace GdriveHandler;

/// <summary>
/// Win32 P/Invoke. Using classic <see cref="DllImportAttribute"/> (no unsafe /
/// no source generator) keeps the self-contained, trimmed build simple.
/// </summary>
internal static class NativeMethods
{
    // ----- MessageBox -----
    public const uint MB_OK = 0x0;
    public const uint MB_YESNO = 0x4;
    public const uint MB_ICONERROR = 0x10;
    public const uint MB_ICONINFORMATION = 0x40;
    public const int IDOK = 1;
    public const int IDYES = 6;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    // ----- Shell association change notification -----
    public const int SHCNE_ASSOCCHANGED = 0x08000000;
    public const uint SHCNF_IDLIST = 0x0;

    [DllImport("shell32.dll")]
    public static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    // ----- Console attach (so --diagnose/--help can print from a WinExe) -----
    public const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FreeConsole();

    // ----- Themed TaskDialog (comctl32 v6, provided via app.manifest) -----
    public const int TDCBF_OK_BUTTON = 0x0001;
    public const int TDCBF_YES_BUTTON = 0x0002;
    public const int TDCBF_NO_BUTTON = 0x0004;

    // Predefined icons are passed as MAKEINTRESOURCE (negative) values.
    public static readonly IntPtr TD_INFORMATION_ICON = (IntPtr)(-3);
    public static readonly IntPtr TD_SHIELD_ICON = (IntPtr)(-4);

    [DllImport("comctl32.dll", CharSet = CharSet.Unicode, EntryPoint = "TaskDialog")]
    public static extern int TaskDialog(
        IntPtr hWndParent,
        IntPtr hInstance,
        string pszWindowTitle,
        string pszMainInstruction,
        string pszContent,
        int dwCommonButtons,
        IntPtr pszIcon,
        out int pnButton);
}
