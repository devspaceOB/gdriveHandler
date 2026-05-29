using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;
using WinRT;
using WinUIColor = Windows.UI.Color;
using WinUIColors = Microsoft.UI.Colors;

namespace GdriveHandler;

public sealed partial class MainWindow : Window
{
    private readonly string _initialPage;
    private bool _installPromptShown;
    private MicaController? _micaController;
    private SystemBackdropConfiguration? _backdropConfig;

    private readonly Logger _log = new();

    public MainWindow(string initialPage = "home")
    {
        _initialPage = initialPage;
        try
        {
            InitializeComponent();
            SetupWindow();
            SetupTitleBar();
            SetupMica();
        }
        catch (Exception ex)
        {
            _log.Error("MainWindow ctor failed: " + ex);
            throw;
        }
    }

    private void SetupWindow()
    {
        Title = AppConstants.DisplayName;

        // Get the HWND so we can ask the system for the actual DPI of this monitor.
        var hwnd = WindowNative.GetWindowHandle(this);
        var dpi = NativeMethods.GetDpiForWindow(hwnd);
        if (dpi == 0) dpi = 96; // guard against failure (e.g., headless)

        var scale = dpi / 96.0;
        var w = (int)Math.Round(1000 * scale);
        var h = (int)Math.Round(700 * scale);

        AppWindow.Resize(new Windows.Graphics.SizeInt32(w, h));

        // Center on the work area of the nearest display.
        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        if (displayArea != null)
        {
            var wa = displayArea.WorkArea;
            var x = wa.X + (wa.Width - w) / 2;
            var y = wa.Y + (wa.Height - h) / 2;
            AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
        }

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMinimizable = true;
            presenter.IsMaximizable = true;
        }

        // Set the taskbar / window icon to the real App.ico file (resolved next to the exe).
        try
        {
            var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory;
            var icoPath = Path.Combine(exeDir, "Assets", "App.ico");
            if (File.Exists(icoPath))
            {
                AppWindow.SetIcon(icoPath);
            }
        }
        catch (Exception ex)
        {
            _log.Warn("Could not set window icon: " + ex.Message);
        }
    }

    private void SetupTitleBar()
    {
        // Extend content into the title bar area so Mica fills the caption strip.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarGrid);

        ApplyTitleBarButtonColors();

        // Re-apply caption button colors when the theme changes (Dark ↔ Light).
        if (Content is FrameworkElement fe)
        {
            fe.ActualThemeChanged += (_, _) => ApplyTitleBarButtonColors();
        }
    }

    private void ApplyTitleBarButtonColors()
    {
        var titleBar = AppWindow.TitleBar;

        // Let Mica show through the caption button background.
        titleBar.ButtonBackgroundColor = WinUIColors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = WinUIColors.Transparent;

        // Pick the foreground colour that matches the current theme so the
        // caption buttons (close / max / min) remain visible in both Light and Dark.
        var theme = (Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default;
        var isDark = theme == ElementTheme.Dark ||
                     (theme == ElementTheme.Default &&
                      Application.Current.RequestedTheme == ApplicationTheme.Dark);

        var fgColor = isDark ? WinUIColors.White : WinUIColors.Black;
        var fgHover = isDark ? WinUIColor.FromArgb(255, 220, 220, 220) : WinUIColor.FromArgb(255, 40, 40, 40);

        titleBar.ButtonForegroundColor = fgColor;
        titleBar.ButtonInactiveForegroundColor = isDark
            ? WinUIColor.FromArgb(255, 150, 150, 150)
            : WinUIColor.FromArgb(255, 120, 120, 120);
        titleBar.ButtonHoverForegroundColor = fgHover;
        titleBar.ButtonPressedForegroundColor = fgColor;
    }

    private void SetupMica()
    {
        if (!MicaController.IsSupported())
        {
            return;
        }

        _backdropConfig = new SystemBackdropConfiguration
        {
            IsInputActive = true,
            Theme = SystemBackdropTheme.Default,
        };

        _micaController = new MicaController();
        _micaController.AddSystemBackdropTarget(
            this.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
        _micaController.SetSystemBackdropConfiguration(_backdropConfig);

        Activated += (_, args) =>
        {
            if (_backdropConfig != null)
            {
                _backdropConfig.IsInputActive =
                    args.WindowActivationState != WindowActivationState.Deactivated;
            }
        };

        Closed += (_, _) =>
        {
            _micaController?.Dispose();
            _micaController = null;
        };
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        Navigate(_initialPage);
        // Select the initial nav item
        var item = NavView.MenuItems.OfType<NavigationViewItem>()
            .Concat(NavView.FooterMenuItems.OfType<NavigationViewItem>())
            .FirstOrDefault(i => i.Tag as string == _initialPage);
        if (item != null)
        {
            NavView.SelectedItem = item;
        }

        DispatcherQueue.TryEnqueue(async () => await ShowFirstLaunchInstallPromptAsync());
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            Navigate(tag);
        }
    }

    private void Navigate(string tag)
    {
        var pageType = tag switch
        {
            "home"     => typeof(Pages.HomePage),
            "guide"    => typeof(Pages.GuidePage),
            "aliases"  => typeof(Pages.AliasesPage),
            "settings" => typeof(Pages.SettingsPage),
            "about"    => typeof(Pages.AboutPage),
            _          => typeof(Pages.HomePage),
        };
        ContentFrame.Navigate(pageType);
    }

    private async System.Threading.Tasks.Task ShowFirstLaunchInstallPromptAsync()
    {
        if (_installPromptShown ||
            AppConstants.IsPackaged ||
            _initialPage != "home" ||
            Installer.IsUserInstallHealthy())
        {
            return;
        }

        _installPromptShown = true;
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = Loc.Get("InstallPromptTitle"),
            Content = Loc.Get("InstallPromptContent"),
            PrimaryButtonText = Loc.Get("InstallPromptInstall"),
            CloseButtonText = Loc.Get("InstallPromptCancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        var code = Installer.InstallUserAndLaunchInstalledCopy(_log);
        if (code == ExitCode.Success)
        {
            Application.Current.Exit();
            return;
        }

        var errorDialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = Loc.Get("DialogError"),
            Content = Loc.Get("HomeInstallFailed"),
            CloseButtonText = Loc.Get("DialogOK"),
        };
        await errorDialog.ShowAsync();
    }
}
