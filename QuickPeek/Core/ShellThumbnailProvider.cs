using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace QuickPeek.Core;

/// <summary>Shell 缩略图：视频/图片无法解码时兜底显示一帧画面。</summary>
public static class ShellThumbnailProvider
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [Flags]
    private enum SIIGBF
    {
        RESIZETOFIT = 0x00,
        THUMBNAILONLY = 0x08
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        void GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string pszPath,
        IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    /// <summary>
    /// 通过 Shell 取系统缩略图（视频首帧、文档预览等）。
    /// 解码失败时用它兜底，至少能看到一帧画面。
    /// </summary>
    public static BitmapSource? Get(string filePath, int size = 1024)
    {
        IntPtr hBitmap = IntPtr.Zero;
        object? item = null;

        try
        {
            var iid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
            SHCreateItemFromParsingName(filePath, IntPtr.Zero, iid, out item);
            if (item is not IShellItemImageFactory factory) return null;

            try
            {
                factory.GetImage(new SIZE { cx = size, cy = size }, SIIGBF.THUMBNAILONLY, out hBitmap);
            }
            catch
            {
                factory.GetImage(new SIZE { cx = size, cy = size }, SIIGBF.RESIZETOFIT, out hBitmap);
            }

            if (hBitmap == IntPtr.Zero) return null;

            var bitmap = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            Logger.Debug($"获取 Shell 缩略图失败: {ex.Message}");
            return null;
        }
        finally
        {
            if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
            if (item != null)
            {
                try { Marshal.ReleaseComObject(item); } catch { }
            }
        }
    }
}
