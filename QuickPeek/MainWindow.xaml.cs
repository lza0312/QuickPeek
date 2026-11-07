using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using QuickPeek.Core;
using QuickPeek.Models;

namespace QuickPeek;

public partial class MainWindow : Window
{
    private readonly PreviewManager _previewManager;
    private AppConfig _config;
    private bool _isDarkTheme = true;

    public MainWindow(PreviewManager previewManager)
    {
        InitializeComponent();
        _previewManager = previewManager;
        _config = _previewManager.GetConfig();
        _isDarkTheme = _config.UseDarkTheme;

        LoadConfigToUi();
        ApplyUiScale();

        // 视觉树在构造函数阶段还没生成，此时 FindVisualChildren 找不到任何控件，
        // 会导致窗口背景变深了但文字仍是黑色。改到 Loaded 之后再套用主题。
        Loaded += (_, _) =>
        {
            ApplyTheme();
            ApplyTitleBarTheme();
        };

        SourceInitialized += (_, _) => ApplyTitleBarTheme();
    }

    private void LoadConfigToUi()
    {
        EnabledCheckBox.IsChecked = _config.Enabled;
        StartMinimizedCheckBox.IsChecked = _config.StartMinimized;
        AutoStartCheckBox.IsChecked = AutoStart.IsEnabled();
        HoverDelayTextBox.Text = _config.HoverDelayMs.ToString();
        UiScaleTextBox.Text = _config.UiScalePercent.ToString();
        PreviewScaleTextBox.Text = _config.PreviewScalePercent.ToString();

        CornerComboBox.Items.Clear();
        CornerComboBox.Items.Add("右下角");
        CornerComboBox.Items.Add("左下角");
        CornerComboBox.Items.Add("右上角");
        CornerComboBox.Items.Add("左上角");
        CornerComboBox.SelectedIndex = (int)_config.PreviewCorner;

        WidthTextBox.Text = _config.PreviewWidthPx.ToString();
        HeightTextBox.Text = _config.PreviewHeightPx.ToString();
        MarginXTextBox.Text = _config.PreviewMarginXPx.ToString();
        MarginYTextBox.Text = _config.PreviewMarginYPx.ToString();

        WindowOpacitySlider.Value = _config.PreviewWindowOpacity;
        ContentOpacitySlider.Value = _config.PreviewContentOpacity;

        ShowFileInfoCheckBox.IsChecked = _config.ShowFileInfo;

        ShowImagesCheckBox.IsChecked = _config.ShowImages;
        ShowVideosCheckBox.IsChecked = _config.ShowVideos;
        ShowTextsCheckBox.IsChecked = _config.ShowTexts;
        ShowAudiosCheckBox.IsChecked = _config.ShowAudios;
        MaxTextSizeTextBox.Text = _config.MaxTextFileSizeKB.ToString();

        DefaultVolumeTextBox.Text = Math.Round(_config.VideoDefaultVolume * 100).ToString();
        DefaultSpeedTextBox.Text = _config.VideoDefaultSpeed.ToString();

        VolumeStepTextBox.Text = _config.VideoVolumeStepPercent.ToString();
        SeekStepTextBox.Text = _config.VideoSeekStepSeconds.ToString();
        SpeedStepTextBox.Text = _config.VideoSpeedStepMultiplier.ToString();

        CtrlWheelVolumeCheckBox.IsChecked = _config.EnableVideoCtrlWheel;
        CtrlWheelSeekCheckBox.IsChecked = _config.EnableVideoCtrlWheelH;

        SizeModeMaxRatioRadio.IsChecked = _config.ImageSizeMode == PreviewSizeMode.MaxOriginalRatio;
        SizeModeFitRadio.IsChecked = _config.ImageSizeMode == PreviewSizeMode.FitToWindow;
        BlankDoubleClickCheckBox.IsChecked = _config.EnableBlankDoubleClickBack;

        DarkThemeCheckBox.IsChecked = _config.UseDarkTheme;

        VolumeUpHotkeyTextBox.Text = _config.VideoHotkeyVolumeUp;
        VolumeDownHotkeyTextBox.Text = _config.VideoHotkeyVolumeDown;
        StepFwdHotkeyTextBox.Text = _config.VideoHotkeyStepFwd;
        StepBackHotkeyTextBox.Text = _config.VideoHotkeyStepBack;
        SpeedUpHotkeyTextBox.Text = _config.VideoHotkeySpeedUp;
        SpeedDownHotkeyTextBox.Text = _config.VideoHotkeySpeedDown;
    }

