using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace GdriveHandler.Pages;

public sealed partial class SettingsPage : Page
{
    private const int LogMaxLines = 200;

    // Guard that prevents auto-save firing while we're loading values into controls.
    private bool _loading = true;

    // Track the language that was in effect when the page loaded so we can
    // detect a real user change (and not re-trigger on programmatic init).
    private string _loadedLanguage = "en";

    public SettingsPage()
    {
        InitializeComponent();
    }

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _loading = true;
        try
        {
            var s = Settings.Load();

            ToggleNewWindow.IsOn = s.OpenInNewWindow;
            ToggleEdge.IsOn = s.IncludeEdge;
            ToggleAdvanced.IsOn = s.AdvancedMode;

            // Language ComboBox — language endonyms stay as-is in any locale.
            _loadedLanguage = s.Language ?? "en";
            LanguageCombo.SelectedIndex = _loadedLanguage == "tr" ? 1 : 0;

            // Localize the SelectorBar items from resource strings (SelectorBarItem
            // has no x:Uid support, so we set Text from code-behind).
            SubtabGeneral.Text = Loc.Get("SubtabGeneral");
            SubtabAdvanced.Text = Loc.Get("SubtabAdvanced");
            SubtabLogs.Text = Loc.Get("SubtabLogs");

            ApplyAdvancedVisibility(s.AdvancedMode);
            PopulatePaths();

            // Start on General tab
            SubtabBar.SelectedItem = SubtabBar.Items[0];
            ShowPanel("general");
        }
        finally
        {
            _loading = false;
        }
    }

    // ------------------------------------------------------------------
    // Subtab navigation
    // ------------------------------------------------------------------

    private void SubtabBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem is SelectorBarItem item && item.Tag is string tag)
        {
            ShowPanel(tag);
            if (tag == "logs")
            {
                LoadLog();
            }
        }
    }

    private void ShowPanel(string tag)
    {
        PanelGeneral.Visibility = tag == "general"
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
        PanelAdvanced.Visibility = tag == "advanced"
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
        PanelLogs.Visibility = tag == "logs"
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    // ------------------------------------------------------------------
    // Advanced mode gating
    // ------------------------------------------------------------------

    private void ApplyAdvancedVisibility(bool advanced)
    {
        var vis = advanced
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        SubtabAdvanced.Visibility = vis;
        SubtabLogs.Visibility = vis;

        // If advanced turns off while an advanced tab is selected, revert to General.
        if (!advanced && SubtabBar.SelectedItem is SelectorBarItem item &&
            item.Tag is string tag && (tag == "advanced" || tag == "logs"))
        {
            SubtabBar.SelectedItem = SubtabBar.Items[0];
            ShowPanel("general");
        }
    }

    private void ToggleAdvanced_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ApplyAdvancedVisibility(ToggleAdvanced.IsOn);
        AutoSave();
    }

    // ------------------------------------------------------------------
    // Auto-save on setting change
    // ------------------------------------------------------------------

    private void SettingToggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        AutoSave();
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;

        // Determine the newly selected language code.
        string? newLang = null;
        if (LanguageCombo.SelectedItem is ComboBoxItem langItem &&
            langItem.Tag is string code)
        {
            newLang = code;
        }

        if (newLang == null) return;

        // Persist the new language.
        var s = Settings.Load();
        s.OpenInNewWindow = ToggleNewWindow.IsOn;
        s.IncludeEdge = ToggleEdge.IsOn;
        s.AdvancedMode = ToggleAdvanced.IsOn;
        s.Language = newLang;
        Settings.Save(s);

        // Only relaunch if the language actually changed from what was loaded.
        if (!string.Equals(newLang, _loadedLanguage, StringComparison.OrdinalIgnoreCase))
        {
            ApplyLanguageSwitch(newLang);
            return;
        }

        FlashSaved();
    }

    private void AutoSave()
    {
        if (_loading) return;

        var s = Settings.Load();
        s.OpenInNewWindow = ToggleNewWindow.IsOn;
        s.IncludeEdge = ToggleEdge.IsOn;
        s.AdvancedMode = ToggleAdvanced.IsOn;

        if (LanguageCombo.SelectedItem is ComboBoxItem langItem &&
            langItem.Tag is string langCode)
        {
            s.Language = langCode;
        }

        Settings.Save(s);
        FlashSaved();
    }

    /// <summary>
    /// Applies a language change in place — no process restart. The App recreates the
    /// window after setting PrimaryLanguageOverride, which re-resolves all x:Uid strings.
    /// </summary>
    private void ApplyLanguageSwitch(string newLang)
    {
        ((App)Application.Current).SwitchLanguage(newLang);
    }

    private void FlashSaved()
    {
        // Brief fade-in / fade-out of the "Saved" label.
        SavedConfirm.Opacity = 1.0;

        var storyboard = new Storyboard();
        var anim = new DoubleAnimation
        {
            To = 0.0,
            BeginTime = TimeSpan.FromSeconds(1.5),
            Duration = new Duration(TimeSpan.FromSeconds(0.5)),
        };
        Storyboard.SetTarget(anim, SavedConfirm);
        Storyboard.SetTargetProperty(anim, "Opacity");
        storyboard.Children.Add(anim);
        storyboard.Begin();
    }

    // ------------------------------------------------------------------
    // Paths panel
    // ------------------------------------------------------------------

    private void PopulatePaths()
    {
        AdvPathsPanel.Children.Clear();
        AddPathRow(Loc.Get("AdvPathInstallDir"), AppConstants.InstallDir);
        AddPathRow(Loc.Get("AdvPathConfigFile"), AppConstants.ConfigFile);
        AddPathRow(Loc.Get("AdvPathLogFile"), AppConstants.LogFile);
    }

    private void AddPathRow(string label, string path)
    {
        var grid = new Grid
        {
            ColumnSpacing = 12,
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new Microsoft.UI.Xaml.GridLength(96) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new Microsoft.UI.Xaml.GridLength(1, Microsoft.UI.Xaml.GridUnitType.Star) });

        var labelBlock = new TextBlock
        {
            Text = label,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Top,
        };

        var valueBlock = new TextBlock
        {
            Text = path,
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };

        Microsoft.UI.Xaml.Controls.Grid.SetColumn(labelBlock, 0);
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(valueBlock, 1);
        grid.Children.Add(labelBlock);
        grid.Children.Add(valueBlock);
        AdvPathsPanel.Children.Add(grid);
    }

    // ------------------------------------------------------------------
    // Advanced — Setup & Management
    // ------------------------------------------------------------------

    private async void AdvBtnInstallUser_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var log = new Logger();
        var code = Installer.Install(log, systemWide: false);
        await ShowResultAsync(
            code == ExitCode.Success
                ? Loc.Get("AdvInstallUserSuccess")
                : Loc.Get("AdvInstallUserFailed"),
            code == ExitCode.Success);
    }

    private async void AdvBtnInstallSystem_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var log = new Logger();
        var code = Installer.Install(log, systemWide: true);
        await ShowResultAsync(
            code == ExitCode.Success
                ? Loc.Get("AdvInstallSystemSuccess")
                : Loc.Get("AdvInstallSystemFailed"),
            code == ExitCode.Success);
    }

    private async void AdvBtnRepair_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var log = new Logger();
        var code = Installer.Repair(log);
        await ShowResultAsync(
            code == ExitCode.Success
                ? Loc.Get("AdvRepairSuccess")
                : Loc.Get("AdvRepairFailed"),
            code == ExitCode.Success);
    }

    private async void AdvBtnReinstall_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var log = new Logger();
        // Reinstall = uninstall then install-for-me.
        var unCode = Installer.Uninstall(log);
        if (unCode != ExitCode.Success)
        {
            await ShowResultAsync(Loc.Get("AdvReinstallFailed"), success: false);
            return;
        }
        var inCode = Installer.Install(log, systemWide: false);
        await ShowResultAsync(
            inCode == ExitCode.Success
                ? Loc.Get("AdvReinstallSuccess")
                : Loc.Get("AdvReinstallFailed"),
            inCode == ExitCode.Success);
    }

    private async void AdvBtnUninstall_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Loc.Get("AdvUninstallDialogTitle"),
            Content = Loc.Get("AdvUninstallDialogContent"),
            PrimaryButtonText = Loc.Get("AdvUninstallDialogConfirm"),
            CloseButtonText = Loc.Get("AdvUninstallDialogCancel"),
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
            code == ExitCode.Success
                ? Loc.Get("AdvUninstallSuccess")
                : Loc.Get("AdvUninstallFailed"),
            code == ExitCode.Success);
    }

    // ------------------------------------------------------------------
    // Advanced — Maintenance
    // ------------------------------------------------------------------

    private async void AdvBtnOpenConfig_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        try
        {
            var path = AppConstants.ConfigFile;
            if (!File.Exists(path))
            {
                Settings.Save(new Settings());
            }
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            await ShowResultAsync(Loc.Get("AdvOpenConfigFailed") + ex.Message, success: false);
        }
    }

    private async void AdvBtnOpenLogs_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppConstants.LogDir);
            Process.Start(new ProcessStartInfo { FileName = AppConstants.LogDir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            await ShowResultAsync(Loc.Get("AdvOpenLogsFailed") + ex.Message, success: false);
        }
    }

    private async void AdvBtnDiagnose_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var log = new Logger();
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"=== {AppConstants.DisplayName} {AppConstants.Version} ===");
        sb.AppendLine();
        sb.AppendLine("[Paths]");
        sb.AppendLine($"  Install dir : {AppConstants.InstallDir}");
        sb.AppendLine($"  Config file : {AppConstants.ConfigFile}  (exists: {File.Exists(AppConstants.ConfigFile)})");
        sb.AppendLine($"  Log file    : {AppConstants.LogFile}  (exists: {File.Exists(AppConstants.LogFile)})");
        sb.AppendLine();

        var candidates = BrowserDiscovery.Discover(log);
        sb.AppendLine($"[Browsers]  {candidates.Count} found");
        foreach (var c in candidates.OrderBy(c => c.Priority))
        {
            sb.AppendLine($"  [{c.Priority,3}] {c.Channel,-20} {c.ExePath}");
        }
        sb.AppendLine();
        sb.AppendLine("[Profiles]");
        var profiles = ProfileMatcher.EnumerateProfiles(candidates, log);
        foreach (var id in profiles)
        {
            sb.AppendLine($"  {id.Browser.Channel,-20} {id.ProfileDir,-12} {id.UserName ?? "(not signed in)"}");
        }
        if (profiles.Count == 0)
        {
            sb.AppendLine("  (none found)");
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Loc.Get("AdvDiagnosticsTitle"),
            Content = new ScrollViewer
            {
                MaxHeight = 480,
                MinHeight = 200,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new TextBox
                {
                    Text = sb.ToString(),
                    IsReadOnly = true,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    AcceptsReturn = true,
                    TextWrapping = Microsoft.UI.Xaml.TextWrapping.NoWrap,
                    BorderThickness = new Microsoft.UI.Xaml.Thickness(0),
                    Background = null,
                    MinWidth = 680,
                    Padding = new Microsoft.UI.Xaml.Thickness(4),
                },
            },
            CloseButtonText = Loc.Get("DialogClose"),
            MinWidth = 720,
        };

        await dialog.ShowAsync();
    }

    // ------------------------------------------------------------------
    // Log viewer
    // ------------------------------------------------------------------

    private void LoadLog()
    {
        var path = AppConstants.LogFile;
        if (!File.Exists(path))
        {
            LogText.Text = Loc.Get("LogsEmpty");
            return;
        }

        try
        {
            var allLines = File.ReadAllLines(path);
            var tail = allLines.Length <= LogMaxLines
                ? allLines
                : allLines[(allLines.Length - LogMaxLines)..];
            LogText.Text = string.Join(Environment.NewLine, tail);
        }
        catch (Exception ex)
        {
            LogText.Text = Loc.Get("LogsReadError") + ex.Message;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            LogScroller.ScrollToVerticalOffset(LogScroller.ExtentHeight);
        });
    }

    private void LogsBtnRefresh_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        LoadLog();
    }

    private void LogsBtnOpenNotepad_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var path = AppConstants.LogFile;
        if (!File.Exists(path)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "notepad.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = false,
            });
        }
        catch
        {
            // Best effort.
        }
    }

    private void LogsBtnOpenFolder_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppConstants.LogDir);
            Process.Start(new ProcessStartInfo { FileName = AppConstants.LogDir, UseShellExecute = true });
        }
        catch
        {
            // Best effort.
        }
    }

    private async void LogsBtnClear_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Loc.Get("LogsClearDialogTitle"),
            Content = Loc.Get("LogsClearDialogContent"),
            PrimaryButtonText = Loc.Get("LogsClearDialogConfirm"),
            CloseButtonText = Loc.Get("LogsClearDialogCancel"),
            DefaultButton = ContentDialogButton.Close,
        };

        var result = await confirm.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        try
        {
            var path = AppConstants.LogFile;
            if (File.Exists(path))
            {
                File.WriteAllText(path, string.Empty);
            }
            LoadLog();
        }
        catch (Exception ex)
        {
            var errDialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = Loc.Get("LogsClearErrorTitle"),
                Content = Loc.Get("LogsClearErrorContent") + ex.Message,
                CloseButtonText = Loc.Get("DialogOK"),
            };
            await errDialog.ShowAsync();
        }
    }

    // ------------------------------------------------------------------
    // Shared helpers
    // ------------------------------------------------------------------

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
