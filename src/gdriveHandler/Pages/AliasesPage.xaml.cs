using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Controls;

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
            Title = "Success",
            Content = "Aliases saved.",
            CloseButtonText = "OK",
        };
        await dialog.ShowAsync();
    }

    private async void BtnDetectProfiles_Click(object sender, RoutedEventArgs e)
    {
        var log = new Logger();
        var candidates = BrowserDiscovery.Discover(log);
        var ids = ProfileMatcher.EnumerateProfiles(candidates, log);

        string text;
        if (ids.Count == 0)
        {
            text = "No signed-in profiles found.";
        }
        else
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"{"Channel",-20} {"Profile",-12} {"Email",-35} GaiaId");
            sb.AppendLine(new string('-', 90));
            foreach (var id in ids)
            {
                sb.AppendLine(
                    $"{id.Browser.Channel,-20} {id.ProfileDir,-12} " +
                    $"{id.UserName ?? "(not signed in)",-35} {id.GaiaId ?? ""}");
            }
            text = sb.ToString();
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Detected profiles",
            Content = new ScrollViewer
            {
                MaxHeight = 360,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new TextBox
                {
                    Text = text,
                    IsReadOnly = true,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                    FontSize = 12,
                    AcceptsReturn = true,
                    TextWrapping = Microsoft.UI.Xaml.TextWrapping.NoWrap,
                    BorderThickness = new Microsoft.UI.Xaml.Thickness(0),
                    Background = null,
                    MinWidth = 620,
                },
            },
            CloseButtonText = "Close",
        };
        await dialog.ShowAsync();
    }
}
