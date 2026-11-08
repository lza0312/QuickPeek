using System;
using System.IO;
using Microsoft.Win32;

namespace QuickPeek.Core;

/// <summary>
/// 开机自启动：写入当前用户的 Run 键，不需要管理员权限。
/// </summary>
public static class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    // full 与 lite 写入不同的值名，两个版本可各自设置自启动，互不覆盖
    private static string ValueName => AppIdentity.AutoStartValueName;

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var value = key?.GetValue(ValueName) as string;
            if (string.IsNullOrEmpty(value)) return false;

            var exePath = TrimQuotes(value);
            if (!File.Exists(exePath))
            {
                // 程序被移动或删除后，注册表里会残留失效路径，视为未启用
                Logger.Debug($"自启动路径已失效: {exePath}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Debug($"读取自启动设置失败: {ex.Message}");
            return false;
        }
    }

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                Logger.Info("已取消开机自启动");
                return true;
            }

            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath) || !exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Error($"自启动失败：无法确定程序路径 ({exePath})");
                return false;
            }

            // 通过 dotnet run 启动时路径是 dotnet.exe，不适合写进注册表
            if (Path.GetFileName(exePath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Error($"自启动失败：当前由 dotnet run 启动，请使用发布后的 {AppIdentity.Name}.exe");
                return false;
            }

            // 只写入真实存在、且位于发布目录的可执行文件，避免把临时/构建路径写进注册表
            if (!File.Exists(exePath))
            {
                Logger.Error($"自启动失败：程序文件不存在 ({exePath})");
                return false;
            }

            var directory = Path.GetDirectoryName(exePath);
            if (string.IsNullOrEmpty(directory) || !File.Exists(Path.Combine(directory, AppIdentity.Name + ".dll")))
            {
                Logger.Error($"自启动失败：{exePath} 不是完整的发布目录，请先执行发布");
                return false;
            }

            key.SetValue(ValueName, $"\"{exePath}\"");
            Logger.Info($"已设置开机自启动: {exePath}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("设置开机自启动失败", ex);
            return false;
        }
    }

    private static string TrimQuotes(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
            value = value.Substring(1, value.Length - 2);
        return value;
    }

    /// <summary>
    /// 旧版精简版（程序名也叫 QuickPeek）改名为 QuickPeekLite 后，自启动记录仍指向旧 exe。
    /// 仅当旧记录指向当前程序目录时才迁移到新值名，避免误改真正的完整版记录。
    /// </summary>
    public static void MigrateLegacyRegistration()
    {
        if (string.IsNullOrEmpty(AppIdentity.LegacyAutoStartValueName)) return;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null) return;

            var legacy = key.GetValue(AppIdentity.LegacyAutoStartValueName) as string;
            if (string.IsNullOrEmpty(legacy)) return;

            var currentExe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExe)) return;

            var currentDir = Path.GetDirectoryName(currentExe);
            var legacyDir = Path.GetDirectoryName(TrimQuotes(legacy));
            if (string.IsNullOrEmpty(currentDir) || string.IsNullOrEmpty(legacyDir)) return;

            // 只有旧记录就位于当前安装目录，才说明它属于本版本
            if (!string.Equals(
                    Path.GetFullPath(currentDir).TrimEnd('\\'),
                    Path.GetFullPath(legacyDir).TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase))
                return;

            key.SetValue(AppIdentity.AutoStartValueName, $"\"{currentExe}\"");
            key.DeleteValue(AppIdentity.LegacyAutoStartValueName, throwOnMissingValue: false);
            Logger.Info($"旧版自启动记录已迁移为 {AppIdentity.AutoStartValueName}");
        }
        catch (Exception ex)
        {
            Logger.Debug($"迁移旧自启动记录失败: {ex.Message}");
        }
    }
}
