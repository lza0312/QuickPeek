using System;
using System.IO;
using QuickPeek.Models;
using QuickPeek.Services;
using Xunit;

namespace QuickPeek.Tests;

public class ConfigTests : IDisposable
{
    private readonly string _tempDir;

    public ConfigTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "QuickPeek.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Normalize_ClampsOutOfRangeValues()
    {
        var config = new AppConfig
        {
            HoverDelayMs = -10,
            PreviewWidthPx = 99999,
            PreviewHeightPx = -1,
            PreviewMarginXPx = 900,
            PreviewScalePercent = 1000,
            PreviewWindowOpacity = 5,
            PreviewContentOpacity = -3,
            VideoDefaultVolume = 2,
            VideoSeekStepSeconds = 0,
            VideoSpeedMin = 10,
            VideoSpeedMax = 0.5
        };

        config.Normalize();

        Assert.Equal(100, config.HoverDelayMs);
        Assert.Equal(7680, config.PreviewWidthPx);
        Assert.Equal(100, config.PreviewHeightPx);
        Assert.Equal(200, config.PreviewMarginXPx);
        Assert.Equal(300, config.PreviewScalePercent);
        Assert.Equal(1.0, config.PreviewWindowOpacity);
        Assert.Equal(0.2, config.PreviewContentOpacity);
        Assert.Equal(1.0, config.VideoDefaultVolume);
        Assert.Equal(0.5, config.VideoSeekStepSeconds);
        Assert.True(config.VideoSpeedMin <= config.VideoSpeedMax);
    }

    [Fact]
    public void LoadFrom_MigratesLegacySingleMargin()
    {
        var path = Path.Combine(_tempDir, "settings.json");
        File.WriteAllText(path, "{ \"PreviewMarginPx\": 40 }");

        var config = SettingsService.LoadFrom(path);

        Assert.Equal(40, config.PreviewMarginXPx);
        Assert.Equal(40, config.PreviewMarginYPx);
        Assert.Equal(AppConfig.CurrentConfigVersion, config.ConfigVersion);
    }

    [Fact]
    public void LoadFrom_MigratesExplicitZeroVersion()
    {
        var path = Path.Combine(_tempDir, "settings.json");
        File.WriteAllText(path, "{ \"ConfigVersion\": 0, \"HoverDelayMs\": 500 }");

        var config = SettingsService.LoadFrom(path);

        Assert.Equal(500, config.HoverDelayMs);
        Assert.Equal(AppConfig.CurrentConfigVersion, config.ConfigVersion);
    }

    [Fact]
    public void LoadFrom_FallsBackToBackupWhenMainIsCorrupt()
    {
        var path = Path.Combine(_tempDir, "settings.json");

        SettingsService.SaveTo(new AppConfig { HoverDelayMs = 300 }, path);
        SettingsService.SaveTo(new AppConfig { HoverDelayMs = 800 }, path);
        File.WriteAllText(path, "{ this is not json");

        var config = SettingsService.LoadFrom(path);

        Assert.Equal(300, config.HoverDelayMs);
    }

    [Fact]
    public void SaveTo_LeavesNoTempAndCreatesBackupOnSecondWrite()
    {
        var path = Path.Combine(_tempDir, "settings.json");

        SettingsService.SaveTo(new AppConfig { HoverDelayMs = 300 }, path);
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
        Assert.False(File.Exists(path + ".bak"));

        SettingsService.SaveTo(new AppConfig { HoverDelayMs = 500 }, path);
        Assert.True(File.Exists(path + ".bak"));
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Equal(500, SettingsService.LoadFrom(path).HoverDelayMs);
    }
}
