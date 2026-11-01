using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuickPeek.Models;

namespace QuickPeek.Preview;

public class ImagePreviewer : Border, IDisposable
{
    private BitmapSource? _bitmapSource;
    private Image? _image;
    private int _pixelWidth;
    private int _pixelHeight;

    /// <summary>图片原始像素尺寸（用于信息栏显示，不受解码缩放影响）。</summary>
    public int PixelWidth => _pixelWidth;
    public int PixelHeight => _pixelHeight;

    public ImagePreviewer()
    {
        Background = Brushes.Transparent;
    }

    public bool Load(string filePath, PreviewSizeMode sizeMode = PreviewSizeMode.MaxOriginalRatio, int maxDecodeWidthPx = 0)
    {
        try
        {
            // 先只读元数据拿到真实分辨率，再按上限解码，避免超大图片占满内存
            var size = ReadPixelSize(filePath);
            _pixelWidth = size.Width;
            _pixelHeight = size.Height;

            _bitmapSource = LoadImage(filePath, maxDecodeWidthPx, size.Width);
            if (_bitmapSource == null) return false;

            return LoadFromBitmap(_bitmapSource, sizeMode);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>直接用现成的 BitmapSource 显示（例如视频解码失败时的 Shell 缩略图）。</summary>
    public bool LoadFromBitmap(BitmapSource source, PreviewSizeMode sizeMode = PreviewSizeMode.MaxOriginalRatio)
    {
        if (source == null) return false;

        _bitmapSource = source;
        if (_pixelWidth <= 0 || _pixelHeight <= 0)
        {
            _pixelWidth = source.PixelWidth;
            _pixelHeight = source.PixelHeight;
        }

        var stretch = sizeMode switch
        {
            PreviewSizeMode.FitToWindow => Stretch.Fill,
            _ => Stretch.Uniform
        };

        _image = new Image
        {
            Source = _bitmapSource,
            Stretch = stretch,
            StretchDirection = StretchDirection.Both,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        Child = _image;
        return true;
    }

    /// <summary>只读图片头信息获取原始尺寸，不触发完整解码。</summary>
    private static (int Width, int Height) ReadPixelSize(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if (decoder.Frames.Count == 0) return (0, 0);

            var frame = decoder.Frames[0];
            return (frame.PixelWidth, frame.PixelHeight);
        }
        catch
        {
            return (0, 0);
        }
    }

    private static BitmapSource? LoadImage(string filePath, int maxDecodeWidthPx, int trueWidth)
    {
        try
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            BitmapImage? bitmap = null;

            if (extension == ".svg")
            {
                return LoadSvg(filePath);
            }

            bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
            if (maxDecodeWidthPx > 0 && trueWidth > maxDecodeWidthPx)
            {
                bitmap.DecodePixelWidth = maxDecodeWidthPx;
            }
            bitmap.EndInit();

            if (bitmap.CanFreeze)
            {
                bitmap.Freeze();
            }

            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource? LoadSvg(string filePath)
    {
        return null;
    }

    public void Dispose()
    {
        if (_image != null)
        {
            _image.Source = null;
        }
        if (_bitmapSource is IDisposable disposable)
        {
            disposable.Dispose();
        }
        _bitmapSource = null;
        _image = null;
        _pixelWidth = 0;
        _pixelHeight = 0;
        Child = null;
        GC.SuppressFinalize(this);
    }
}
