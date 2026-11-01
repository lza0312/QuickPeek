﻿﻿﻿using System;
using System.IO;
using QuickPeek.Injector;

namespace QuickPeek.InjectorHost;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] == "--help" || args[0] == "-h" || args[0] == "/?")
        {
            PrintUsage();
            return 0;
        }

        var command = args[0].ToLowerInvariant();

        switch (command)
        {
            case "inject":
                return HandleInject(args);
            case "uninject":
                return HandleUninject(args);
            case "list":
                HandleList();
                return 0;
            case "restart-explorer":
                HandleRestartExplorer();
                return 0;
            default:
                Console.WriteLine($"未知命令: {command}");
                PrintUsage();
                return 1;
        }
    }

    static void PrintUsage()
    {
        Console.WriteLine("QuickPeek DLL 注入工具");
        Console.WriteLine();
        Console.WriteLine("用法:");
        Console.WriteLine("  QuickPeek.InjectorHost inject <path-to-dll> [pid]     将 DLL 注入到 explorer.exe");
        Console.WriteLine("  QuickPeek.InjectorHost uninject <path-to-dll> [pid]   从 explorer.exe 卸载 DLL");
        Console.WriteLine("  QuickPeek.InjectorHost list                            列出所有 explorer.exe 进程");
        Console.WriteLine("  QuickPeek.InjectorHost restart-explorer                重启 explorer.exe");
        Console.WriteLine();
        Console.WriteLine("说明:");
        Console.WriteLine("  - 如果不指定 pid，将注入/卸载到所有 explorer.exe 进程");
        Console.WriteLine("  - 需要以管理员权限运行");
    }

    static int HandleInject(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("错误: 请指定要注入的 DLL 路径");
            return 1;
        }

        var dllPath = Path.GetFullPath(args[1]);
        if (!File.Exists(dllPath))
        {
            Console.WriteLine($"错误: DLL 不存在 - {dllPath}");
            return 1;
        }

        int[] pids;
        if (args.Length >= 3 && int.TryParse(args[2], out int specificPid))
        {
            pids = new[] { specificPid };
        }
        else
        {
            pids = ExplorerDllInjector.GetExplorerPids();
        }

        if (pids.Length == 0)
        {
            Console.WriteLine("错误: 未找到 explorer.exe 进程。请先打开文件资源管理器。");
            return 1;
        }

        int successCount = 0;
        foreach (var pid in pids)
        {
            try
            {
                Console.Write($"正在注入到 PID {pid}... ");
                if (ExplorerDllInjector.InjectDll(pid, dllPath))
                {
                    Console.WriteLine("成功 ✓");
                    successCount++;
                }
                else
                {
                    Console.WriteLine("失败 ✗");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"失败 ✗ ({ex.Message})");
            }
        }

        Console.WriteLine($"\n完成: {successCount}/{pids.Length} 个进程注入成功");
        return successCount == pids.Length ? 0 : 1;
    }

    static int HandleUninject(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("错误: 请指定要卸载的 DLL 路径");
            return 1;
        }

        var dllPath = Path.GetFullPath(args[1]);

        int[] pids;
        if (args.Length >= 3 && int.TryParse(args[2], out int specificPid))
        {
            pids = new[] { specificPid };
        }
        else
        {
            pids = ExplorerDllInjector.GetExplorerPids();
        }

        int successCount = 0;
        foreach (var pid in pids)
        {
            try
            {
                Console.Write($"正在从 PID {pid} 卸载... ");
                if (ExplorerDllInjector.UninjectDll(pid, dllPath))
                {
                    Console.WriteLine("成功 ✓");
                    successCount++;
                }
                else
                {
                    Console.WriteLine("失败 ✗");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"失败 ✗ ({ex.Message})");
            }
        }

        Console.WriteLine($"\n完成: {successCount}/{pids.Length} 个进程卸载成功");
        return successCount == pids.Length ? 0 : 1;
    }

    static void HandleList()
    {
        var pids = ExplorerDllInjector.GetExplorerPids();
        Console.WriteLine($"找到 {pids.Length} 个 explorer.exe 进程:");
        foreach (var pid in pids)
        {
            Console.WriteLine($"  PID: {pid}");
        }
    }

    static void HandleRestartExplorer()
    {
        Console.WriteLine("正在重启 explorer.exe...");
        try
        {
            foreach (var proc in System.Diagnostics.Process.GetProcessesByName("explorer"))
            {
                proc.Kill();
                proc.WaitForExit(3000);
            }
        }
        catch
        {
        }

        System.Diagnostics.Process.Start("explorer.exe");
        Console.WriteLine("explorer.exe 已重启");
    }
}
