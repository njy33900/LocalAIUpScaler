using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Upscaler.Core;

namespace Upscaler.PythonBridge;

/// <summary>
/// Python 실행 경로와 시작·작업·취소 응답의 제한시간을 지정합니다.
/// </summary>
public sealed record WorkerOptions
{
    public string? PythonPath
    {
        get;
        init;
    }
    public string? WorkerPath
    {
        get;
        init;
    }
    public TimeSpan StartupTimeout
    {
        get;
        init;
    } = TimeSpan.FromSeconds(90);
    public TimeSpan CancelTimeout
    {
        get;
        init;
    } = TimeSpan.FromSeconds(15);
    public TimeSpan JobTimeout
    {
        get;
        init;
    } = TimeSpan.FromMinutes(30);
}

/// <summary>
/// Python 프로세스의 JSONL 요청과 응답을 세션별로 격리하고 추론·취소·재시작을 직렬화합니다.
/// </summary>
public sealed class PythonWorker : IUpscaleEngine, IAsyncDisposable
{
    // 응답 대기 상태를 프로세스 세션별로 격리한다. 이전 세션의 응답이나 종료가
    // 재시작한 프로세스의 작업을 완료시키지 않도록 이 경계를 유지해야 한다.

    /// <summary>
    /// 한 프로세스의 읽기 작업과 ready·진행 중 요청을 보관해 이전 응답의 혼입을 막습니다.
    /// </summary>
    private sealed class Session(Process process)
    {
        public Process Process
        {
            get;
        } = process;

        public TaskCompletionSource<JsonElement> Ready
        {
            get;
        } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<JsonElement>? Pending;
        public string? JobId;
        public bool Stopped;
        public Task Reader = Task.CompletedTask;
        public Task Errors = Task.CompletedTask;
    }

    // 시작과 추론을 직렬화한다. Worker 하나에는 한 번에 한 작업만 보낸다.
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly WorkerOptions options;
    private Session? session;
    private bool disposed;

    public string Root
    {
        get;
    }

    public int? ProcessId => session is { Process.HasExited: false } s ? s.Process.Id : null;

    public event Action<JsonElement>? Progress;
    public event Action<string>? StatusChanged;

    public JsonElement DeviceInfo
    {
        get;
        private set;
    }

    /// <summary>
    /// 프로젝트 루트와 실행 정책을 보관합니다. 실제 프로세스는 시작 요청 시 생성합니다.
    /// </summary>
    public PythonWorker(string root, WorkerOptions? options = null)
    {
        Root = root;
        this.options = options ?? new();
    }

