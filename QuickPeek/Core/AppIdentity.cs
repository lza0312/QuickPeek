namespace QuickPeek.Core;

/// <summary>
/// 版本身份。full 与 lite 共用同一套源码，但必须使用不同的进程名、
/// 单实例互斥体、自启动注册表值和配置目录，避免两个版本互相冲突。
/// </summary>
public static class AppIdentity
{
#if LITE_BUILD
    public const string Name = "QuickPeekLite";
    /// <summary>旧版 lite 也称为 QuickPeek，首次升级时从该配置目录迁移。</summary>
    public const string LegacyConfigDirectoryName = "QuickPeek";
    /// <summary>旧版 lite 写入的自启动注册表值名称。</summary>
    public const string LegacyAutoStartValueName = "QuickPeek";
#else
    public const string Name = "QuickPeek";
    public const string LegacyConfigDirectoryName = "";
    public const string LegacyAutoStartValueName = "";
#endif

    /// <summary>单实例互斥体名称（full / lite 各一个）。</summary>
    public const string MutexName = Name + ".SingleInstance.v1";

    /// <summary>自启动注册表值名称，两个版本分别写入，互不覆盖。</summary>
    public const string AutoStartValueName = Name;

    /// <summary>配置目录名：%APPDATA%\&lt;Name&gt;。</summary>
    public const string ConfigDirectoryName = Name;
}
