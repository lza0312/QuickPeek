using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace QuickPeek.Core;

/// <summary>
/// 统一的快捷键表示。钩子回调、录制输入框、配置字符串都走同一套解析，
/// 保证无论用户写成 Ctrl+Right 还是 Control + Right 都能匹配。
/// </summary>
public sealed class KeyCombo : IEquatable<KeyCombo>
{
    public ModifierKeys Modifiers { get; }
    public Key Key { get; }

    public KeyCombo(ModifierKeys modifiers, Key key)
    {
        Modifiers = modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt | ModifierKeys.Windows);
        Key = key;
    }

    public bool IsValid => Key != Key.None && !HotkeyText.IsModifierKey(Key);

    public override string ToString()
    {
        var parts = new List<string>(4);
        if ((Modifiers & ModifierKeys.Control) != 0) parts.Add("Control");
        if ((Modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((Modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((Modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");
        parts.Add(Key.ToString());
        return string.Join(" + ", parts);
    }

    public bool Equals(KeyCombo? other)
        => other != null && other.Modifiers == Modifiers && other.Key == Key;

    public override bool Equals(object? obj) => Equals(obj as KeyCombo);

    public override int GetHashCode() => HashCode.Combine(Modifiers, Key);
}

public static class HotkeyText
{
    public static bool IsModifierKey(Key key)
        => key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin
            or Key.System;

    /// <summary>从 Win32 虚拟键码构造组合键（读取当前物理修饰键状态）。</summary>
    public static KeyCombo? FromVirtualKey(int vkCode)
    {
        var key = KeyInterop.KeyFromVirtualKey(vkCode);
        if (key == Key.None || IsModifierKey(key)) return null;

        var mods = ModifierKeys.None;
        if (PreviewManager.IsCtrlKeyPressed()) mods |= ModifierKeys.Control;
        if (PreviewManager.IsShiftKeyPressed()) mods |= ModifierKeys.Shift;
        if (PreviewManager.IsAltKeyPressed()) mods |= ModifierKeys.Alt;

        return new KeyCombo(mods, key);
    }

    /// <summary>从 WPF 键盘事件构造组合键（录制输入框使用）。</summary>
    public static KeyCombo? FromWpfKey(Key key, ModifierKeys mods)
    {
        if (key == Key.None || IsModifierKey(key)) return null;
        return new KeyCombo(mods, key);
    }

    /// <summary>解析配置里的快捷键字符串，容忍 Ctrl/Control、加号两侧空格等写法。</summary>
    public static KeyCombo? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var mods = ModifierKeys.None;
        Key key = Key.None;

        foreach (var rawPart in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var part = rawPart.Trim();
            if (part.Length == 0) continue;

            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    mods |= ModifierKeys.Control;
                    continue;
                case "shift":
                    mods |= ModifierKeys.Shift;
                    continue;
                case "alt":
                    mods |= ModifierKeys.Alt;
                    continue;
                case "win":
                case "windows":
                case "meta":
                    mods |= ModifierKeys.Windows;
                    continue;
            }

            if (!Enum.TryParse<Key>(part, ignoreCase: true, out var parsed)) return null;
            key = parsed;
        }

        if (key == Key.None || IsModifierKey(key)) return null;
        return new KeyCombo(mods, key);
    }
}
