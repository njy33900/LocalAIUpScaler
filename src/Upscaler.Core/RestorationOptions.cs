namespace Upscaler.Core;

public sealed record RestorationOptions
{
    public string Engine { get; init; } = "diffbir_v21";
    public string Preset { get; init; } = "balanced";
    public int Scale { get; init; } = 2;
    public int Steps { get; init; } = 10;
    public int Seed { get; init; } = -1;
    public double Cfg { get; init; } = 6;
    public double Strength { get; init; } = 1;
    public int NoiseAug
    {
        get; init;
    }
    public string Positive { get; init; } = "";
    public string Negative { get; init; } = "";
    public string ColorFix { get; init; } = "Wavelet";
    public bool Tiled { get; init; } = true;

    public void Validate()
    {
        if (Engine is not ("supir_v0q" or "supir_v0f" or "diffbir_v21"))
            throw new ArgumentException("지원하지 않는 고급 엔진입니다.");
        if (Preset is not ("balanced" or "faithful" or "custom"))
            throw new ArgumentException("지원하지 않는 복원 프리셋입니다.");
        if (Scale is not (1 or 2 or 4) || Steps is < 1 or > 100 || Seed < -1)
            throw new ArgumentException("배율·스텝·시드 값이 올바르지 않습니다.");
        if (!double.IsFinite(Cfg) || Cfg is < 1 or > 20 || !double.IsFinite(Strength) || Strength is < 0 or > 2 || NoiseAug is < 0 or > 1000)
            throw new ArgumentException("복원 강도·CFG·노이즈 값이 올바르지 않습니다.");
        if (ColorFix is not ("None" or "AdaIn" or "Wavelet") || Positive is null || Negative is null || Positive.Length > 4000 || Negative.Length > 4000)
            throw new ArgumentException("프롬프트·색 보정 설정이 올바르지 않습니다.");
        if (Engine == "diffbir_v21" && (Strength != 1 || ColorFix != "Wavelet") || Engine != "diffbir_v21" && NoiseAug != 0)
            throw new ArgumentException("다른 엔진 전용 옵션이 포함되어 있습니다.");
    }
}
