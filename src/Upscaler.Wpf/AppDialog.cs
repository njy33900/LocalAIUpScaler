using System.Windows;
using System.Windows.Controls;

namespace Upscaler.Wpf;

/// <summary>
/// 확인과 안내에 공통으로 사용하는 소유 창 기반의 다크 테마 모달입니다.
/// </summary>
public sealed class AppDialog : ThemedWindow
{

    /// <summary>
    /// 메시지와 확인·취소 버튼을 구성하고 키보드 기본 동작을 지정합니다.
    /// </summary>
    public AppDialog(string title, string message, string accept = "확인", bool allowCancel = true)
    {
        Title = title;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 640;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 16) });
        panel.Children.Add(new ScrollViewer
        {
            MaxHeight = 390,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, LineHeight = 23 }
        });
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new(0, 24, 0, 0)
        };

        if (allowCancel)
        {
            var cancel = new Button
            {
                Content = "돌아가기",
                IsCancel = true,
                MinWidth = 90
            };
            cancel.Click += (_, _) => DialogResult = false;
            buttons.Children.Add(cancel);
        }

        var confirm = new Button
        {
            Content = accept,
            IsDefault = true,
            MinWidth = 100
        };
        confirm.SetResourceReference(StyleProperty, "PrimaryButtonStyle");
        confirm.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(confirm);
        panel.Children.Add(buttons);
        var frame = new Border
        {
            Padding = new(24),
            Child = panel
        };
        frame.SetResourceReference(Border.BackgroundProperty, "BackgroundBrush");
        Content = frame;
    }

    /// <summary>
    /// 지정한 소유 창에 확인 모달을 열고 사용자가 승인한 경우에만 true를 반환합니다.
    /// </summary>
    public static bool Confirm(Window owner, string title, string message, string accept = "확인")
    {
        return new AppDialog(title, message, accept)
        {
            Owner = owner
        }.ShowDialog() == true;
    }

    /// <summary>
    /// 취소 선택이 필요 없는 안내 모달을 소유 창 위에 표시합니다.
    /// </summary>
    public static void Inform(Window owner, string title, string message)
    {
        new AppDialog(title, message, allowCancel: false)
        {
            Owner = owner
        }.ShowDialog();
    }
}
