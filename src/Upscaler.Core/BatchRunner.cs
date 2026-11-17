using System.Text.Json;

namespace Upscaler.Core;

public sealed record BatchProgress(int Total, int Completed, int Failed, int Cancelled, string Current, string? Error = null)
{
    public string Summary => $"완료 {Completed} · 실패 {Failed} · 취소 {Cancelled}";
}

public sealed class BatchRunner(IUpscaleEngine engine)
{
    public event Action<BatchProgress>? Changed;
    public string? LastOutput
    {
        get; private set;
    }

    // 파일별 실패는 기록하고 계속 진행한다. 취소는 현재 작업의 종료와
    // 임시 파일 정리가 확인된 뒤 남은 파일 처리를 중단한다.
    public async Task<BatchProgress> RunAsync(IReadOnlyList<string> paths, Settings settings, CancellationToken token)
    {
        int completed = 0, failed = 0;
        foreach (var path in paths)
        {
            if (token.IsCancellationRequested)
                break;
            Changed?.Invoke(new(paths.Count, completed, failed, 0, path));
            string? error = null;
            try
            {
                var message = await engine.UpscaleAsync(path, settings, token);
                switch (message.GetProperty("type").GetString())
                {
                    case "completed":
                        completed++;
                        LastOutput = message.GetProperty("output").GetString();
                        break;
                    case "cancelled":
                        return Finish(paths.Count, completed, failed);
                    case "failed":
                        failed++;
                        error = message.GetProperty("message").GetString();
                        break;
                    default:
                        throw new IOException("예상하지 못한 Worker 응답입니다.");
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                failed++;
                error = exception.Message;
                AppLog.Write(exception.ToString());
            }

            Changed?.Invoke(new(paths.Count, completed, failed, 0, path, error));
        }

        return Finish(paths.Count, completed, failed);
    }

    private BatchProgress Finish(int total, int completed, int failed)
    {
        var result = new BatchProgress(total, completed, failed, total - completed - failed, "");
        Changed?.Invoke(result);
        return result;
    }
}
