using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
namespace Upscaler.Core;

public class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Changed(name); return true;
    }
}

public interface IUpscaleEngine
{
    event Action<JsonElement>? Progress;
    Task<JsonElement> UpscaleAsync(string input, Settings settings, CancellationToken token);
}

public sealed class Settings
{
    public string Folder { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    public string Output { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Upscaled");
    public string Device { get; set; } = "auto";
    public string Precision { get; set; } = "auto";
    public string Format { get; set; } = "PNG";
    public int Tile { get; set; }
    public double TreeWidth { get; set; } = 220;
    public double ThumbnailHeight { get; set; } = 170;
    public string Theme { get; set; } = "dark";
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalAIUpscaler");

    public void Validate()
    {
        if (Device is not ("auto" or "cuda" or "cpu")) throw new ArgumentException("장치 설정이 올바르지 않습니다.");
        if (Precision is not ("auto" or "fp16" or "fp32")) throw new ArgumentException("정밀도 설정이 올바르지 않습니다.");
        if (Format is not ("PNG" or "JPEG")) throw new ArgumentException("출력 형식이 올바르지 않습니다.");
        if (Tile != 0 && (Tile < 32 || Tile > 2048)) throw new ArgumentException("타일 크기는 0(자동) 또는 32~2048이어야 합니다.");
        if (string.IsNullOrWhiteSpace(Output) || !Path.IsPathFullyQualified(Output)) throw new ArgumentException("출력 폴더의 전체 경로를 입력하세요.");
        _ = Path.GetFullPath(Output);
    }

    public Settings Snapshot() => (Settings)MemberwiseClone();
    public static Settings Load(string? path = null)
    {
        try
        {
            var value = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path ?? Path.Combine(DirectoryPath, "settings.json"))) ?? new();
            value.Validate();
            if (string.IsNullOrWhiteSpace(value.Folder)) value.Folder = new Settings().Folder;
            value.TreeWidth = double.IsFinite(value.TreeWidth) ? Math.Clamp(value.TreeWidth, 140, 500) : 220;
            value.ThumbnailHeight = double.IsFinite(value.ThumbnailHeight) ? Math.Clamp(value.ThumbnailHeight, 100, 350) : 170;
            value.Theme = "dark";
            return value;
        }
        catch { return new(); }
    }

    public void Save(string? path = null)
    {
        path ??= Path.Combine(DirectoryPath, "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(this)); File.Move(temporary, path, true); }
        finally { File.Delete(temporary); }
    }
}

public static class Images
{
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".webp" };
    public static string[] Scan(string folder, CancellationToken token = default)
    {
        var files = new List<string>();
        foreach (var path in Directory.EnumerateFiles(folder))
        {
            token.ThrowIfCancellationRequested();
            if (Extensions.Contains(Path.GetExtension(path))) files.Add(path);
        }
        return files.OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
}

public static class AppLog
{
    private static readonly object Sync = new();
    public static void Write(string message)
    {
        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(Settings.DirectoryPath);
                var path = Path.Combine(Settings.DirectoryPath, "worker.log");
                if (File.Exists(path) && new FileInfo(path).Length > 5 * 1024 * 1024)
                    File.Move(path, path + ".previous", true);
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
            catch { /* Logging must not prevent cleanup or stderr draining. */ }
        }
    }
}
