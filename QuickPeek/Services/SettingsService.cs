using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using QuickPeek.Core;
using QuickPeek.Models;

namespace QuickPeek.Services;

public class SettingsService
{
    // full 与 lite 使用各自的配置目录，避免两个版本互相覆盖设置
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppIdentity.ConfigDirectoryName);

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "settings.json");

    public static AppConfig Load()
    {
        EnsureMigratedFromLegacyDirectory();
        return LoadFrom(ConfigPath);
    }

    /// <summary>
    /// lite 版从旧版（同样叫 QuickPeek）升级时，把旧配置目录的设置复制过来。
    /// 只复制不删除，旧版本配置保持原样。full 版没有旧目录，直接跳过。
    /// </summary>
    private static void EnsureMigratedFromLegacyDirectory()
    {
        if (string.IsNullOrEmpty(AppIdentity.LegacyConfigDirectoryName)) return;
        if (File.Exists(ConfigPath)) return;

        try
        {
            var legacyDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppIdentity.LegacyConfigDirectoryName);
            if (!Directory.Exists(legacyDir)) return;

            Directory.CreateDirectory(ConfigDir);
            foreach (var name in new[] { "settings.json", "settings.json.bak" })
            {
                var source = Path.Combine(legacyDir, name);
                var target = Path.Combine(ConfigDir, name);
                if (File.Exists(source) && !File.Exists(target))
                    File.Copy(source, target, overwrite: false);
            }

            Logger.Info($"已从旧配置目录迁移设置: {legacyDir}");
        }
        catch (Exception ex)
        {
            Logger.Debug($"迁移旧配置失败: {ex.Message}");
        }
    }

    /// <summary>从指定路径读取；主文件损坏或缺失时回退 .bak，并对结果做迁移与范围钳制。</summary>
    public static AppConfig LoadFrom(string configPath)
    {
        var config = TryLoad(configPath) ?? TryLoad(configPath + ".bak");
        if (config == null) return AppConfig.Default;

        Migrate(config);
        config.Normalize();
        return config;
    }

    /// <summary>按 ConfigVersion 执行旧配置迁移。</summary>
    private static void Migrate(AppConfig config)
    {
        if (config.ConfigVersion < 1)
        {
            // v0 早期配置只有单一边距 PreviewMarginPx，
            // 具体的字段映射在 TryLoad 中完成，这里统一升级版本号
            config.ConfigVersion = AppConfig.CurrentConfigVersion;
        }
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

            // 旧版本没有 ConfigVersion 字段，反序列化后会保留默认值，
            // 这里显式标记为 0，交给 Migrate 统一升级
            if (root["ConfigVersion"] == null) config.ConfigVersion = 0;

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

    public static void Save(AppConfig config) => SaveTo(config, ConfigPath);

    /// <summary>原子写入指定路径：临时文件 + File.Replace，并保留 .bak 备份。</summary>
    public static void SaveTo(AppConfig config, string configPath)
    {
        var tempPath = configPath + ".tmp";
        var backupPath = configPath + ".bak";

        try
        {
            var dir = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var json = JsonConvert.SerializeObject(config, Formatting.Indented);
            File.WriteAllText(tempPath, json);

            if (File.Exists(configPath))
            {
                File.Replace(tempPath, configPath, backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, configPath);
            }
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
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