    /// <summary>只放大设置界面的文字，窗口尺寸保持不变。</summary>
    private void ApplyUiScale()
    {
        var scale = Math.Clamp(_config.UiScalePercent, 100, 300) / 100.0;

        FontSize = 12 * scale;
        TitleTextBlock.FontSize = 24 * scale;
        SubtitleTextBlock.FontSize = 13 * scale;

        // 按钮样式里固定了 FontSize，需要单独覆盖
        SaveButton.FontSize = 13 * scale;
        ApplyButton.FontSize = 13 * scale;
        ResetButton.FontSize = 13 * scale;
    }

    private void ApplyConfigFromUi()
    {
        _config.Enabled = EnabledCheckBox.IsChecked ?? true;
        _config.StartMinimized = StartMinimizedCheckBox.IsChecked ?? true;

        var wantAutoStart = AutoStartCheckBox.IsChecked ?? false;
        if (wantAutoStart != AutoStart.IsEnabled())
        {
            if (!AutoStart.SetEnabled(wantAutoStart))
            {
                AutoStartCheckBox.IsChecked = AutoStart.IsEnabled();
            }
        }

        if (int.TryParse(HoverDelayTextBox.Text, out int delay) && delay >= 100 && delay <= 5000)
            _config.HoverDelayMs = delay;

        if (int.TryParse(UiScaleTextBox.Text, out int uiScale) && uiScale >= 100 && uiScale <= 300)
            _config.UiScalePercent = uiScale;
        if (int.TryParse(PreviewScaleTextBox.Text, out int previewScale) && previewScale >= 100 && previewScale <= 300)
            _config.PreviewScalePercent = previewScale;

        _config.PreviewCorner = (CornerPosition)CornerComboBox.SelectedIndex;

        if (double.TryParse(WidthTextBox.Text, out double w) && w >= 100 && w <= 7680)
            _config.PreviewWidthPx = w;
        if (double.TryParse(HeightTextBox.Text, out double h) && h >= 100 && h <= 4320)
            _config.PreviewHeightPx = h;
        if (int.TryParse(MarginXTextBox.Text, out int mx) && mx >= 0 && mx <= 200)
            _config.PreviewMarginXPx = mx;
        if (int.TryParse(MarginYTextBox.Text, out int my) && my >= 0 && my <= 200)
            _config.PreviewMarginYPx = my;

        _config.PreviewWindowOpacity = WindowOpacitySlider.Value;
        _config.PreviewContentOpacity = ContentOpacitySlider.Value;

        _config.ShowFileInfo = ShowFileInfoCheckBox.IsChecked ?? true;

        _config.ShowImages = ShowImagesCheckBox.IsChecked ?? true;
        _config.ShowVideos = ShowVideosCheckBox.IsChecked ?? true;
        _config.ShowTexts = ShowTextsCheckBox.IsChecked ?? true;
        _config.ShowAudios = ShowAudiosCheckBox.IsChecked ?? false;

        if (double.TryParse(MaxTextSizeTextBox.Text, out double ts) && ts >= 1 && ts <= 102400)
            _config.MaxTextFileSizeKB = ts;

        if (int.TryParse(DefaultVolumeTextBox.Text, out int defVolume) && defVolume >= 0 && defVolume <= 100)
            _config.VideoDefaultVolume = defVolume / 100.0;
        if (double.TryParse(DefaultSpeedTextBox.Text, out double defSpeed) && defSpeed >= 0.1 && defSpeed <= 8)
            _config.VideoDefaultSpeed = defSpeed;

        if (int.TryParse(VolumeStepTextBox.Text, out int volStep) && volStep >= 1 && volStep <= 100)
            _config.VideoVolumeStepPercent = volStep;
        if (double.TryParse(SeekStepTextBox.Text, out double seekStep) && seekStep >= 0.5 && seekStep <= 60)
            _config.VideoSeekStepSeconds = seekStep;
        if (double.TryParse(SpeedStepTextBox.Text, out double spdStep) && spdStep >= 0.05 && spdStep <= 2)
            _config.VideoSpeedStepMultiplier = spdStep;

        _config.EnableVideoCtrlWheel = CtrlWheelVolumeCheckBox.IsChecked ?? true;
        _config.EnableVideoCtrlWheelH = CtrlWheelSeekCheckBox.IsChecked ?? true;

        _config.ImageSizeMode = SizeModeFitRadio.IsChecked == true ? PreviewSizeMode.FitToWindow : PreviewSizeMode.MaxOriginalRatio;
        _config.EnableBlankDoubleClickBack = BlankDoubleClickCheckBox.IsChecked ?? true;
        _config.UseDarkTheme = DarkThemeCheckBox.IsChecked ?? true;

        _config.VideoHotkeyVolumeUp = VolumeUpHotkeyTextBox.Text;
        _config.VideoHotkeyVolumeDown = VolumeDownHotkeyTextBox.Text;
        _config.VideoHotkeyStepFwd = StepFwdHotkeyTextBox.Text;
        _config.VideoHotkeyStepBack = StepBackHotkeyTextBox.Text;
        _config.VideoHotkeySpeedUp = SpeedUpHotkeyTextBox.Text;
        _config.VideoHotkeySpeedDown = SpeedDownHotkeyTextBox.Text;

        _config.Normalize();
        ApplyUiScale();
    }

