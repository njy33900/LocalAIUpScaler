using System.Diagnostics;
using System.Text.Json;

namespace Upscaler.PythonBridge;

public sealed record AdvancedStatus(string State, string Message, bool Ready, int[]? Scales = null);

public static class AdvancedRuntime
{
    public static string ConfigPath(string root) => Path.Combine(root, ".advanced", "models.json");

    public static string PythonPath(string root, string engine)
    {
        using var config = JsonDocument.Parse(File.ReadAllText(ConfigPath(root)));
        var value = config.RootElement.GetProperty(engine).GetProperty("python").GetString()!;
        return Path.GetFullPath(value, root);
    }

    public static async Task<AdvancedStatus> InspectAsync(string root, string engine)
    {
        try
        {
            var start = new ProcessStartInfo(Path.Combine(root, ".venv", "Scripts", "python.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add(Path.Combine(root, "python", "advanced", "worker.py"));
            start.ArgumentList.Add("--health");
            start.ArgumentList.Add(engine);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            }
            catch
            {
                process.Kill(true);
                await process.WaitForExitAsync();
                throw;
            }
            var text = await output;
            if (process.ExitCode != 0)
                return new("invalid", "설치 상태 검사 실패: " + await errors, false);
            using var doc = JsonDocument.Parse(text);
            var data = doc.RootElement;
            return new(data.GetProperty("state").GetString()!, data.GetProperty("message").GetString()!, data.GetProperty("verified_inference").GetBoolean(), data.GetProperty("verified_scales").EnumerateArray().Select(x => x.GetInt32()).ToArray());
        }
        catch (Exception error)
        {
            return new("not_installed", error.Message, false);
        }
    }
}
