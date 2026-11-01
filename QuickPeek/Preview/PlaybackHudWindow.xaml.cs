using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace QuickPeek.Preview;

/// <summary>
/// 视频/音频播放信息浮层：悬浮在内容下方，独立窗口以避免与 Flyleaf 原生渲染表面冲突。
/// </summary>
public partial class PlaybackHudWindow : Window
{
    public PlaybackHudWindow()
    {
        InitializeComponent();
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);

        // WS_EX_NOACTIVATE 不抢焦点 / WS_EX_TOOLWINDOW 不占任务栏 / WS_EX_TRANSPARENT 鼠标穿透
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT);
    }

    public void ShowInfo(string text, double left, double top, double width, double scale = 1.0)
    {
        InfoText.Text = text;
        Left = left;
        Top = top;
        Width = Math.Max(160, width);

        var s = Math.Max(1.0, scale);
        HudBorder.LayoutTransform = new ScaleTransform(s, s);
        Height = 28 * s;

        if (!IsVisible) Show();
    }

    public void HideHud()
    {
        if (IsVisible) Hide();
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TRANSPARENT = 0x00000020;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
