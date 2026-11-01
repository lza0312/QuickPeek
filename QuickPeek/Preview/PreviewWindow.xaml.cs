using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
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
    private int _contentWidth;
    private int _contentHeight;

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
        _instance = this;
    }

    public void ApplyConfig(AppConfig config)
    {
        _config = config;
        Opacity = 1.0;

        // 不显示文件信息时：标题栏隐藏、窗口底色去掉 → 真正的无边框悬浮
        if (config.ShowFileInfo)
        {
            byte bgAlpha = (byte)(230 * config.PreviewWindowOpacity);
            RootBorder.Background = new SolidColorBrush(Color.FromArgb(bgAlpha, 0x1E, 0x1E, 0x1E));
            HeaderBorder.Visibility = Visibility.Visible;
            HeaderBorder.Background = new SolidColorBrush(Color.FromArgb((byte)(242 * config.PreviewWindowOpacity), 0x2D, 0x2D, 0x2D));
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
        var screen = SystemParameters.WorkArea;
        var marginX = _config.PreviewMarginXPx;
        var marginY = _config.PreviewMarginYPx;
        var scale = Math.Clamp(_config.PreviewScalePercent, 100, 300) / 100.0;

        // 缩放只影响文字 UI（标题栏字号、高度），图片/文本内容仍按配置尺寸显示
        ApplyTextScale(scale);

        var maxW = _config.PreviewWidthPx > 0 ? _config.PreviewWidthPx : 1280;
        var maxH = _config.PreviewHeightPx > 0 ? _config.PreviewHeightPx : 720;

        maxW = Math.Min(maxW, screen.Width - marginX * 2);
        maxH = Math.Min(maxH, screen.Height - marginY * 2);

        var header = (_config.ShowFileInfo ? HeaderHeight : 0) * scale;
        var contentMaxH = Math.Max(120, maxH - header);

        double width;
        double height;

        if (_contentWidth > 0 && _contentHeight > 0)
        {
            // 与视频浮窗一致：窗口按内容比例铺满可用宽度，不留下背景边
            var ratio = (double)_contentWidth / _contentHeight;
            width = maxW;
            height = width / ratio;
            if (height > contentMaxH)
            {
                height = contentMaxH;
                width = height * ratio;
            }
        }
        else
        {
            width = maxW;
            height = contentMaxH;
        }

        Width = Math.Max(160, width);
        Height = Math.Max(120, Math.Min(maxH, height + header));

        switch (_config.PreviewCorner)
        {
            case CornerPosition.BottomRight:
                Left = screen.Right - Width - marginX;
                Top = screen.Bottom - Height - marginY;
                break;
            case CornerPosition.BottomLeft:
                Left = screen.Left + marginX;
                Top = screen.Bottom - Height - marginY;
                break;
            case CornerPosition.TopRight:
                Left = screen.Right - Width - marginX;
                Top = screen.Top + marginY;
                break;
            case CornerPosition.TopLeft:
                Left = screen.Left + marginX;
                Top = screen.Top + marginY;
                break;
        }

        Left = Math.Max(screen.Left, Left);
        Top = Math.Max(screen.Top, Top);
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

    public void ShowPreview(string fileName, string? info = null, int contentWidth = 0, int contentHeight = 0)
    {
        _contentWidth = contentWidth;
        _contentHeight = contentHeight;

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

        var hwndSource = HwndSource.FromHwnd(_handle);
        hwndSource?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32Constants.WM_MOUSEWHEEL)
        {
            handled = true;
        }
        return IntPtr.Zero;
    }
}
