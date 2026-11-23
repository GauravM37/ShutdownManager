using System.Text;

namespace ShutdownManager.Services;

public sealed class Logger
{
    private readonly object _gate = new();
    private readonly string _logDirectory;
    private readonly string _logPath;

    public Logger()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ScheduledShutdownManager");
        _logDirectory = Path.Combine(root, "logs");
        _logPath = Path.Combine(_logDirectory, "application.log");
    }

    public string LogPath => _logPath;

    public void Info(string message) => Write("INFO", message, null);
    public void Warning(string message) => Write("WARN", message, null);
    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(_logDirectory);
                var line = new StringBuilder()
                    .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                    .Append(" [").Append(level).Append("] ")
                    .Append(message);

                if (exception is not null)
                    line.AppendLine().Append(exception);

                File.AppendAllText(_logPath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never crash the application.
        }
    }
}
