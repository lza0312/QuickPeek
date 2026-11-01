using System;
using System.Globalization;
using System.IO;
using FlyleafLib.MediaPlayer;
using QuickPeek.Core;
using QuickPeek.Models;

namespace QuickPeek.Preview;

public class VideoSizeEventArgs : EventArgs
{
    public int Width { get; }
    public int Height { get; }

    public VideoSizeEventArgs(int width, int height)
    {
        Width = width;
        Height = height;
    }
}

/// <summary>
/// 视频预览器：改用 FlyleafLib（FFmpeg 内核），支持 WebM/MKV/VP9/AV1 等
/// WPF MediaElement 无法解码的格式。本身不参与 WPF 布局，
/// 画面由 VideoWindow 中的 FlyleafHost 承载。
/// </summary>
public class VideoPreviewer : IDisposable
{
    private Player? _player;
    private AppConfig? _config;
    private bool _isDisposed;
    private bool _autoPlay;

    public Player? Player => _player;

    public int VideoWidth { get; private set; }
    public int VideoHeight { get; private set; }

    // Flyleaf 的 CurTime / Duration 单位是 TimeSpan 的 Tick（100ns），不是毫秒
    public TimeSpan CurrentPosition => TimeSpan.FromTicks(_player?.CurTime ?? 0);
    public TimeSpan TotalDuration => TimeSpan.FromTicks(_player?.Duration ?? 0);

    public event EventHandler<string>? PlaybackFailed;
    public event EventHandler<VideoSizeEventArgs>? VideoSizeChanged;

