using System;
using System.IO;
using FlyleafLib;

namespace QuickPeek.Preview;

/// <summary>
/// FlyleafLib 引擎初始化（FFmpeg 内核）。全局只需启动一次，
/// 之后所有 Player 共享同一套解码资源。
/// </summary>
public static class VideoEngine
{
    private static readonly object _lock = new();
    private static bool _started;
    private static bool _failed;

    public static bool IsReady => _started && !_failed;

    public static bool EnsureStarted()
    {
        lock (_lock)
        {
            if (_failed) return false;
            if (_started) return true;

            try
            {
                var ffmpegPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
                if (!Directory.Exists(ffmpegPath))
                {
                    QuickPeek.Core.Logger.Error($"FFmpeg 目录不存在: {ffmpegPath}");
                    _failed = true;
                    return false;
                }

                var config = new EngineConfig
                {
                    FFmpegPath = ffmpegPath,
                    FFmpegLoadProfile = Flyleaf.FFmpeg.LoadProfile.All,
                    FFmpegLogLevel = Flyleaf.FFmpeg.LogLevel.Quiet,
                    DisableAudio = false,
                    LogLevel = LogLevel.Quiet
                };

                Engine.Start(config);
                _started = true;
                QuickPeek.Core.Logger.Info($"FlyleafLib 引擎已启动, FFmpeg={ffmpegPath}");
                return true;
            }
            catch (Exception ex)
            {
                _failed = true;
                QuickPeek.Core.Logger.Error("FlyleafLib 引擎启动失败", ex);
                return false;
            }
        }
    }
}
