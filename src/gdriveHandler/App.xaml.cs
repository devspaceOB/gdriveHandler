namespace GdriveHandler;

public sealed partial class App : Application
{
    private readonly string _initialPage;
    private readonly Logger _log = new();
    private MainWindow? _window;

    public App(string initialPage = "home")
    {
        _initialPage = initialPage;
        UnhandledException += (_, e) =>
        {
            _log.Error("XAML UnhandledException: " + e.Message + Environment.NewLine + e.Exception);
            e.Handled = true;
        };

        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            _log.Error("App.InitializeComponent failed: " + ex);
            throw;
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow(_initialPage);
            _window.Activate();
        }
        catch (Exception ex)
        {
            _log.Error("OnLaunched failed: " + ex);
            throw;
        }
    }
}