    public bool Load(string filePath, bool autoPlay, bool loop, PreviewSizeMode sizeMode, AppConfig config)
    {
        _config = config;
        _autoPlay = autoPlay;

        if (!VideoEngine.EnsureStarted()) return false;

        try
        {
            _player = new Player(new FlyleafLib.Config())
            {
                LoopPlayback = loop
            };

            var defaultVolume = (int)Math.Round(Math.Clamp(config.VideoDefaultVolume, 0, 1) * 100);
            _player.Audio.Volume = defaultVolume;
            _player.Speed = ClampSpeed(config.VideoDefaultSpeed);

            _player.OpenCompleted += OnOpenCompleted;
            _player.PlaybackStopped += OnPlaybackStopped;

            _player.OpenAsync(filePath);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"Flyleaf 打开视频失败: {filePath}", ex);
            Dispose();
            return false;
        }
    }

    private void OnOpenCompleted(object? sender, OpenCompletedArgs e)
    {
        try
        {
            if (_player == null || _isDisposed) return;

            if (!e.Success)
            {
                var reason = e.Error?.ToString() ?? "未知错误";
                Logger.Error($"视频解码失败: {reason}");
                PlaybackFailed?.Invoke(this, reason);
                return;
            }

            var width = _player.Video.Width;
            var height = _player.Video.Height;

            if (width > 0 && height > 0)
            {
                ReportSize(width, height);
            }
            else
            {
                // 有些格式（VP8/VP9 等）要解码出首帧后才知道分辨率
                _player.Video.PropertyChanged += OnVideoPropertyChanged;
                RetrySize();
            }

            // 关闭「自动播放」时也要先渲染一帧，否则窗口是黑的
            _player.Play();
            if (!_autoPlay) _player.Pause();
        }
        catch (Exception ex)
        {
            Logger.Error("OpenCompleted 处理异常", ex);
        }
    }

    private void OnVideoPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        var player = _player;
        if (player == null || _isDisposed) return;

        var w = player.Video.Width;
        var h = player.Video.Height;
        if (w > 0 && h > 0) ReportSize(w, h);
    }

    /// <summary>
    /// 兜底轮询：OpenCompleted 不一定在 UI 线程触发，这里显式挂到 UI 调度器。
    /// </summary>
    private void RetrySize()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        dispatcher.InvokeAsync(() =>
        {
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };

            var attempts = 0;
            timer.Tick += (_, _) =>
            {
                attempts++;
                if (_player == null || _isDisposed)
                {
                    timer.Stop();
                    return;
                }

                var w = _player.Video.Width;
                var h = _player.Video.Height;

                if (w > 0 && h > 0)
                {
                    timer.Stop();
                    ReportSize(w, h);
                }
                else if (attempts >= 8)
                {
                    timer.Stop();
                }
            };
            timer.Start();
        });
    }

    private void ReportSize(int width, int height)
    {
        if (_isDisposed) return;
        if (width == VideoWidth && height == VideoHeight && VideoWidth > 0) return;

        VideoWidth = width;
        VideoHeight = height;
        VideoSizeChanged?.Invoke(this, new VideoSizeEventArgs(width, height));
    }

    private void OnPlaybackStopped(object? sender, PlaybackStoppedArgs e)
    {
        if (_player == null || _isDisposed) return;
        if (e.Error != null)
        {
            var reason = e.Error.ToString();
            Logger.Error($"播放中断: {reason}");
            PlaybackFailed?.Invoke(this, reason);
        }
    }

    private double ClampSpeed(double value)
    {
        var min = _config?.VideoSpeedMin ?? 0.25;
        var max = _config?.VideoSpeedMax ?? 4.0;
        return Math.Clamp(value, min, max);
    }

    private string PositionText => $"{FormatTime(CurrentPosition)} / {FormatTime(TotalDuration)}";

    public string VolumeUp()
    {
        if (_player == null) return string.Empty;

        var step = _config?.VideoVolumeStepPercent ?? 10;
        _player.Audio.Volume = Math.Min(100, _player.Audio.Volume + step);
        return $"音量 {_player.Audio.Volume}%";
    }

    public string VolumeDown()
    {
        if (_player == null) return string.Empty;

        var step = _config?.VideoVolumeStepPercent ?? 10;
        _player.Audio.Volume = Math.Max(0, _player.Audio.Volume - step);
        return $"音量 {_player.Audio.Volume}%";
    }

    public string StepForward()
    {
        var seconds = _config?.VideoSeekStepSeconds ?? 3.0;

        // 用 Flyleaf 自带的相对跳转：内部处理关键帧对齐与暂停态画面刷新，
        // 直接 Seek(绝对毫秒) 在暂停时会停在首帧不动。
        if (_player != null && _player.Duration > 0)
            _player.SeekForward_((long)(seconds * 1000));

        return $"快进 {seconds:0.#} 秒  {PositionText}";
    }

    public string StepBackward()
    {
        var seconds = _config?.VideoSeekStepSeconds ?? 3.0;

        if (_player != null && _player.Duration > 0)
            _player.SeekBackward_((long)(seconds * 1000));

        return $"后退 {seconds:0.#} 秒  {PositionText}";
    }

    public string SpeedUp()
    {
        if (_player == null) return string.Empty;

        var step = _config?.VideoSpeedStepMultiplier ?? 0.25;
        _player.Speed = ClampSpeed(_player.Speed + step);
        return $"播放速度 {_player.Speed:0.##} 倍";
    }

    public string SpeedDown()
    {
        if (_player == null) return string.Empty;

        var step = _config?.VideoSpeedStepMultiplier ?? 0.25;
        _player.Speed = ClampSpeed(_player.Speed - step);
        return $"播放速度 {_player.Speed:0.##} 倍";
    }

    /// <summary>播放信息浮层内容：已播放 hh:mm:ss/总时长 hh:mm:ss、进度百分比、倍速、音量百分比。</summary>
    public string GetPlaybackInfo()
    {
        var player = _player;
        if (player == null) return string.Empty;

        var total = TotalDuration;
        if (total.TotalMilliseconds <= 0) return string.Empty;

        var position = CurrentPosition;
        var ratio = Math.Clamp(position.TotalMilliseconds / total.TotalMilliseconds, 0, 1);
        var percent = (int)Math.Round(ratio * 100);

        return $"已播放 {FormatTime(position)}/{FormatTime(total)}    {percent}%    {player.Speed:0.00}x    音量 {player.Audio.Volume}%";
    }

    private static string FormatTime(TimeSpan ts)
    {
        if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;

        // 统一 hh:mm:ss 格式（小时按实际累计，超过 24 小时也能正确显示）
        return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        var player = _player;
        _player = null;

        try
        {
            if (player != null)
            {
                player.OpenCompleted -= OnOpenCompleted;
                player.PlaybackStopped -= OnPlaybackStopped;
                try { player.Video.PropertyChanged -= OnVideoPropertyChanged; } catch { }

                // 先静音再停止，避免停止瞬间漏音
                player.Audio.Mute = true;
                player.Audio.Volume = 0;
                player.Stop();
                player.Dispose();
            }
        }
        catch (Exception ex)
        {
            Logger.Debug($"释放播放器异常: {ex.Message}");
        }
    }
}
