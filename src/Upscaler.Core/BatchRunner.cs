using System.Text.Json;

namespace Upscaler.Core;

/// <summary>
/// 현재 파일과 완료·실패·취소 건수를 담는 배치 진행 스냅샷입니다.
/// </summary>
public sealed record BatchProgress(int Total, int Completed, int Failed, int Cancelled, string Current, string? Error = null)
{
    public string Summary => $"완료 {Completed} · 실패 {Failed} · 취소 {Cancelled}";
}

/// <summary>
/// 확정된 입력 목록을 한 장씩 처리하고 파일별 실패와 배치 취소를 집계합니다.
/// </summary>
public sealed class BatchRunner(IUpscaleEngine engine)
{
    public event Action<BatchProgress>? Changed;

    public string? LastOutput
    {
        get;
        private set;
    }

    // 파일별 실패는 기록하고 계속 진행한다. 취소는 현재 작업의 종료와
    // 임시 파일 정리가 확인된 뒤 남은 파일 처리를 중단한다.

    /// <summary>
    /// 입력 순서대로 처리합니다. 개별 실패 후에는 계속 진행하고 취소 시 남은 파일을 중단합니다.
    /// </summary>
    /// <param name="paths">호출 시 확정한 비재귀 입력 목록입니다.</param>
    /// <param name="settings">배치 전체에서 사용하는 작업용 설정 복사본입니다.</param>
    /// <param name="token">현재 파일의 종료 정리를 기다린 후 남은 작업을 중단할 취소 신호입니다.</param>
    /// <returns>완료·실패·취소 건수가 반영된 최종 상태입니다.</returns>
    public async Task<BatchProgress> RunAsync(IReadOnlyList<string> paths, Settings settings, CancellationToken token)
    {
        int completed = 0;
        int failed = 0;

        foreach (var path in paths)
        {
            if (token.IsCancellationRequested)
            {
                break;
            }

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

    /// <summary>
    /// 남은 파일을 취소 건수로 계산하고 최종 진행 상태를 구독자에게 알립니다.
    /// </summary>
    private BatchProgress Finish(int total, int completed, int failed)
    {
        var result = new BatchProgress(total, completed, failed, total - completed - failed, "");
        Changed?.Invoke(result);

        return result;
    }
}
