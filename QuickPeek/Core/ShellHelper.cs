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
    public const string SysListView32ClassName = "SysListView32";
    public const string SysTreeView32ClassName = "SysTreeView32";
    public const string DirectUIHWNDClassName = "DirectUIHWND";
    public const string SHELLDLL_DefViewClassName = "SHELLDLL_DefView";
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

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private static IntPtr FindChildRecursive(IntPtr parent, string className)
    {
        var sb = new StringBuilder(256);
        PInvoke.GetClassName(parent, sb, sb.Capacity);
        if (sb.ToString() == className)
            return parent;

        var child = GetWindow(parent, 5);
        while (child != IntPtr.Zero)
        {
            var result = FindChildRecursive(child, className);
            if (result != IntPtr.Zero)
                return result;
            child = GetWindow(child, 2);
        }
        return IntPtr.Zero;
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

    private static IntPtr _foundListView;

    private static bool EnumChildCallback(IntPtr hWnd, IntPtr lParam)
    {
        if (_foundListView != IntPtr.Zero) return false;

        var sb = new StringBuilder(256);
        PInvoke.GetClassName(hWnd, sb, sb.Capacity);
        var cls = sb.ToString();

        if (cls == SHELLDLL_DefViewClassName || cls.Contains("SysListView32"))
        {
            _foundListView = hWnd;
            return false;
        }

        EnumChildWindows(hWnd, EnumChildCallback, IntPtr.Zero);
        return _foundListView == IntPtr.Zero;
    }

    private static IntPtr FindShellListViewControl(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        PInvoke.GetClassName(hwnd, sb, sb.Capacity);
        var className = sb.ToString();

        Console.WriteLine($"[ShellHelper.Find] start hwnd=0x{hwnd.ToInt64():X} '{className}'");

        if (className == SHELLDLL_DefViewClassName || className.Contains("SysListView32"))
        {
            Console.WriteLine("[ShellHelper.Find] direct match");
            return hwnd;
        }

        var topExplorer = FindTopLevelExplorerWindow(hwnd);
        Console.WriteLine($"[ShellHelper.Find] topExplorer=0x{topExplorer.ToInt64():X} '{GetClassNameSafe(topExplorer)}'");
        if (topExplorer == IntPtr.Zero) return IntPtr.Zero;

        bool isExplorer = IsExplorerWindow(topExplorer);
        Console.WriteLine($"[ShellHelper.Find] IsExplorerWindow={isExplorer}");
        if (!isExplorer) return IntPtr.Zero;

        _foundListView = IntPtr.Zero;
        EnumChildWindows(topExplorer, EnumChildCallback, IntPtr.Zero);
        if (_foundListView != IntPtr.Zero)
        {
            Console.WriteLine($"[ShellHelper.Find] EnumChildWindows found '{GetClassNameSafe(_foundListView)}' at 0x{_foundListView.ToInt64():X}");
            return _foundListView;
        }

        Console.WriteLine("[ShellHelper.Find] EnumChildWindows found nothing, trying ancestor walk");
        var parent = hwnd;
        for (int i = 0; i < 15; i++)
        {
            parent = PInvoke.GetAncestor(parent, 2);
            if (parent == IntPtr.Zero) break;

            PInvoke.GetClassName(parent, sb, sb.Capacity);
            var pc = sb.ToString();

            if (pc == SHELLDLL_DefViewClassName || pc.Contains("SysListView32"))
            {
                Console.WriteLine($"[ShellHelper.Find] Ancestor[{i}] is '{pc}'");
                return parent;
            }
            if (pc == CabinetWClass || pc == ExploreWClass || pc == DirectUIHWNDClassName || pc == "Microsoft.UI.Content.DesktopChildSiteBridge")
            {
                var found = FindChildRecursive(parent, SHELLDLL_DefViewClassName);
                if (found == IntPtr.Zero)
                    found = FindChildRecursive(parent, SysListView32ClassName);
                if (found != IntPtr.Zero)
                {
                    Console.WriteLine($"[ShellHelper.Find] FindChildRecursive from '{pc}' found '{GetClassNameSafe(found)}'");
                    return found;
                }
                Console.WriteLine($"[ShellHelper.Find] FindChildRecursive from '{pc}' found nothing");
                break;
            }
        }

        Console.WriteLine("[ShellHelper.Find] all methods failed");
        return IntPtr.Zero;
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
            if (element == null)
            {
                Console.WriteLine("[ShellHelper.UIA] FromPoint returned null");
                return false;
            }

            Console.WriteLine($"[ShellHelper.UIA] element: Name='{element.Current.Name}' ControlType={element.Current.ControlType.ProgrammaticName} Class='{element.Current.ClassName}'");

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
                    Console.WriteLine($"[ShellHelper.UIA] Found item: Name='{itemName}' Type={ctrlType.ProgrammaticName}");

                    if (string.IsNullOrEmpty(itemName))
                    {
                        Console.WriteLine("[ShellHelper.UIA] item has empty Name");
                        return false;
                    }

                    var topExplorer = FindTopLevelExplorerWindow(PInvoke.WindowFromPoint(screenPoint));
                    if (topExplorer == IntPtr.Zero)
                    {
                        Console.WriteLine("[ShellHelper.UIA] no top explorer hwnd");
                        return false;
                    }

                    var currentDir = GetExplorerCurrentDirectory(topExplorer, out _);
                    Console.WriteLine($"[ShellHelper.UIA] CurrentDir='{currentDir}'");

                    if (string.IsNullOrEmpty(currentDir))
                    {
                        Console.WriteLine("[ShellHelper.UIA] cannot determine current directory");
                        return false;
                    }

                    var fullPath = Path.Combine(currentDir, itemName);
                    Console.WriteLine($"[ShellHelper.UIA] FullPath='{fullPath}' FileExists={File.Exists(fullPath)} DirExists={Directory.Exists(fullPath)}");

                    if (File.Exists(fullPath) || Directory.Exists(fullPath))
                    {
                        filePath = fullPath;
                        return true;
                    }

                    Console.WriteLine("[ShellHelper.UIA] path does not exist on disk");
                    return false;
                }

                if (currentHwnd != IntPtr.Zero)
                {
                    var clsName = GetClassNameSafe(currentHwnd);
                    if (clsName == CabinetWClass || clsName == ExploreWClass)
                    {
                        Console.WriteLine($"[ShellHelper.UIA] reached top Explorer window (class='{clsName}') -> not a file item");
                        return false;
                    }
                }

                if (className == CabinetWClass || className == ExploreWClass)
                {
                    Console.WriteLine($"[ShellHelper.UIA] reached top Explorer window via className -> not a file item");
                    return false;
                }

                current = walker.GetParent(current);
                depth++;
            }

            Console.WriteLine("[ShellHelper.UIA] walked off tree without finding item");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ShellHelper.UIA] exception: {ex.Message}");
            return false;
        }
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

    private const int LVM_FIRST = 0x1000;
    private const int LVM_SUBITEMHITTEST = LVM_FIRST + 57;
    private const int LVM_GETITEMCOUNT = LVM_FIRST + 4;
    private const uint LVM_GETITEMW = LVM_FIRST + 76;
    private const int LVIF_TEXT = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    private struct LVHITTESTINFO
    {
        public int pt_x;
        public int pt_y;
        public uint flags;
        public int iItem;
        public int iSubItem;
        public int iGroup;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LVITEM
    {
        public uint mask;
        public int iItem;
        public int iSubItem;
        public uint state;
        public uint stateMask;
        public IntPtr pszText;
        public int cchTextMax;
        public int iImage;
        public IntPtr lParam;
        public int iIndent;
        public int iGroupId;
        public uint cColumns;
        public IntPtr puColumns;
        public IntPtr piColFmt;
        public int iGroup;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize,
        uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress,
        IntPtr lpBuffer, uint dwSize, out uint lpNumberOfBytesRead);

    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress,
        IntPtr lpBuffer, uint dwSize, out uint lpNumberOfBytesWritten);

    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_RESERVE = 0x2000;
    private const uint MEM_RELEASE = 0x8000;
    private const uint PAGE_READWRITE = 0x04;
    private const uint PROCESS_ALL_ACCESS = 0x1F0FFF;
    private const uint PROCESS_VM_READ = 0x0010;
    private const uint PROCESS_VM_WRITE = 0x0020;
    private const uint PROCESS_VM_OPERATION = 0x0008;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;

    private static bool WriteProcessMemorySafe(IntPtr hProcess, IntPtr remoteAddr, IntPtr localAddr, uint size)
    {
        uint written;
        return WriteProcessMemory(hProcess, remoteAddr, localAddr, size, out written);
    }

    private static bool GetItemPathFromListView(IntPtr listViewHandle, POINT screenPoint, out string? filePath)
    {
        filePath = null;

        uint pid;
        PInvoke.GetWindowThreadProcessId(listViewHandle, out pid);
        Console.WriteLine($"[ShellHelper] PID={pid}");
        if (pid == 0) return false;

        var hProcess = OpenProcess(PROCESS_VM_READ | PROCESS_VM_OPERATION | PROCESS_QUERY_INFORMATION, false, (int)pid);
        if (hProcess == IntPtr.Zero)
        {
            Console.WriteLine($"[ShellHelper] OpenProcess failed err={Marshal.GetLastWin32Error()}");
            return false;
        }

        try
        {
            int hittestSize = Marshal.SizeOf(typeof(LVHITTESTINFO));
            Console.WriteLine($"[ShellHelper] LVHITTESTINFO size={hittestSize} (IntPtr.Size={IntPtr.Size})");
            var remoteHitTestAddr = VirtualAllocEx(hProcess, IntPtr.Zero, (uint)hittestSize, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
            if (remoteHitTestAddr == IntPtr.Zero) return false;

            try
            {
                var localHitTest = new LVHITTESTINFO
                {
                    pt_x = screenPoint.X,
                    pt_y = screenPoint.Y,
                    flags = 0,
                    iItem = -1,
                    iSubItem = -1,
                    iGroup = -1
                };

                var hitTestPtr = Marshal.AllocHGlobal(hittestSize);
                LVHITTESTINFO hitTestOut;
                try
                {
                    Marshal.StructureToPtr(localHitTest, hitTestPtr, false);
                    WriteProcessMemorySafe(hProcess, remoteHitTestAddr, hitTestPtr, (uint)hittestSize);

                    var clientPoint = screenPoint;
                    ScreenToClient(listViewHandle, ref clientPoint);

                    var msgResult = SendMessage(listViewHandle, LVM_SUBITEMHITTEST, IntPtr.Zero, remoteHitTestAddr);
                    Console.WriteLine($"[ShellHelper] LVM_SUBITEMHITTEST result={msgResult.ToInt32()} (0x{msgResult.ToInt64():X})");
                    if (msgResult.ToInt32() == -1) return false;

                    uint bytesRead;
                    if (!ReadProcessMemory(hProcess, remoteHitTestAddr, hitTestPtr, (uint)hittestSize, out bytesRead))
                        return false;

                    hitTestOut = Marshal.PtrToStructure<LVHITTESTINFO>(hitTestPtr);
                    Console.WriteLine($"[ShellHelper] HitTest: iItem={hitTestOut.iItem} iSubItem={hitTestOut.iSubItem} flags=0x{hitTestOut.flags:X}");
                }
                finally { Marshal.FreeHGlobal(hitTestPtr); }

                if (hitTestOut.iItem < 0)
                {
                    Console.WriteLine("[ShellHelper] iItem<0 (blank area or no hit)");
                    return false;
                }

                var itemCount = (int)SendMessage(listViewHandle, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
                Console.WriteLine($"[ShellHelper] LVM_GETITEMCOUNT={itemCount}");

                int targetItem = hitTestOut.iItem;
                if (targetItem < 0)
                {
                    Console.WriteLine("[ShellHelper] iItem<0 (blank area or no hit)");
                    return false;
                }

                return TryGetItemShellPath(hProcess, listViewHandle, targetItem, out filePath);
            }
            finally { VirtualFreeEx(hProcess, remoteHitTestAddr, 0, MEM_RELEASE); }
        }
        finally { CloseHandle(hProcess); }
    }

    private static bool TryGetItemShellPath(IntPtr hProcess, IntPtr listViewHandle, int itemIndex, out string? filePath)
    {
        filePath = null;

        var explorerHwnd = FindTopLevelExplorerWindow(listViewHandle);
        Console.WriteLine($"[ShellHelper] explorerHwnd=0x{explorerHwnd.ToInt64():X}");
        if (explorerHwnd == IntPtr.Zero) return false;

        var itemName = GetItemDisplayName(listViewHandle, hProcess, itemIndex);
        Console.WriteLine($"[ShellHelper] LVM_GETITEMW itemIndex={itemIndex} -> ItemName='{itemName}'");

        var currentDir = GetExplorerCurrentDirectory(explorerHwnd, out _);
        Console.WriteLine($"[ShellHelper] CurrentDir='{currentDir}'");

        if (string.IsNullOrEmpty(itemName))
        {
            Console.WriteLine("[ShellHelper] ItemName empty - SHELLDLL_DefView may not support LVM_GETITEMW!");
            return false;
        }

        if (string.IsNullOrEmpty(currentDir))
        {
            Console.WriteLine("[ShellHelper] CurrentDir empty - cannot build full path");
            return false;
        }

        var fullPath = Path.Combine(currentDir, itemName);
        Console.WriteLine($"[ShellHelper] FullPath='{fullPath}'");
        Console.WriteLine($"[ShellHelper] DirExists={Directory.Exists(currentDir)} FileExists={File.Exists(fullPath)} ItemDirExists={Directory.Exists(fullPath)}");

        if (Directory.Exists(currentDir) && (File.Exists(fullPath) || Directory.Exists(fullPath)))
        {
            filePath = fullPath;
            return true;
        }

        return false;
    }

    private static int _pszTextOffset;
    private static int _cchTextMaxOffset;
    private static int _lvItemSize;
    private static bool _lvItemOffsetsInitialized;

    private static void InitLvItemOffsets()
    {
        if (_lvItemOffsetsInitialized) return;

        _lvItemSize = Marshal.SizeOf(typeof(LVITEM));
        _pszTextOffset = Marshal.OffsetOf<LVITEM>(nameof(LVITEM.pszText)).ToInt32();
        _cchTextMaxOffset = Marshal.OffsetOf<LVITEM>(nameof(LVITEM.cchTextMax)).ToInt32();
        _lvItemOffsetsInitialized = true;

        Console.WriteLine($"[ShellHelper] LVITEM: size={_lvItemSize} pszText@+{_pszTextOffset} cchTextMax@+{_cchTextMaxOffset}");
    }

    private static string? GetItemDisplayName(IntPtr listViewHandle, IntPtr hProcess, int itemIndex)
    {
        InitLvItemOffsets();

        const int textBufferSize = 512;
        var remoteItemAddr = VirtualAllocEx(hProcess, IntPtr.Zero, (uint)(_lvItemSize + textBufferSize), MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
        if (remoteItemAddr == IntPtr.Zero) return null;

        var remoteTextAddr = remoteItemAddr + _lvItemSize;

        try
        {
            var localItem = new LVITEM
            {
                mask = LVIF_TEXT,
                iItem = itemIndex,
                iSubItem = 0,
                state = 0,
                stateMask = 0,
                pszText = remoteTextAddr,
                cchTextMax = textBufferSize,
                iImage = 0,
                lParam = IntPtr.Zero,
                iIndent = 0,
                iGroupId = 0,
                cColumns = 0,
                puColumns = IntPtr.Zero,
                piColFmt = IntPtr.Zero,
                iGroup = 0
            };

            var localItemPtr = Marshal.AllocHGlobal(_lvItemSize);
            try
            {
                Marshal.StructureToPtr(localItem, localItemPtr, false);

                if (!WriteProcessMemorySafe(hProcess, remoteItemAddr, localItemPtr, (uint)_lvItemSize))
                {
                    Console.WriteLine($"[ShellHelper] WriteProcessMemory LVITEM failed err={Marshal.GetLastWin32Error()}");
                    return null;
                }

                var result = SendMessage(listViewHandle, LVM_GETITEMW, IntPtr.Zero, remoteItemAddr);
                Console.WriteLine($"[ShellHelper] LVM_GETITEMW result=0x{result.ToInt64():X}");
                if (result == IntPtr.Zero) return null;

                var textBuffer = Marshal.AllocHGlobal(textBufferSize);
                try
                {
                    uint bytesRead;
                    if (!ReadProcessMemory(hProcess, remoteTextAddr, textBuffer, textBufferSize, out bytesRead))
                    {
                        Console.WriteLine($"[ShellHelper] ReadProcessMemory text failed err={Marshal.GetLastWin32Error()}");
                        return null;
                    }

                    var text = Marshal.PtrToStringUni(textBuffer);
                    if (string.IsNullOrEmpty(text)) return null;

                    int nullIdx = text.IndexOf('\0');
                    if (nullIdx >= 0) text = text.Substring(0, nullIdx);

                    Console.WriteLine($"[ShellHelper] GetItemDisplayName raw text length={text.Length}");
                    return text;
                }
                finally { Marshal.FreeHGlobal(textBuffer); }
            }
            finally { Marshal.FreeHGlobal(localItemPtr); }
        }
        finally { VirtualFreeEx(hProcess, remoteItemAddr, 0, MEM_RELEASE); }
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
            Console.WriteLine($"[ShellHelper] ConvertShellUrlToPath input='{url}'");
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
                Console.WriteLine($"[ShellHelper] URL doesn't start with file://: {url}");
                return null;
            }

            path = Uri.UnescapeDataString(path);
            if (path.EndsWith("\\")) path = path.TrimEnd('\\');

            Console.WriteLine($"[ShellHelper] Decoded path='{path}'");

            if (Directory.Exists(path)) return path;

            var parent = Path.GetDirectoryName(path);
            Console.WriteLine($"[ShellHelper] Path not found, trying parent='{parent}' exists={Directory.Exists(parent)}");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ShellHelper] ConvertShellUrlToPath error: {ex.Message}");
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
            Console.WriteLine($"[ShellHelper] Fallback: window title='{title}'");

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
        out string pszPath);

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

        try
        {
            var iid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
            SHCreateItemFromParsingName(filePath, IntPtr.Zero, iid, out var item);
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
        }
    }

    private static string? GetKnownFolderPath(Guid guid)
    {
        try
        {
            var hr = SHGetKnownFolderPath(guid, 0, IntPtr.Zero, out var path);
            if (hr == 0) return path;
        }
        catch { }
        return null;
    }
}
