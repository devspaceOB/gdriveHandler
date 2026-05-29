using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;

namespace GdriveHandler.Pages;

public sealed partial class LogViewerPage : Page
{
    private const int MaxLines = 200;

    public LogViewerPage()
    {
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        LoadLog();
    }

    private void LoadLog()
    {
        var path = AppConstants.LogFile;
        if (!File.Exists(path))
        {
            LogText.Text = "(no log file found — log is created on first use)";
            return;
        }

        try
        {
            var allLines = File.ReadAllLines(path);
            var tail = allLines.Length <= MaxLines
                ? allLines
                : allLines[(allLines.Length - MaxLines)..];
            LogText.Text = string.Join(Environment.NewLine, tail);
        }
        catch (Exception ex)
        {
            LogText.Text = "Could not read log file: " + ex.Message;
        }

        // Scroll to bottom
        DispatcherQueue.TryEnqueue(() =>
        {
            LogScroller.ScrollToVerticalOffset(LogScroller.ExtentHeight);
        });
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        LoadLog();
    }

    private void BtnOpenNotepad_Click(object sender, RoutedEventArgs e)
    {
        var path = AppConstants.LogFile;
        if (!File.Exists(path))
        {
            return;
        }

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

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
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

    private async void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Clear log",
            Content = "This will permanently delete the log file contents. Proceed?",
            PrimaryButtonText = "Clear",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        var result = await confirm.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

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
                Title = "Error",
                Content = "Could not clear log: " + ex.Message,
                CloseButtonText = "OK",
            };
            await errDialog.ShowAsync();
        }
    }
}
