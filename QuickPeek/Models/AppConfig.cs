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
    public bool ShowVideoDuration { get; set; } = true;
    public bool AutoPlayVideo { get; set; } = true;
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
}
