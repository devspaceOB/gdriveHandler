namespace GdriveHandler;

/// <summary>
/// Minimal append-only file logger. Logging must never break file opening, so
/// all failures are swallowed (there is nowhere reliable to report a logging
/// failure to).
/// </summary>
internal sealed class Logger
{
    private readonly object _gate = new();
    private readonly string _file;

    public Logger()
    {
        _file = AppConstants.LogFile;
        try
        {
            Directory.CreateDirectory(AppConstants.LogDir);
        }
        catch
        {
            // Best effort; Write() tolerates a missing directory too.
        }
    }

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        try
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}][{level}] {message}{Environment.NewLine}";
            lock (_gate)
            {
                File.AppendAllText(_file, line);
            }
        }
        catch
        {
            // Intentionally ignored: logging is best-effort only.
        }
    }
}
