using System;
using System.Windows;
using System.Windows.Media;
using Hardcodet.Wpf.TaskbarNotification;
using QuickPeek.Core;
using QuickPeek.Models;
using QuickPeek.Preview;
using QuickPeek.Services;

namespace QuickPeek;

public partial class App : Application
{
    /// <summary>
    /// 程序图标 / 窗口图标 / 托盘图标共用的内嵌资源。
    /// full 与 lite 图形完全一致，只换配色，避免被当成两个不相关的软件。
    /// </summary>
#if LITE_BUILD
    private const string IconResourceName = "QuickPeek.Resources.icon-lite.ico";
#else
    private const string IconResourceName = "QuickPeek.Resources.icon.ico";
#endif

    private PreviewManager? _previewManager;
    private TaskbarIcon? _trayIcon;
    private System.Threading.Mutex? _singleInstanceMutex;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 单实例：多个进程会各自安装全局钩子，导致明显卡顿。
        // full 与 lite 使用不同的互斥体名称，两个版本可以同时运行。
        _singleInstanceMutex = new System.Threading.Mutex(true, AppIdentity.MutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show($"{AppIdentity.Name} 已经在运行了，请查看任务栏托盘图标。",
                AppIdentity.Name, MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _ownsMutex = true;

        // 任何未捕获异常都记录到日志并阻止进程直接崩溃
        DispatcherUnhandledException += (s, args) =>
        {
            Logger.Error("UI 线程未处理异常", args.Exception);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            Logger.Error("CLR 未处理异常", args.ExceptionObject as Exception);
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            Logger.Error("未观察的任务异常", args.Exception);
            args.SetObserved();
        };

        try
        {
            // lite 版升级：旧版自启动记录仍指向改名前的 exe，先就地迁移
            AutoStart.MigrateLegacyRegistration();

            _previewManager = new PreviewManager();
            _previewManager.Start();

            _trayIcon = CreateTrayIcon();

            var config = _previewManager.GetConfig();
            if (!config.StartMinimized)
            {
                ShowMainWindow();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("启动失败", ex);
            MessageBox.Show($"{AppIdentity.Name} 启动失败：{ex.Message}",
                AppIdentity.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private TaskbarIcon CreateTrayIcon()
    {
        // 菜单配色必须早于菜单创建，否则首次弹出会落到系统默认色
        ApplyMenuPalette(_previewManager?.GetConfig().UseDarkTheme ?? false);

        var trayIcon = new TaskbarIcon
        {
            ToolTipText = $"{AppIdentity.Name} - 文件悬停预览",
            ContextMenu = BuildContextMenu()
        };

        // 预览窗口是置顶且可能覆盖托盘区域的分层窗口。右键托盘前先收起预览，
        // 否则鼠标在移向托盘的途中会停留在预览窗口上，右键落在预览窗口而不是托盘图标上，
        // 表现为「托盘右键菜单不显示或显示在错误位置」。
        trayIcon.PreviewTrayContextMenuOpen += (s, e) => _previewManager?.HidePreview();

        trayIcon.TrayMouseDoubleClick += (s, e) => ShowMainWindow();

        try
        {
            trayIcon.Icon = LoadIcon();
        }
        catch
        {
        }

        return trayIcon;
    }

    private System.Drawing.Icon? LoadIcon()
    {
        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            using var iconStream = assembly.GetManifestResourceStream(IconResourceName);
            if (iconStream != null)
            {
                return new System.Drawing.Icon(iconStream);
            }
        }
        catch
        {
        }
        return System.Drawing.SystemIcons.Information;
    }

    /// <summary>
    /// 用同一个 ico 给 WPF 窗口设置图标（标题栏与任务栏跟随）。
    /// 取最接近 32px 的帧，DPI 缩放时始终保持整数倍清晰度。
    /// </summary>
    internal static System.Windows.Media.ImageSource? LoadWindowIcon()
    {
        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            using var iconStream = assembly.GetManifestResourceStream(IconResourceName);
            if (iconStream == null) return null;

            var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
                iconStream,
                System.Windows.Media.Imaging.BitmapCreateOptions.None,
                System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);

            System.Windows.Media.Imaging.BitmapFrame best = decoder.Frames[0];
            foreach (var frame in decoder.Frames)
            {
                if (Math.Abs(frame.PixelWidth - 32) < Math.Abs(best.PixelWidth - 32))
                {
                    best = frame;
                }
            }

            best.Freeze();
            return best;
        }
        catch
        {
            return null;
        }
    }

    private System.Windows.Controls.ContextMenu BuildContextMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu
        {
            Style = (Style)FindResource("QP.ContextMenu")
        };

        var toggleItem = new System.Windows.Controls.MenuItem
        {
            Header = $"启用 {AppIdentity.Name}",
            IsCheckable = true,
            IsChecked = _previewManager?.GetConfig().Enabled ?? true,
            Style = (Style)FindResource("QP.MenuItem")
        };
        toggleItem.Click += (s, e) =>
        {
            if (_previewManager == null) return;
            var config = _previewManager.GetConfig();
            config.Enabled = !config.Enabled;
            _previewManager.UpdateConfig(config);
            ((System.Windows.Controls.MenuItem)s!).IsChecked = config.Enabled;
        };
        menu.Items.Add(toggleItem);

        menu.Items.Add(new System.Windows.Controls.Separator { Style = (Style)FindResource("QP.MenuSeparatorStyle") });

        var showItem = new System.Windows.Controls.MenuItem
        {
            Header = "设置...",
            Style = (Style)FindResource("QP.MenuItem")
        };
        showItem.Click += (s, e) => ShowMainWindow();
        menu.Items.Add(showItem);

        menu.Items.Add(new System.Windows.Controls.Separator { Style = (Style)FindResource("QP.MenuSeparatorStyle") });

        var quitItem = new System.Windows.Controls.MenuItem
        {
            Header = "退出",
            Style = (Style)FindResource("QP.MenuItem")
        };
        quitItem.Click += (s, e) => Shutdown();
        menu.Items.Add(quitItem);

        return menu;
    }

    /// <summary>
    /// 按 QuickPeek 自己的深色模式设置刷新托盘菜单配色。
    /// 菜单样式全部用 DynamicResource 引用这些键，切换后立即生效，无需重启。
    /// 两套都是深底白字，不跟随也不依赖 Windows 系统主题，避免白底白字/黑底黑字。
    /// </summary>
    internal static void ApplyMenuPalette(bool darkTheme)
    {
        var res = Application.Current?.Resources;
        if (res == null) return;

        // 常规版接近纯黑，深色模式版用面板灰，两者都与白色文字保持足够对比度
        var bg = darkTheme ? Color.FromRgb(0x2D, 0x2D, 0x30) : Color.FromRgb(0x12, 0x12, 0x12);
        var border = darkTheme ? Color.FromRgb(0x4A, 0x4A, 0x50) : Color.FromRgb(0x3F, 0x3F, 0x46);
        var disabled = darkTheme ? Color.FromRgb(0x8A, 0x8A, 0x8A) : Color.FromRgb(0x76, 0x76, 0x76);

        res["QP.MenuBg"] = new SolidColorBrush(bg);
        res["QP.MenuFg"] = new SolidColorBrush(Colors.White);
        res["QP.MenuBorder"] = new SolidColorBrush(border);
        res["QP.MenuSeparator"] = new SolidColorBrush(border);
        res["QP.MenuDisabledFg"] = new SolidColorBrush(disabled);
        res["QP.MenuHoverBg"] = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4));
        res["QP.MenuHoverFg"] = new SolidColorBrush(Colors.White);
    }

    private MainWindow? _mainWindow;

    private void ShowMainWindow()
    {
        if (_mainWindow == null)
        {
            _mainWindow = new MainWindow(_previewManager!);
            _mainWindow.Icon = LoadWindowIcon();
            _mainWindow.Closed += (s, e) => _mainWindow = null;
        }

        _mainWindow.Show();
        _mainWindow.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _previewManager?.Dispose();
        if (_ownsMutex)
        {
            try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
        }
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