    /// <summary>
    /// 수명 취소와 호출자 취소를 연결하고 시작 게이트 안에서 Worker 준비를 기다립니다.
    /// </summary>
    public async Task StartAsync(CancellationToken token = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        await gate.WaitAsync(linked.Token);

        try
        {
            await EnsureStartedAsync(linked.Token);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// 준비된 세션을 재사용하거나 새 프로세스를 시작합니다. 시작 실패 시 해당 세션을 종료합니다.
    /// </summary>
    private async Task<Session> EnsureStartedAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (session is { Process.HasExited: false } existing && existing.Ready.Task.IsCompletedSuccessfully)
        {
            return existing;
        }

        if (session is not null)
        {
            await StopSessionAsync(session);
        }

        StatusChanged?.Invoke("Worker 준비 중…");
        var start = new ProcessStartInfo(options.PythonPath ?? Path.Combine(Root, ".venv", "Scripts", "python.exe"))
        {
            WorkingDirectory = Root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add("-u");
        start.ArgumentList.Add(options.WorkerPath ?? Path.Combine(Root, "python", "worker.py"));
        var current = new Session(Process.Start(start) ?? throw new IOException("Worker를 시작하지 못했습니다."));
        session = current;
        current.Reader = ReadAsync(current);
        current.Errors = DrainErrorsAsync(current);

        try
        {
            DeviceInfo = await current.Ready.Task.WaitAsync(options.StartupTimeout, token);
            StatusChanged?.Invoke(DeviceInfo.TryGetProperty("cuda", out var cuda) && cuda.GetBoolean() ? "Worker 준비됨 · CUDA / CPU" : "Worker 준비됨 · CPU");

            return current;
        }
        catch
        {
            await StopSessionAsync(current);
            StatusChanged?.Invoke("Worker 준비 실패 · 다시 시도할 수 있습니다.");
            throw;
        }
    }

    /// <summary>
    /// stdout의 JSONL을 검증하고 현재 세션·작업에 해당하는 응답만 대기 작업과 UI에 전달합니다.
    /// </summary>
    private async Task ReadAsync(Session current)
    {
        try
        {
            while (await current.Process.StandardOutput.ReadLineAsync() is { } line)
            {
                using var document = JsonDocument.Parse(line);
                var message = document.RootElement.Clone();

                if (message.GetProperty("protocol_version").GetInt32() != 1)
                {
                    throw new IOException("Worker 프로토콜 버전이 다릅니다.");
                }

                var type = message.GetProperty("type").GetString();

                if (type == "ready")
                {
                    current.Ready.TrySetResult(message);
                }
                else if (message.TryGetProperty("job_id", out var id) && id.GetString() == current.JobId)
                {
                    if (type is "completed" or "failed" or "cancelled")
                    {
                        current.Pending?.TrySetResult(message);
                    }
                    else if (type == "progress")
                    {
                        Progress?.Invoke(message);
                    }
                }
            }

            throw new IOException("Worker가 종료되었습니다. 다음 실행 시 다시 시작합니다.");
        }
        catch (Exception error)
        {
            current.Ready.TrySetException(error);
            current.Pending?.TrySetException(error);
        }
    }

    /// <summary>
    /// stderr를 계속 배출해 파이프 교착을 방지하고 진단 로그 오류를 격리합니다.
    /// </summary>
    private static async Task DrainErrorsAsync(Session current)
    {
        // Always drain stderr, even if logging is unavailable, to avoid pipe deadlock.
        try
        {
            while (await current.Process.StandardError.ReadLineAsync() is { } line)
            {
                try
                {
                    AppLog.Write(line);
                }
                catch
                {
                }
            }
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException)
        {
        }
    }

    /// <summary>
    /// 설정 복사본으로 추론을 요청합니다. 취소·오류 시 프로세스 정리를 기다리고 해당 작업의 임시 파일만 제거합니다.
    /// </summary>
    /// <param name="input">추론할 원본 이미지 경로입니다.</param>
    /// <param name="settings">실행 중 사용자 편집과 분리된 작업 설정입니다.</param>
    /// <param name="token">응답 수신뿐 아니라 추론과 임시 파일 정리까지 연결할 취소 신호입니다.</param>
    /// <returns>현재 작업 ID에 해당하는 completed 또는 failed 응답입니다.</returns>
    /// <remarks>취소 응답에 앞서 저장이 확정되었다면 완료 결과를 반환할 수 있습니다.</remarks>
    public async Task<JsonElement> UpscaleAsync(string input, Settings settings, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        await gate.WaitAsync(linked.Token);
        Session? current = null;
        string? temporary = null;

        try
        {
            settings.Validate();
            current = await EnsureStartedAsync(linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            var id = Guid.NewGuid().ToString("N");
            current.JobId = id;
            current.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            temporary = Path.Combine(Path.GetFullPath(settings.Output), $".upscaler-{id}.tmp");
            await SendAsync(
                current,
                new
                {
                    protocol_version = 1,
                    type = "upscale",
                    job_id = id,
                    input = Path.GetFullPath(input),
                    output_dir = Path.GetFullPath(settings.Output),
                    device = settings.Device,
                    precision = settings.Precision,
                    format = settings.Format,
                    model = settings.Model,
                    denoise = settings.Denoise,
                    tile_size = settings.Tile
                }
            );

            try
            {
                return await current.Pending.Task.WaitAsync(options.JobTimeout, linked.Token);
            }
            catch (OperationCanceledException)
            {
                var terminal = await CancelAndWaitAsync(current);

                if (terminal is { } result && result.GetProperty("type").GetString() is "completed" or "failed")
                {
                    return result;
                }

                throw new OperationCanceledException(linked.Token);
            }
            catch (TimeoutException)
            {
                await CancelAndWaitAsync(current);
                throw new TimeoutException("작업 제한시간을 초과했습니다. Worker를 정리했습니다.");
            }
        }
        catch
        {
            if (current is not null && !current.Stopped
                && (current.Pending?.Task.IsFaulted == true || !current.Process.HasExited))
            {
                await StopSessionAsync(current);
            }

            throw;
        }
        finally
        {
            if (current is not null)
            {
                current.JobId = null;
                current.Pending = null;
            }

            if (temporary is not null)
            {
                try
                {
                    File.Delete(temporary);
                }
                catch (Exception error)
                {
                    AppLog.Write("임시 파일 정리 실패: " + error);
                }
            }

            gate.Release();
        }
    }

    /// <summary>
    /// 현재 작업에 취소를 보내 최종 응답을 기다립니다. 응답 실패 시 세션을 강제 종료합니다.
    /// </summary>
    private async Task<JsonElement?> CancelAndWaitAsync(Session current)
    {
        StatusChanged?.Invoke("작업 취소 및 정리 중…");

        try
        {
            await SendAsync(
                current,
                new
                {
                    protocol_version = 1,
                    type = "cancel",
                    job_id = current.JobId
                }
            );

            if (current.Pending is not null)
            {
                return await current.Pending.Task.WaitAsync(options.CancelTimeout);
            }
        }
        catch
        {
            await StopSessionAsync(current);
        }

        return null;
    }

    /// <summary>
    /// 요청을 UTF-8 JSON 한 줄로 직렬화해 지정 세션의 stdin에 쓰고 flush합니다.
    /// </summary>
    private static async Task SendAsync(Session current, object request)
    {
        await current.Process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request));
        await current.Process.StandardInput.FlushAsync();
    }

    /// <summary>
    /// 지정 세션의 프로세스 트리와 스트림 읽기가 끝난 뒤 세션 자원을 해제합니다.
    /// </summary>
    private async Task StopSessionAsync(Session current)
    {
        if (current.Stopped)
        {
            return;
        }

        current.Stopped = true;

        try
        {
            if (!current.Process.HasExited)
            {
                current.Process.Kill(entireProcessTree: true);
            }

            await current.Process.WaitForExitAsync();
            await Task.WhenAll(current.Reader, current.Errors);
        }
        finally
        {
            current.Process.Dispose();

            if (ReferenceEquals(session, current))
            {
                session = null;
            }
        }
    }

    /// <summary>
    /// 수명 취소로 진행 중 작업을 중단하고 게이트를 확보한 뒤 종료 확인까지 기다립니다.
    /// </summary>
    /// <remarks>다른 엔진 실행이나 앱 종료 전에 이 비동기 해제를 기다려야 합니다.</remarks>
    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        lifetime.Cancel();
        await gate.WaitAsync();

        try
        {
            if (session is { } current)
            {
                try
                {
                    await SendAsync(
                        current,
                        new
                        {
                            protocol_version = 1,
                            type = "shutdown",
                            job_id = ""
                        }
                    );
                    await current.Process.WaitForExitAsync().WaitAsync(options.CancelTimeout);
                }
                catch
                {
                }

                await StopSessionAsync(current);
            }

            disposed = true;
        }
        finally
        {
            gate.Release();
        }
    }
}
