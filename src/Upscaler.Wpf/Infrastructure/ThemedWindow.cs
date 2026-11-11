using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
namespace Upscaler.Wpf;

/// <summary>Explicitly themes derived windows as well as their native caption.</summary>
public class ThemedWindow : Window
{
    public int NativeDarkModeResult { get; private set; } = -1;
    public ThemedWindow()
    {
        SetResourceReference(StyleProperty, "AppWindowStyle");
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Upscaler.Wpf;component/Assets/App.ico"));
        SourceInitialized += (_, _) => ApplyNativeTheme();
    }
    private void ApplyNativeTheme()
    {
        var handle = new WindowInteropHelper(this).Handle;
        int dark = 1;
        NativeDarkModeResult = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        if (NativeDarkModeResult < 0) NativeDarkModeResult = DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
        // Windows 11 supports explicit caption/border colors. Older versions ignore these.
        int caption = 0x00241C17, text = 0x00F7F1E8, border = 0x004C3E32;
        _ = DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
        _ = DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
        _ = DwmSetWindowAttribute(handle, 34, ref border, sizeof(int));
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
