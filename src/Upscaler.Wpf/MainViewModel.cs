using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using Upscaler.Core;
namespace Upscaler.Wpf;

public sealed class ImageItem : Observable
{
    public required string Path { get; init; }
    public string Name => System.IO.Path.GetFileName(Path);
    private BitmapSource? thumbnail;
    private string? error;
    public BitmapSource? Thumbnail { get => thumbnail; set => Set(ref thumbnail, value); }
    public string? Error { get => error; set => Set(ref error, value); }
    public CancellationTokenSource? Loading { get; set; }
}

public sealed class MainViewModel : Observable
{
    public Settings Settings { get; }
    public string Root { get; }
    public ObservableCollection<ImageItem> Files { get; } = new();
    public string[] Devices { get; } = ["auto", "cuda", "cpu"];
    public string[] Precisions { get; } = ["auto", "fp16", "fp32"];
    public string[] Formats { get; } = ["PNG", "JPEG"];
    public string Device { get => Settings.Device; set => Settings.Device = value; }
    public string Precision { get => Settings.Precision; set => Settings.Precision = value; }
    public string Format { get => Settings.Format; set => Settings.Format = value; }
    public string Output { get => Settings.Output; set { Settings.Output = value; Changed(); } }
    public string TileText { get => tileText; set => Set(ref tileText, value); }
    private string tileText;
    private string folder = "", address = "", filter = "", status = "폴더를 선택하세요.", engineStatus = "Worker 준비 전";
    private string previewMessage = "이미지를 선택하세요.", imageInfo = "";
    private BitmapSource? preview;
    private ImageItem? selected;
    private string[] snapshot = [];
    private long navigationVersion;
    private CancellationTokenSource navigation = new(), previewCancel = new(), thumbnails = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim decoders = new(3, 3);
    private readonly LruCache<string, BitmapSource> cache = new(128);
    private readonly Func<string, CancellationToken, Task<string[]>> scan;
    public string Folder { get => folder; private set => Set(ref folder, value); }
    public string Address { get => address; set => Set(ref address, value); }
    public string Filter { get => filter; set { if (Set(ref filter, value)) ApplyFilter(); } }
    public string Status { get => status; set => Set(ref status, value); }
    public string EngineStatus { get => engineStatus; set => Set(ref engineStatus, value); }
    public string PreviewMessage { get => previewMessage; set => Set(ref previewMessage, value); }
    public string ImageInfo { get => imageInfo; set => Set(ref imageInfo, value); }
    public BitmapSource? Preview { get => preview; set => Set(ref preview, value); }
    public int OriginalWidth { get; private set; }
    public int OriginalHeight { get; private set; }
    public ImageItem? Selected
    {
        get => selected;
        set { if (Set(ref selected, value)) { Changed(nameof(HasSelection)); _ = LoadPreviewAsync(value); } }
    }
    public bool HasSelection => Selected is not null;
    public bool HasFiles => Files.Count > 0;
    public int CachedThumbnailCount => cache.Count;

