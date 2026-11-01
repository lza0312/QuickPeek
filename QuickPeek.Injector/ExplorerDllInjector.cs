using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace QuickPeek.Injector;

public static class ExplorerDllInjector
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize,
        uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress,
        IntPtr lpBuffer, uint dwSize, out uint lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes,
        uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, out IntPtr lpThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeThread(IntPtr hThread, out uint lpExitCode);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);

    private const uint PROCESS_ALL_ACCESS = 0x001F0FFF;
    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_RESERVE = 0x2000;
    private const uint MEM_RELEASE = 0x8000;
    private const uint PAGE_READWRITE = 0x04;
    private const uint MEM_COMMIT_RESERVE = MEM_COMMIT | MEM_RESERVE;
    private const uint INFINITE = 0xFFFFFFFF;

    public static int[] GetExplorerPids()
    {
        var processIds = new List<int>();
        foreach (var proc in System.Diagnostics.Process.GetProcessesByName("explorer"))
        {
            processIds.Add(proc.Id);
            proc.Dispose();
        }
        return processIds.ToArray();
    }

    public static bool InjectDll(int processId, string dllPath)
    {
        if (!File.Exists(dllPath))
        {
            throw new FileNotFoundException($"DLL not found: {dllPath}", dllPath);
        }

        var hProcess = OpenProcess(PROCESS_ALL_ACCESS, false, processId);
        if (hProcess == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Failed to open process {processId}. Error: {Marshal.GetLastWin32Error()}");
        }

        try
        {
            var dllPathBytes = Encoding.Unicode.GetBytes(dllPath + '\0');
            var remoteMemory = VirtualAllocEx(hProcess, IntPtr.Zero, (uint)dllPathBytes.Length,
                MEM_COMMIT_RESERVE, PAGE_READWRITE);

            if (remoteMemory == IntPtr.Zero)
            {
                throw new InvalidOperationException($"VirtualAllocEx failed. Error: {Marshal.GetLastWin32Error()}");
            }

            try
            {
                var localBuffer = Marshal.UnsafeAddrOfPinnedArrayElement(dllPathBytes, 0);
                if (!WriteProcessMemory(hProcess, remoteMemory, localBuffer,
                    (uint)dllPathBytes.Length, out _))
                {
                    throw new InvalidOperationException($"WriteProcessMemory failed. Error: {Marshal.GetLastWin32Error()}");
                }

                var kernel32 = GetModuleHandle("kernel32.dll");
                if (kernel32 == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Failed to get kernel32.dll handle");
                }

                var loadLibraryAddr = GetProcAddress(kernel32, "LoadLibraryW");
                if (loadLibraryAddr == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Failed to get LoadLibraryW address");
                }

                var hThread = CreateRemoteThread(hProcess, IntPtr.Zero, 0,
                    loadLibraryAddr, remoteMemory, 0, out var threadId);

                if (hThread == IntPtr.Zero)
                {
                    throw new InvalidOperationException($"CreateRemoteThread failed. Error: {Marshal.GetLastWin32Error()}");
                }

                WaitForSingleObject(hThread, 5000);

                GetExitCodeThread(hThread, out var exitCode);
                Marshal.FreeHGlobal(hThread);

                return exitCode != 0;
            }
            finally
            {
                VirtualFreeEx(hProcess, remoteMemory, 0, MEM_RELEASE);
            }
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }

    public static bool UninjectDll(int processId, string dllPath)
    {
        var hProcess = OpenProcess(PROCESS_ALL_ACCESS, false, processId);
        if (hProcess == IntPtr.Zero) return false;

        try
        {
            var kernel32 = GetModuleHandle("kernel32.dll");
            var freeLibraryAddr = GetProcAddress(kernel32, "FreeLibrary");
            if (freeLibraryAddr == IntPtr.Zero) return false;

            var dllName = Path.GetFileNameWithoutExtension(dllPath);
            var moduleHandle = GetModuleHandle(dllName);
            if (moduleHandle == IntPtr.Zero) return false;

            CreateRemoteThread(hProcess, IntPtr.Zero, 0, freeLibraryAddr,
                moduleHandle, 0, out _);

            return true;
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }
}
