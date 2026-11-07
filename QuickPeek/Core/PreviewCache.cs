using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;

namespace QuickPeek.Core;

/// <summary>
/// 按字节预算淘汰的简单 LRU 缓存。图片预算 64 MB 起步；文本单条最大 256 KB。
/// 缓存值都是已 Freeze 的 BitmapSource 或字符串，可跨线程安全读取。
/// </summary>
public static class PreviewCache
{
    private const long ImageBudgetBytes = 64L * 1024 * 1024;
    private const long TextBudgetBytes = 8L * 1024 * 1024;

    /// <summary>单条文本允许进入缓存的上限。</summary>
    public const long MaxTextItemBytes = 256L * 1024;

    public sealed class ImageEntry
    {
        public BitmapSource Bitmap { get; init; } = null!;
        public int PixelWidth { get; init; }
        public int PixelHeight { get; init; }
        public long Bytes { get; init; }
    }

    private sealed class Entry<T>
    {
        public string Key { get; init; } = null!;
        public T Value { get; init; } = default!;
        public long Bytes { get; init; }
    }

    private static readonly object Lock = new();

    private static readonly Dictionary<string, LinkedListNode<Entry<ImageEntry>>> ImageMap = new();
    private static readonly LinkedList<Entry<ImageEntry>> ImageList = new();
    private static long _imageBytes;

    private static readonly Dictionary<string, LinkedListNode<Entry<string>>> TextMap = new();
    private static readonly LinkedList<Entry<string>> TextList = new();
    private static long _textBytes;

    public static ImageEntry? GetImage(string key)
    {
        lock (Lock)
        {
            var node = ImageMap.TryGetValue(key, out var found) ? found : null;
            if (node == null) return null;

            ImageList.Remove(node);
            ImageList.AddFirst(node);
            return node.Value.Value;
        }
    }

    public static void PutImage(string key, ImageEntry entry)
    {
        if (entry.Bytes <= 0 || entry.Bytes > ImageBudgetBytes) return;

        lock (Lock)
        {
            if (ImageMap.TryGetValue(key, out var existing))
            {
                ImageList.Remove(existing);
                ImageMap.Remove(key);
                _imageBytes -= existing.Value.Bytes;
            }

            var node = ImageList.AddFirst(new Entry<ImageEntry> { Key = key, Value = entry, Bytes = entry.Bytes });
            ImageMap[key] = node;
            _imageBytes += entry.Bytes;

            while (_imageBytes > ImageBudgetBytes && ImageList.Last != null)
            {
                var last = ImageList.Last;
                ImageList.RemoveLast();
                ImageMap.Remove(last.Value.Key);
                _imageBytes -= last.Value.Bytes;
            }
        }
    }

    public static string? GetText(string key)
    {
        lock (Lock)
        {
            var node = TextMap.TryGetValue(key, out var found) ? found : null;
            if (node == null) return null;

            TextList.Remove(node);
            TextList.AddFirst(node);
            return node.Value.Value;
        }
    }

    public static void PutText(string key, string text)
    {
        var bytes = EstimateTextBytes(text);
        if (bytes <= 0 || bytes > MaxTextItemBytes) return;

        lock (Lock)
        {
            if (TextMap.TryGetValue(key, out var existing))
            {
                TextList.Remove(existing);
                TextMap.Remove(key);
                _textBytes -= existing.Value.Bytes;
            }

            var node = TextList.AddFirst(new Entry<string> { Key = key, Value = text, Bytes = bytes });
            TextMap[key] = node;
            _textBytes += bytes;

            while (_textBytes > TextBudgetBytes && TextList.Last != null)
            {
                var last = TextList.Last;
                TextList.RemoveLast();
                TextMap.Remove(last.Value.Key);
                _textBytes -= last.Value.Bytes;
            }
        }
    }

    public static long EstimateTextBytes(string text) => (long)text.Length * 2;
}
