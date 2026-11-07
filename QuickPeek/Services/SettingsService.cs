using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using QuickPeek.Models;

namespace QuickPeek.Services;

public class SettingsService
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "QuickPeek");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "settings.json");
    private static readonly string BackupPath = ConfigPath + ".bak";
    private static readonly string TempPath = ConfigPath + ".tmp";

    public static AppConfig Load()
    {
        var config = TryLoad(ConfigPath) ?? TryLoad(BackupPath);
        if (config == null) return AppConfig.Default;

        config.Normalize();
        return config;
    }

    private static AppConfig? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;

            var json = File.ReadAllText(path);
            var root = JObject.Parse(json);
            var config = root.ToObject<AppConfig>();
            if (config == null) return null;

            // 旧版本只有单一边距，升级后同步到宽/高两个字段
            var legacy = root["PreviewMarginPx"];
            if (legacy != null && root["PreviewMarginXPx"] == null && root["PreviewMarginYPx"] == null)
            {
                config.PreviewMarginXPx = config.PreviewMarginYPx = legacy.Value<int>();
            }
            return config;
        }
        catch
        {
            return null;
        }
    }

    public static void Save(AppConfig config)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonConvert.SerializeObject(config, Formatting.Indented);
            File.WriteAllText(TempPath, json);

            // 先写临时文件再替换，避免写入过程中崩溃留下半截配置；
            // File.Replace 会同时生成 .bak，读取失败时回退到备份
            if (File.Exists(ConfigPath))
            {
                File.Replace(TempPath, ConfigPath, BackupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(TempPath, ConfigPath);
            }
        }
        catch
        {
            try
            {
                if (File.Exists(TempPath)) File.Delete(TempPath);
            }
            catch
            {
            }
        }
    }

    public static void Reset()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                File.Delete(ConfigPath);
            }
        }
        catch
        {
        }
    }
}
