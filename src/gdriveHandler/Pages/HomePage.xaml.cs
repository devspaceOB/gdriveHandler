using Microsoft.UI.Xaml.Controls;

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
    }

    private void RefreshStatus()
    {
        if (AppConstants.IsPackaged)
        {
            StatusTitle.Text = Loc.Get("HomeStatusManaged");
            StatusDetail.Text = Loc.Get("HomeStatusManagedDetail");
            BtnSetup.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            return;
        }

        var installed = File.Exists(AppConstants.InstalledExePath);
        var systemInstalled = File.Exists(AppConstants.SystemExePath);

        if (installed)
        {
            StatusTitle.Text = Loc.Get("HomeStatusInstalledUser");
            StatusDetail.Text = AppConstants.InstalledExePath;
            BtnSetup.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        }
        else if (systemInstalled)
        {
            StatusTitle.Text = Loc.Get("HomeStatusInstalledSystem");
            StatusDetail.Text = AppConstants.SystemExePath;
            BtnSetup.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        }
        else
        {
            StatusTitle.Text = Loc.Get("HomeStatusNotInstalled");
            StatusDetail.Text = Loc.Get("HomeStatusNotInstalledDetail");
            BtnSetup.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        }
    }

    private async void BtnSetup_Click(object sender, RoutedEventArgs e)
    {
        var log = new Logger();
        var code = Installer.Install(log, systemWide: false);
        await ShowResultAsync(
            code == ExitCode.Success
                ? Loc.Get("HomeInstallSuccess")
                : Loc.Get("HomeInstallFailed"),
            code == ExitCode.Success);
        RefreshStatus();
    }

    private void LinkGuide_Click(object sender, RoutedEventArgs e)
    {
        // Navigate the parent frame to the Guide page.
        if (Frame != null)
        {
            Frame.Navigate(typeof(GuidePage));
        }
    }

    private void LinkAliases_Click(object sender, RoutedEventArgs e)
    {
        if (Frame != null)
        {
            Frame.Navigate(typeof(AliasesPage));
        }
    }

    private async System.Threading.Tasks.Task ShowResultAsync(string message, bool success)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = success ? Loc.Get("DialogSuccess") : Loc.Get("DialogError"),
            Content = message,
            CloseButtonText = Loc.Get("DialogOK"),
        };
        await dialog.ShowAsync();
    }
}
