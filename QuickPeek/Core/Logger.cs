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
    // full 与 lite 各自使用独立目录与日志文件名，便于区分两个版本的运行记录
    private static readonly string _dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppIdentity.ConfigDirectoryName);

    private static readonly string _logFileName = AppIdentity.Name.ToLowerInvariant() + ".log";
    private static readonly string _logPath = Path.Combine(_dir, _logFileName);

    public static bool Verbose { get; set; }

    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex == null ? message : $"{message} -> {ex}");

    public static void Info(string message) => Write("INFO", message);

    public static void Debug(string message)
    {
        if (!Verbose) return;
        Write("DEBUG", message);
    }

    private const long MaxLogBytes = 1_000_000;
    private const int MaxArchivedFiles = 2;

    private static string ArchivePath(int index)
        => Path.Combine(_dir, $"{AppIdentity.Name.ToLowerInvariant()}.{index}.log");

    /// <summary>超过上限时滚动保留 quickpeek.1.log / quickpeek.2.log，避免日志无限增长。</summary>
    private static void RotateIfNeeded()
    {
        var fi = new FileInfo(_logPath);
        if (!fi.Exists || fi.Length < MaxLogBytes) return;

        for (int i = MaxArchivedFiles - 1; i >= 1; i--)
        {
            var source = ArchivePath(i);
            if (!File.Exists(source)) continue;

            var target = ArchivePath(i + 1);
            if (File.Exists(target)) File.Delete(target);
            File.Move(source, target);
        }

        File.Move(_logPath, ArchivePath(1));
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(_dir);

                RotateIfNeeded();

                File.AppendAllText(_logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 日志失败不能影响主流程
        }
    }
}
