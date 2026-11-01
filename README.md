# QuickPeek

Windows 资源管理器文件悬停预览工具。鼠标在资源管理器中的文件上停留片刻，即可在屏幕角落弹出浮动预览窗口，无需打开对应程序。

## 功能

- **图片预览**：按原图比例自适应窗口，标题栏显示日期、分辨率、文件大小
- **视频预览**：基于 FlyleafLib 播放，支持循环播放、播放/暂停、快进快退、倍速与音量调节
- **音频预览**：与视频共用播放器，以紧凑面板显示播放信息
- **文本预览**：快速查看文本文件开头内容，可设置最大文件大小
- **播放信息浮层**：内容下方悬浮显示 `已播放 hh:mm:ss/总时长 hh:mm:ss`、进度、倍速、音量
- **浮窗位置**：四角可选，宽边距/高边距按贴边方向分别控制
- **尺寸与外观**：可配置窗口宽高、窗口/内容透明度、深色/浅色主题；关闭"显示详细信息"后为无边框浮窗
- **缩放适配**：设置页缩放与浮窗文字缩放（100%–300%），仅放大文字 UI，不改变预览尺寸
- **快捷键**：Ctrl+滚轮调节音量 / 快进快退，支持自定义音量加减、快进快退、加速减速快捷键
- **其他**：开机自启、启动最小化、空白区域双击返回上一层、托盘常驻

## 环境要求

- Windows 10 / 11
- .NET 8 SDK
- Visual Studio 2022（可选，也可直接用 `dotnet` CLI）

## 构建

```bash
git clone https://github.com/lza0312/QuickPeek.git
cd QuickPeek
dotnet build QuickPeek.sln
```

运行：

```bash
dotnet run --project QuickPeek
```

## FFmpeg 依赖

视频/音频预览由 [FlyleafLib](https://github.com/amfdev/FlyleafLib) 驱动，需要 FFmpeg 7.x 的共享库。由于体积较大，仓库不包含这些 DLL，请将它们放到：

```
QuickPeek/ffmpeg/bin/
├── avcodec-63.dll
├── avdevice-63.dll
├── avfilter-12.dll
├── avformat-63.dll
├── avutil-61.dll
├── swresample-7.dll
└── swscale-10.dll
```

构建时 `QuickPeek.csproj` 会把这些文件复制到输出目录的 `ffmpeg` 子目录。缺少 DLL 时图片与文本预览仍可用，视频/音频预览会回退为 Shell 缩略图。

## 配置与日志

- 配置：`%APPDATA%\QuickPeek\settings.json`
- 日志：`%APPDATA%\QuickPeek\quickpeek.log`

## 项目结构

| 目录 | 说明 |
| --- | --- |
| `QuickPeek` | WPF 主程序：悬停钩子、预览窗口、播放器、设置界面 |
| `QuickPeek.Injector` | 注入辅助库（实验性） |
| `QuickPeek.InjectorHost` | 注入命令行宿主（实验性） |

## 使用说明

1. 启动 QuickPeek，托盘出现图标（默认启动时最小化）
2. 在资源管理器中把鼠标停在文件图标上，超过设定的悬停延迟即弹出预览
3. 通过托盘菜单或设置窗口调整位置、尺寸、边距、透明度与快捷键
4. 移动鼠标或点击即关闭预览
