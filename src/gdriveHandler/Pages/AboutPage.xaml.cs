using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GdriveHandler.Pages;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        VersionText.Text = $"Version {AppConstants.Version}";
        PopulatePaths();
    }

    private void PopulatePaths()
    {
        PathsPanel.Children.Clear();
        AddPathRow(Loc.Get("AdvPathInstallDir"), AppConstants.InstallDir);
        AddPathRow(Loc.Get("AdvPathDataDir"), AppConstants.DataDir);
        AddPathRow(Loc.Get("AdvPathConfigFile"), AppConstants.ConfigFile);
        AddPathRow(Loc.Get("AdvPathLogFile"), AppConstants.LogFile);
        AddPathRow(Loc.Get("AdvPathSystemDir"), AppConstants.SystemInstallDir);
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
}
