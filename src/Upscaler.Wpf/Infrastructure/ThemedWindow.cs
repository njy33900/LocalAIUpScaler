using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Upscaler.Wpf;

/// <summary>

/// Explicitly themes derived windows as well as their native caption.

/// </summary>
public class ThemedWindow : Window
{
    public int NativeDarkModeResult
    {
        get;
        private set;
    } = -1;

    /// <summary>
    /// 공통 창 스타일·아이콘을 연결하고 네이티브 핸들이 준비되면 제목 표시줄 테마를 적용합니다.
    /// </summary>
    public ThemedWindow()
    {
        SetResourceReference(StyleProperty, "AppWindowStyle");
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Upscaler.Wpf;component/Assets/App.ico"));
        SourceInitialized += (_, _) => ApplyNativeTheme();
    }

    /// <summary>
    /// 지원되는 DWM 속성으로 다크 제목 표시줄을 적용하며 구버전 속성 번호도 시도합니다.
    /// </summary>
    private void ApplyNativeTheme()
    {
        var handle = new WindowInteropHelper(this).Handle;
        int dark = 1;
        NativeDarkModeResult = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));

        if (NativeDarkModeResult < 0)
        {
            NativeDarkModeResult = DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
        }

        // Windows 11 supports explicit caption/border colors. Older versions ignore these.
        int caption = 0x00241C17;
        int text = 0x00F7F1E8;
        int border = 0x004C3E32;
        _ = DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
        _ = DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
        _ = DwmSetWindowAttribute(handle, 34, ref border, sizeof(int));
    }

    /// <summary>
    /// Windows DWM 창 속성을 설정하고 HRESULT를 반환합니다.
    /// </summary>
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
