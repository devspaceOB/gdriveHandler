namespace GdriveHandler;

/// <summary>
/// Tiny native UI layer. Prefers a themed TaskDialog (Win11 look) and falls
/// back to a classic MessageBox if comctl32 v6 is unavailable.
/// </summary>
internal static class Ui
{
    public static void ShowError(string message)
    {
        var full = message + Environment.NewLine + Environment.NewLine + "Log file:" + Environment.NewLine + AppConstants.LogFile;
        try
        {
            NativeMethods.MessageBoxW(IntPtr.Zero, full, AppConstants.DisplayName, NativeMethods.MB_OK | NativeMethods.MB_ICONERROR);
        }
        catch
        {
            // No UI available; nothing more we can do.
        }
    }

    public static void ShowInfo(string title, string message)
    {
        if (TryTaskDialog(title, title, message, NativeMethods.TDCBF_OK_BUTTON, NativeMethods.TD_INFORMATION_ICON, out _))
        {
            return;
        }

        try
        {
            NativeMethods.MessageBoxW(IntPtr.Zero, message, title, NativeMethods.MB_OK | NativeMethods.MB_ICONINFORMATION);
        }
        catch
        {
            // Ignore: informational only.
        }
    }

    /// <summary>Asks the user whether to install for the current user.</summary>
    public static bool ConfirmInstall()
    {
        const string title = "gdriveHandler";
        const string instruction = "Set gdriveHandler as the handler for Google Workspace shortcuts?";
        const string content =
            "Installs for the current user only (no admin needed) and registers .gdoc, " +
            ".gsheet, .gslides and related files.\n\n" +
            "Opening one of those shortcuts will launch it in the Chrome/Edge profile signed " +
            "in with the account stored inside the file.";

        if (TryTaskDialog(title, instruction, content,
                NativeMethods.TDCBF_YES_BUTTON | NativeMethods.TDCBF_NO_BUTTON,
                NativeMethods.TD_SHIELD_ICON, out int button))
        {
            return button == NativeMethods.IDYES;
        }

        try
        {
            int result = NativeMethods.MessageBoxW(IntPtr.Zero, instruction + "\n\n" + content, title,
                NativeMethods.MB_YESNO | NativeMethods.MB_ICONINFORMATION);
            return result == NativeMethods.IDYES;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryTaskDialog(string title, string instruction, string content, int buttons, IntPtr icon, out int button)
    {
        button = 0;
        try
        {
            int hr = NativeMethods.TaskDialog(IntPtr.Zero, IntPtr.Zero, title, instruction, content, buttons, icon, out button);
            return hr == 0; // S_OK
        }
        catch
        {
            return false;
        }
    }
}
