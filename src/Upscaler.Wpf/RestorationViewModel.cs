using System.IO;
using Upscaler.Core;
using Upscaler.PythonBridge;

namespace Upscaler.Wpf;

public sealed class RestorationViewModel(string root, Settings settings, Action notify) : Observable
{
    public sealed record EngineChoice(string Id, string Label);
    public EngineChoice[] Engines
    {
        get;
    } =
    [
        new("diffbir_v21", "DiffBIR v2.1 · SR"),
        new("supir_v0q", "SUPIR-v0Q · 고품질 재구성"),
        new("supir_v0f", "SUPIR-v0F · 원본 보존 지향")
    ];
    private bool active, ready;
    private int generation;
    private string status = "설치 상태 확인 전 · 실행 차단";
    public string Status
    {
        get => status; private set => Set(ref status, value);
    }
    public bool IsActive
    {
        get => active; set
        {
            Set(ref active, value);
            notify();
        }
    }
    public bool IsReady
    {
        get => ready; private set
        {
            Set(ref ready, value);
            notify();
        }
    }
    public int[] Scales { get; private set; } = [];
    public bool IsSupir => Engine != "diffbir_v21";
    public bool IsDiffbir => !IsSupir;
    public EngineChoice[] Presets { get; } = [new("balanced", "기본"), new("faithful", "원본 보존 지향"), new("custom", "사용자 설정")];
    public string Preset
    {
        get => Profile.Preset;
        set
        {
            if (value == "custom")
                Profile = Profile with
                {
                    Preset = value
                };
            else
                Profile = Profile with
                {
                    Preset = value,
                    Steps = IsSupir ? 50 : value == "faithful" ? 20 : 10,
                    Cfg = IsSupir ? 4 : value == "faithful" ? 4 : 6,
                    Strength = IsSupir && value == "faithful" ? 1.5 : 1,
                    NoiseAug = 0
                };
            foreach (var property in new[] { nameof(Preset), nameof(Steps), nameof(Cfg), nameof(Strength), nameof(NoiseAug) })
                Changed(property);
        }
    }
    public string ConfigPath => AdvancedRuntime.ConfigPath(root);
    public string Engine
    {
        get => settings.RestorationEngine;
        set
        {
            settings.RestorationEngine = value;
            IsReady = false;
            foreach (var name in new[] { nameof(Engine), nameof(IsSupir), nameof(IsDiffbir), nameof(Preset), nameof(Scale), nameof(Steps), nameof(Seed), nameof(Cfg), nameof(Strength), nameof(NoiseAug), nameof(Positive), nameof(Negative), nameof(ColorFix), nameof(Tiled) })
                Changed(name);
            _ = RefreshAsync();
        }
    }
    private RestorationOptions Profile
    {
        get
        {
            settings.RestorationProfiles ??= [];
            if (!settings.RestorationProfiles.TryGetValue(Engine, out var result))
            {
                result = new()
                {
                    Engine = Engine,
                    Steps = IsSupir ? 50 : 10,
                    Cfg = IsSupir ? 4 : 6
                };
                settings.RestorationProfiles[Engine] = result;
            }
            return result;
        }
        set => settings.RestorationProfiles[Engine] = value;
    }
    private void Update(RestorationOptions value, string property)
    {
        if (Profile == value)
            return;
        Profile = value with
        {
            Preset = "custom"
        };
        Changed(property);
        Changed(nameof(Preset));
    }
    public int Scale
    {
        get => Profile.Scale; set => Update(Profile with { Scale = value }, nameof(Scale));
    }
    public int Steps
    {
        get => Profile.Steps; set => Update(Profile with { Steps = value }, nameof(Steps));
    }
    public int Seed
    {
        get => Profile.Seed; set => Update(Profile with { Seed = value }, nameof(Seed));
    }
    public double Cfg
    {
        get => Profile.Cfg; set => Update(Profile with { Cfg = value }, nameof(Cfg));
    }
    public double Strength
    {
        get => Profile.Strength; set => Update(Profile with { Strength = value }, nameof(Strength));
    }
    public int NoiseAug
    {
        get => Profile.NoiseAug; set => Update(Profile with { NoiseAug = value }, nameof(NoiseAug));
    }
    public string Positive
    {
        get => Profile.Positive; set => Update(Profile with { Positive = value }, nameof(Positive));
    }
    public string Negative
    {
        get => Profile.Negative; set => Update(Profile with { Negative = value }, nameof(Negative));
    }
    public string ColorFix
    {
        get => Profile.ColorFix; set => Update(Profile with { ColorFix = value }, nameof(ColorFix));
    }
    public bool Tiled
    {
        get => Profile.Tiled; set => Update(Profile with { Tiled = value }, nameof(Tiled));
    }
    public RestorationOptions Capture()
    {
        if (!IsReady)
            throw new ArgumentException("고급 엔진은 설치와 실제 추론 검증을 완료한 후 사용할 수 있습니다.\n" + Status);
        if (!Scales.Contains(Scale))
            throw new ArgumentException("실제 추론이 검증되지 않은 배율입니다.");
        Profile.Validate();
        return Profile with
        {
        };
    }
    public async Task RefreshAsync()
    {
        var version = ++generation;
        IsReady = false;
        Status = "설치·검증 상태 확인 중…";
        var result = await AdvancedRuntime.InspectAsync(root, Engine);
        if (generation != version)
            return;
        Scales = result.Scales ?? [];
        Changed(nameof(Scales));
        if (Scales.Length > 0 && !Scales.Contains(Scale))
            Scale = Scales[0];
        Changed(nameof(Scale));
        IsReady = result.Ready;
        Status = (result.Ready ? "실제 추론 검증됨 · CUDA" : "미설치 / 검증 필요 · 실행 차단") + "\n" + result.Message;
    }
}