    public MainViewModel(Func<string, CancellationToken, Task<string[]>>? scanner = null, Settings? settings = null)
    {
        Settings = settings ?? Settings.Load();
        tileText = Settings.Tile.ToString();
        scan = scanner ?? ((path, token) => Task.Run(() => Images.Scan(path, token), token));
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(System.IO.Path.Combine(dir.FullName, "python", "worker.py"))) dir = dir.Parent;
        Root = dir?.FullName ?? AppContext.BaseDirectory;
    }

    public Settings CaptureSettings()
    {
        if (!int.TryParse(TileText, out var tile)) throw new ArgumentException("타일 크기는 정수로 입력하세요.");
        var copy = Settings.Snapshot(); copy.Tile = tile; copy.Validate(); Settings.Tile = tile; return copy;
    }

    public async Task<bool> NavigateAsync(string path)
    {
        var version = ++navigationVersion;
        navigation.Cancel(); navigation.Dispose(); navigation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = navigation.Token;
        thumbnails.Cancel();
        Status = "폴더를 읽는 중…";
        try
        {
            var full = System.IO.Path.GetFullPath(path);
            var files = await scan(full, token);
            if (token.IsCancellationRequested || version != navigationVersion) return false;
            var previous = string.Equals(Folder, full, StringComparison.OrdinalIgnoreCase) ? Selected?.Path : null;
            snapshot = files; Folder = full; Address = full; Settings.Folder = full;
            ApplyFilter(previous);
            Status = $"{full} · {files.Length}개 이미지";
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception error)
        {
            if (version == navigationVersion)
            {
                Status = "폴더를 열 수 없습니다: " + error.Message;
                Address = Folder;
                ApplyFilter(Selected?.Path);
            }
            return false;
        }
    }

    private void ApplyFilter(string? preserve = null)
    {
        preserve ??= Selected?.Path;
        thumbnails.Cancel(); thumbnails.Dispose(); thumbnails = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        foreach (var item in Files) ReleaseThumbnail(item);
        Files.Clear();
        foreach (var path in snapshot.Where(p => System.IO.Path.GetFileName(p).Contains(Filter, StringComparison.CurrentCultureIgnoreCase)))
            Files.Add(new() { Path = path });
        Selected = Files.FirstOrDefault(x => string.Equals(x.Path, preserve, StringComparison.OrdinalIgnoreCase));
        Changed(nameof(HasFiles));
        if (Files.Count == 0 && Selected is null) PreviewMessage = "표시할 이미지가 없습니다.";
    }

    public async Task LoadThumbnailAsync(ImageItem item)
    {
        ReleaseThumbnail(item);
        var loading = CancellationTokenSource.CreateLinkedTokenSource(thumbnails.Token, lifetime.Token);
        item.Loading = loading;
        try
        {
            var key = await Task.Run(() => { var info = new FileInfo(item.Path); return $"{item.Path}|{info.LastWriteTimeUtc.Ticks}|{info.Length}"; }, loading.Token);
            loading.Token.ThrowIfCancellationRequested();
            if (!cache.TryGet(key, out var bitmap))
            {
                bitmap = (await DecodeAsync(item.Path, 160, loading.Token)).Image;
                loading.Token.ThrowIfCancellationRequested();
                cache.Put(key, bitmap);
            }
            if (ReferenceEquals(item.Loading, loading)) item.Thumbnail = bitmap;
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!loading.IsCancellationRequested) item.Error = error.Message; }
    }

    public void ReleaseThumbnail(ImageItem item)
    {
        item.Loading?.Cancel(); item.Loading?.Dispose(); item.Loading = null; item.Thumbnail = null;
    }

    public async Task<(BitmapSource Image, string Dimensions)> DecodeAsync(string path, int size, CancellationToken token)
    {
        await decoders.WaitAsync(token);
        try
        {
            // Bitmap decoding and subprocess setup are also kept off the Dispatcher.
            return await Task.Run(async () =>
            {
                var start = new ProcessStartInfo(System.IO.Path.Combine(Root, ".venv", "Scripts", "python.exe"))
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
                  StandardErrorEncoding = System.Text.Encoding.UTF8 };
                start.Environment["PYTHONUTF8"] = "1";
                start.ArgumentList.Add(System.IO.Path.Combine(Root, "python", "decode.py"));
                start.ArgumentList.Add(path); start.ArgumentList.Add(size.ToString());
                using var process = Process.Start(start) ?? throw new IOException("이미지 디코더를 시작하지 못했습니다.");
                using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });
                var error = process.StandardError.ReadToEndAsync();
                using var buffer = new MemoryStream();
                try
                {
                    await process.StandardOutput.BaseStream.CopyToAsync(buffer, token);
                    await process.WaitForExitAsync(token);
                    var info = await error;
                    token.ThrowIfCancellationRequested();
                    if (process.ExitCode != 0) throw new IOException(info.Trim());
                    buffer.Position = 0;
                    var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = buffer; bitmap.EndInit(); bitmap.Freeze();
                    return ((BitmapSource)bitmap, info.Trim());
                }
                finally
                {
                    if (!process.HasExited) process.Kill(true);
                    await process.WaitForExitAsync(); await error;
                }
            }, token);
        }
        finally { decoders.Release(); }
    }

    public async Task LoadPreviewAsync(ImageItem? item)
    {
        previewCancel.Cancel(); previewCancel.Dispose(); previewCancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = previewCancel.Token;
        Preview = null; ImageInfo = ""; OriginalWidth = OriginalHeight = 0;
        PreviewMessage = item is null ? "이미지를 선택하세요." : "이미지를 불러오는 중…";
        if (item is null) return;
        try
        {
            var decoded = await DecodeAsync(item.Path, 4096, token);
            token.ThrowIfCancellationRequested();
            var dimensions = decoded.Dimensions.Split('x');
            OriginalWidth = int.Parse(dimensions[0]); OriginalHeight = int.Parse(dimensions[1]);
            ImageInfo = $"{item.Name} · {decoded.Dimensions}";
            PreviewMessage = ""; Preview = decoded.Image;
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!token.IsCancellationRequested) PreviewMessage = error.Message; }
    }

    public void Stop()
    {
        lifetime.Cancel(); navigation.Cancel(); previewCancel.Cancel(); thumbnails.Cancel();
        foreach (var item in Files) ReleaseThumbnail(item);
        cache.Clear();
    }
}
