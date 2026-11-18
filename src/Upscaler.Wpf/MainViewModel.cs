using System.Windows.Data;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using Upscaler.Core;

namespace Upscaler.Wpf;

/// <summary>
/// 설정 저장용 키와 사용자에게 표시할 이름을 연결합니다.
/// </summary>
public sealed record SettingChoice(string Key, string Label);

/// <summary>
/// 이미지 경로·메타데이터·썸네일과 항목별 비동기 요청 상태를 보관합니다.
/// </summary>
public sealed class ImageItem : Observable
{
    public required string Path
    {
        get;
        init;
    }

    public DateTime Modified
    {
        get;
        set;
    }

    public long FileSize
    {
        get;
        set;
    }

    public string Name => System.IO.Path.GetFileName(Path);

    private BitmapSource? thumbnail;
    private string? error;

    public BitmapSource? Thumbnail
    {
        get
        {
            return thumbnail;
        }

        set
        {
            Set(ref thumbnail, value);
        }
    }

    public string? Error
    {
        get
        {
            return error;
        }

        set
        {
            Set(ref error, value);
        }
    }

    public CancellationTokenSource? Loading
    {
        get;
        set;
    }
}

/// <summary>
/// 이미지 탐색·정렬·프리뷰와 작업 설정을 관리합니다. UI 바인딩과 캐시 접근은 UI 스레드에서 수행합니다.
/// </summary>
public sealed class MainViewModel : Observable
{
    public Settings Settings
    {
        get;
    }

    public string Root
    {
        get;
    }

    public ObservableCollection<ImageItem> Files
    {
        get;
    } = new();

    public SettingChoice[] Devices
    {
        get;
    } = [
        new("auto", "자동 · GPU 우선"),
        new("cuda", "NVIDIA CUDA"),
        new("cpu", "CPU")
    ];

    public SettingChoice[] Precisions
    {
        get;
    } = [
        new("auto", "자동"),
        new("fp16", "FP16"),
        new("fp32", "FP32")
    ];

    public SettingChoice[] SortChoices
    {
        get;
    } = [
        new("name", "이름"),
        new("modified", "수정일"),
        new("size", "파일 크기")
    ];

    public string SortKey
    {
        get
        {
            return Settings.ThumbnailSort;
        }

        set
        {
            if (value is "name" or "modified" or "size")
            {
                Settings.ThumbnailSort = value;
                Changed();
                _ = SortThumbnailsAsync();
            }
        }
    }

    public bool SortDescending
    {
        get
        {
            return Settings.ThumbnailDescending;
        }

        set
        {
            Settings.ThumbnailDescending = value;
            Changed();
            Changed(nameof(SortDirectionLabel));
            _ = SortThumbnailsAsync();
        }
    }

    public string SortDirectionLabel => SortDescending ? "↓" : "↑";

    private long sortVersion;
    // 정렬 도중 폴더나 기준이 바뀌면 이전 메타데이터 조회 결과를 폐기한다.

