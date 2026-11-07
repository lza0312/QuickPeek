using System;

namespace QuickPeek.Models;

public enum CornerPosition
{
    BottomRight,
    BottomLeft,
    TopRight,
    TopLeft
}

public enum PreviewSizeMode
{
    FitToWindow,
    MaxOriginalRatio
}

public class AppConfig
{
    /// <summary>当前配置结构版本，用于读取旧配置时执行迁移。</summary>
    public const int CurrentConfigVersion = 1;

    public int ConfigVersion { get; set; } = CurrentConfigVersion;

    public bool Enabled { get; set; } = true;
    public int HoverDelayMs { get; set; } = 500;
    public CornerPosition PreviewCorner { get; set; } = CornerPosition.BottomRight;
    public double PreviewWidthPx { get; set; } = 672;
    public double PreviewHeightPx { get; set; } = 972;
    // 宽边距控制窗口到左右屏幕边界的距离，高边距控制到上下边界的距离
    public int PreviewMarginXPx { get; set; } = 20;
    public int PreviewMarginYPx { get; set; } = 20;
    // 浮窗缩放与设置界面缩放，单位 %，100 为原始大小，最大 300
    public int PreviewScalePercent { get; set; } = 100;
    public int UiScalePercent { get; set; } = 100;
    public double PreviewWindowOpacity { get; set; } = 0.95;
    public double PreviewContentOpacity { get; set; } = 1.0;
    public bool ShowFileInfo { get; set; } = true;
    public double MaxTextFileSizeKB { get; set; } = 2048.0;
    public bool ShowImages { get; set; } = true;
    public bool ShowVideos { get; set; } = true;
    public bool ShowTexts { get; set; } = true;
    public bool ShowAudios { get; set; } = false;
    public bool StartMinimized { get; set; } = true;
    public PreviewSizeMode ImageSizeMode { get; set; } = PreviewSizeMode.MaxOriginalRatio;
    public bool EnableBlankDoubleClickBack { get; set; } = true;
    public bool UseDarkTheme { get; set; } = true;

    public double VideoDefaultVolume { get; set; } = 0.5;
    public double VideoDefaultSpeed { get; set; } = 1.0;

    public int VideoVolumeStepPercent { get; set; } = 10;
    public double VideoSeekStepSeconds { get; set; } = 3.0;
    public double VideoSpeedStepMultiplier { get; set; } = 0.25;
    public double VideoSpeedMin { get; set; } = 0.25;
    public double VideoSpeedMax { get; set; } = 4.0;

    public bool EnableVideoCtrlWheel { get; set; } = true;
    public bool EnableVideoCtrlWheelH { get; set; } = true;

    public string VideoHotkeyVolumeUp { get; set; } = "Control+Add";
    public string VideoHotkeyVolumeDown { get; set; } = "Control+Subtract";
    public string VideoHotkeyStepFwd { get; set; } = "Control+Right";
    public string VideoHotkeyStepBack { get; set; } = "Control+Left";
    public string VideoHotkeySpeedUp { get; set; } = "Control+Up";
    public string VideoHotkeySpeedDown { get; set; } = "Control+Down";

    public static AppConfig Default => new();

    /// <summary>把各项数值钳制到可用范围，避免手改配置或旧配置文件导致异常尺寸/透明度。</summary>
    public void Normalize()
    {
        HoverDelayMs = Math.Clamp(HoverDelayMs, 100, 5000);
        PreviewWidthPx = Math.Clamp(PreviewWidthPx, 100, 7680);
        PreviewHeightPx = Math.Clamp(PreviewHeightPx, 100, 4320);
        PreviewMarginXPx = Math.Clamp(PreviewMarginXPx, 0, 200);
        PreviewMarginYPx = Math.Clamp(PreviewMarginYPx, 0, 200);
        PreviewScalePercent = Math.Clamp(PreviewScalePercent, 100, 300);
        UiScalePercent = Math.Clamp(UiScalePercent, 100, 300);
        PreviewWindowOpacity = Math.Clamp(PreviewWindowOpacity, 0.2, 1.0);
        PreviewContentOpacity = Math.Clamp(PreviewContentOpacity, 0.2, 1.0);
        MaxTextFileSizeKB = Math.Clamp(MaxTextFileSizeKB, 1, 102400);
        VideoDefaultVolume = Math.Clamp(VideoDefaultVolume, 0.0, 1.0);
        VideoDefaultSpeed = Math.Clamp(VideoDefaultSpeed, 0.1, 8.0);
        VideoVolumeStepPercent = Math.Clamp(VideoVolumeStepPercent, 1, 100);
        VideoSeekStepSeconds = Math.Clamp(VideoSeekStepSeconds, 0.5, 60.0);
        VideoSpeedStepMultiplier = Math.Clamp(VideoSpeedStepMultiplier, 0.05, 2.0);
        VideoSpeedMin = Math.Clamp(VideoSpeedMin, 0.1, 4.0);
        VideoSpeedMax = Math.Clamp(VideoSpeedMax, VideoSpeedMin, 8.0);

        if (!Enum.IsDefined(typeof(CornerPosition), PreviewCorner))
            PreviewCorner = CornerPosition.BottomRight;
        if (!Enum.IsDefined(typeof(PreviewSizeMode), ImageSizeMode))
            ImageSizeMode = PreviewSizeMode.MaxOriginalRatio;
    }
}
