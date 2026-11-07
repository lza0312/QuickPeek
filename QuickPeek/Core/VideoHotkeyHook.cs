using System;
using System.Runtime.InteropServices;
using QuickPeek.Native;

namespace QuickPeek.Core;

/// <summary>
/// 低级键盘钩子：只负责匹配与投递，不在回调里做窗口/播放器操作，
/// 避免 UIA、Flyleaf 等耗时调用触发系统 LowLevelHooksTimeout 而静默摘掉钩子。
/// </summary>
public sealed class VideoHotkeyHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private readonly Func<KeyCombo, bool> _isHotkey;
    private readonly Action<KeyCombo> _onPressed;
    private IntPtr _handle;
    private LowLevelKeyboardProc? _proc;

    public VideoHotkeyHook(Func<KeyCombo, bool> isHotkey, Action<KeyCombo> onPressed)
    {
        _isHotkey = isHotkey;
        _onPressed = onPressed;
    }

    public void Start()
    {
        if (_handle != IntPtr.Zero) return;

        _proc = HookProc;
        _handle = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, PInvoke.GetModuleHandle(null), 0);

        if (_handle == IntPtr.Zero)
        {
            Logger.Error($"安装键盘钩子失败, err={Marshal.GetLastWin32Error()}");
            _proc = null;
        }
    }

    public void Stop()
    {
        if (_handle == IntPtr.Zero) return;

        UnhookWindowsHookEx(_handle);
        _handle = IntPtr.Zero;
        _proc = null;
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return CallNextHookEx(_handle, nCode, wParam, lParam);

        try
        {
            var msg = wParam.ToInt32();
            if (msg != WM_KEYDOWN && msg != WM_SYSKEYDOWN)
                return CallNextHookEx(_handle, nCode, wParam, lParam);

            var kbd = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var combo = HotkeyText.FromVirtualKey((int)kbd.vkCode);

            if (combo != null && _isHotkey(combo))
            {
                _onPressed(combo);
                return (IntPtr)1;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("键盘钩子回调异常", ex);
        }

        return CallNextHookEx(_handle, nCode, wParam, lParam);
    }

    public void Dispose() => Stop();
}