    private void ApplyTheme()
    {
        var bgColor = _isDarkTheme ? Color.FromRgb(0x1E, 0x1E, 0x1E) : Color.FromRgb(0xF5, 0xF5, 0xF5);
        var fgColor = _isDarkTheme ? Colors.White : Colors.Black;
        var groupBg = _isDarkTheme ? Color.FromRgb(0x25, 0x25, 0x26) : Color.FromRgb(0xFF, 0xFF, 0xFF);
        var borderColor = _isDarkTheme ? Color.FromRgb(0x3F, 0x3F, 0x46) : Color.FromRgb(0xE0, 0xE0, 0xE0);
        var accentColor = Color.FromRgb(0x00, 0x78, 0xD4);

        Background = new SolidColorBrush(bgColor);

        TitleTextBlock.Foreground = new SolidColorBrush(fgColor);
        SubtitleTextBlock.Foreground = new SolidColorBrush(_isDarkTheme ? Colors.Gray : Color.FromRgb(0x66, 0x66, 0x66));

        foreach (var gb in FindVisualChildren<GroupBox>(this))
        {
            gb.Background = new SolidColorBrush(groupBg);
            gb.BorderBrush = new SolidColorBrush(borderColor);
            gb.Foreground = new SolidColorBrush(fgColor);
        }

        foreach (var tb in FindVisualChildren<TextBox>(this))
        {
            tb.Background = new SolidColorBrush(_isDarkTheme ? Color.FromRgb(0x1E, 0x1E, 0x1E) : Colors.White);
            tb.Foreground = new SolidColorBrush(fgColor);
            tb.BorderBrush = new SolidColorBrush(borderColor);
        }

        foreach (var cb in FindVisualChildren<CheckBox>(this))
        {
            cb.Foreground = new SolidColorBrush(fgColor);
        }

        foreach (var rb in FindVisualChildren<RadioButton>(this))
        {
            rb.Foreground = new SolidColorBrush(fgColor);
        }

        foreach (var cb in FindVisualChildren<ComboBox>(this))
        {
            ApplyComboBoxTheme(cb, fgColor, groupBg, borderColor);
        }

        foreach (var tb in FindVisualChildren<TextBlock>(this))
        {
            if (tb != TitleTextBlock && tb != SubtitleTextBlock)
            {
                tb.Foreground = new SolidColorBrush(fgColor);
            }
        }

        ApplyButtonTheme(SaveButton, accentColor, Colors.White);
        ApplyButtonTheme(ApplyButton, _isDarkTheme ? Color.FromRgb(0x3F, 0x3F, 0x46) : Color.FromRgb(0xE0, 0xE0, 0xE0), fgColor);
        ApplyButtonTheme(ResetButton, _isDarkTheme ? Color.FromRgb(0x3F, 0x3F, 0x46) : Color.FromRgb(0xE0, 0xE0, 0xE0), fgColor);

        // 滚动条、滑块、提示框等共用同一套调色板，统一深/浅色外观
        ApplySharedPalette(bgColor, groupBg, borderColor, fgColor, accentColor);
    }

    private void ApplySharedPalette(Color bg, Color panel, Color border, Color fg, Color accent)
    {
        var app = Application.Current;
        if (app == null) return;

        var subText = _isDarkTheme ? Color.FromRgb(0xAA, 0xAA, 0xAA) : Color.FromRgb(0x66, 0x66, 0x66);
        var buttonBg = _isDarkTheme ? Color.FromRgb(0x3F, 0x3F, 0x46) : Color.FromRgb(0xE0, 0xE0, 0xE0);
        var track = _isDarkTheme ? Color.FromRgb(0x2A, 0x2A, 0x2E) : Color.FromRgb(0xE4, 0xE4, 0xE4);
        var thumb = _isDarkTheme ? Color.FromRgb(0x5A, 0x5A, 0x60) : Color.FromRgb(0xC0, 0xC0, 0xC0);
        var thumbHover = _isDarkTheme ? Color.FromRgb(0x7A, 0x7A, 0x82) : Color.FromRgb(0x9A, 0x9A, 0x9A);

        app.Resources["QP.Bg"] = new SolidColorBrush(bg);
        app.Resources["QP.Panel"] = new SolidColorBrush(panel);
        app.Resources["QP.Border"] = new SolidColorBrush(border);
        app.Resources["QP.Text"] = new SolidColorBrush(fg);
        app.Resources["QP.SubText"] = new SolidColorBrush(subText);
        app.Resources["QP.Accent"] = new SolidColorBrush(accent);
        app.Resources["QP.ButtonBg"] = new SolidColorBrush(buttonBg);
        app.Resources["QP.Track"] = new SolidColorBrush(track);
        app.Resources["QP.Thumb"] = new SolidColorBrush(thumb);
        app.Resources["QP.ThumbHover"] = new SolidColorBrush(thumbHover);
    }

