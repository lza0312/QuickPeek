using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using QuickPeek.Models;

namespace QuickPeek.Preview;

/// <summary>
/// 视频预览窗口。为了避开 WPF 分层窗口与原生视频渲染的冲突，
/// 这里使用不透明窗口，尺寸跟随视频比例；播放信息由独立浮层显示在画面下方。
/// </summary>
public partial class VideoWindow : Window
{
    private const double HeaderHeight = 44;
    private const double HudHeight = 28;
    private const double HudMargin = 10;

    private static VideoWindow? _instance;
    private readonly DispatcherTimer _statusTimer;
    private AppConfig _config = AppConfig.Default;
    private IntPtr _handle;

    /// <summary>窗口句柄，供外部判断光标是否落在预览窗口内。</summary>
    public IntPtr Handle => _handle;

    private int _videoWidth;
    private int _videoHeight;
    private string _playbackInfo = string.Empty;
    private bool _statusActive;
    private bool _isAudio;
    private PlaybackHudWindow? _hud;

    public static VideoWindow? Instance => _instance;

    public VideoWindow()
    {
        InitializeComponent();

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2500) };
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            _statusActive = false;
            RenderHud();
        };

        Loaded += (_, _) => _handle = new WindowInteropHelper(this).Handle;
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;

        // 不抢焦点：保持资源管理器焦点，全局快捷键仍然可用
        var exStyle = NativeMethods.GetWindowLong(_handle, GWL_EXSTYLE);
        NativeMethods.SetWindowLong(_handle, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    public void ApplyConfig(AppConfig config)
    {
        _config = config;
        HeaderBorder.Visibility = config.ShowFileInfo ? Visibility.Visible : Visibility.Collapsed;
        ApplyHeaderScale(ScaleFactor);
    }

    private double ScaleFactor => Math.Clamp(_config.PreviewScalePercent, 100, 300) / 100.0;

    private void ApplyHeaderScale(double scale)
    {
        HeaderBorder.Height = HeaderHeight * scale;
        HeaderBorder.Padding = new Thickness(12 * scale, 6 * scale, 12 * scale, 6 * scale);
        FileNameTextBlock.FontSize = 13 * scale;
        HeaderInfoTextBlock.FontSize = 11 * scale;
    }

    /// <summary>开始播放指定视频或音频。videoWidth/Height 用于按比例定尺寸（未知时传 0）。</summary>
    public void ShowVideo(VideoPreviewer previewer, string fileName, string? info, int videoWidth, int videoHeight, bool isAudio = false)
    {
        _videoWidth = videoWidth;
        _videoHeight = videoHeight;
        _isAudio = isAudio;

        // 切换新视频前先解绑旧播放器，避免 Host 继续渲染已释放的实例
        try { Host.Player = null; } catch { }

        Host.Player = previewer.Player;
        FileNameTextBlock.Text = fileName;
        UpdateFileInfo(info);

        _playbackInfo = string.Empty;
        _statusActive = false;
        _statusTimer.Stop();

        ResizeToContent();
        PositionWindow();

        if (!IsVisible) Show();
        NativeMethods.SetWindowPos(_handle, HWND_TOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);

        RenderHud();
    }

    /// <summary>更新标题栏里的文件信息（日期、分辨率、大小）。</summary>
    public void UpdateFileInfo(string? info)
    {
        var text = _config.ShowFileInfo ? (info ?? string.Empty) : string.Empty;
        HeaderInfoTextBlock.Text = text;
        HeaderInfoTextBlock.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>更新画面下方的播放信息浮层（已播放/总时长、倍速、进度百分比）。</summary>
    public void UpdatePlaybackInfo(string text)
    {
        _playbackInfo = text ?? string.Empty;
        if (!_statusActive) RenderHud();
    }

    /// <summary>快捷键提示（音量、快进、倍速等），短暂覆盖播放信息。</summary>
    public void SetStatus(string message)
    {
        if (string.IsNullOrEmpty(message)) return;

        _statusActive = true;
        _statusTimer.Stop();
        _statusTimer.Start();

        RenderHud(message);
    }

    /// <summary>视频真实分辨率已知后重新按比例调整窗口大小。</summary>
    public void ResizeToVideo(int width, int height)
    {
        _videoWidth = width;
        _videoHeight = height;
        ResizeToContent();
        PositionWindow();
        RenderHud();
    }

    private void RenderHud(string? overrideText = null)
    {
        var text = overrideText ?? (_statusActive ? string.Empty : _playbackInfo);

        if (string.IsNullOrEmpty(text) || !IsVisible)
        {
            _hud?.HideHud();
            return;
        }

        if (_hud == null)
        {
            _hud = new PlaybackHudWindow();
        }

        var scale = ScaleFactor;
        var hudHeight = HudHeight * scale;
        _hud.ShowInfo(text, Left + HudMargin, Top + Height - hudHeight - HudMargin, Width - HudMargin * 2, scale);
    }

    /// <summary>窗口按视频比例调整，并限制在配置的最大尺寸与屏幕工作区之内。</summary>
    private void ResizeToContent()
    {
        var scale = ScaleFactor;
        ApplyHeaderScale(scale);
        var headerHeight = (_config.ShowFileInfo ? HeaderHeight : 0) * scale;

        // 缩放只影响标题栏与播放信息文字，画面尺寸仍按配置宽高
        var maxW = _config.PreviewWidthPx > 0 ? _config.PreviewWidthPx : 1280;
        var maxH = _config.PreviewHeightPx > 0 ? _config.PreviewHeightPx : 720;

        var screen = SystemParameters.WorkArea;
        var marginX = _config.PreviewMarginXPx;
        var marginY = _config.PreviewMarginYPx;

        maxW = Math.Min(maxW, screen.Width - marginX * 2);
        maxH = Math.Min(maxH, screen.Height - marginY * 2);

        var contentMaxH = Math.Max(120, maxH - headerHeight);

        double w;
        double h;

        if (_isAudio && _videoWidth <= 0)
        {
            // 音频没有画面，用紧凑的 16:9 面板承载标题栏与播放信息浮层
            w = Math.Min(maxW, 480);
            h = Math.Min(contentMaxH, 270);
        }
        else if (_videoWidth > 0 && _videoHeight > 0)
        {
            var ratio = (double)_videoWidth / _videoHeight;
            w = maxW;
            h = w / ratio;
            if (h > contentMaxH)
            {
                h = contentMaxH;
                w = h * ratio;
            }
        }
        else
        {
            w = maxW;
            h = contentMaxH;
        }

        Width = Math.Max(160, w);
        Height = Math.Max(120, h + headerHeight);
    }

    private void PositionWindow()
    {
        var screen = SystemParameters.WorkArea;
        var marginX = _config.PreviewMarginXPx;
        var marginY = _config.PreviewMarginYPx;

        double left, top;

        switch (_config.PreviewCorner)
        {
            case CornerPosition.BottomRight:
                left = screen.Right - Width - marginX;
                top = screen.Bottom - Height - marginY;
                break;
            case CornerPosition.BottomLeft:
                left = screen.Left + marginX;
                top = screen.Bottom - Height - marginY;
                break;
            case CornerPosition.TopRight:
                left = screen.Right - Width - marginX;
                top = screen.Top + marginY;
                break;
            default:
                left = screen.Left + marginX;
                top = screen.Top + marginY;
                break;
        }

        Left = Math.Max(screen.Left, left);
        Top = Math.Max(screen.Top, top);
    }

    public void HidePreview()
    {
        _statusTimer.Stop();
        _statusActive = false;
        _playbackInfo = string.Empty;

        _hud?.HideHud();

        try { Host.Player = null; } catch { }

        Hide();
    }

    public static void SetInstance(VideoWindow window) => _instance = window;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);
    }
}
