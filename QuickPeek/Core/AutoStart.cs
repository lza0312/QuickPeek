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
    private const string ValueName = "QuickPeek";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) != null;
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
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath) || !exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Error($"自启动失败：无法确定程序路径 ({exePath})");
                return false;
            }

            // 通过 dotnet run 启动时路径是 dotnet.exe，不适合写进注册表
            if (Path.GetFileName(exePath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Error("自启动失败：当前由 dotnet run 启动，请使用发布后的 QuickPeek.exe");
                return false;
            }

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

            if (enabled)
            {
                key.SetValue(ValueName, $"\"{exePath}\"");
                Logger.Info($"已设置开机自启动: {exePath}");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                Logger.Info("已取消开机自启动");
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("设置开机自启动失败", ex);
            return false;
        }
    }
}
