using System;
using System.Diagnostics;
using System.IO;

namespace Aion2TR.App.Services;

public static class AppLogger
{
    private static readonly object _lock = new();
    private static readonly string _logDir;
    private static readonly string _logPath;

    static AppLogger()
    {
        _logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aion2TR", "logs");
        _logPath = Path.Combine(_logDir, "aion2tr.log");

        try
        {
            if (!Directory.Exists(_logDir))
                Directory.CreateDirectory(_logDir);
        }
        catch { }
    }

    public static string LogPath => _logPath;
    public static string LogDir => _logDir;

    public static void Info(string message) => Log("INFO", message);
    public static void Warn(string message) => Log("WARN", message);
    public static void Error(string message, Exception? ex = null)
    {
        string full = ex != null ? $"{message}\n{ex}" : message;
        Log("ERROR", full);
    }

    private static void Log(string level, string message)
    {
        try
        {
            lock (_lock)
            {
                if (!Directory.Exists(_logDir))
                    Directory.CreateDirectory(_logDir);

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                File.AppendAllText(_logPath, $"[{timestamp}] [{level}] {message}{Environment.NewLine}");
            }
        }
        catch { }
    }

    public static void OpenLogFile()
    {
        try
        {
            if (!File.Exists(_logPath))
            {
                Info("Log file initialized.");
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = _logPath,
                UseShellExecute = true
            });
        }
        catch { }
    }

    public static void OpenLogFolder()
    {
        try
        {
            if (!Directory.Exists(_logDir))
                Directory.CreateDirectory(_logDir);

            Process.Start(new ProcessStartInfo
            {
                FileName = _logDir,
                UseShellExecute = true
            });
        }
        catch { }
    }
}
