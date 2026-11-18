using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Upscaler.Wpf;

/// <summary>
/// 앱 시작·종료와 사용자 세션별 단일 실행 창 활성화를 담당합니다.
/// </summary>
public partial class App : Application
{
    private SingleInstance? instance;

    /// <summary>
    /// 첫 인스턴스이면 메인 창을 열고, 후속 인스턴스이면 기존 창에 활성화 요청만 보냅니다.
    /// </summary>
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
            var window = new MainWindow();
            MainWindow = window;
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

    /// <summary>
    /// 소유 창 위의 활성 모달을 찾아 실제로 전면에 표시할 창을 반환합니다.
    /// </summary>
    public static Window ActivationTarget(Window window)
    {
        // Confirmation dialogs can be owned by the progress modal; activate the deepest visible owner.
        var child = window.OwnedWindows.Cast<Window>().LastOrDefault(w => w.IsVisible);

        return child is null ? window : ActivationTarget(child);
    }

    /// <summary>
    /// 메인 창과 열린 모달을 UI Dispatcher에서 복원·활성화합니다.
    /// </summary>
    private void ActivateExistingWindow()
    {
        if (MainWindow is null)
        {
            return;
        }

        if (MainWindow.WindowState == WindowState.Minimized)
        {
            MainWindow.WindowState = WindowState.Normal;
        }

        var target = ActivationTarget(MainWindow);

        if (target.WindowState == WindowState.Minimized)
        {
            target.WindowState = WindowState.Normal;
        }

        target.Activate();
        _ = SetForegroundWindow(new WindowInteropHelper(target).Handle);
    }

    /// <summary>
    /// 앱 종료 시 단일 실행 알림과 소유한 인스턴스 자원을 정리합니다.
    /// </summary>
    protected override void OnExit(ExitEventArgs e)
    {
        instance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Windows에 지정한 창을 전면으로 활성화하도록 요청합니다.
    /// </summary>
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
}
