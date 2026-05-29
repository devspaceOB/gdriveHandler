namespace GdriveHandler;

public sealed partial class App : Application
{
    private readonly string _initialPage;
    private MainWindow? _window;

    public App(string initialPage = "home")
    {
        _initialPage = initialPage;
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow(_initialPage);
        _window.Activate();
    }
}
