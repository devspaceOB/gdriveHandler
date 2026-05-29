using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GdriveHandler.Pages;

public sealed partial class HomePage : Page
{
    public HomePage()
    {
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshStatus();
        PopulatePaths();
    }

    private void RefreshStatus()
    {
        var installed = File.Exists(AppConstants.InstalledExePath);
        var systemInstalled = File.Exists(AppConstants.SystemExePath);

        if (installed)
        {
            StatusTitle.Text = "Installed (per-user)";
            StatusDetail.Text = AppConstants.InstalledExePath;
        }
        else if (systemInstalled)
        {
            StatusTitle.Text = "Installed (system-wide)";
            StatusDetail.Text = AppConstants.SystemExePath;
        }
        else
        {
            StatusTitle.Text = "Not installed";
            StatusDetail.Text = "Use the buttons below to install gdriveHandler as the handler for Google Workspace files.";
        }
    }

    private void PopulatePaths()
    {
        PathsPanel.Children.Clear();
        AddPathRow("Install dir", AppConstants.InstallDir);
        AddPathRow("Config file", AppConstants.ConfigFile);
        AddPathRow("Log file", AppConstants.LogFile);
    }

    private void AddPathRow(string label, string path)
    {
        var panel = new StackPanel { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 8 };

        var labelBlock = new TextBlock
        {
            Text = label + ":",
            Width = 80,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        };

        var valueBlock = new TextBlock
        {
            Text = path,
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };

        panel.Children.Add(labelBlock);
        panel.Children.Add(valueBlock);
        PathsPanel.Children.Add(panel);
    }

    private async void BtnInstallUser_Click(object sender, RoutedEventArgs e)
    {
        var log = new Logger();
        var code = Installer.Install(log, systemWide: false);
        await ShowResultAsync(
            code == ExitCode.Success
                ? "Installed successfully.\n\nIf Windows keeps another app as the default for these files, " +
                  "set gdriveHandler once via Settings → Apps → Default apps."
                : "Installation failed. Check the log for details.",
            code == ExitCode.Success);
        RefreshStatus();
    }

    private async void BtnInstallSystem_Click(object sender, RoutedEventArgs e)
    {
        var log = new Logger();
        var code = Installer.Install(log, systemWide: true);
        await ShowResultAsync(
            code == ExitCode.Success
                ? "Installed for all users successfully."
                : "System installation failed. Check the log for details.",
            code == ExitCode.Success);
        RefreshStatus();
    }

    private async void BtnRepair_Click(object sender, RoutedEventArgs e)
    {
        var log = new Logger();
        var code = Installer.Repair(log);
        await ShowResultAsync(
            code == ExitCode.Success ? "Repair complete." : "Repair failed. Check the log for details.",
            code == ExitCode.Success);
        RefreshStatus();
    }

    private async void BtnUninstall_Click(object sender, RoutedEventArgs e)
    {
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Uninstall gdriveHandler",
            Content = "This will remove gdriveHandler file associations and uninstall entries.\n\n" +
                      "Your aliases and settings in config.ini will be kept.\n\nProceed?",
            PrimaryButtonText = "Uninstall",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        var result = await confirm.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        var log = new Logger();
        var code = Installer.Uninstall(log);
        await ShowResultAsync(
            code == ExitCode.Success ? "Uninstalled successfully." : "Uninstall failed. Check the log for details.",
            code == ExitCode.Success);
        RefreshStatus();
    }

    private async void BtnOpenConfig_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = AppConstants.ConfigFile;
            if (!File.Exists(path))
            {
                // Create a default file so the user has something to open.
                Settings.Save(new Settings());
            }

            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            await ShowResultAsync("Could not open config.ini: " + ex.Message, success: false);
        }
    }

    private async void BtnOpenLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppConstants.LogDir);
            Process.Start(new ProcessStartInfo { FileName = AppConstants.LogDir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            await ShowResultAsync("Could not open logs folder: " + ex.Message, success: false);
        }
    }

    private async void BtnDiagnose_Click(object sender, RoutedEventArgs e)
    {
        var log = new Logger();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{AppConstants.DisplayName} {AppConstants.Version}");
        sb.AppendLine($"Install dir : {AppConstants.InstallDir}");
        sb.AppendLine($"Config file : {AppConstants.ConfigFile}  (exists: {File.Exists(AppConstants.ConfigFile)})");
        sb.AppendLine($"Log file    : {AppConstants.LogFile}");
        sb.AppendLine();

        var candidates = BrowserDiscovery.Discover(log);
        sb.AppendLine($"Browsers found: {candidates.Count}");
        foreach (var c in candidates.OrderBy(c => c.Priority))
        {
            sb.AppendLine($"  [{c.Priority,3}] {c.Channel,-20} {c.ExePath}");
        }
        sb.AppendLine();
        sb.AppendLine("Profiles:");
        foreach (var id in ProfileMatcher.EnumerateProfiles(candidates, log))
        {
            sb.AppendLine($"  {id.Browser.Channel,-20} {id.ProfileDir,-12} {id.UserName ?? "(not signed in)"}");
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Diagnostics",
            Content = new ScrollViewer
            {
                MaxHeight = 400,
                Content = new TextBox
                {
                    Text = sb.ToString(),
                    IsReadOnly = true,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                    FontSize = 12,
                    AcceptsReturn = true,
                    TextWrapping = Microsoft.UI.Xaml.TextWrapping.NoWrap,
                    BorderThickness = new Microsoft.UI.Xaml.Thickness(0),
                    Background = null,
                    MinWidth = 560,
                },
            },
            CloseButtonText = "Close",
        };

        await dialog.ShowAsync();
    }

    private async System.Threading.Tasks.Task ShowResultAsync(string message, bool success)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = success ? "Success" : "Error",
            Content = message,
            CloseButtonText = "OK",
        };
        await dialog.ShowAsync();
    }
}
