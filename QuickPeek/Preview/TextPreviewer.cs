using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace QuickPeek.Preview;

public class TextPreviewer : ScrollViewer, IDisposable
{
    private TextBox? _textBox;
    private bool _isDisposed;

    public TextPreviewer()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
        Padding = new Thickness(12);
    }

    public bool Load(string filePath, long maxSizeBytes = 2 * 1024 * 1024)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length > maxSizeBytes)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"文件过大，仅显示前 {(maxSizeBytes / 1024):F0} KB 预览");
                sb.AppendLine(new string('─', 40));

                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var buffer = new byte[maxSizeBytes];
                var read = stream.Read(buffer, 0, buffer.Length);

                var encoding = DetectEncoding(buffer);
                sb.Append(encoding.GetString(buffer, 0, read));

                SetText(sb.ToString());
                return true;
            }

            var content = ReadFileContent(filePath);
            if (content == null) return false;

            SetText(content);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? ReadFileContent(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[Math.Min(stream.Length, 4096)];
            var headerRead = stream.Read(buffer, 0, buffer.Length);

            var encoding = DetectEncoding(buffer);
            stream.Position = 0;

            using var reader = new StreamReader(stream, encoding);
            var content = reader.ReadToEnd();
            return content;
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
