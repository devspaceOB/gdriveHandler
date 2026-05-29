using Microsoft.UI.Xaml.Controls;

namespace GdriveHandler.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        var settings = Settings.Load();
        ToggleNewWindow.IsOn = settings.OpenInNewWindow;
        ToggleEdge.IsOn = settings.IncludeEdge;
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var s = Settings.Load();
        s.OpenInNewWindow = ToggleNewWindow.IsOn;
        s.IncludeEdge = ToggleEdge.IsOn;
        Settings.Save(s);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Success",
            Content = "Settings saved.",
            CloseButtonText = "OK",
        };
        await dialog.ShowAsync();
    }
}
