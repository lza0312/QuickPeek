using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace QuickPeek.Preview;

public class TextPreviewer : ScrollViewer, IDisposable
{
    /// <summary>交给 TextBox 的最大字符数，避免超大文本把 UI 线程拖死。</summary>
    private const int MaxRenderChars = 200_000;

    /// <summary>GB18030 代码页，用于没有 BOM 的简体中文文本兜底。</summary>
    private const uint CP_GB18030 = 54936;

    private TextBox? _textBox;
    private bool _isDisposed;

    public TextPreviewer(string content)
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
        Padding = new Thickness(12);
        SetText(content);
    }

    /// <summary>在后台线程读取文本内容；超过上限时只读前 maxSizeBytes 字节并加提示。</summary>
    public static string? ReadText(string filePath, long maxSizeBytes)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            var truncated = stream.Length > maxSizeBytes;
            var length = (int)Math.Min(truncated ? maxSizeBytes : stream.Length, int.MaxValue);
            var buffer = new byte[Math.Max(1, length)];
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read < buffer.Length) Array.Resize(ref buffer, read);

            var encoding = DetectEncoding(buffer);
            var content = encoding.GetString(buffer).TrimStart('\uFEFF');

            // 没有 BOM 的中文文本常被误判为 UTF-8；出现替换字符时尝试 GB18030
            if (content.Contains('\uFFFD') &&
                !encoding.Equals(Encoding.Unicode) &&
                !encoding.Equals(Encoding.BigEndianUnicode))
            {
                var gb = DecodeWithCodePage(buffer, CP_GB18030);
                if (gb != null && CountReplacement(gb) < CountReplacement(content))
                {
                    content = gb.TrimStart('\uFEFF');
                }
            }

            var notice = new StringBuilder();
            if (truncated)
            {
                notice.AppendLine($"文件过大，仅显示前 {(maxSizeBytes / 1024):F0} KB 预览");
                notice.AppendLine(new string('─', 40));
            }

            if (content.Length > MaxRenderChars)
            {
                content = content.Substring(0, MaxRenderChars);
                notice.AppendLine();
                notice.Append("（内容过长，已截断显示）");
            }

            return notice.Length > 0 ? notice + content : content;
        }
        catch
        {
            return null;
        }
    }

    private static int CountReplacement(string text) => text.Count(c => c == '\uFFFD');

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int MultiByteToWideChar(
        uint CodePage,
        uint dwFlags,
        byte[] lpMultiByteStr,
        int cbMultiByte,
        [Out] char[]? lpWideCharStr,
        int cchWideChar);

    private static string? DecodeWithCodePage(byte[] bytes, uint codePage)
    {
        try
        {
            var charCount = MultiByteToWideChar(codePage, 0, bytes, bytes.Length, null, 0);
            if (charCount <= 0) return null;

            var chars = new char[charCount];
            if (MultiByteToWideChar(codePage, 0, bytes, bytes.Length, chars, chars.Length) <= 0)
                return null;

            return new string(chars);
        }
        catch
        {
            return null;
        }
    }

    private static Encoding DetectEncoding(byte[] buffer)
    {
        if (buffer.Length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
        {
            return Encoding.UTF8;
        }
        if (buffer.Length >= 2 && buffer[0] == 0xFF && buffer[1] == 0xFE)
        {
            return Encoding.Unicode;
        }
        if (buffer.Length >= 2 && buffer[0] == 0xFE && buffer[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode;
        }

        for (int i = 0; i < buffer.Length && i < 1024; i++)
        {
            if (buffer[i] == 0)
            {
                return Encoding.Unicode;
            }
        }

        return Encoding.UTF8;
    }

    private void SetText(string content)
    {
        if (_textBox == null)
        {
            _textBox = new TextBox
            {
                IsReadOnly = true,
                IsReadOnlyCaretVisible = false,
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Text = content
            };
            Content = _textBox;
        }
        else
        {
            _textBox.Text = content;
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_textBox != null)
        {
            _textBox.Text = string.Empty;
            _textBox = null;
        }
        Content = null;

        GC.SuppressFinalize(this);
    }
}
