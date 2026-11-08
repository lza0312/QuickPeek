using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using QuickPeek.Models;
using QuickPeek.Native;
using QuickPeek.Preview;
using QuickPeek.Services;

namespace QuickPeek.Core;

public class PreviewManager : IDisposable
{
    private readonly GlobalMouseHook _mouseHook;
    private readonly System.Windows.Threading.DispatcherTimer _durationTimer;
    private AppConfig _config;
    private string? _lastPreviewedFile;
    private bool _isDisposed;
    private VideoPreviewer? _videoPreviewer;
    private VideoPreviewer? _activeVideoPreviewer;
    private string? _currentVideoPath;

    private int _previewVersion;
    private CancellationTokenSource? _pendingPreviewCts;

    private readonly VideoHotkeyHook _hotkeyHook;

    private KeyCombo? _volumeUpCombo;
    private KeyCombo? _volumeDownCombo;
    private KeyCombo? _stepForwardCombo;
    private KeyCombo? _stepBackwardCombo;
    private KeyCombo? _speedUpCombo;
    private KeyCombo? _speedDownCombo;

    public event EventHandler<bool>? EnabledChanged;
    public bool IsEnabled => _config.Enabled;

    public PreviewManager()
    {
        _config = SettingsService.Load();
        ParseHotkeys();

        _hotkeyHook = new VideoHotkeyHook(
            combo => _activeVideoPreviewer != null && IsVideoHotkey(combo),
            combo => PostToUi(() => TryHandleVideoHotkey(combo)));

        _mouseHook = new GlobalMouseHook
        {
            HoverThresholdMs = _config.HoverDelayMs
        };

        _mouseHook.HoverDetected += OnHoverDetected;
        _mouseHook.MouseButtonClicked += OnMouseClicked;
        _mouseHook.MouseWheelScrolled += OnMouseWheelScrolled;
        _mouseHook.MouseWheelScrolledH += OnMouseWheelScrolledH;
        _mouseHook.MouseDoubleClicked += OnMouseDoubleClicked;

        _durationTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _durationTimer.Tick += OnDurationTimerTick;
    }

    public void Start()
    {
        if (!_config.Enabled) return;

        _mouseHook.HoverThresholdMs = _config.HoverDelayMs;
        _mouseHook.Start();
        _hotkeyHook.Start();
        Logger.Info($"{AppIdentity.Name} 已启动");
    }

    public void Stop()
    {
        _mouseHook.Stop();
        _hotkeyHook.Stop();
        HidePreview();
    }

    public void UpdateConfig(AppConfig config)
    {
        _config = config;
        SettingsService.Save(_config);
        ParseHotkeys();
        _mouseHook.HoverThresholdMs = _config.HoverDelayMs;

        if (_config.Enabled)
        {
            _mouseHook.Start();
            _hotkeyHook.Start();
        }
        else
        {
            _mouseHook.Stop();
            _hotkeyHook.Stop();
        }
        EnabledChanged?.Invoke(this, _config.Enabled);
    }

    private int _isHoverProcessing;
    private long _hoverStartedTicks;

    private const long HoverWatchdogMs = 10_000;

