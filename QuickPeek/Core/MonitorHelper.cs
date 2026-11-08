using System;
using System.Runtime.InteropServices;
using System.Windows;
using QuickPeek.Native;
using QuickPeek.Models;

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

    public static Rect GetCursorWorkArea() => GetCursorWorkArea(out _);

    /// <summary>返回光标所在显示器的工作区（DIP），同时输出该显示器的缩放比例。</summary>
    public static Rect GetCursorWorkArea(out double scale)
    {
        scale = 1.0;
        if (!PInvoke.GetCursorPos(out var pt)) return SystemParameters.WorkArea;
        return GetWorkAreaForPoint(pt, out scale);
    }

    public static Rect GetWorkAreaForPoint(PInvoke.POINT pt) => GetWorkAreaForPoint(pt, out _);

    public static Rect GetWorkAreaForPoint(PInvoke.POINT pt, out double scale)
    {
        scale = 1.0;

        try
        {
            var monitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero) return SystemParameters.WorkArea;

            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(monitor, ref info)) return SystemParameters.WorkArea;

            if (GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0)
            {
                scale = dpiX / 96.0;
            }

            var work = info.rcWork;
            return new Rect(work.Left / scale, work.Top / scale, work.Width / scale, work.Height / scale);
        }
        catch
        {
            scale = 1.0;
            return SystemParameters.WorkArea;
        }
    }

    /// <summary>
    /// 四角定位：按配置的角落固定摆放，边距沿用原有计算方式。
    /// 返回值和 <paramref name="workArea"/> 使用同一套 DIP 坐标。
    /// </summary>
    public static Rect PlaceInCorner(Rect workArea, double width, double height,
        CornerPosition corner, double marginX, double marginY)
    {
        double left, top;

        switch (corner)
        {
            case CornerPosition.BottomLeft:
                left = workArea.Left + marginX;
                top = workArea.Bottom - height - marginY;
                break;
            case CornerPosition.TopRight:
                left = workArea.Right - width - marginX;
                top = workArea.Top + marginY;
                break;
            case CornerPosition.TopLeft:
                left = workArea.Left + marginX;
                top = workArea.Top + marginY;
                break;
            default: // BottomRight
                left = workArea.Right - width - marginX;
                top = workArea.Bottom - height - marginY;
                break;
        }

        return ClampToWorkArea(workArea, left, top, width, height);
    }

    /// <summary>
    /// 跟随鼠标：以光标所在显示器工作区中心点为基准，
    /// 光标在左半边则预览放右侧，在右半边则放左侧；上下同理。
    /// 边距是预览与光标之间的距离（复用配置里的 X/Y 边距）。
    /// </summary>
    public static Rect PlaceFollowCursor(Rect workArea, double width, double height,
        double marginX, double marginY, double scale)
    {
        if (!PInvoke.GetCursorPos(out var pt))
            return PlaceInCorner(workArea, width, height, CornerPosition.BottomRight, marginX, marginY);

        // 光标是物理像素，按该显示器的缩放换算成与 workArea 一致的 DIP
        var cursorX = pt.X / scale;
        var cursorY = pt.Y / scale;

        var centerX = workArea.Left + workArea.Width / 2;
        var centerY = workArea.Top + workArea.Height / 2;

        var left = cursorX < centerX
            ? cursorX + marginX
            : cursorX - width - marginX;

        var top = cursorY < centerY
            ? cursorY + marginY
            : cursorY - height - marginY;

        return ClampToWorkArea(workArea, left, top, width, height);
    }

    /// <summary>按当前配置的位置模式选择落点算法。</summary>
    public static Rect PlacePreview(Rect workArea, double width, double height,
        CornerPosition corner, double marginX, double marginY, double scale)
    {
        return corner == CornerPosition.FollowMouse
            ? PlaceFollowCursor(workArea, width, height, marginX, marginY, scale)
            : PlaceInCorner(workArea, width, height, corner, marginX, marginY);
    }

    private static Rect ClampToWorkArea(Rect workArea, double left, double top, double width, double height)
    {
        left = Math.Clamp(left, workArea.Left, Math.Max(workArea.Left, workArea.Right - width));
        top = Math.Clamp(top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - height));
        return new Rect(left, top, width, height);
    }
}
