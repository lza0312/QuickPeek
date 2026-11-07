using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace QuickPeek.Core;

public enum FileCategory
{
    Unknown,
    Image,
    Video,
    Text,
    Audio
}

public class FileTypeDetector
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".tif", ".webp",
        ".svg", ".ico", ".heic", ".heif", ".raw", ".psd", ".ai",
        ".tga", ".exr", ".hdr", ".pbm", ".pgm", ".ppm"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".m4v",
        ".mpg", ".mpeg", ".3gp", ".ts", ".mts", ".m2ts", ".vob", ".ogv"
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".cfg", ".ini", ".csv", ".json", ".xml", ".yaml", ".yml",
        ".cs", ".csproj", ".vb", ".vbproj", ".py", ".js", ".ts", ".html", ".htm",
        ".css", ".scss", ".less", ".sql", ".md", ".markdown", ".rtf",
        ".c", ".h", ".cpp", ".hpp", ".cc", ".cxx", ".java", ".kt", ".go", ".rs",
        ".php", ".rb", ".sh", ".bat", ".cmd", ".ps1", ".psm1", ".psd1",
        ".xaml", ".aspx", ".cshtml", ".jsp", ".jspx", ".vue", ".jsx", ".tsx",
        ".gradle", ".properties", ".env", ".conf", ".toml"
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma", ".m4a", ".opus"
    };

    public static FileCategory Detect(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return FileCategory.Unknown;
        if (!File.Exists(filePath)) return FileCategory.Unknown;

        var ext = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(ext)) return FileCategory.Unknown;

        if (ImageExtensions.Contains(ext)) return FileCategory.Image;
        if (VideoExtensions.Contains(ext)) return FileCategory.Video;
        if (AudioExtensions.Contains(ext)) return FileCategory.Audio;
        if (TextExtensions.Contains(ext)) return FileCategory.Text;

        return FileCategory.Unknown;
    }
}
