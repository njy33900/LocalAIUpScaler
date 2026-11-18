using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Upscaler.Core;

/// <summary>
/// WPF 바인딩에 필요한 속성 변경 알림을 제공하는 공통 기반입니다.
/// </summary>
public class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 호출한 속성 또는 지정한 이름의 변경을 바인딩 구독자에게 알립니다.
    /// </summary>
    protected void Changed([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new(name));
    }

    /// <summary>
    /// 값이 달라진 경우에만 필드를 갱신하고 알림을 보냅니다. 실제 변경 여부를 반환합니다.
    /// </summary>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Changed(name);

        return true;
    }
}

/// <summary>
/// 배치 처리기가 사용하는 단일 이미지 추론과 진행 이벤트의 계약입니다.
/// </summary>
public interface IUpscaleEngine
{
    event Action<JsonElement>? Progress;

    /// <summary>
    /// 한 이미지의 추론을 요청하고 Worker의 최종 응답을 반환합니다. 취소 정리는 구현체가 담당합니다.
    /// </summary>
    Task<JsonElement> UpscaleAsync(string input, Settings settings, CancellationToken token);
}

/// <summary>
/// 사용자 설정을 검증·저장하고 실행 중 변경되지 않는 작업용 복사본을 제공합니다.
/// </summary>
public sealed class Settings
{
    private static string DefaultPictures
    {
        get
        {
            var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

            if (!string.IsNullOrEmpty(pictures))
            {
                return pictures;
            }

            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            return Path.Combine(string.IsNullOrEmpty(profile) ? Path.GetTempPath() : profile, "Pictures");
        }
    }

    public string Folder
    {
        get;
        set;
    } = DefaultPictures;

    public string Output
    {
        get;
        set;
    } = Path.Combine(DefaultPictures, "Upscaled");

    public string Device
    {
        get;
        set;
    } = "auto";

    public string Precision
    {
        get;
        set;
    } = "auto";

    public string Format
    {
        get;
        set;
    } = "PNG";

    public int Tile
    {
        get;
        set;
    }

    public string? TilePreset
    {
        get;
        set;
    }

    public double TreeWidth
    {
        get;
        set;
    } = 220;

    public double ThumbnailHeight
    {
        get;
        set;
    } = 170;

    public string PreviewLayout
    {
        get;
        set;
    } = "vertical";

    public string ThumbnailSort
    {
        get;
        set;
    } = "name";

    public bool ThumbnailDescending
    {
        get;
        set;
    }

    public string Model
    {
        get;
        set;
    } = "RealESRGAN_x4plus";

    public double Denoise
    {
        get;
        set;
    } = 0.5;

    public string Theme
    {
        get;
        set;
    } = "dark";

    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalAIUpscaler");

    /// <summary>
    /// 모델·장치·정밀도·출력 경로를 검사하고 지원하지 않는 값에는 예외를 발생시킵니다.
    /// </summary>
    /// <exception cref="ArgumentException">지원하지 않는 모델·옵션 또는 출력 경로가 지정되었습니다.</exception>
    public void Validate()
    {
        if (Model is not ("RealESRGAN_x4plus" or "RealESRGAN_x4plus_anime_6B" or "realesr-general-x4v3"))
        {
            throw new ArgumentException("지원하지 않는 모델입니다.");
        }

        if (!double.IsFinite(Denoise) || Denoise < 0 || Denoise > 1)
        {
            throw new ArgumentException("노이즈 강도는 0~1이어야 합니다.");
        }

        if (Device is not ("auto" or "cuda" or "cpu"))
        {
            throw new ArgumentException("장치 설정이 올바르지 않습니다.");
        }

        if (Precision is not ("auto" or "fp16" or "fp32"))
        {
            throw new ArgumentException("정밀도 설정이 올바르지 않습니다.");
        }

        if (Format is not ("PNG" or "JPEG"))
        {
            throw new ArgumentException("출력 형식이 올바르지 않습니다.");
        }

        if (Tile != 0 && (Tile < 32 || Tile > 2048))
        {
            throw new ArgumentException("타일 크기는 0(자동) 또는 32~2048이어야 합니다.");
        }

        if (string.IsNullOrWhiteSpace(Output) || !Path.IsPathFullyQualified(Output))
        {
            throw new ArgumentException("출력 폴더의 전체 경로를 입력하세요.");
        }

        _ = Path.GetFullPath(Output);
    }

