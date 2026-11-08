using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuickPeek.Models;

namespace QuickPeek.Preview;

/// <summary>
/// 图片头信息：存储尺寸、EXIF 方向、文件大小与修改时间。
/// 只读文件头即可得到，用于推导预览窗口宽高比与信息栏，不需要解码像素。
/// </summary>
public sealed class ImageFileInfo
{
    public static readonly ImageFileInfo Unknown = new(0, 0, 1, 0, default);

    public ImageFileInfo(int width, int height, int orientation, long length, DateTime lastWriteTimeUtc)
    {
        Width = width;
        Height = height;
        Orientation = orientation;
        Length = length;
        LastWriteTimeUtc = lastWriteTimeUtc;
    }

    /// <summary>存储宽度（未旋转）。</summary>
    public int Width { get; }

    /// <summary>存储高度（未旋转）。</summary>
    public int Height { get; }

    /// <summary>EXIF Orientation，1–8。</summary>
    public int Orientation { get; }

    public long Length { get; }
    public DateTime LastWriteTimeUtc { get; }

    /// <summary>按 EXIF 方向换算后的显示宽度。</summary>
    public int DisplayWidth => SwapsAxes(Orientation) ? Height : Width;

    /// <summary>按 EXIF 方向换算后的显示高度。</summary>
    public int DisplayHeight => SwapsAxes(Orientation) ? Width : Height;

    /// <summary>显示宽高比（宽 / 高）；无效时为 0。</summary>
    public double Aspect => DisplayWidth > 0 && DisplayHeight > 0
        ? (double)DisplayWidth / DisplayHeight
        : 0;

    /// <summary>EXIF 方向 5/6/7/8 需要交换宽高。</summary>
    internal static bool SwapsAxes(int orientation) => orientation is 5 or 6 or 7 or 8;
}

/// <summary>后台线程解码结果：冻结后的位图 + 原始像素尺寸（用于信息栏）。</summary>
public sealed class ImageDecodeResult
{
    public BitmapSource? Bitmap { get; }
    public int PixelWidth { get; }
    public int PixelHeight { get; }

    /// <summary>解码时读到的文件头信息（可能来自缓存）。</summary>
    public ImageFileInfo Info { get; }

