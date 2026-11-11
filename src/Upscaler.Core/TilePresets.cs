namespace Upscaler.Core;

public sealed record TilePreset(string Key, string Label, int TileSize, string Description);

public static class TilePresets
{
    public static IReadOnlyList<TilePreset> All { get; } = Array.AsReadOnly(new[]
    {
        new TilePreset("128", "128", 128, "여유 VRAM 2 GB 이상 권장 (추정)\n정밀도·실행 환경에 따라 달라집니다."),
        new TilePreset("256", "256", 256, "여유 VRAM 3 GB 이상 권장 (추정)\n정밀도·실행 환경에 따라 달라집니다."),
        new TilePreset("512", "512", 512, "여유 VRAM 6 GB 이상 권장 (추정)\n정밀도·실행 환경에 따라 달라집니다.")
    });
    public static TilePreset Resolve(string? key, int legacyTile = 0) =>
        All.FirstOrDefault(p => p.Key == key) ?? ((key switch
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