    /// <summary>
    /// 下拉框的下拉列表由 Popup 承载、走系统主题色，只设 Background 不会变深。
    /// 这里同时覆盖系统画刷键 + 给 ComboBoxItem 单独套样式。
    /// </summary>
    private void ApplyComboBoxTheme(ComboBox cb, Color fgColor, Color itemBg, Color borderColor)
    {
        var foreground = new SolidColorBrush(fgColor);
        var background = new SolidColorBrush(_isDarkTheme ? Color.FromRgb(0x1E, 0x1E, 0x1E) : Colors.White);
        var itemBackground = new SolidColorBrush(itemBg);
        var highlight = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4));

        cb.Background = background;
        cb.Foreground = foreground;
        cb.BorderBrush = new SolidColorBrush(borderColor);

        // Popup 内的模板使用这些系统画刷，覆盖后才能变深
        cb.Resources[SystemColors.WindowBrushKey] = itemBackground;
        cb.Resources[SystemColors.ControlBrushKey] = itemBackground;
        cb.Resources[SystemColors.HighlightBrushKey] = highlight;
        cb.Resources[SystemColors.HighlightTextBrushKey] = Brushes.White;

        var itemStyle = new Style(typeof(ComboBoxItem));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, itemBackground));
        itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, foreground));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 4, 6, 4)));

        var hoverTrigger = new Trigger { Property = ComboBoxItem.IsHighlightedProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, highlight));
        hoverTrigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        itemStyle.Triggers.Add(hoverTrigger);

        cb.ItemContainerStyle = itemStyle;
    }

    private void ApplyButtonTheme(Button btn, Color bg, Color fg)
    {
        btn.Background = new SolidColorBrush(bg);
        btn.Foreground = new SolidColorBrush(fg);
        btn.BorderBrush = Brushes.Transparent;
    }

    /// <summary>
    /// Windows 标题栏跟随系统主题，需要 DWM 属性才能变深色。
    /// 20 是 Win10 20H1+ / Win11 的值，19 是更早版本用的旧值。
    /// </summary>
    private void ApplyTitleBarTheme()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        int dark = _isDarkTheme ? 1 : 0;

        if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref dark, sizeof(int));
        }
    }

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private static System.Collections.Generic.IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
    {
        if (depObj == null) yield break;

        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(depObj); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(depObj, i);
            if (child is T t) yield return t;
            foreach (var gc in FindVisualChildren<T>(child)) yield return gc;
        }
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // 滑块右侧实时显示百分比
        if (WindowOpacityValueText != null && ReferenceEquals(sender, WindowOpacitySlider))
        {
            WindowOpacityValueText.Text = $"{Math.Round(e.NewValue * 100)}%";
        }
        else if (ContentOpacityValueText != null && ReferenceEquals(sender, ContentOpacitySlider))
        {
            ContentOpacityValueText.Text = $"{Math.Round(e.NewValue * 100)}%";
        }
    }

    private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box)
        {
            box.SelectAll();
        }
    }

    private void HotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not TextBox box) return;

        e.Handled = true;

        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;

        if (key == System.Windows.Input.Key.Escape ||
            key == System.Windows.Input.Key.Back ||
            key == System.Windows.Input.Key.Delete)
        {
            box.Text = string.Empty;
            return;
        }

        if (HotkeyText.IsModifierKey(key)) return;

        var combo = HotkeyText.FromWpfKey(key, System.Windows.Input.Keyboard.Modifiers);
        if (combo == null) return;

        box.Text = combo.ToString();
        box.CaretIndex = box.Text.Length;
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ApplyConfigFromUi();
            _previewManager.UpdateConfig(_config);

            _isDarkTheme = _config.UseDarkTheme;
            ApplyTheme();
            ApplyTitleBarTheme();

            ThemedDialog.Info(this, "设置已应用。");
        }
        catch (Exception ex)
        {
            ThemedDialog.Info(this, $"应用失败: {ex.Message}");
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ApplyConfigFromUi();
            _previewManager.UpdateConfig(_config);

            ThemedDialog.Info(this, "设置已保存并应用。");
            Hide();
        }
        catch (Exception ex)
        {
            ThemedDialog.Info(this, $"保存失败: {ex.Message}");
        }
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        var confirmed = ThemedDialog.Confirm(this, "确定要恢复默认设置吗？");

        if (confirmed)
        {
            _config = AppConfig.Default;
            _isDarkTheme = _config.UseDarkTheme;
            LoadConfigToUi();
            ApplyTheme();
            _previewManager.UpdateConfig(_config);
        }
    }
}
