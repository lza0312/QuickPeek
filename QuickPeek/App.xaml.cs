using System;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using QuickPeek.Core;
using QuickPeek.Models;
using QuickPeek.Preview;
using QuickPeek.Services;

namespace QuickPeek;

public partial class App : Application
{
    private PreviewManager? _previewManager;
    private TaskbarIcon? _trayIcon;
    private System.Threading.Mutex? _singleInstanceMutex;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 单实例：多个进程会各自安装全局钩子，导致明显卡顿
        _singleInstanceMutex = new System.Threading.Mutex(true, "QuickPeek.SingleInstance.v1", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("QuickPeek 已经在运行了，请查看任务栏托盘图标。", "QuickPeek", MessageBoxButton.OK, MessageBoxImage.Information);
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
            MessageBox.Show($"QuickPeek 启动失败：{ex.Message}", "QuickPeek", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private TaskbarIcon CreateTrayIcon()
    {
        var trayIcon = new TaskbarIcon
        {
            ToolTipText = "QuickPeek - 文件悬停预览",
            ContextMenu = BuildContextMenu()
        };

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
            using var iconStream = assembly.GetManifestResourceStream("QuickPeek.Resources.icon.ico");
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

    private System.Windows.Controls.ContextMenu BuildContextMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();

        var toggleItem = new System.Windows.Controls.MenuItem
        {
            Header = "启用 QuickPeek",
            IsCheckable = true,
            IsChecked = _previewManager?.GetConfig().Enabled ?? true
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

        menu.Items.Add(new System.Windows.Controls.Separator());

        var showItem = new System.Windows.Controls.MenuItem { Header = "设置..." };
        showItem.Click += (s, e) => ShowMainWindow();
        menu.Items.Add(showItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var quitItem = new System.Windows.Controls.MenuItem { Header = "退出" };
        quitItem.Click += (s, e) => Shutdown();
        menu.Items.Add(quitItem);

        return menu;
    }

    private MainWindow? _mainWindow;

    private void ShowMainWindow()
    {
        if (_mainWindow == null)
        {
            _mainWindow = new MainWindow(_previewManager!);
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
