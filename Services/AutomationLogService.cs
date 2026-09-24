using System.IO;

namespace UIAutomationInspectorWpf.Services;

public sealed class AutomationLogService
{
    private readonly string _logDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UIAutomationInspectorWpf",
        "logs");

    public void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(_logDirectory);
            var filePath = Path.Combine(_logDirectory, $"automation-{DateTime.Now:yyyy-MM-dd}.log");
            File.AppendAllText(filePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Logging must not interrupt receipt processing.
        }
    }

    public AutomationRunLog StartRun()
    {
        Directory.CreateDirectory(_logDirectory);
        var filePath = Path.Combine(
            _logDirectory,
            $"automation-run-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}-{Guid.NewGuid():N}.log");
        return new AutomationRunLog(filePath, this);
    }
}

public sealed class AutomationRunLog : IDisposable
{
    private readonly string _filePath;
    private readonly AutomationLogService _dailyLog;
    private readonly object _sync = new();

    internal AutomationRunLog(string filePath, AutomationLogService dailyLog)
    {
        _filePath = filePath;
        _dailyLog = dailyLog;
    }

    public string FilePath => _filePath;

    public void Write(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}";
        lock (_sync)
        {
            try
            {
                File.AppendAllText(_filePath, line);
            }
            catch (IOException)
            {
                // Logging must not interrupt receipt processing.
            }
        }

        _dailyLog.Write(message);
    }

    public void Dispose()
    {
    }
}