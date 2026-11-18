namespace Upscaler.Core;

/// <summary>
/// 타일 크기와 UI 표시명·메모리 권장 설명을 묶은 선택 항목입니다.
/// </summary>
public sealed record TilePreset(string Key, string Label, int TileSize, string Description);

/// <summary>
/// 타일 프리셋과 이전 버전 설정의 호환 변환을 관리합니다.
/// </summary>
public static class TilePresets
{
    public static IReadOnlyList<TilePreset> All
    {
        get;
    } = Array.AsReadOnly(
        new[]
        {
            new TilePreset("128", "128", 128, "여유 VRAM 2 GB 이상 권장 (추정)\n정밀도·실행 환경에 따라 달라집니다."),
            new TilePreset("256", "256", 256, "여유 VRAM 3 GB 이상 권장 (추정)\n정밀도·실행 환경에 따라 달라집니다."),
            new TilePreset("512", "512", 512, "여유 VRAM 6 GB 이상 권장 (추정)\n정밀도·실행 환경에 따라 달라집니다.")
        });

    /// <summary>
    /// 저장된 키를 우선 사용하고, 이전 이름·타일 크기는 현재 세 프리셋으로 변환합니다.
    /// </summary>
    public static TilePreset Resolve(string? key, int legacyTile = 0)
    {
        return All.FirstOrDefault(p => p.Key == key)
            ?? ((key switch
            {
                "potato" => 64,
                "mainstream" => 128,
                "high-end" => 256,
                _ => legacyTile
            }) switch
            {
                >= 512 => All[2],
                > 128 => All[1],
                _ => All[0]
            });
    }
}
