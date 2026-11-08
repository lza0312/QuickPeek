using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using QuickPeek.Core;
using QuickPeek.Models;
using QuickPeek.Native;

namespace QuickPeek.Preview;

public partial class PreviewWindow : Window
{
    private static PreviewWindow? _instance;

    public static PreviewWindow? Instance => _instance;

    public static void SetInstance(PreviewWindow instance) => _instance = instance;

    private AppConfig _config = AppConfig.Default;
    private IntPtr _handle;

    private readonly System.Windows.Threading.DispatcherTimer _statusTimer;

    /// <summary>内容宽高比（宽 / 高），只由原图尺寸决定，和解码尺寸、缓存命中无关。</summary>
    private double _contentAspect;
    private const double MinSide = 24;

    private const double HeaderHeight = 44;

    public PreviewWindow()
    {
        InitializeComponent();

        _statusTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(2500)
        };
        _statusTimer.Tick += OnStatusTimerTick;

        Loaded += OnLoaded;
        DpiChanged += (_, _) => PositionWindow();
        _instance = this;
    }

    public void ApplyConfig(AppConfig config)
    {
        _config = config;
        Opacity = 1.0;

        // 不显示文件信息时：标题栏隐藏、窗口底色去掉 → 真正的无边框悬浮
        if (config.ShowFileInfo)
        {
            var opacity = Math.Clamp(config.PreviewWindowOpacity, 0.0, 1.0);
            byte bgAlpha = (byte)(230 * opacity);
            RootBorder.Background = new SolidColorBrush(Color.FromArgb(bgAlpha, 0x1E, 0x1E, 0x1E));
            HeaderBorder.Visibility = Visibility.Visible;
            HeaderBorder.Background = new SolidColorBrush(Color.FromArgb((byte)(242 * opacity), 0x2D, 0x2D, 0x2D));
        }
        else
        {
            RootBorder.Background = Brushes.Transparent;
            HeaderBorder.Visibility = Visibility.Collapsed;
        }

        if (PreviewContent is FrameworkElement fe)
        {
            fe.Opacity = config.PreviewContentOpacity;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionWindow();
    }

    private void Window_SourceInitialized(object sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        MakeToolWindow();
    }

    private void MakeToolWindow()
    {
        if (_handle == IntPtr.Zero) return;

        var exStyle = GetWindowLong(_handle, -20);
        SetWindowLong(_handle, -20, exStyle | (int)Win32Constants.WS_EX_TOOLWINDOW | (int)Win32Constants.WS_EX_NOACTIVATE | (int)Win32Constants.WS_EX_TOPMOST | (int)Win32Constants.WS_EX_LAYERED);
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    public void PositionWindow()
    {
        var screen = MonitorHelper.GetCursorWorkArea(out var dpiScale);
        var marginX = _config.PreviewMarginXPx;
        var marginY = _config.PreviewMarginYPx;
        var uiScale = Math.Clamp(_config.PreviewScalePercent, 100, 300) / 100.0;

        // 缩放只影响文字 UI（标题栏字号、高度），图片/文本内容仍按配置尺寸显示
        ApplyTextScale(uiScale);

        var maxW = _config.PreviewWidthPx > 0 ? _config.PreviewWidthPx : 1280;
        var maxH = _config.PreviewHeightPx > 0 ? _config.PreviewHeightPx : 720;

        maxW = Math.Min(maxW, screen.Width - marginX * 2);
        maxH = Math.Min(maxH, screen.Height - marginY * 2);

        var header = (_config.ShowFileInfo ? HeaderHeight : 0) * uiScale;
        var contentMaxH = Math.Max(120, maxH - header);

        double width;
        double height;

        if (_contentAspect > 0)
        {
            // 只按「原图宽高比 + 配置的上限」推导窗口尺寸：
            // 先铺满可用宽度，超高时再按高度反算；不读取任何已解码位图的尺寸。
            width = maxW;
            height = width / _contentAspect;
            if (height > contentMaxH)
            {
                height = contentMaxH;
                width = height * _contentAspect;
            }

            // 最小可点击尺寸：按比例放大后再受上限约束，
            // 全程只改一边、另一边由宽高比推导，避免超长条形图被强行拉伸或裁切
            if (width < MinSide)
            {
                width = MinSide;
                height = width / _contentAspect;
            }
            if (height < MinSide)
            {
                height = MinSide;
                width = height * _contentAspect;
            }
            if (width > maxW)
            {
                width = maxW;
                height = width / _contentAspect;
            }
            if (height > contentMaxH)
            {
                height = contentMaxH;
                width = height * _contentAspect;
            }
        }
        else
        {
            width = maxW;
            height = contentMaxH;
        }

        Width = width;
        Height = height + header;

        // 四角模式按配置角落固定摆放；「跟随鼠标」模式放在光标的另一侧
        var rect = MonitorHelper.PlacePreview(
            screen, Width, Height, _config.PreviewCorner, marginX, marginY, dpiScale);
        Left = rect.Left;
        Top = rect.Top;

        // 便于验证「同一张图连续悬停尺寸一致」：同一文件的宽高比与尺寸每次都应相同
        Logger.Debug($"预览尺寸 aspect={_contentAspect:F4} -> {Width:F0}x{Height:F0}");
    }

    /// <summary>只放大标题栏的文字与高度，不改变预览内容尺寸。</summary>
    private void ApplyTextScale(double scale)
    {
        HeaderBorder.Height = HeaderHeight * scale;
        HeaderBorder.Padding = new Thickness(12 * scale, 6 * scale, 12 * scale, 6 * scale);
        FileNameTextBlock.FontSize = 13 * scale;
        HeaderInfoTextBlock.FontSize = 11 * scale;
        StatusTextBlock.FontSize = 11 * scale;
        CloseGlyphTextBlock.FontSize = 12 * scale;
    }

    /// <summary>
    /// 显示预览。窗口尺寸只由 <paramref name="contentAspect"/>（原图宽/高）与配置上限决定，
    /// 与解码后的位图尺寸、缓存是否命中无关，因此同一张图反复悬停尺寸完全一致。
    /// </summary>
    public void ShowPreview(string fileName, string? info = null, double contentAspect = 0)
    {
        // 防止除零/异常值把窗口算成 0 或无穷大
        _contentAspect = contentAspect is > 0 and < 1000 ? contentAspect : 0;

        FileNameTextBlock.Text = fileName;

        var infoText = _config.ShowFileInfo ? (info ?? string.Empty) : string.Empty;
        HeaderInfoTextBlock.Text = infoText;
        HeaderInfoTextBlock.Visibility = string.IsNullOrEmpty(infoText) ? Visibility.Collapsed : Visibility.Visible;

        StatusTextBlock.Text = string.Empty;
        StatusTextBlock.Visibility = Visibility.Collapsed;
        _statusTimer.Stop();

        PositionWindow();

        if (!IsVisible)
        {
            Show();
        }
        else
        {
            UpdateLayout();
        }

        _ = NativeMethods.BringWindowToTop(_handle);
    }

    /// <summary>标题栏临时提示，例如「播放速度 1.25 倍」，约 2.5 秒后自动消失。</summary>
    public void SetStatus(string message)
    {
        if (string.IsNullOrEmpty(message) || !_config.ShowFileInfo) return;

        StatusTextBlock.Text = message;
        StatusTextBlock.Visibility = Visibility.Visible;

        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private void OnStatusTimerTick(object? sender, EventArgs e)
    {
        _statusTimer.Stop();
        StatusTextBlock.Text = string.Empty;
        StatusTextBlock.Visibility = Visibility.Collapsed;
    }

    public void SetContent(object content)
    {
        // 换内容时必须先释放上一个预览器，否则旧 MediaElement 会继续播放声音
        if (PreviewContent.Content is IDisposable previous && !ReferenceEquals(previous, content))
        {
            try
            {
                previous.Dispose();
            }
            catch (Exception ex)
            {
                QuickPeek.Core.Logger.Error("释放旧预览内容失败", ex);
            }
        }

        PreviewContent.Content = content;
    }

    public void ClearContent()
    {
        if (PreviewContent.Content is IDisposable disposable)
        {
            disposable.Dispose();
        }
        PreviewContent.Content = null;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        ClearContent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _handle = new WindowInteropHelper(this).Handle;
        MakeToolWindow();

    }
}