    /// <summary>
    /// 悬停判定会调用 UI Automation / Shell COM，单次可达数百毫秒。
    /// 这里放到后台线程执行，避免卡住资源管理器和设置窗口。
    /// </summary>
    private void OnHoverDetected(object? sender, PInvoke.POINT pt)
    {
        if (!_config.Enabled) return;

        if (Interlocked.CompareExchange(ref _isHoverProcessing, 1, 0) != 0)
        {
            // 看门狗：UIA 偶尔会卡住不返回，超时后强制允许下一次判定，
            // 否则悬停功能会永久失效
            var started = Interlocked.Read(ref _hoverStartedTicks);
            if (Environment.TickCount64 - started < HoverWatchdogMs) return;

            Logger.Debug("上一次悬停判定超过 10 秒未结束，强制复位");
        }
        Interlocked.Exchange(ref _hoverStartedTicks, Environment.TickCount64);

        var point = pt;
        var detectedAt = Stopwatch.GetTimestamp();

        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                if (!ShellHelper.GetExplorerItemAtPoint(point, out var filePath) || string.IsNullOrEmpty(filePath))
                {
                    Logger.Debug($"悬停点 ({point.X},{point.Y}) 未命中文件，隐藏预览");
                    PostToUi(HidePreview);
                    return;
                }

                // 分段计时：资源管理器定位（UIA / Shell COM）是悬停链路里最不可控的一段
                var locateMs = Stopwatch.GetElapsedTime(detectedAt).TotalMilliseconds;

                var typeSw = Stopwatch.StartNew();
                var category = FileTypeDetector.Detect(filePath);
                var typeMs = typeSw.Elapsed.TotalMilliseconds;

                Logger.Debug($"悬停命中 {filePath} ({category})");

                switch (category)
                {
                    case FileCategory.Image:
                        if (!_config.ShowImages) { PostToUi(HidePreview); return; }
                        break;
                    case FileCategory.Video:
                        if (!_config.ShowVideos) { PostToUi(HidePreview); return; }
                        break;
                    case FileCategory.Text:
                        if (!_config.ShowTexts) { PostToUi(HidePreview); return; }
                        break;
                    case FileCategory.Audio:
                        if (!_config.ShowAudios) { PostToUi(HidePreview); return; }
                        break;
                    default:
                        PostToUi(HidePreview);
                        return;
                }

                if (filePath == _lastPreviewedFile) return;

                PostToUi(() => ShowPreview(filePath, category, locateMs, typeMs, detectedAt));
            }
            catch (Exception ex)
            {
                Logger.Error("处理悬停失败", ex);
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _isHoverProcessing, 0);
            }
        });
    }

    private static void PostToUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        if (dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }

    private void OnMouseClicked(object? sender, EventArgs e)
    {
        // 钩子回调里只投递消息，真正的窗口/播放器操作放回 UI 线程
        PostToUi(HidePreview);
    }

    private void OnMouseWheelScrolled(object? sender, WheelEventArgs e)
    {
        // Ctrl+滚轮：视频预览时调节音量
        if (_activeVideoPreviewer != null && _config.EnableVideoCtrlWheel && IsCtrlKeyPressed())
        {
            // 必须在钩子回调内同步设置 Handled，否则滚轮已经传给资源管理器
            e.Handled = true;
            var delta = e.Delta;
            PostToUi(() => HandleVolumeWheel(delta));
            return;
        }

        PostToUi(HidePreview);
    }

    private void HandleVolumeWheel(int delta)
    {
        var video = _activeVideoPreviewer;
        if (video == null) return;

        ShowStatus(delta > 0 ? video.VolumeUp() : video.VolumeDown());
    }

    private void OnMouseWheelScrolledH(object? sender, WheelEventArgs e)
    {
        // Ctrl+横向滚轮：视频预览时快进/后退（不隐藏预览）
        if (_activeVideoPreviewer == null || !_config.EnableVideoCtrlWheelH || !IsCtrlKeyPressed())
            return;

        e.Handled = true;
        var delta = e.Delta;
        PostToUi(() => HandleSeekWheel(delta));
    }

    private void HandleSeekWheel(int delta)
    {
        var video = _activeVideoPreviewer;
        if (video == null) return;

        ShowStatus(delta > 0 ? video.StepForward() : video.StepBackward());
    }

    private void OnMouseDoubleClicked(object? sender, PInvoke.POINT pt)
    {
        // 钩子回调内不能做耗时操作（UIA / Shell COM 可能超过 LowLevelHooksTimeout，
        // 导致系统静默摘掉钩子），这里立刻交回 UI 线程处理
        PostToUi(() => HandleBlankDoubleClick(pt));
    }

    private void HandleBlankDoubleClick(PInvoke.POINT pt)
    {
        try
        {
            if (!_config.EnableBlankDoubleClickBack) return;

            if (!ShellHelper.IsExplorerWindowAtPoint(pt))
            {
                Logger.Info($"双击 ({pt.X},{pt.Y}) 不在资源管理器内，忽略");
                return;
            }

            if (!ShellHelper.HitTestExplorerBlankArea(pt))
            {
                Logger.Info($"双击 ({pt.X},{pt.Y}) 命中文件图标，不是空白区域");
                return;
            }

            Logger.Info($"双击空白区域 ({pt.X},{pt.Y}) -> 上层文件夹");
            HidePreview();

            var explorerHwnd = ShellHelper.FindExplorerWindowAtPoint(pt);
            Logger.Info($"目标资源管理器窗口 hwnd=0x{explorerHwnd.ToInt64():X}");
            SendUpOneLevel(explorerHwnd);
        }
        catch (Exception ex)
        {
            Logger.Error("双击返回失败", ex);
        }
    }

    private void ShowPreview(string filePath, FileCategory category,
        double locateMs = 0, double typeMs = 0, long detectedAt = 0)
    {
        CancelPendingPreview();
        var version = ++_previewVersion;
        var cts = new CancellationTokenSource();
        _pendingPreviewCts = cts;

        var previewWindow = Preview.PreviewWindow.Instance;
        if (previewWindow == null)
        {
            previewWindow = new Preview.PreviewWindow();
            Preview.PreviewWindow.SetInstance(previewWindow);
        }

        previewWindow.ApplyConfig(_config);

        var fileName = Path.GetFileName(filePath);

        // 先停止播放，再隐藏视频窗口（视频窗口隐藏时不再解绑宿主）
        _activeVideoPreviewer?.Stop();
        _activeVideoPreviewer = null;

        if (category != FileCategory.Video && category != FileCategory.Audio)
        {
            Preview.VideoWindow.Instance?.HidePreview();
            _currentVideoPath = null;
        }

        // 先标记，避免解码期间同一个文件被重复触发、反复取消
        _lastPreviewedFile = filePath;

        switch (category)
        {
            case FileCategory.Image:
                _ = LoadImagePreviewAsync(filePath, fileName, version, cts.Token,
                    locateMs, typeMs, detectedAt);
                break;

            case FileCategory.Video:
            case FileCategory.Audio:
                // 视频/音频都由独立的 Flyleaf 窗口渲染（PreviewWindow 是透明分层窗口，
                // 无法承载 Flyleaf 的原生渲染表面）
                previewWindow.ClearContent();
                previewWindow.Hide();

                _currentVideoPath = filePath;
                var video = EnsureVideoPreviewer();
                if (video.Load(filePath, true, true, _config.ImageSizeMode, _config))
                {
                    _activeVideoPreviewer = video;

                    var videoWindow = GetVideoWindow();
                    videoWindow.ApplyConfig(_config);
                    videoWindow.ShowVideo(video, fileName, BuildFileInfo(filePath, video.VideoWidth, video.VideoHeight),
                        video.VideoWidth, video.VideoHeight, category == FileCategory.Audio);

                    StartDurationTimer();
                    Logger.Debug($"{(category == FileCategory.Audio ? "音频" : "视频")}预览已加载 {fileName}");
                }
                else
                {
                    var reason = Preview.VideoEngine.IsLiteBuild
                        ? "精简版不支持视频/音频预览，已显示系统缩略图"
                        : "视频/音频引擎不可用（请检查 ffmpeg 目录）";
                    ShowVideoFallback(filePath, fileName, reason);
                }
                break;

            case FileCategory.Text:
                _ = LoadTextPreviewAsync(filePath, fileName, version, cts.Token);
                break;
        }
    }

    private void CancelPendingPreview()
    {
        var cts = _pendingPreviewCts;
        _pendingPreviewCts = null;
        if (cts == null) return;

        cts.Cancel();
    }

    /// <summary>超大图的总像素上限，避免整幅解码耗尽内存。</summary>
    private const long MaxImagePixels = 16_000_000;

    /// <summary>
    /// 解码宽度按「预览窗口物理像素」取，不再无条件乘 2：
    /// 实测 24MP JPEG 把解码宽度从 2560 降到 1280 只省约 10% 时间，但内存占用降到 1/4，
    /// 同样 64MB 预算下能容纳 14 张而不是 3 张，缓存命中率提升明显。
    /// </summary>
    private int GetImageDecodeWidth(double dpiScale)
        => (int)Math.Ceiling((_config.PreviewWidthPx > 0 ? _config.PreviewWidthPx : 1280)
            * Math.Max(1.0, dpiScale));

    private int GetImageDecodeHeight(double dpiScale)
        => (int)Math.Ceiling((_config.PreviewHeightPx > 0 ? _config.PreviewHeightPx : 720)
            * Math.Max(1.0, dpiScale));

    /// <summary>一次图片加载的产物与分段耗时。</summary>
    private sealed class ImageLoadOutcome
    {
        public System.Windows.Media.Imaging.BitmapSource? Bitmap;
        public int PixelWidth;
        public int PixelHeight;
        public ImageFileInfo Info = ImageFileInfo.Unknown;
        public bool CacheHit;
        public double StatMs;
        public double MetaMs;
        public double DecodeMs;
        public string InfoText = string.Empty;
    }

    /// <summary>显示宽高比（宽 / 高）；尺寸无效时返回 0。</summary>
    private static double ToAspect(int width, int height)
        => width > 0 && height > 0 ? (double)width / height : 0;

    /// <summary>
    /// 预览窗口尺寸只依赖「原图宽高比 + 配置上限」。
    /// 优先读原图文件头（含 EXIF 方向）；读不到时（HEIC/RAW 等）退回兜底缩略图的比例。
    /// </summary>
    private static double ResolveAspect(string filePath, System.Windows.Media.Imaging.BitmapSource? fallback = null)
    {
        var info = ImagePreviewer.ReadInfo(filePath);
        if (info.Aspect > 0) return info.Aspect;

        return fallback != null
            ? ToAspect(fallback.PixelWidth, fallback.PixelHeight)
            : 0;
    }

    private static string BuildCacheKey(string filePath, string variant)
    {
        try
        {
            var fi = new FileInfo(filePath);
            return BuildCacheKey(filePath, fi.Length, fi.LastWriteTimeUtc.Ticks, variant);
        }
        catch
        {
            return $"{filePath}|{variant}";
        }
    }

    private static string BuildCacheKey(string filePath, long length, long ticks, string variant)
        => $"{filePath}|{length}|{ticks}|{variant}";

    /// <summary>
    /// 在后台线程完成「查缓存 → 读文件头 → 解码 → 生成信息栏」。
    /// 全部 IO 都在这里，UI 线程只负责挂载。
    /// </summary>
    private ImageLoadOutcome LoadImageCore(string filePath, int decodeWidth, int decodeHeight)
    {
        var outcome = new ImageLoadOutcome();

        // 一次属性查询同时供缓存键与信息栏使用，不额外开文件
        var (length, ticks) = ImagePreviewer.Stat(filePath);
        outcome.StatMs = 0;

        var cacheKey = BuildCacheKey(filePath, length, ticks, $"img-{decodeWidth}-{decodeHeight}");

        var cached = PreviewCache.GetImage(cacheKey);

        // 元数据：命中元数据缓存时只做一次属性查询，不打开文件
        var metaSw = Stopwatch.StartNew();
        outcome.Info = ImagePreviewer.ReadInfo(filePath, length, ticks);
        outcome.MetaMs = metaSw.Elapsed.TotalMilliseconds;

        if (cached != null)
        {
            outcome.CacheHit = true;
            outcome.Bitmap = cached.Bitmap;
            outcome.PixelWidth = cached.PixelWidth;
            outcome.PixelHeight = cached.PixelHeight;
        }
        else
        {
            var decodeSw = Stopwatch.StartNew();
            var result = ImagePreviewer.Decode(
                filePath, decodeWidth, decodeHeight, MaxImagePixels, outcome.Info);
            outcome.DecodeMs = decodeSw.Elapsed.TotalMilliseconds;

            outcome.Bitmap = result.Bitmap;
            outcome.PixelWidth = result.PixelWidth;
            outcome.PixelHeight = result.PixelHeight;
            outcome.Info = result.Info;

            if (result.Bitmap != null)
            {
                PreviewCache.PutImage(cacheKey, new PreviewCache.ImageEntry
                {
                    Bitmap = result.Bitmap,
                    PixelWidth = result.PixelWidth > 0 ? result.PixelWidth : result.Bitmap.PixelWidth,
                    PixelHeight = result.PixelHeight > 0 ? result.PixelHeight : result.Bitmap.PixelHeight,
                    Bytes = (long)result.Bitmap.PixelWidth * result.Bitmap.PixelHeight * 4
                });
            }
        }

        outcome.InfoText = BuildFileInfo(filePath, outcome.Info, outcome.PixelWidth, outcome.PixelHeight);
        return outcome;
    }

    private async Task LoadImagePreviewAsync(string filePath, string fileName, int version, CancellationToken token,
        double locateMs, double typeMs, long detectedAt)
    {
        try
        {
            MonitorHelper.GetCursorWorkArea(out var dpiScale);
            var decodeWidth = GetImageDecodeWidth(dpiScale);
            var decodeHeight = GetImageDecodeHeight(dpiScale);

            var waitSw = Stopwatch.StartNew();
            var outcome = await Task.Run(() => LoadImageCore(filePath, decodeWidth, decodeHeight), token);
            var coreMs = waitSw.Elapsed.TotalMilliseconds;

            if (token.IsCancellationRequested || version != _previewVersion) return;

            var previewWindow = PreviewWindow.Instance;
            if (previewWindow == null) return;

            if (outcome.Bitmap == null)
            {
                ShowImageFallback(filePath, fileName, "解码失败", outcome.Info);
                return;
            }

            var mountSw = Stopwatch.StartNew();
            var img = new ImagePreviewer(outcome.Bitmap, outcome.PixelWidth, outcome.PixelHeight, _config.ImageSizeMode);
            previewWindow.SetContent(img);
            previewWindow.ShowPreview(fileName, outcome.InfoText, outcome.Info.Aspect);
            var mountMs = mountSw.Elapsed.TotalMilliseconds;

            var totalMs = detectedAt != 0
                ? Stopwatch.GetElapsedTime(detectedAt).TotalMilliseconds
                : locateMs + typeMs + coreMs + mountMs;
            var (hits, misses) = PreviewCache.ImageStats();
            Logger.Info(
                $"悬停计时 {fileName} 定位={locateMs:F1}ms 类型={typeMs:F2}ms " +
                $"缓存={(outcome.CacheHit ? "命中" : "未命中")} 元数据={outcome.MetaMs:F2}ms " +
                $"解码={outcome.DecodeMs:F1}ms 挂载={mountMs:F1}ms 总计={totalMs:F0}ms " +
                $"| 图片缓存 命中={hits} 未命中={misses}");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Error("图片预览失败", ex);
        }
    }

    private async Task LoadTextPreviewAsync(string filePath, string fileName, int version, CancellationToken token)
    {
        try
        {
            var maxBytes = (long)(_config.MaxTextFileSizeKB * 1024);
            var cacheKey = BuildCacheKey(filePath, $"txt-{maxBytes}");

            var content = PreviewCache.GetText(cacheKey);
            if (content == null)
            {
                content = await Task.Run(() => TextPreviewer.ReadText(filePath, maxBytes), token);
                if (content != null) PreviewCache.PutText(cacheKey, content);
            }

            if (token.IsCancellationRequested || version != _previewVersion) return;

            var previewWindow = PreviewWindow.Instance;
            if (previewWindow == null) return;

            if (content == null)
            {
                previewWindow.ClearContent();
                previewWindow.ShowPreview(fileName, BuildFileInfo(filePath));
                previewWindow.SetStatus("无法读取该文本文件（可能编码不支持或文件被占用）");
                return;
            }

            var text = new TextPreviewer(content);
            previewWindow.SetContent(text);
            previewWindow.ShowPreview(fileName, BuildFileInfo(filePath));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Error("文本预览失败", ex);
        }
    }

    /// <summary>
    /// 图片无法解码（SVG/HEIC/RAW/PSD 等缺少解码器）时，用系统 Shell 缩略图兜底。
    /// </summary>
    private void ShowImageFallback(string filePath, string fileName, string reason, ImageFileInfo? known = null)
    {
        Logger.Debug($"图片解码失败，回退缩略图: {filePath} ({reason})");

        var previewWindow = PreviewWindow.Instance;
        if (previewWindow == null) return;

        var thumb = ShellThumbnailProvider.Get(filePath, 1024);
        if (thumb != null)
        {
            var img = new ImagePreviewer();
            if (img.LoadFromBitmap(thumb, _config.ImageSizeMode))
            {
                // 解码失败时不再回头读文件头，直接用原图比例（若有）或缩略图比例
                var aspect = known is { Aspect: > 0 }
                    ? known.Aspect
                    : ToAspect(thumb.PixelWidth, thumb.PixelHeight);

                previewWindow.SetContent(img);
                previewWindow.ShowPreview(fileName, BuildFileInfo(filePath, img.PixelWidth, img.PixelHeight),
                    aspect);
                previewWindow.SetStatus("无法解码该图片格式，已显示系统缩略图");
                _lastPreviewedFile = filePath;
                return;
            }
            img.Dispose();
        }

        previewWindow.ClearContent();
        previewWindow.ShowPreview(fileName, BuildFileInfo(filePath));
        previewWindow.SetStatus("无法解码该图片格式");
        _lastPreviewedFile = filePath;
    }

    /// <summary>
    /// 标题栏信息：修改日期 | 分辨率（图片/视频） | 文件大小，不含扩展名。
    /// 图片路径复用已读到的文件头，不再二次查询文件属性。
    /// </summary>
    private string BuildFileInfo(string filePath, ImageFileInfo info, int pixelWidth, int pixelHeight)
    {
        if (!_config.ShowFileInfo) return string.Empty;

        try
        {
            var modified = info.LastWriteTimeUtc != default
                ? info.LastWriteTimeUtc.ToLocalTime()
                : new FileInfo(filePath).LastWriteTime;
            var length = info.Length > 0 ? info.Length : new FileInfo(filePath).Length;

            var parts = new System.Collections.Generic.List<string>
            {
                modified.ToString("yyyy-MM-dd HH:mm")
            };

            if (pixelWidth > 0 && pixelHeight > 0)
                parts.Add($"{pixelWidth}×{pixelHeight}");

            parts.Add(FormatFileSize(length));
            return string.Join("  |  ", parts);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 标题栏信息：修改日期 | 分辨率（图片/视频） | 文件大小，不含扩展名。
    /// </summary>
    private string BuildFileInfo(string filePath, int pixelWidth = 0, int pixelHeight = 0)
    {
        if (!_config.ShowFileInfo) return string.Empty;

        try
        {
            var fi = new FileInfo(filePath);
            var parts = new System.Collections.Generic.List<string>
            {
                fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
            };

            if (pixelWidth > 0 && pixelHeight > 0)
                parts.Add($"{pixelWidth}×{pixelHeight}");

            parts.Add(FormatFileSize(fi.Length));
            return string.Join("  |  ", parts);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    public void HidePreview()
    {
        CancelPendingPreview();
        _previewVersion++;
        StopDurationTimer();

        // 不做早退：即使 _lastPreviewedFile 为空，也要确保视频窗口和播放器被真正停掉，
        // 否则会出现「预览已消失但声音还在响」。
        try
        {
            var previewWindow = Preview.PreviewWindow.Instance;
            if (previewWindow != null)
            {
                previewWindow.ClearContent();
                previewWindow.Hide();
            }

            // 先停止播放，再隐藏视频窗口；避免渲染线程仍在播放时改动宿主绑定
            _activeVideoPreviewer?.Stop();
            _activeVideoPreviewer = null;
            _currentVideoPath = null;

            Preview.VideoWindow.Instance?.HidePreview();
        }
        catch (Exception ex)
        {
            Logger.Error("关闭预览失败", ex);
        }

        _lastPreviewedFile = null;
    }

    /// <summary>整个进程只创建一个播放器，切换视频文件时复用。</summary>
    private VideoPreviewer EnsureVideoPreviewer()
    {
        if (_videoPreviewer != null) return _videoPreviewer;

        var previewer = new VideoPreviewer();
        _videoPreviewer = previewer;

        previewer.PlaybackFailed += (_, reason) =>
        {
            var path = _currentVideoPath;
            if (path == null) return;

            PostToUi(() =>
            {
                // 已经切换到别的预览时忽略迟到的失败事件
                if (_activeVideoPreviewer != previewer) return;
                ShowVideoFallback(path, Path.GetFileName(path), reason);
            });
        };

        previewer.VideoSizeChanged += (_, size) =>
        {
            var path = _currentVideoPath;
            PostToUi(() =>
            {
                if (_activeVideoPreviewer != previewer) return;
                var vw = GetVideoWindow();
                vw.ResizeToVideo(size.Width, size.Height);
                vw.UpdateFileInfo(BuildFileInfo(path ?? string.Empty, size.Width, size.Height));
            });
        };

        return previewer;
    }

    private VideoWindow GetVideoWindow()
    {
        var window = VideoWindow.Instance;
        if (window == null)
        {
            window = new VideoWindow();
            VideoWindow.SetInstance(window);
        }
        return window;
    }

    private void UpdateVideoDurationDisplay()
    {
        if (_activeVideoPreviewer == null) return;
        var info = _activeVideoPreviewer.GetPlaybackInfo();
        if (string.IsNullOrEmpty(info)) return;

        VideoWindow.Instance?.UpdatePlaybackInfo(info);
    }

    private void StartDurationTimer()
    {
        UpdateVideoDurationDisplay();
        _durationTimer.Start();
    }

    private void OnDurationTimerTick(object? sender, EventArgs e)
    {
        UpdateVideoDurationDisplay();
    }

    private static void ShowStatus(string message)
    {
        Preview.VideoWindow.Instance?.SetStatus(message);
        Preview.PreviewWindow.Instance?.SetStatus(message);
    }

    /// <summary>
    /// 视频无法解码时，用 Shell 缩略图兜底显示首帧。
    /// </summary>
    private void ShowVideoFallback(string filePath, string fileName, string reason)
    {
        Logger.Info($"视频解码失败，回退缩略图: {filePath} ({reason})");

        StopDurationTimer();

        // 先停止播放，再隐藏视频窗口
        _activeVideoPreviewer?.Stop();
        _activeVideoPreviewer = null;
        _currentVideoPath = null;

        Preview.VideoWindow.Instance?.HidePreview();

        var previewWindow = Preview.PreviewWindow.Instance;
        if (previewWindow == null) return;

        var thumb = ShellThumbnailProvider.Get(filePath, 1024);

        if (thumb != null)
        {
            var img = new ImagePreviewer();
            if (img.LoadFromBitmap(thumb, _config.ImageSizeMode))
            {
                previewWindow.SetContent(img);
                previewWindow.ShowPreview(fileName, BuildFileInfo(filePath, img.PixelWidth, img.PixelHeight),
                    ResolveAspect(filePath, thumb));
                previewWindow.SetStatus("系统缺少该格式的解码器，已显示首帧缩略图");
                _lastPreviewedFile = filePath;
                return;
            }
            img.Dispose();
        }

        previewWindow.ClearContent();
        previewWindow.ShowPreview(fileName, BuildFileInfo(filePath));
        previewWindow.SetStatus("系统缺少该格式的解码器，无法预览");
        _lastPreviewedFile = filePath;
    }

    private void StopDurationTimer()
    {
        _durationTimer.Stop();
    }

    // ---------- 修饰键状态 ----------

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VK_CONTROL = 0x11;
    private const int VK_SHIFT = 0x10;
    private const int VK_MENU = 0x12;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    public static bool IsCtrlKeyPressed() => (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
    public static bool IsShiftKeyPressed() => (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
    public static bool IsAltKeyPressed() => (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
    public static bool IsWinKeyPressed() =>
        (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;

    private bool IsVideoHotkey(KeyCombo combo)
    {
        return combo.Equals(_volumeUpCombo)
            || combo.Equals(_volumeDownCombo)
            || combo.Equals(_stepForwardCombo)
            || combo.Equals(_stepBackwardCombo)
            || combo.Equals(_speedUpCombo)
            || combo.Equals(_speedDownCombo);
    }

    private bool TryHandleVideoHotkey(KeyCombo combo)
    {
        if (_activeVideoPreviewer == null) return false;

        string? message = null;

        if (combo.Equals(_volumeUpCombo)) message = _activeVideoPreviewer.VolumeUp();
        else if (combo.Equals(_volumeDownCombo)) message = _activeVideoPreviewer.VolumeDown();
        else if (combo.Equals(_stepForwardCombo)) message = _activeVideoPreviewer.StepForward();
        else if (combo.Equals(_stepBackwardCombo)) message = _activeVideoPreviewer.StepBackward();
        else if (combo.Equals(_speedUpCombo)) message = _activeVideoPreviewer.SpeedUp();
        else if (combo.Equals(_speedDownCombo)) message = _activeVideoPreviewer.SpeedDown();

        if (message == null) return false;

        ShowStatus(message);
        UpdateVideoDurationDisplay();
        Logger.Debug($"视频快捷键 {combo} -> {message}");
        return true;
    }

    private void ParseHotkeys()
    {
        _volumeUpCombo = HotkeyText.Parse(_config.VideoHotkeyVolumeUp);
        _volumeDownCombo = HotkeyText.Parse(_config.VideoHotkeyVolumeDown);
        _stepForwardCombo = HotkeyText.Parse(_config.VideoHotkeyStepFwd);
        _stepBackwardCombo = HotkeyText.Parse(_config.VideoHotkeyStepBack);
        _speedUpCombo = HotkeyText.Parse(_config.VideoHotkeySpeedUp);
        _speedDownCombo = HotkeyText.Parse(_config.VideoHotkeySpeedDown);
    }

    // ---------- 发送 BackSpace ----------

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
        public static int Size => Marshal.SizeOf(typeof(INPUT));
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize;
        public uint flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const uint INPUT_KEYBOARD = 1;
    private const ushort VK_UP_KEY = 0x28;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

    /// <summary>
    /// 双击空白后跳到「上层文件夹」。
    /// 优先通过 UIA 点击资源管理器自带的「向上一级」按钮（Explorer 自己处理层级链，
    /// 文件夹 → 盘符根 → 此电脑 → 桌面），失败再用 Shell COM 计算父目录。
    /// </summary>
    private static void SendUpOneLevel(IntPtr explorerHwnd)
    {
        try
        {
            if (ShellHelper.NavigateUpViaUia(explorerHwnd))
            {
                return;
            }

            Logger.Info("UIA 向上失败，改用 Shell COM 导航父目录");

            if (ShellHelper.NavigateToParentFolder(explorerHwnd))
            {
                Logger.Info("已通过 Shell COM 导航到父目录");
                return;
            }

            Logger.Info("Shell COM 导航失败，改用 Alt+Up 注入");

            if (explorerHwnd != IntPtr.Zero)
            {
                SetForegroundWindow(explorerHwnd);
                System.Threading.Thread.Sleep(40);
            }

            // INPUT 的 cbSize 必须是 Win32 的 sizeof(INPUT)（x64 为 40）
            var inputs = new INPUT[4];

            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].U.ki = new KEYBDINPUT { wVk = (ushort)VK_MENU };

            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].U.ki = new KEYBDINPUT { wVk = VK_UP_KEY, dwFlags = KEYEVENTF_EXTENDEDKEY };

            inputs[2].type = INPUT_KEYBOARD;
            inputs[2].U.ki = new KEYBDINPUT { wVk = VK_UP_KEY, dwFlags = KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP };

            inputs[3].type = INPUT_KEYBOARD;
            inputs[3].U.ki = new KEYBDINPUT { wVk = (ushort)VK_MENU, dwFlags = KEYEVENTF_KEYUP };

            var sent = SendInput(4, inputs, INPUT.Size);
            if (sent == 4)
            {
                Logger.Info($"Alt+Up 已注入 (cbSize={INPUT.Size})");
                return;
            }

            Logger.Error($"SendInput 失败 sent={sent} cbSize={INPUT.Size} err={Marshal.GetLastWin32Error()}");
        }
        catch (Exception ex)
        {
            Logger.Error("跳到上层文件夹失败", ex);
        }
    }

    public AppConfig GetConfig() => _config;

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        Stop();

        // 停止播放并隐藏视频窗口后，再释放复用的播放器
        // （Player.Dispose 内部会先停掉空闲渲染循环，再释放 SwapChain）
        Preview.VideoWindow.Instance?.HidePreview();
        _videoPreviewer?.Dispose();
        _videoPreviewer = null;

        _mouseHook.Dispose();
        _hotkeyHook.Dispose();

        GC.SuppressFinalize(this);
    }
}
