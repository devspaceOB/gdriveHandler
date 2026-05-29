namespace GdriveHandler;

public sealed partial class App : Application
{
    private readonly string _initialPage;
    private readonly Logger _log = new();
    private MainWindow? _window;

    internal MainWindow? MainWindow => _window;

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

    /// <summary>
    /// Switches the UI language in place — no process restart. Setting
    /// <c>PrimaryLanguageOverride</c> and recreating the window re-resolves every
    /// x:Uid string (including the nav labels) in the new language.
    /// </summary>
    internal void SwitchLanguage(string lang)
    {
        try
        {
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride =
                string.Equals(lang, "tr", StringComparison.OrdinalIgnoreCase) ? "tr" : "en";

            // Drop the cached ResourceLoader so code-behind strings (Loc.Get) resolve
            // in the new language too — not just the x:Uid strings.
            Loc.Reset();

            var old = _window;
            _window = new MainWindow("settings");
            _window.Activate();
            old?.Close();
        }
        catch (Exception ex)
        {
            _log.Error("SwitchLanguage failed: " + ex);
        }
    }
}
