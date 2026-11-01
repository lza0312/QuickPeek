using System;
using System.IO;

namespace QuickPeek.Core;

/// <summary>
/// 轻量文件日志。WinExe 没有控制台，异常和关键事件统一写文件，
/// 避免同步控制台 IO 造成卡顿。
/// </summary>
public static class Logger
{
    private static readonly object _lock = new();
    private static readonly string _dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickPeek");
    private static readonly string _logPath = Path.Combine(_dir, "quickpeek.log");

    public static bool Verbose { get; set; }

    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex == null ? message : $"{message} -> {ex}");

    public static void Info(string message) => Write("INFO", message);

    public static void Debug(string message)
    {
        if (!Verbose) return;
        Write("DEBUG", message);
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(_dir);

                var fi = new FileInfo(_logPath);
                if (fi.Exists && fi.Length > 1_000_000)
                {
                    File.Delete(_logPath);
                }

                File.AppendAllText(_logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 日志失败不能影响主流程
        }
    }
}
