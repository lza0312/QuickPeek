using System;

namespace QuickPeek.Native;

public static class Win32Constants
{
    public const int WH_MOUSE_LL = 14;
    public const int WM_MOUSEMOVE = 0x0200;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_RBUTTONDOWN = 0x0204;
    public const int WM_MBUTTONDOWN = 0x0207;
    public const int WM_MOUSEWHEEL = 0x020A;
    public const int WM_LBUTTONDBLCLK = 0x0203;

    public const int GWL_WNDPROC = -4;

    public const uint SW_RESTORE = 9;
    public const uint SW_SHOW = 5;
    public const uint SW_HIDE = 0;

    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WS_EX_NOACTIVATE = 0x08000000;
    public const uint WS_EX_TOPMOST = 0x00000008;
    public const uint WS_EX_LAYERED = 0x00080000;

    public const uint WPF_RESTORETOMAXIMIZED = 2;
    public const int WM_SYSCOMMAND = 0x0112;
    public const int SC_MINIMIZE = 0xF020;
    public const int SC_MAXIMIZE = 0xF030;

    public const uint SHGFI_DISPLAYNAME = 0x000000200;
    public const uint SHGFI_PIDL = 0x000000008;
    public const uint SHGFI_SHELLICONSIZE = 0x000000004;
    public const uint SHGFI_ICON = 0x000000100;

    public const uint LWA_COLORKEY = 0x00000001;
    public const uint LWA_ALPHA = 0x00000002;
}
