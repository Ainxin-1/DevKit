using System.Collections.ObjectModel;
using System.IO;

namespace DevEnvManager.Core;

/// <summary>
/// 日志服务：所有操作写入日志文件，并同步给 UI 内存列表。
/// </summary>
public static class Logger
{
    private static readonly object _lock = new();
    private static string? _logFile;

    /// <summary>UI 展示用内存日志</summary>
    public static ObservableCollection<string> Entries { get; } = new();

    /// <summary>日志文件路径</summary>
    public static string LogFile
    {
        get
        {
            if (_logFile is null)
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DevEnvManager", "logs");
                Directory.CreateDirectory(dir);
                _logFile = Path.Combine(dir, $"app-{DateTime.Now:yyyyMMdd}.log");
            }
            return _logFile;
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] [{level}] {message}";
        lock (_lock)
        {
            try
            {
                File.AppendAllText(LogFile, line + Environment.NewLine);
            }
            catch
            {
                // 日志写失败不影响主流程
            }
            App.Current?.Dispatcher.Invoke(() =>
            {
                Entries.Add(line);
                if (Entries.Count > 2000) Entries.RemoveAt(0);
            });
        }
    }
}
