using System;
using System.Runtime.InteropServices;
using System.Windows;
using QuickPeek.Native;

namespace QuickPeek.Core;

/// <summary>
/// 按光标所在显示器返回工作区，并把物理像素换算成 WPF 的 DIP。
/// 多显示器、不同缩放比例下都按实际落点定位，而不是主屏工作区。
/// </summary>
public static class MonitorHelper
{
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public PInvoke.RECT rcMonitor;
        public PInvoke.RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(PInvoke.POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    public static Rect GetCursorWorkArea()
    {
        if (!PInvoke.GetCursorPos(out var pt)) return SystemParameters.WorkArea;
        return GetWorkAreaForPoint(pt);
    }

    public static Rect GetWorkAreaForPoint(PInvoke.POINT pt)
    {
        try
        {
            var monitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero) return SystemParameters.WorkArea;

            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(monitor, ref info)) return SystemParameters.WorkArea;

            var scale = 1.0;
            if (GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0)
            {
                scale = dpiX / 96.0;
            }

            var work = info.rcWork;
            return new Rect(work.Left / scale, work.Top / scale, work.Width / scale, work.Height / scale);
        }
        catch
        {
            return SystemParameters.WorkArea;
        }
    }
}
