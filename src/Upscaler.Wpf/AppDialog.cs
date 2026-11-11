using System.Windows;
using System.Windows.Controls;
namespace Upscaler.Wpf;

public sealed class AppDialog : ThemedWindow
{
    public AppDialog(string title, string message, string accept = "확인", bool allowCancel = true)
    {
        Title = title; Width = 480; SizeToContent = SizeToContent.Height; MaxHeight = 640;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 16) });
        panel.Children.Add(new ScrollViewer { MaxHeight = 390, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, LineHeight = 23 } });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 24, 0, 0) };
        if (allowCancel)
        {
            var cancel = new Button { Content = "돌아가기", IsCancel = true, MinWidth = 90 };
            cancel.Click += (_, _) => DialogResult = false; buttons.Children.Add(cancel);
        }
        var confirm = new Button { Content = accept, IsDefault = true, MinWidth = 100 };
        confirm.SetResourceReference(StyleProperty, "PrimaryButtonStyle");
        confirm.Click += (_, _) => DialogResult = true; buttons.Children.Add(confirm);
        panel.Children.Add(buttons); var frame = new Border { Padding = new(24), Child = panel };
        frame.SetResourceReference(Border.BackgroundProperty, "BackgroundBrush"); Content = frame;
    }
    public static bool Confirm(Window owner, string title, string message, string accept = "확인") =>
        new AppDialog(title, message, accept) { Owner = owner }.ShowDialog() == true;
    public static void Inform(Window owner, string title, string message) =>
        new AppDialog(title, message, allowCancel: false) { Owner = owner }.ShowDialog();
}