    public ImageDecodeResult(BitmapSource? bitmap, int pixelWidth, int pixelHeight, ImageFileInfo? info = null)
    {
        Bitmap = bitmap;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        Info = info ?? ImageFileInfo.Unknown;
    }
}

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

    public ImagePreviewer(BitmapSource source, int pixelWidth, int pixelHeight, PreviewSizeMode sizeMode)
    {
        Background = Brushes.Transparent;
        _pixelWidth = pixelWidth;
        _pixelHeight = pixelHeight;
        LoadFromBitmap(source, sizeMode);
    }

    // ---------- 文件头信息缓存 ----------
    // 每次悬停都重新打开文件读头会多出一次 IO（杀毒软件还会再扫一遍），
    // 这里按「路径 + 文件大小 + 修改时间」缓存，命中时完全不碰磁盘。
    private static readonly object InfoLock = new();
    private static readonly Dictionary<string, ImageFileInfo> InfoCache =
        new(StringComparer.OrdinalIgnoreCase);
    private const int MaxInfoEntries = 512;

    /// <summary>
    /// 读取文件头信息。命中缓存时只做一次属性查询（不打开文件），
    /// 未命中或文件已变化时才打开文件读 EXIF 方向。
    /// </summary>
    public static ImageFileInfo ReadInfo(string filePath)
    {
        var (length, ticks) = Stat(filePath);
        return ReadInfo(filePath, length, ticks);
    }

    /// <summary>同 <see cref="ReadInfo(string)"/>，复用调用方已经查到的文件属性，省一次查询。</summary>
    public static ImageFileInfo ReadInfo(string filePath, long length, long ticks)
    {
        lock (InfoLock)
        {
            if (InfoCache.TryGetValue(filePath, out var cached)
                && cached.Length == length
                && cached.LastWriteTimeUtc.Ticks == ticks)
            {
                return cached;
            }
        }

        var info = ReadInfoFromFile(filePath, length, ticks);
        StoreInfo(filePath, info);
        return info;
    }

    /// <summary>读取文件大小与修改时间；失败时返回 (0, 0)。</summary>
    public static (long Length, long Ticks) Stat(string filePath)
    {
        try
        {
            var fi = new FileInfo(filePath);
            return fi.Exists ? (fi.Length, fi.LastWriteTimeUtc.Ticks) : (0, 0);
        }
        catch
        {
            return (0, 0);
        }
    }

    /// <summary>
    /// 解码图片。传入 <paramref name="known"/> 可复用已读到的文件头，省掉一次读头。
    /// 返回的 PixelWidth/PixelHeight 是「按 EXIF 旋转后」的显示尺寸。
    /// </summary>
    public static ImageDecodeResult Decode(string filePath, int maxWidthPx, int maxHeightPx,
        long maxPixels, ImageFileInfo? known = null)
    {
        try
        {
            // 头信息缓存未命中时先读一次头（只读文件头，不开整幅解码）
            var info = known ?? ReadInfo(filePath);

            var displayWidth = info.DisplayWidth;
            var displayHeight = info.DisplayHeight;

            var displayDecodeWidth = ComputeDecodeWidth(
                displayWidth, displayHeight, maxWidthPx, maxHeightPx, maxPixels);

            // DecodePixelWidth 作用于未旋转的原始位图，按同一缩放比例换算回存储宽度
            var storedDecodeWidth = displayWidth > 0 && info.Width > 0
                ? (int)Math.Round(info.Width * ((double)displayDecodeWidth / displayWidth))
                : 0;

            using var stream = OpenRead(filePath);
            var bitmap = LoadImage(stream, filePath, storedDecodeWidth, info.Width);
            bitmap = ApplyOrientation(bitmap, info.Orientation);

            return new ImageDecodeResult(bitmap, displayWidth, displayHeight, info);
        }
        catch
        {
            return new ImageDecodeResult(null, 0, 0, null);
        }
    }

    private static void StoreInfo(string filePath, ImageFileInfo info)
    {
        if (info.Width <= 0 || info.Height <= 0) return;

        lock (InfoLock)
        {
            if (InfoCache.Count >= MaxInfoEntries) InfoCache.Clear();
            InfoCache[filePath] = info;
        }
    }

    private static FileStream OpenRead(string filePath)
        => new(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    private static string? GetSvgOrNull(string filePath)
        => Path.GetExtension(filePath).Equals(".svg", StringComparison.OrdinalIgnoreCase) ? filePath : null;

    /// <summary>打开文件读一次头信息（存储尺寸 + EXIF 方向）。</summary>
    private static ImageFileInfo ReadInfoFromFile(string filePath, long length, long ticks)
    {
        try
        {
            // SVG 没有可读的位图头，交给解码阶段直接失败并走缩略图兜底
            if (GetSvgOrNull(filePath) != null) return ImageFileInfo.Unknown;

            using var stream = OpenRead(filePath);
            return ReadInfoFromStream(stream, filePath, length, ticks);
        }
        catch
        {
            return ImageFileInfo.Unknown;
        }
    }

    /// <summary>从已打开的流读头信息（调用方负责关闭流）。</summary>
    private static ImageFileInfo ReadInfoFromStream(Stream stream, string filePath, long length, long ticks)
    {
        try
        {
            if (GetSvgOrNull(filePath) != null) return ImageFileInfo.Unknown;

            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if (decoder.Frames.Count == 0) return ImageFileInfo.Unknown;

            var frame = decoder.Frames[0];
            return new ImageFileInfo(frame.PixelWidth, frame.PixelHeight,
                ReadOrientation(frame), length, new DateTime(ticks, DateTimeKind.Utc));
        }
        catch
        {
            return ImageFileInfo.Unknown;
        }
        finally
        {
            try { if (stream.CanSeek) stream.Position = 0; } catch { }
        }
    }

    /// <summary>按宽、高、总像素三个上限计算等比缩放后的解码宽度（不放大）。</summary>
    private static int ComputeDecodeWidth(int width, int height, int maxWidthPx, int maxHeightPx, long maxPixels)
    {
        if (width <= 0 || height <= 0) return 0;

        var scale = 1.0;
        if (maxWidthPx > 0 && width > maxWidthPx)
            scale = Math.Min(scale, (double)maxWidthPx / width);
        if (maxHeightPx > 0 && height > maxHeightPx)
            scale = Math.Min(scale, (double)maxHeightPx / height);
        if (maxPixels > 0 && (long)width * height > maxPixels)
            scale = Math.Min(scale, Math.Sqrt((double)maxPixels / ((long)width * height)));

        return Math.Max(1, (int)Math.Round(width * scale));
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
            // 不再用 Fill：窗口比例已经等于图片比例，Uniform 既是满铺也不会拉伸变形
            PreviewSizeMode.FitToWindow => Stretch.Uniform,
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

    private static readonly string[] OrientationQueries =
    {
        "/app1/ifd/{ushort=274}", // JPEG / TIFF
        "/ifd/{ushort=274}",
        "/eXIf/{ushort=274}"      // PNG eXIf 块
    };

    /// <summary>读取 EXIF Orientation，读不到时按 1（正常方向）处理。</summary>
    private static int ReadOrientation(BitmapFrame frame)
    {
        if (frame.Metadata is not BitmapMetadata metadata) return 1;

        foreach (var query in OrientationQueries)
        {
            try
            {
                var value = metadata.GetQuery(query);
                if (value is ushort us && us is >= 1 and <= 8) return us;
                if (value is int i && i is >= 1 and <= 8) return i;
            }
            catch
            {
                // 该格式不支持此查询路径，继续尝试下一个
            }
        }

        return 1;
    }

    /// <summary>
    /// 按 EXIF Orientation 对解码后的位图做旋转/镜像。
    /// WPF 的 BitmapImage 不会自动应用该标记，必须显式转换。
    /// </summary>
    private static BitmapSource? ApplyOrientation(BitmapSource? source, int orientation)
    {
        // 绝大多数照片是 1，直接返回原图，避免多分配一份位图
        if (source == null || orientation is <= 1 or > 8) return source;

        Transform? transform = orientation switch
        {
            2 => new ScaleTransform(-1, 1),                       // 水平镜像
            3 => new RotateTransform(180),                        // 旋转 180°
            4 => new ScaleTransform(1, -1),                       // 垂直镜像
            // 5/7 是「转置」与「翻转转置」。这里的组合顺序是「先镜像再旋转」，
            // 对应 5 要用 270°、7 要用 90°，写反就会得到另一条对角线。
            5 => new TransformGroup { Children = { new ScaleTransform(-1, 1), new RotateTransform(270) } },
            6 => new RotateTransform(90),                         // 手机竖拍最常见
            7 => new TransformGroup { Children = { new ScaleTransform(-1, 1), new RotateTransform(90) } },
            8 => new RotateTransform(270),
            _ => null
        };

        if (transform == null) return source;

        try
        {
            var transformed = new TransformedBitmap(source, transform);
            transformed.Freeze();
            return transformed;
        }
        catch
        {
            // 个别编码器不支持该变换时退回未旋转的结果，至少保证能显示
            return source;
        }
    }

    private static BitmapSource? LoadImage(Stream stream, string filePath, int decodeWidth, int trueWidth)
    {
        try
        {
            if (GetSvgOrNull(filePath) != null) return null;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            // 注意：IgnoreImageCache 只对 UriSource 有效，用 StreamSource 时必须去掉，否则抛异常
            bitmap.StreamSource = stream;
            if (decodeWidth > 0 && trueWidth > decodeWidth)
            {
                // 只设宽度上限，高度由 WPF 等比推导，避免两个维度同时缩放产生变形
                bitmap.DecodePixelWidth = decodeWidth;
            }
            bitmap.EndInit();

            if (!bitmap.CanFreeze) return null;
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
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