    /// <summary>
    /// 작업 도중 UI 설정이 바뀌어도 영향을 받지 않도록 현재 값의 얕은 복사본을 반환합니다.
    /// </summary>
    /// <returns>원래 사용자 설정과 분리된 작업 설정입니다.</returns>
    public Settings Snapshot()
    {
        return (Settings)MemberwiseClone();
    }

    /// <summary>
    /// 저장된 설정을 읽고 이전 설정을 정규화합니다. 읽기·검증 실패 시 기본값으로 복구합니다.
    /// </summary>
    public static Settings Load(string? path = null)
    {
        try
        {
            var value = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path ?? Path.Combine(DirectoryPath, "settings.json")))
                ?? new();
            value.Validate();

            if (string.IsNullOrWhiteSpace(value.Folder))
            {
                value.Folder = new Settings().Folder;
            }

            value.TreeWidth = double.IsFinite(value.TreeWidth) ? Math.Clamp(value.TreeWidth, 140, 500) : 220;
            value.ThumbnailHeight = double.IsFinite(value.ThumbnailHeight) ? Math.Clamp(value.ThumbnailHeight, 100, 350) : 170;
            value.Theme = "dark";

            if (value.ThumbnailSort is not ("name" or "modified" or "size"))
            {
                value.ThumbnailSort = "name";
            }

            if (value.PreviewLayout is not ("horizontal" or "vertical"))
            {
                value.PreviewLayout = "vertical";
            }

            return value;
        }
        catch
        {
            return new();
        }
    }

    /// <summary>
    /// 같은 디렉터리의 임시 파일을 거쳐 설정을 교체하고 실패 시 임시 파일을 정리합니다.
    /// </summary>
    public void Save(string? path = null)
    {
        path ??= Path.Combine(DirectoryPath, "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this));
            File.Move(temporary, path, true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}

/// <summary>
/// 지원하는 이미지 확장자와 비재귀 폴더 탐색 정책을 관리합니다.
/// </summary>
public static class Images
{
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".tif",
        ".tiff",
        ".webp"
    };

    /// <summary>
    /// 하위 폴더를 제외한 이미지 목록을 확정합니다. 열거 중 취소 요청을 확인합니다.
    /// </summary>
    /// <param name="folder">이미지를 열거할 폴더입니다. 하위 폴더는 포함하지 않습니다.</param>
    /// <param name="token">파일 열거 중 확인할 취소 토큰입니다.</param>
    /// <returns>지원 확장자로 필터링하고 이름순으로 정렬한 경로 배열입니다.</returns>
    public static string[] Scan(string folder, CancellationToken token = default)
    {
        var files = new List<string>();

        foreach (var path in Directory.EnumerateFiles(folder))
        {
            token.ThrowIfCancellationRequested();

            if (Extensions.Contains(Path.GetExtension(path)))
            {
                files.Add(path);
            }
        }

        return files.OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
}

/// <summary>
/// 프로세스와 UI의 진단 메시지를 사용자별 로컬 로그에 기록합니다.
/// </summary>
public static class AppLog
{
    private static readonly object Sync = new();

    /// <summary>
    /// 기록 동시성을 직렬화하고 로그 파일 오류가 본래 작업을 중단시키지 않도록 처리합니다.
    /// </summary>
    public static void Write(string message)
    {
        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(Settings.DirectoryPath);
                var path = Path.Combine(Settings.DirectoryPath, "worker.log");

                if (File.Exists(path) && new FileInfo(path).Length > 5 * 1024 * 1024)
                {
                    File.Move(path, path + ".previous", true);
                }

                File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
            catch
            { /* Logging must not prevent cleanup or stderr draining. */
            }
        }
    }
}
