using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;
using WinRT;

namespace GdriveHandler;

public sealed partial class MainWindow : Window
{
    private readonly string _initialPage;
    private MicaController? _micaController;
    private SystemBackdropConfiguration? _backdropConfig;

    public MainWindow(string initialPage = "home")
    {
        _initialPage = initialPage;
        InitializeComponent();
        SetupWindow();
        SetupMica();
    }

    private void SetupWindow()
    {
        Title = AppConstants.DisplayName;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(900, 640));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMinimizable = true;
            presenter.IsMaximizable = true;
        }
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
            "logs"     => typeof(Pages.LogViewerPage),
            "about"    => typeof(Pages.AboutPage),
            _          => typeof(Pages.HomePage),
        };
        ContentFrame.Navigate(pageType);
    }
}
