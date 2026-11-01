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

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var root = JObject.Parse(json);
                var config = root.ToObject<AppConfig>();
                if (config == null) return AppConfig.Default;

                // 旧版本只有单一边距，升级后同步到宽/高两个字段
                var legacy = root["PreviewMarginPx"];
                if (legacy != null && root["PreviewMarginXPx"] == null && root["PreviewMarginYPx"] == null)
                {
                    config.PreviewMarginXPx = config.PreviewMarginYPx = legacy.Value<int>();
                }
                return config;
            }
        }
        catch
        {
        }
        return AppConfig.Default;
    }

    public static void Save(AppConfig config)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonConvert.SerializeObject(config, Formatting.Indented);
            File.WriteAllText(ConfigPath, json);
        }
        catch
        {
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
