using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Upscaler.Core;

namespace Upscaler.PythonBridge;

public sealed record WorkerOptions
{
    public string? PythonPath { get; init; }
    public string? WorkerPath { get; init; }
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(90);
    public TimeSpan CancelTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan JobTimeout { get; init; } = TimeSpan.FromMinutes(30);
}

public sealed class PythonWorker : IUpscaleEngine, IAsyncDisposable
{
    private sealed class Session(Process process)
    {
        public Process Process { get; } = process;
        public TaskCompletionSource<JsonElement> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<JsonElement>? Pending;
        public string? JobId;
        public bool Stopped;
        public Task Reader = Task.CompletedTask;
        public Task Errors = Task.CompletedTask;
    }

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly WorkerOptions options;
    private Session? session;
    private bool disposed;
    public string Root { get; }
    public int? ProcessId => session is { Process.HasExited: false } s ? s.Process.Id : null;
    public event Action<JsonElement>? Progress;
    public event Action<string>? StatusChanged;
    public JsonElement DeviceInfo { get; private set; }

    public PythonWorker(string root, WorkerOptions? options = null)
    {
        Root = root;
        this.options = options ?? new();
    }

    public async Task StartAsync(CancellationToken token = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        await gate.WaitAsync(linked.Token);
        try { await EnsureStartedAsync(linked.Token); }
        finally { gate.Release(); }
    }

    private async Task<Session> EnsureStartedAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (session is { Process.HasExited: false } existing && existing.Ready.Task.IsCompletedSuccessfully)
            return existing;
        if (session is not null) await StopSessionAsync(session);
        StatusChanged?.Invoke("Worker 준비 중…");
        var start = new ProcessStartInfo(options.PythonPath ?? Path.Combine(Root, ".venv", "Scripts", "python.exe"))
        {
            WorkingDirectory = Root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
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
            StatusChanged?.Invoke(DeviceInfo.TryGetProperty("cuda", out var cuda) && cuda.GetBoolean()
                ? "Worker 준비됨 · CUDA / CPU" : "Worker 준비됨 · CPU");
            return current;
        }
        catch
        {
            await StopSessionAsync(current);
            StatusChanged?.Invoke("Worker 준비 실패 · 다시 시도할 수 있습니다.");
            throw;
        }
    }

    private async Task ReadAsync(Session current)
    {
        try
        {
            while (await current.Process.StandardOutput.ReadLineAsync() is { } line)
            {
                using var document = JsonDocument.Parse(line);
                var message = document.RootElement.Clone();
                if (message.GetProperty("protocol_version").GetInt32() != 1)
                    throw new IOException("Worker 프로토콜 버전이 다릅니다.");
                var type = message.GetProperty("type").GetString();
                if (type == "ready") current.Ready.TrySetResult(message);
                else if (message.TryGetProperty("job_id", out var id) && id.GetString() == current.JobId)
                {
                    if (type is "completed" or "failed" or "cancelled") current.Pending?.TrySetResult(message);
                    else if (type == "progress") Progress?.Invoke(message);
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

    private static async Task DrainErrorsAsync(Session current)
    {
        // Always drain stderr, even if logging is unavailable, to avoid pipe deadlock.
        try
        {
            while (await current.Process.StandardError.ReadLineAsync() is { } line)
            {
                try { AppLog.Write(line); } catch { }
            }
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException) { }
    }

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
            await SendAsync(current, new { protocol_version = 1, type = "upscale", job_id = id,
                input = Path.GetFullPath(input), output_dir = Path.GetFullPath(settings.Output),
                device = settings.Device, precision = settings.Precision, format = settings.Format, tile_size = settings.Tile });
            try
            {
                return await current.Pending.Task.WaitAsync(options.JobTimeout, linked.Token);
            }
            catch (OperationCanceledException)
            {
                var terminal = await CancelAndWaitAsync(current);
                if (terminal is { } result && result.GetProperty("type").GetString() is "completed" or "failed") return result;
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
            if (current is not null && !current.Stopped && (current.Pending?.Task.IsFaulted == true || !current.Process.HasExited))
                await StopSessionAsync(current);
            throw;
        }
        finally
        {
            if (current is not null) { current.JobId = null; current.Pending = null; }
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (Exception error) { AppLog.Write("임시 파일 정리 실패: " + error); }
            }
            gate.Release();
        }
    }

    private async Task<JsonElement?> CancelAndWaitAsync(Session current)
    {
        StatusChanged?.Invoke("작업 취소 및 정리 중…");
        try
        {
            await SendAsync(current, new { protocol_version = 1, type = "cancel", job_id = current.JobId });
            if (current.Pending is not null) return await current.Pending.Task.WaitAsync(options.CancelTimeout);
        }
        catch { await StopSessionAsync(current); }
        return null;
    }

    private static async Task SendAsync(Session current, object request)
    {
        await current.Process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request));
        await current.Process.StandardInput.FlushAsync();
    }

    private async Task StopSessionAsync(Session current)
    {
        if (current.Stopped) return;
        current.Stopped = true;
        try
        {
            if (!current.Process.HasExited) current.Process.Kill(entireProcessTree: true);
            await current.Process.WaitForExitAsync();
            await Task.WhenAll(current.Reader, current.Errors);
        }
        finally
        {
            current.Process.Dispose();
            if (ReferenceEquals(session, current)) session = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        lifetime.Cancel();
        await gate.WaitAsync();
        try
        {
            if (session is { } current)
            {
                try
                {
                    await SendAsync(current, new { protocol_version = 1, type = "shutdown", job_id = "" });
                    await current.Process.WaitForExitAsync().WaitAsync(options.CancelTimeout);
                }
                catch { }
                await StopSessionAsync(current);
            }
            disposed = true;
        }
        finally { gate.Release(); }
    }
}
