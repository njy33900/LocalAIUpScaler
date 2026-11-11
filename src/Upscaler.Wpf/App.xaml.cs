using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
namespace Upscaler.Wpf;

public partial class App : Application
{
    private SingleInstance? instance;
    protected override async void OnStartup(StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);
        try
        {
            instance = new SingleInstance();
            if (!instance.IsPrimary)
            {
                await instance.NotifyExistingAsync();
                Shutdown();
                return;
            }
            instance.Listen(() => Dispatcher.BeginInvoke(ActivateExistingWindow));
            var window = new MainWindow(); MainWindow = window;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            window.Show();
        }
        catch (Exception error)
        {
            Upscaler.Core.AppLog.Write(error.ToString());
            new AppDialog("프로그램 시작 오류", "프로그램을 시작하지 못했습니다. 실행 환경을 확인해 주세요.", allowCancel: false).ShowDialog();
            Shutdown(1);
        }
    }
    public static Window ActivationTarget(Window window)
    {
        // Confirmation dialogs can be owned by the progress modal; activate the deepest visible owner.
        var child = window.OwnedWindows.Cast<Window>().LastOrDefault(w => w.IsVisible);
        return child is null ? window : ActivationTarget(child);
    }
    private void ActivateExistingWindow()
    {
        if (MainWindow is null) return;
        if (MainWindow.WindowState == WindowState.Minimized) MainWindow.WindowState = WindowState.Normal;
        var target = ActivationTarget(MainWindow);
        if (target.WindowState == WindowState.Minimized) target.WindowState = WindowState.Normal;
        target.Activate();
        _ = SetForegroundWindow(new WindowInteropHelper(target).Handle);
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
}
