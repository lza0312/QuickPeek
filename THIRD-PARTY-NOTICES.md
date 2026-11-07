# 第三方声明

QuickPeek 使用或分发以下第三方组件。各组件版权归其作者所有，适用其自身的许可证条款。

## 运行时依赖

| 组件 | 版本 | 许可证 | 项目地址 |
| --- | --- | --- | --- |
| FlyleafLib | 3.11.13 | LGPL-3.0-or-later | https://github.com/SuRGeoNix/Flyleaf |
| FFmpeg 共享库 | n9.0.2（avcodec-63 / avformat-63 / avutil-61 / avfilter-12 / swresample-7 / swscale-10） | LGPL-3.0-or-later（见下方说明） | https://ffmpeg.org |
| Newtonsoft.Json | 13.0.3 | MIT | https://www.newtonsoft.com/json |
| Hardcodet.NotifyIcon.Wpf | 1.1.0 | Code Project Open License (CPOL) 1.02 | https://github.com/hardcodet/wpf-notifyicon |
| System.Drawing.Common | 8.0.0 | MIT | https://github.com/dotnet/winforms |

## FlyleafLib（LGPL-3.0-or-later）

- QuickPeek 以未修改的 NuGet 二进制形式引用 FlyleafLib。
- 许可证全文：https://www.gnu.org/licenses/lgpl-3.0.html
- 源代码：https://github.com/SuRGeoNix/Flyleaf
- 如需将本项目中的 FlyleafLib 替换为自行编译的版本，直接替换发布目录下的 `FlyleafLib.dll` 即可（动态链接，无需重新编译 QuickPeek）。

## FFmpeg（LGPL）

- 随包分发的 FFmpeg DLL 为 FlyleafLib 提供的共享库构建，二进制内标注为
  `libavcodec license: LGPL version 3 or later`，对应 LGPL-3.0-or-later。
- 许可证全文：https://www.gnu.org/licenses/lgpl-3.0.html
- 源代码与构建说明：https://ffmpeg.org/download.html
- 若你自行替换为启用 GPL 组件（如 `--enable-gpl`）的 FFmpeg 构建，则相应构建适用 GPL，请同时遵守 GPL 条款。
- 完整版（full）包含这些 DLL；精简版（lite）不包含，因而不分发 FFmpeg。

## 其他

- Hardcodet.NotifyIcon.Wpf 采用 CPOL 1.02：https://www.codeproject.com/info/cpol10.aspx
- Newtonsoft.Json、System.Drawing.Common 采用 MIT 许可证：https://opensource.org/licenses/MIT
