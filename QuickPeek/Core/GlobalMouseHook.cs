using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using QuickPeek.Native;
using POINT = QuickPeek.Native.PInvoke.POINT;

namespace QuickPeek.Core;

public class WheelEventArgs : EventArgs
{
    public int Delta { get; set; }
    public bool Handled { get; set; }
}

public class GlobalMouseHook : IDisposable
{
    private IntPtr _hookHandle;
    private PInvoke.LowLevelMouseProc? _hookProc;
    private readonly DispatcherTimer _pollTimer;
    private POINT _lastMousePos;
    private long _lastMoveTick;
    private bool _hoverReported;
    private bool _isDisposed;
    private bool _isRunning;

    // 低级鼠标钩子收不到 WM_LBUTTONDBLCLK（该消息由窗口自己合成），
    // 这里按系统双击时间 + 双击矩形自行判定。
    private long _lastLeftDownTick;
    private POINT _lastLeftDownPos;
    private uint _doubleClickTime;
    private int _doubleClickWidth;
    private int _doubleClickHeight;

    public event EventHandler<POINT>? HoverDetected;
    public event EventHandler? MouseButtonClicked;
    public event EventHandler<WheelEventArgs>? MouseWheelScrolled;
    public event EventHandler<WheelEventArgs>? MouseWheelScrolledH;
    public event EventHandler<POINT>? MouseDoubleClicked;

    public int HoverThresholdMs { get; set; } = 500;
    public int HoverTolerancePx { get; set; } = 3;

    private const int WM_MOUSEHWHEEL = 0x020E;

    public GlobalMouseHook()
    {
        _pollTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(80)
        };
        _pollTimer.Tick += PollTimer_Tick;
    }

    public void Start()
    {
        if (_isRunning) return;

        _hookProc = HookCallback;
        var hModule = PInvoke.GetModuleHandle(null);

        _hookHandle = PInvoke.SetWindowsHookEx(Win32Constants.WH_MOUSE_LL, _hookProc, hModule, 0);

        if (_hookHandle == IntPtr.Zero)
        {
            Logger.Error($"安装鼠标钩子失败, err={Marshal.GetLastWin32Error()}");
            _hookProc = null;
            return;
        }

        PInvoke.GetCursorPos(out _lastMousePos);
        _lastMoveTick = Environment.TickCount64;
        _hoverReported = false;

        _isRunning = true;
        _pollTimer.Start();
    }

    public void Stop()
    {
        _pollTimer.Stop();
        _isRunning = false;

        if (_hookHandle != IntPtr.Zero)
        {
            PInvoke.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
        _hookProc = null;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return PInvoke.CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        try
        {
            var mouseData = Marshal.PtrToStructure<PInvoke.MSLLHOOKSTRUCT>(lParam);
            var message = wParam.ToInt32();

            switch (message)
            {
                case Win32Constants.WM_MOUSEMOVE:
                    TrackMouseMove(mouseData.pt);
                    break;

                case Win32Constants.WM_LBUTTONDOWN:
                    DetectDoubleClick(mouseData.pt);
                    _hoverReported = true;
                    MouseButtonClicked?.Invoke(this, EventArgs.Empty);
                    break;

                case Win32Constants.WM_RBUTTONDOWN:
                case Win32Constants.WM_MBUTTONDOWN:
                    _hoverReported = true;
                    MouseButtonClicked?.Invoke(this, EventArgs.Empty);
                    break;

                case Win32Constants.WM_MOUSEWHEEL:
                    if (HandleVerticalWheel((short)((mouseData.mouseData >> 16) & 0xFFFF))) return (IntPtr)1;
                    break;

                case WM_MOUSEHWHEEL:
                    if (HandleHorizontalWheel((short)((mouseData.mouseData >> 16) & 0xFFFF))) return (IntPtr)1;
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("鼠标钩子回调异常", ex);
        }

        return PInvoke.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    /// <summary>
    /// 鼠标移动在钩子里只做最轻量的记录，不触发任何事件，
    /// 悬停判定交给 80ms 的轮询定时器，避免高频移动拖慢系统。
    /// </summary>
    private void TrackMouseMove(POINT pt)
    {
        var dx = Math.Abs(pt.X - _lastMousePos.X);
        var dy = Math.Abs(pt.Y - _lastMousePos.Y);

        if (dx <= HoverTolerancePx && dy <= HoverTolerancePx) return;

        _lastMousePos = pt;
        _lastMoveTick = Environment.TickCount64;
        _hoverReported = false;

        // 只在鼠标真正移动后启动轮询，静止时定时器保持关闭
        if (!_pollTimer.IsEnabled) _pollTimer.Start();
    }

    /// <summary>
    /// 低级鼠标钩子不会收到 WM_LBUTTONDBLCLK，只能自己按系统参数判定第二次按下。
    /// </summary>
    private void DetectDoubleClick(POINT pt)
    {
        var now = Environment.TickCount64;

        if (_doubleClickTime == 0)
        {
            _doubleClickTime = GetDoubleClickTime();
            _doubleClickWidth = GetSystemMetrics(SM_CXDOUBLECLK);
            _doubleClickHeight = GetSystemMetrics(SM_CYDOUBLECLK);
        }

        var withinTime = _lastLeftDownTick != 0 && now - _lastLeftDownTick <= _doubleClickTime;
        var withinRect = Math.Abs(pt.X - _lastLeftDownPos.X) <= _doubleClickWidth
                         && Math.Abs(pt.Y - _lastLeftDownPos.Y) <= _doubleClickHeight;

        if (withinTime && withinRect)
        {
            _lastLeftDownTick = 0;
            MouseDoubleClicked?.Invoke(this, pt);
            return;
        }

        _lastLeftDownTick = now;
        _lastLeftDownPos = pt;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_CXDOUBLECLK = 36;
    private const int SM_CYDOUBLECLK = 37;

    private void PollTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            if (_hoverReported)
            {
                _pollTimer.Stop();
                return;
            }

            if (Environment.TickCount64 - _lastMoveTick < HoverThresholdMs) return;

            _hoverReported = true;
            // 鼠标静止后不再需要轮询，避免空闲时每 80ms 唤醒一次线程
            _pollTimer.Stop();
            HoverDetected?.Invoke(this, _lastMousePos);
        }
        catch (Exception ex)
        {
            Logger.Error("悬停判定异常", ex);
        }
    }

    private bool HandleVerticalWheel(int delta)
    {
        _hoverReported = true;
        var args = new WheelEventArgs { Delta = delta };
        MouseWheelScrolled?.Invoke(this, args);
        return args.Handled;
    }

    private bool HandleHorizontalWheel(int delta)
    {
        _hoverReported = true;
        var args = new WheelEventArgs { Delta = delta };
        MouseWheelScrolledH?.Invoke(this, args);
        return args.Handled;
    }

    public POINT GetCurrentMousePos()
    {
        PInvoke.GetCursorPos(out var pt);
        return pt;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }
}
