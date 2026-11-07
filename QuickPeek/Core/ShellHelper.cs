using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using QuickPeek.Native;
using POINT = QuickPeek.Native.PInvoke.POINT;

namespace QuickPeek.Core;

public class ShellHelper
{
    public const string CabinetWClass = "CabinetWClass";
    public const string ExploreWClass = "ExploreWClass";

    public static bool IsExplorerWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;

        var sb = new StringBuilder(256);
        PInvoke.GetClassName(hwnd, sb, sb.Capacity);
        var className = sb.ToString();

        if (className == CabinetWClass || className == ExploreWClass)
            return true;

        uint pid;
        PInvoke.GetWindowThreadProcessId(hwnd, out pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static bool IsExplorerWindowAtPoint(POINT point)
    {
        var hwnd = PInvoke.WindowFromPoint(point);
        if (hwnd == IntPtr.Zero) return false;

        var topExplorer = FindTopLevelExplorerWindow(hwnd);
        return IsExplorerWindow(topExplorer);
    }

    public static IntPtr FindExplorerWindowAtPoint(POINT point)
    {
        var hwnd = PInvoke.WindowFromPoint(point);
        if (hwnd == IntPtr.Zero) return IntPtr.Zero;

        return FindTopLevelExplorerWindow(hwnd);
    }

    private static IntPtr FindTopLevelExplorerWindow(IntPtr hwnd)
    {
        var parent = hwnd;
        for (int i = 0; i < 20; i++)
        {
            if (parent == IntPtr.Zero) break;
            var sb = new StringBuilder(256);
            PInvoke.GetClassName(parent, sb, sb.Capacity);
            var className = sb.ToString();
            if (className == CabinetWClass || className == ExploreWClass)
                return parent;
            parent = PInvoke.GetAncestor(parent, 2);
        }
        return parent;
    }

    private static string GetClassNameSafe(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        PInvoke.GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static bool GetExplorerItemAtPoint(POINT screenPoint, out string? filePath)
    {
        filePath = null;
        return TryGetViaUIA(screenPoint, out filePath);
    }

    private static bool TryGetViaUIA(POINT screenPoint, out string? filePath)
    {
        filePath = null;
        try
        {
            var uiaPoint = new System.Windows.Point(screenPoint.X, screenPoint.Y);
            var element = AutomationElement.FromPoint(uiaPoint);
            if (element == null) return false;

            var walker = TreeWalker.RawViewWalker;
            var current = element;
            int depth = 0;
            while (current != null && depth < 30)
            {
                var ctrlType = current.Current.ControlType;
                var className = current.Current.ClassName ?? "";
                var currentHwnd = current.Current.NativeWindowHandle;

                if (ctrlType == ControlType.ListItem || ctrlType == ControlType.DataItem)
                {
                    var itemName = current.Current.Name;
                    if (string.IsNullOrEmpty(itemName)) return false;

                    var topExplorer = FindTopLevelExplorerWindow(PInvoke.WindowFromPoint(screenPoint));
                    if (topExplorer == IntPtr.Zero) return false;

                    var currentDir = GetExplorerCurrentDirectory(topExplorer, out _);
                    if (string.IsNullOrEmpty(currentDir)) return false;

                    var fullPath = ResolveExplorerItemPath(currentDir, itemName);
                    if (fullPath != null)
                    {
                        filePath = fullPath;
                        return true;
                    }

                    return false;
                }

                if (currentHwnd != IntPtr.Zero)
                {
                    var clsName = GetClassNameSafe(currentHwnd);
                    if (clsName == CabinetWClass || clsName == ExploreWClass) return false;
                }

                if (className == CabinetWClass || className == ExploreWClass) return false;

                current = walker.GetParent(current);
                depth++;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 资源管理器「隐藏已知文件扩展名」时，UIA 暴露的 Name 不含扩展名，
    /// 直接拼接会找不到文件。这里再做一次目录枚举按文件名匹配。
    /// </summary>
    private static string? ResolveExplorerItemPath(string directory, string itemName)
    {
        var direct = Path.Combine(directory, itemName);
        if (File.Exists(direct) || Directory.Exists(direct)) return direct;

        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var name = Path.GetFileName(entry);
                if (string.Equals(name, itemName, StringComparison.OrdinalIgnoreCase))
                    return entry;

                if (string.Equals(Path.GetFileNameWithoutExtension(name), itemName, StringComparison.OrdinalIgnoreCase))
                    return entry;
            }
        }
        catch
        {
        }

        return null;
    }

    public static bool HitTestExplorerBlankArea(POINT screenPoint)
    {
        try
        {
            var hwnd = PInvoke.WindowFromPoint(screenPoint);
            if (hwnd == IntPtr.Zero) return false;

            var topExplorer = FindTopLevelExplorerWindow(hwnd);
            if (topExplorer == IntPtr.Zero || !IsExplorerWindow(topExplorer))
                return false;

            var uiaPoint = new System.Windows.Point(screenPoint.X, screenPoint.Y);
            var element = AutomationElement.FromPoint(uiaPoint);
            if (element == null) return false;

            var walker = TreeWalker.RawViewWalker;
            var current = element;
            int depth = 0;
            while (current != null && depth < 30)
            {
                var ctrlType = current.Current.ControlType;
                var className = current.Current.ClassName ?? "";
                var currentHwnd = current.Current.NativeWindowHandle;

                if (ctrlType == ControlType.ListItem || ctrlType == ControlType.DataItem)
                {
                    Logger.Debug($"[HitTest] 命中 {ctrlType.ProgrammaticName} -> 不是空白区域");
                    return false;
                }

                if (currentHwnd == topExplorer || className == CabinetWClass || className == ExploreWClass)
                {
                    Logger.Debug("[HitTest] 到达资源管理器顶层窗口 -> 是空白区域");
                    return true;
                }

                current = walker.GetParent(current);
                depth++;
            }

            Logger.Debug("[HitTest] 遍历结束仍未到顶层，按空白处理");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("HitTestExplorerBlankArea 异常", ex);
            return false;
        }
    }

    /// <summary>
    /// 通过 UI Automation 调用资源管理器命令栏里的「向上一级」按钮。
    /// 这是最可靠的方式：Explorer 自己知道层级链（文件夹 → 盘符根 → 此电脑 → 桌面），
    /// 也不依赖前台焦点或键盘消息。
    /// </summary>
    public static bool NavigateUpViaUia(IntPtr explorerHwnd)
    {
        try
        {
            var root = AutomationElement.FromHandle(explorerHwnd);
            if (root == null)
            {
                Logger.Debug("[NavigateUp] UIA 无法获取窗口元素");
                return false;
            }

            // Win11 的文件/文件夹窗口里，向上按钮的 AutomationId 固定为 upButton
            var condition = new PropertyCondition(AutomationElement.AutomationIdProperty, "upButton");
            var button = root.FindFirst(TreeScope.Descendants, condition);

            if (button == null)
            {
                Logger.Debug("[NavigateUp] 未找到 upButton（可能已是顶层）");
                return false;
            }

            if (!button.TryGetCurrentPattern(InvokePattern.Pattern, out var patternObj) || patternObj == null)
            {
                Logger.Debug("[NavigateUp] upButton 不支持 Invoke");
                return false;
            }

            ((InvokePattern)patternObj).Invoke();
            Logger.Info($"[NavigateUp] 已点击向上按钮 ({button.Current.Name})");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Debug($"[NavigateUp] UIA 向上失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 用 Shell COM 把指定资源管理器窗口导航到父目录。
    /// 作为 Alt+Up 发送失败时的兜底，不依赖键盘消息是否被接收。
    /// </summary>
    public static bool NavigateToParentFolder(IntPtr explorerHwnd)
    {
        try
        {
            string? locationName;
            var current = GetExplorerCurrentDirectory(explorerHwnd, out locationName);

            string parent;

            if (string.IsNullOrEmpty(current))
            {
                // 虚拟文件夹：此电脑的上一级是桌面
                if (locationName == "此电脑" || locationName == "This PC")
                {
                    parent = "shell:Desktop";
                }
                else
                {
                    Logger.Debug($"[NavigateUp] 无法获取当前目录（LocationName='{locationName}'）");
                    return false;
                }
            }
            else
            {
                var trimmed = current.TrimEnd('\\', '/');
                var parentPath = Path.GetDirectoryName(trimmed);

                // 驱动器根目录（如 K:）没有磁盘父目录，上一级应是「此电脑」
                if (string.IsNullOrEmpty(parentPath))
                {
                    if (trimmed.Length <= 2 && trimmed.EndsWith(":"))
                    {
                        parentPath = "shell:MyComputerFolder";
                    }
                    else
                    {
                        Logger.Debug($"[NavigateUp] 已是顶层: {current}");
                        return false;
                    }
                }

                parent = parentPath;
                Logger.Debug($"[NavigateUp] {current} -> {parent}");
            }

            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return false;

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic windows = shell.Windows();

            foreach (var window in windows)
            {
                try
                {
                    IntPtr windowHwnd;
                    try
                    {
                        windowHwnd = new IntPtr(Convert.ToInt64(window.HWND));
                    }
                    catch
                    {
                        continue;
                    }

                    if (windowHwnd != explorerHwnd) continue;

                    window.Navigate2(parent);
                    Logger.Info($"[NavigateUp] Navigate2 -> {parent}");
                    return true;
                }
                catch (Exception ex)
                {
                    Logger.Debug($"[NavigateUp] Navigate2 异常: {ex.Message}");
                }
            }

            Logger.Debug($"[NavigateUp] 未找到句柄 0x{explorerHwnd.ToInt64():X} 对应的 Shell 窗口");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Debug($"[NavigateUp] 失败: {ex.Message}");
            return false;
        }
    }

    private static string? GetExplorerCurrentDirectory(IntPtr explorerHwnd, out string? locationName)
    {
        locationName = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null)
            {
                Logger.Debug("[ShellHelper] 未找到 Shell.Application");
                return FallbackGetDirectory(explorerHwnd);
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic windows = shell.Windows();

            foreach (var window in windows)
            {
                try
                {
                    IntPtr windowHwnd;
                    try
                    {
                        // COM 返回的可能是 int / long / double，统一转换
                        windowHwnd = new IntPtr(Convert.ToInt64(window.HWND));
                    }
                    catch
                    {
                        continue;
                    }

                    if (windowHwnd != explorerHwnd) continue;

                    var url = window.LocationURL?.ToString();
                    Logger.Debug($"[ShellHelper] 命中窗口 0x{explorerHwnd.ToInt64():X} URL='{url}'");

                    if (!string.IsNullOrEmpty(url))
                    {
                        var path = ConvertShellUrlToPath(url);
                        if (path != null) return path;
                    }
                    else
                    {
                        // 虚拟文件夹（此电脑/桌面等）没有 LocationURL，但 LocationName 仍可用于判断层级
                        locationName = window.LocationName?.ToString();
                        Logger.Debug($"[ShellHelper] 虚拟文件夹 LocationName='{locationName}'");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug($"[ShellHelper] 枚举窗口异常: {ex.Message}");
                }
            }

            Logger.Debug("[ShellHelper] COM 未匹配到资源管理器窗口");
            return FallbackGetDirectory(explorerHwnd);
        }
        catch (Exception ex)
        {
            Logger.Debug($"[ShellHelper] COM 调用失败: {ex.Message}");
            return FallbackGetDirectory(explorerHwnd);
        }
    }

    private static string? ConvertShellUrlToPath(string url)
    {
        try
        {
            string path;
            if (url.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
            {
                path = url.Substring(8).Replace('/', '\\');
            }
            else if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                path = url.Substring(7).Replace('/', '\\');
            }
            else
            {
                return null;
            }

            path = Uri.UnescapeDataString(path);
            if (path.EndsWith("\\")) path = path.TrimEnd('\\');

            if (Directory.Exists(path)) return path;

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string? FallbackGetDirectory(IntPtr explorerHwnd)
    {
        try
        {
            var sb = new StringBuilder(1024);
            PInvoke.GetWindowText(explorerHwnd, sb, sb.Capacity);
            var title = sb.ToString();

            if (string.IsNullOrEmpty(title)) return null;

            var downloads = GetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"));

            var specialFolders = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                downloads,
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            };

            foreach (var folder in specialFolders)
            {
                if (string.IsNullOrEmpty(folder)) continue;
                var name = Path.GetFileName(folder);
                if (title.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                    title.StartsWith(name + " - ", StringComparison.OrdinalIgnoreCase) ||
                    title.StartsWith(name + "  - ", StringComparison.OrdinalIgnoreCase))
                {
                    return folder;
                }
            }

            if (title.StartsWith("此电脑") || title.Equals("This PC", StringComparison.OrdinalIgnoreCase) || title.Equals("Computer", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                var driveLabel = drive.Name.TrimEnd('\\');
                if (title.Contains(driveLabel, StringComparison.OrdinalIgnoreCase))
                {
                    var root = drive.RootDirectory.FullName;
                    if (Directory.Exists(root)) return root;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rfid,
        uint dwFlags,
        IntPtr hToken,
        out IntPtr pszPath);

    // ---------- Shell 缩略图（供视频无法解码时兜底显示首帧） ----------

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
        BIGGERSIZEOK = 0x01,
        THUMBNAILONLY = 0x08,
        ICONONLY = 0x10
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
    /// 视频解码失败时用它兜底，至少能看到一帧画面。
    /// </summary>
    public static System.Windows.Media.Imaging.BitmapSource? GetShellThumbnail(string filePath, int size = 1024)
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

            var bitmap = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                System.Windows.Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            Logger.Debug($"GetShellThumbnail 失败: {ex.Message}");
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

    private static string? GetKnownFolderPath(Guid guid)
    {
        IntPtr pathPtr = IntPtr.Zero;
        try
        {
            var hr = SHGetKnownFolderPath(guid, 0, IntPtr.Zero, out pathPtr);
            if (hr != 0 || pathPtr == IntPtr.Zero) return null;
            return Marshal.PtrToStringUni(pathPtr);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (pathPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(pathPtr);
        }
    }
}
