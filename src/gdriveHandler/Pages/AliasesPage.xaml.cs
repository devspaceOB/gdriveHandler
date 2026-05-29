using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace GdriveHandler.Pages;

public sealed class AliasRow : INotifyPropertyChanged
{
    private string _from = "";
    private string _to = "";

    public string From
    {
        get => _from;
        set { _from = value; OnPropertyChanged(); }
    }

    public string To
    {
        get => _to;
        set { _to = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed partial class AliasesPage : Page
{
    private readonly ObservableCollection<AliasRow> _rows = new();

    public AliasesPage()
    {
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _rows.Clear();
        var settings = Settings.Load();
        foreach (var kv in settings.Aliases.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            _rows.Add(new AliasRow { From = kv.Key, To = kv.Value });
        }
        AliasItems.ItemsSource = _rows;
    }

    private void BtnAddRow_Click(object sender, RoutedEventArgs e)
    {
        _rows.Add(new AliasRow());
    }

    private void BtnRemoveRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is AliasRow row)
        {
            _rows.Remove(row);
        }
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var s = Settings.Load();
        s.Aliases.Clear();
        foreach (var r in _rows)
        {
            if (!string.IsNullOrWhiteSpace(r.From) && !string.IsNullOrWhiteSpace(r.To))
            {
                s.Aliases[r.From.Trim()] = r.To.Trim();
            }
        }
        Settings.Save(s);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Loc.Get("AliasesSaveSuccessTitle"),
            Content = Loc.Get("AliasesSaveSuccess"),
            CloseButtonText = Loc.Get("DialogOK"),
        };
        await dialog.ShowAsync();
    }

    private async void BtnDetectProfiles_Click(object sender, RoutedEventArgs e)
    {
        var log = new Logger();
        var candidates = BrowserDiscovery.Discover(log);
        var ids = ProfileMatcher.EnumerateProfiles(candidates, log);

        Microsoft.UI.Xaml.UIElement content;

        if (ids.Count == 0)
        {
            content = new TextBlock
            {
                Text = Loc.Get("AliasesDetectNone"),
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            };
        }
        else
        {
            var profileList = BuildProfileList(ids);
            content = new ScrollViewer
            {
                Content = profileList,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MinHeight = 200,
            };
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Loc.Get("AliasesDetectDialogTitle"),
            Content = content,
            CloseButtonText = Loc.Get("DialogClose"),
            MinWidth = 720,
            MinHeight = 480,
        };

        await dialog.ShowAsync();
    }

    private static StackPanel BuildProfileList(IReadOnlyList<ProfileIdentity> ids)
    {
        var list = new StackPanel { Spacing = 4 };

        foreach (var id in ids.OrderBy(i => i.Browser.Channel).ThenBy(i => i.ProfileDir))
        {
            var card = BuildProfileCard(id);
            list.Children.Add(card);
        }

        return list;
    }

    private static Border BuildProfileCard(ProfileIdentity id)
    {
        var card = new Border
        {
            CornerRadius = new Microsoft.UI.Xaml.CornerRadius(6),
            Padding = new Microsoft.UI.Xaml.Thickness(12, 8, 12, 8),
            Margin = new Microsoft.UI.Xaml.Thickness(0, 2, 0, 2),
        };

        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new Microsoft.UI.Xaml.GridLength(1, Microsoft.UI.Xaml.GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = Microsoft.UI.Xaml.GridLength.Auto });

        // Left: browser + profile dir
        var infoStack = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
        };
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(infoStack, 0);

        var browserLabel = new TextBlock
        {
            Text = id.Browser.Channel,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        };

        var dirLabel = new TextBlock
        {
            Text = id.ProfileDir,
            Foreground = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorSecondaryBrush"],
        };

        infoStack.Children.Add(browserLabel);
        infoStack.Children.Add(dirLabel);

        // Right: email + copy button
        var emailStack = new StackPanel
        {
            Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
        };
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(emailStack, 1);

        var email = id.UserName ?? "(not signed in)";
        var emailBlock = new TextBlock
        {
            Text = email,
            IsTextSelectionEnabled = true,
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
        };

        emailStack.Children.Add(emailBlock);

        if (!string.IsNullOrEmpty(id.UserName))
        {
            var copyBtn = new Button
            {
                Content = Loc.Get("AliasesCopyEmail"),
                Padding = new Microsoft.UI.Xaml.Thickness(8, 4, 8, 4),
            };
            var emailCopy = id.UserName; // capture for closure
            copyBtn.Click += (_, _) =>
            {
                var pkg = new DataPackage();
                pkg.SetText(emailCopy);
                Clipboard.SetContent(pkg);
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(copyBtn, "BtnCopyEmail");
            emailStack.Children.Add(copyBtn);
        }

        grid.Children.Add(infoStack);
        grid.Children.Add(emailStack);
        card.Child = grid;

        return card;
    }
}
