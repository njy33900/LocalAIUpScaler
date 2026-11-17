using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Upscaler.Wpf;

public partial class RestorationPanel : UserControl
{
    public RestorationPanel() => InitializeComponent();
    private async void Refresh(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            await vm.Advanced.RefreshAsync();
    }
    private void Configure(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;
        var config = vm.Advanced.ConfigPath;
        var target = File.Exists(config) ? config : Path.Combine(vm.Root, "docs", "advanced-model-installation.md");
        try
        {
            Process.Start(new ProcessStartInfo("notepad.exe") { ArgumentList = { target }, UseShellExecute = false });
        }
        catch (Exception error)
        {
            AppDialog.Inform(Window.GetWindow(this)!, "설치 안내", error.Message);
        }
    }
}