    /// <summary>
    /// 정렬에 필요한 메타데이터를 백그라운드에서 읽고 오래된 탐색 결과를 제외한 뒤 선택을 복원합니다.
    /// </summary>
    public async Task SortThumbnailsAsync()
    {
        var version = ++sortVersion;
        var items = Files.ToArray();
        var key = SortKey;
        var descending = SortDescending;

        if (key != "name")
        {
            var metadata = await Task.Run(() => items.Select(item =>
            {
                try
                {
                    var info = new FileInfo(item.Path);

                    return (Item: item, Modified: info.LastWriteTimeUtc, Size: info.Length);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    return (Item: item, Modified: DateTime.MinValue, Size: -1L);
                }
            }).ToArray());

            if (version != sortVersion || lifetime.IsCancellationRequested)
            {
                return;
            }

            foreach (var data in metadata)
            {
                data.Item.Modified = data.Modified;
                data.Item.FileSize = data.Size;
            }
        }

        if (version != sortVersion || lifetime.IsCancellationRequested)
        {
            return;
        }

        var view = CollectionViewSource.GetDefaultView(Files);
        using (view.DeferRefresh())
        {
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(
                    key switch
                    {
                "modified" => nameof(ImageItem.Modified),
                "size" => nameof(ImageItem.FileSize),
                _ => nameof(ImageItem.Name)
            },
                    descending ? ListSortDirection.Descending : ListSortDirection.Ascending
                ));

            if (key != "name")
            {
                view.SortDescriptions.Add(new SortDescription(nameof(ImageItem.Name), ListSortDirection.Ascending));
            }
        }
    }

    public SettingChoice[] Models
    {
        get;
    } = [
        new("RealESRGAN_x4plus", "범용 · x4plus"),
        new("RealESRGAN_x4plus_anime_6B", "애니메이션 · anime 6B"),
        new("realesr-general-x4v3", "경량 범용 · general x4v3")
    ];

    public string Model
    {
        get
        {
            return Settings.Model;
        }

        set
        {
            Settings.Model = value;
            Changed();
            Changed(nameof(DenoiseEnabled));
            Changed(nameof(ModelStatus));
        }
    }

    public string ModelStatus
    {
        get
        {
            var installed = File.Exists(System.IO.Path.Combine(Root, "models", Model + ".pth"));

            if (DenoiseEnabled)
            {
                installed &= File.Exists(System.IO.Path.Combine(Root, "models", "realesr-general-wdn-x4v3.pth"));
            }

            return installed ? "설치됨 · 실행 시 무결성 확인" : "설치 필요 · scripts/install_models.ps1";
        }
    }

    public bool DenoiseEnabled => Model == "realesr-general-x4v3";

    public double Denoise
    {
        get
        {
            return Settings.Denoise;
        }

        set
        {
            Settings.Denoise = value;
            Changed();
        }
    }

    public string[] Formats
    {
        get;
    } = ["PNG", "JPEG"];

    public string Device
    {
        get
        {
            return Settings.Device;
        }

        set
        {
            Settings.Device = value;
            Changed();
            Changed(nameof(Precision));
            Changed(nameof(PrecisionEnabled));

            if (EngineReady)
            {
                MarkEngineReady(cudaAvailable);
            }
        }
    }

    public bool PrecisionEnabled => Device != "cpu";

    public string Precision
    {
        get
        {
            return Device == "cpu" ? "fp32" : Settings.Precision;
        }

        set
        {
            if (Device != "cpu")
            {
                Settings.Precision = value;
            }

            Changed();
        }
    }

    public string Format
    {
        get
        {
            return Settings.Format;
        }

        set
        {
            Settings.Format = value;
        }
    }

    public string Output
    {
        get
        {
            return Settings.Output;
        }

        set
        {
            Settings.Output = value;
            Changed();
        }
    }

    public IReadOnlyList<TilePreset> Presets => TilePresets.All;

    private TilePreset selectedPreset;

    public TilePreset SelectedPreset
    {
        get
        {
            return selectedPreset;
        }

        set
        {
            if (value is not null && Presets.Contains(value) && Set(ref selectedPreset, value))
            {
                Settings.TilePreset = value.Key;
                Settings.Tile = value.TileSize;
                Changed(nameof(PresetHint));
            }
        }
    }

    public string PresetHint => SelectedPreset.Description;

    private bool cudaAvailable;

    public bool EngineReady
    {
        get;
        private set;
    }

    public bool EngineError => EngineStatus.Contains("오류") || EngineStatus.Contains("실패");

    /// <summary>
    /// Worker가 보고한 CUDA 가용성을 보관하고 장치·정밀도 안내를 갱신합니다.
    /// </summary>
    public void MarkEngineReady(bool cuda)
    {
        cudaAvailable = cuda;
        EngineStatus = Device == "cuda" && !cuda ? "엔진 오류 · CUDA 사용 불가" : "엔진 준비됨";
        EngineReady = !EngineError;
        Changed(nameof(EngineReady));
    }

    private string folder = "", address = "", filter = "", status = "폴더를 선택하세요.", engineStatus = "엔진 준비 중…";
    private string previewMessage = "이미지를 선택하세요.", imageInfo = "";
    private BitmapSource? preview;
    private ImageItem? selected;
    private string[] snapshot = [];
    private long navigationVersion;
    private CancellationTokenSource navigation = new(), previewCancel = new(), thumbnails = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim decoders = new(3, 3);
    private readonly LruCache<string, BitmapSource> cache = new(256);
    private readonly ThumbnailDecoder thumbnailDecoder;

    public int ThumbnailDecoderStarts => thumbnailDecoder.Starts;

    private readonly Func<string, CancellationToken, Task<string[]>> scan;

    public string Folder
    {
        get
        {
            return folder;
        }

        private set
        {
            Set(ref folder, value);
        }
    }

    public string Address
    {
        get
        {
            return address;
        }

        set
        {
            Set(ref address, value);
        }
    }

    public string Filter
    {
        get
        {
            return filter;
        }

        set
        {
            if (Set(ref filter, value))
            {
                ApplyFilter();
            }
        }
    }

    public string Status
    {
        get
        {
            return status;
        }

        set
        {
            Set(ref status, value);
        }
    }

    public string EngineStatus
    {
        get
        {
            return engineStatus;
        }

        set
        {
            if (Set(ref engineStatus, value))
            {
                EngineReady = false;
                Changed(nameof(EngineReady));
                Changed(nameof(EngineError));
            }
        }
    }

    public string PreviewMessage
    {
        get
        {
            return previewMessage;
        }

        set
        {
            Set(ref previewMessage, value);
        }
    }

    public string ImageInfo
    {
        get
        {
            return imageInfo;
        }

        set
        {
            Set(ref imageInfo, value);
        }
    }

    public string ImageDimensions => OriginalWidth > 0 ? $"{OriginalWidth:N0} × {OriginalHeight:N0}" : "";

    public BitmapSource? Preview
    {
        get
        {
            return preview;
        }

        set
        {
            Set(ref preview, value);
        }
    }

    public int OriginalWidth
    {
        get;
        private set;
    }

    public int OriginalHeight
    {
        get;
        private set;
    }

    public ImageItem? Selected
    {
        get
        {
            return selected;
        }

        set
        {
            if (Set(ref selected, value))
            {
                Changed(nameof(HasSelection));
                _ = LoadPreviewAsync(value);
            }
        }
    }

    public bool HasSelection => Selected is not null;

    public bool HasFiles => Files.Count > 0;

    public int CachedThumbnailCount => cache.Count;

    /// <summary>
    /// 설정과 탐색기를 구성하고 저장된 프리셋을 호환 변환한 뒤 프로젝트 루트·디코더를 준비합니다.
    /// </summary>
    public MainViewModel(Func<string, CancellationToken, Task<string[]>>? scanner = null, Settings? settings = null)
    {
        Settings = settings ?? Settings.Load();
        selectedPreset = TilePresets.Resolve(Settings.TilePreset, Settings.Tile);
        Settings.TilePreset = selectedPreset.Key;
        Settings.Tile = selectedPreset.TileSize;
        scan = scanner ?? ((path, token) => Task.Run(() => Images.Scan(path, token), token));
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(System.IO.Path.Combine(dir.FullName, "python", "worker.py")))
        {
            dir = dir.Parent;
        }

        Root = dir?.FullName ?? AppContext.BaseDirectory;
        thumbnailDecoder = new(Root);
    }

    /// <summary>
    /// 현재 설정을 검증한 작업 복사본을 반환합니다. 폴더 작업의 하위 출력 경로만 복사본에 반영합니다.
    /// </summary>
    /// <param name="sourceFolder">폴더 작업의 원본 폴더입니다. 단일 이미지 작업이면 null입니다.</param>
    /// <returns>검증된 작업 복사본입니다. 사용자 기본 출력 경로는 변경하지 않습니다.</returns>
    public Settings CaptureSettings(string? sourceFolder = null)
    {
        var copy = Settings.Snapshot();
        copy.Tile = SelectedPreset.TileSize;
        copy.TilePreset = SelectedPreset.Key;
        copy.Precision = Precision;
        copy.Validate();

        if (sourceFolder is not null)
        {
            var folder = new DirectoryInfo(System.IO.Path.GetFullPath(sourceFolder));
            var name = folder.Name;

            if (folder.Parent is null)
            {
                // Drive and share roots must still yield a valid child directory name.
                name = string.Concat(name.Where(c => !System.IO.Path.GetInvalidFileNameChars().Contains(c)));
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = "Root";
            }

            copy.Output = System.IO.Path.Combine(copy.Output, name + "_UpScaled");
        }

        return copy;
    }

    // 완료 순서가 뒤바뀌더라도 마지막 탐색만 화면에 반영한다.

    /// <summary>
    /// 이전 탐색을 취소하고 최신 세대의 파일 목록만 화면에 적용합니다. 실패 시 기존 폴더를 유지합니다.
    /// </summary>
    /// <returns>최신 탐색 결과가 적용된 경우 true입니다. 실패·취소·오래된 결과이면 false입니다.</returns>
    public async Task<bool> NavigateAsync(string path)
    {
        var version = ++navigationVersion;
        navigation.Cancel();
        navigation.Dispose();
        navigation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = navigation.Token;
        thumbnails.Cancel();
        Status = "폴더를 읽는 중…";

        try
        {
            var full = System.IO.Path.GetFullPath(path);
            var files = await scan(full, token);

            if (token.IsCancellationRequested || version != navigationVersion)
            {
                return false;
            }

            var previous = string.Equals(Folder, full, StringComparison.OrdinalIgnoreCase) ? Selected?.Path : null;
            snapshot = files;
            Folder = full;
            Address = full;
            Settings.Folder = full;
            ApplyFilter(previous);
            Status = $"{full} · {files.Length}개 이미지";

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
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

    /// <summary>
    /// 현재 폴더 스냅샷에 검색 조건을 적용하고 가능한 경우 기존 선택을 유지합니다.
    /// </summary>
    private void ApplyFilter(string? preserve = null)
    {
        preserve ??= Selected?.Path;
        thumbnails.Cancel();
        thumbnails.Dispose();
        thumbnails = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);

        foreach (var item in Files)
        {
            ReleaseThumbnail(item);
        }

        Files.Clear();

        foreach (var path in snapshot.Where(p => System.IO.Path.GetFileName(p).Contains(Filter, StringComparison.CurrentCultureIgnoreCase)))
        {
            Files.Add(new()
            {
                Path = path
            });
        }

        Selected = Files.FirstOrDefault(x => string.Equals(x.Path, preserve, StringComparison.OrdinalIgnoreCase));
        _ = SortThumbnailsAsync();
        Changed(nameof(HasFiles));

        if (Files.Count == 0 && Selected is null)
        {
            PreviewMessage = "표시할 이미지가 없습니다.";
        }
    }

    // 재활용된 항목에는 이전 요청의 결과를 넣지 않는다.
    // 캐시는 UI 스레드에서 접근하고 실제 디코딩은 별도 프로세스 풀이 담당한다.

    /// <summary>
    /// 캐시와 진행 중 요청을 재사용하고 항목의 요청 수명이 유효한 경우에만 썸네일을 적용합니다.
    /// </summary>
    /// <remarks>UI 스레드에서 호출합니다. 재활용 컨테이너는 해제 시 ReleaseThumbnail을 호출해야 합니다.</remarks>
    public async Task LoadThumbnailAsync(ImageItem item)
    {
        if (item.Thumbnail is not null || item.Loading is not null)
        {
            return;
        }

        var loading = CancellationTokenSource.CreateLinkedTokenSource(thumbnails.Token, lifetime.Token);
        item.Loading = loading;
        var token = loading.Token;

        try
        {
            var key = await Task.Run(
                () =>
                {
                    var info = new FileInfo(item.Path);

                    return $"{item.Path}|{info.LastWriteTimeUtc.Ticks}|{info.Length}";
                },
                token
            );
            token.ThrowIfCancellationRequested();

            if (!cache.TryGet(key, out var bitmap))
            {
                bitmap = await thumbnailDecoder.DecodeAsync(item.Path, token, lifetime.Token);
                lifetime.Token.ThrowIfCancellationRequested();
                cache.Put(key, bitmap);
                token.ThrowIfCancellationRequested();
            }

            if (ReferenceEquals(item.Loading, loading))
            {
                item.Error = null;
                item.Thumbnail = bitmap;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested)
            {
                item.Error = error.Message;
            }
        }
        finally
        {
            if (ReferenceEquals(item.Loading, loading))
            {
                item.Loading = null;
            }

            loading.Dispose();
        }
    }

    // 화면 밖 비트맵 참조만 해제하고 LRU 캐시는 유지한다.
    // 요청의 finally에서 토큰을 정리하므로 여기서는 취소만 한다.

    /// <summary>
    /// 화면 밖으로 재활용되는 항목의 요청을 취소하고 Bitmap 참조를 놓습니다.
    /// </summary>
    public void ReleaseThumbnail(ImageItem item)
    {
        item.Loading?.Cancel();
        item.Loading = null;
        item.Thumbnail = null;
    }

    /// <summary>
    /// 별도 디코더로 지정 크기의 이미지를 읽고 원본 치수와 고정된 Bitmap을 반환합니다.
    /// </summary>
    public async Task<(BitmapSource Image, string Dimensions)> DecodeAsync(string path, int size, CancellationToken token)
    {
        await decoders.WaitAsync(token);

        try
        {
            // Bitmap decoding and subprocess setup are also kept off the Dispatcher.
            return await Task.Run(
                async () =>
                {
                    var start = new ProcessStartInfo(System.IO.Path.Combine(Root, ".venv", "Scripts", "python.exe"))
                    {
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardErrorEncoding = System.Text.Encoding.UTF8
                    };
                    start.Environment["PYTHONUTF8"] = "1";
                    start.ArgumentList.Add(System.IO.Path.Combine(Root, "python", "decode.py"));
                    start.ArgumentList.Add(path);
                    start.ArgumentList.Add(size.ToString());
                    using var process = Process.Start(start) ?? throw new IOException("이미지 디코더를 시작하지 못했습니다.");
                    using var registration = token.Register(() =>
                    {
                        try
                        {
                            if (!process.HasExited)
                            {
                                process.Kill(true);
                            }
                        }
                        catch
                        {
                        }
                    });
                    var error = process.StandardError.ReadToEndAsync();
                    using var buffer = new MemoryStream();

                    try
                    {
                        await process.StandardOutput.BaseStream.CopyToAsync(buffer, token);
                        await process.WaitForExitAsync(token);
                        var info = await error;
                        token.ThrowIfCancellationRequested();

                        if (process.ExitCode != 0)
                        {
                            throw new IOException(info.Trim());
                        }

                        buffer.Position = 0;
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = buffer;
                        bitmap.EndInit();
                        bitmap.Freeze();

                        return ((BitmapSource)bitmap, info.Trim());
                    }
                    finally
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(true);
                        }

                        await process.WaitForExitAsync();
                        await error;
                    }
                },
                token
            );
        }
        finally
        {
            decoders.Release();
        }
    }

    /// <summary>
    /// 이전 프리뷰 요청을 취소하고 현재 선택에 해당하는 결과만 확대뷰에 적용합니다.
    /// </summary>
    public async Task LoadPreviewAsync(ImageItem? item)
    {
        previewCancel.Cancel();
        previewCancel.Dispose();
        previewCancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = previewCancel.Token;
        Preview = null;
        ImageInfo = "";
        OriginalWidth = OriginalHeight = 0;
        Changed(nameof(ImageDimensions));
        PreviewMessage = item is null ? "이미지를 선택하세요." : "이미지를 불러오는 중…";

        if (item is null)
        {
            return;
        }

        try
        {
            var decoded = await DecodeAsync(item.Path, 4096, token);
            token.ThrowIfCancellationRequested();
            var dimensions = decoded.Dimensions.Split('x');
            OriginalWidth = int.Parse(dimensions[0]);
            OriginalHeight = int.Parse(dimensions[1]);
            Changed(nameof(ImageDimensions));
            ImageInfo = $"{item.Name} · {decoded.Dimensions}";
            PreviewMessage = "";
            Preview = decoded.Image;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested)
            {
                PreviewMessage = error.Message;
            }
        }
    }

    /// <summary>
    /// 탐색·썸네일·프리뷰 요청을 취소하고 디코더와 이미지 참조를 정리합니다.
    /// </summary>
    /// <remarks>새 디코딩 작업을 요청하지 않을 때 호출하며 UI 바인딩의 Bitmap 참조도 해제합니다.</remarks>
    public void Stop()
    {
        lifetime.Cancel();
        navigation.Cancel();
        previewCancel.Cancel();
        thumbnails.Cancel();

        foreach (var item in Files)
        {
            ReleaseThumbnail(item);
        }

        thumbnailDecoder.Dispose();
        cache.Clear();
    }
}
