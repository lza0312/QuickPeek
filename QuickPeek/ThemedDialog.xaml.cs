using System.Windows;
using System.Windows.Input;

namespace QuickPeek;

/// <summary>
/// 与设置界面同风格的提示框，替代系统 MessageBox（系统弹窗始终是浅色的）。
/// </summary>
public partial class ThemedDialog : Window
{
    private ThemedDialog(string message, bool confirm)
    {
        InitializeComponent();

        MessageText.Text = message;
        CancelButton.Visibility = confirm ? Visibility.Visible : Visibility.Collapsed;

        OkButton.Click += (_, _) => DialogResult = true;
        CancelButton.Click += (_, _) => DialogResult = false;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) DialogResult = false;
        };
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    public static void Info(Window? owner, string message)
    {
        Show(owner, message, confirm: false);
    }

    public static bool Confirm(Window? owner, string message)
    {
        return Show(owner, message, confirm: true);
    }

    private static bool Show(Window? owner, string message, bool confirm)
    {
        var dialog = new ThemedDialog(message, confirm);

        if (owner != null && owner.IsVisible)
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return dialog.ShowDialog() == true;
    }
}
