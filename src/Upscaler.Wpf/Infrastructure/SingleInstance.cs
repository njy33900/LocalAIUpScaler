using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace Upscaler.Wpf;

/// <summary>

/// One GUI per user/session. A second process only sends an activation request.

/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly string pipeName;
    private readonly CancellationTokenSource stopped = new();
    private Task? listener;
    private bool disposed;

    public bool IsPrimary
    {
        get;
    }

    public static string DefaultName
    {
        get
        {
            var sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sid)))[..16];

            return $"LocalAIUpscaler-{hash}-{Process.GetCurrentProcess().SessionId}";
        }
    }

    /// <summary>
    /// 사용자·세션별 뮤텍스를 획득하고 이전 소유자가 비정상 종료한 경우에도 주 인스턴스를 결정합니다.
    /// </summary>
    public SingleInstance(string? name = null)
    {
        pipeName = name ?? DefaultName;
        mutex = new Mutex(false, "Local\\" + pipeName);

        try
        {
            IsPrimary = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            IsPrimary = true;
        }
    }

    /// <summary>
    /// 주 인스턴스에서만 기존 창 활성화 요청의 비동기 수신을 시작합니다.
    /// </summary>
    public void Listen(Action activated)
    {
        if (!IsPrimary || listener is not null)
        {
            throw new InvalidOperationException("Only the primary instance can listen.");
        }

        listener = ListenAsync(activated);
    }

    /// <summary>
    /// 현재 사용자 전용 파이프로 활성화 요청을 받고 요청 단위 제한시간과 종료 취소를 적용합니다.
    /// </summary>
    private async Task ListenAsync(Action activated)
    {
        while (!stopped.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
                );
                await server.WaitForConnectionAsync(stopped.Token).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopped.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                using var reader = new StreamReader(server, Encoding.UTF8, false, 1024, true);
                using var writer = new StreamWriter(server, new UTF8Encoding(false), 1024, true)
                {
                    AutoFlush = true
                };
                await writer.WriteLineAsync(Environment.ProcessId.ToString()).ConfigureAwait(false);

                if (await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false) == "activate")
                {
                    activated();
                    await writer.WriteLineAsync("ok").ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException error)
            {
                Upscaler.Core.AppLog.Write(error.Message);
            }
            catch (Exception error) when (!stopped.IsCancellationRequested)
            {
                Upscaler.Core.AppLog.Write(error.ToString());

                try
                {
                    await Task.Delay(200, stopped.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }
        }
    }

    /// <summary>
    /// 기존 프로세스에 전면 활성화 권한과 요청을 전달하고 수신 확인 여부를 반환합니다.
    /// </summary>
    public async Task<bool> NotifyExistingAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(timeout.Token).ConfigureAwait(false);
            using var reader = new StreamReader(client, Encoding.UTF8, false, 1024, true);
            using var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, true)
            {
                AutoFlush = true
            };

            if (int.TryParse(await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false), out var pid))
            {
                _ = AllowSetForegroundWindow(pid);
            }

            await writer.WriteLineAsync("activate").ConfigureAwait(false);

            return await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false) == "ok";
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(error.ToString());
            Upscaler.Core.AppLog.Write("기존 창 활성화 요청 실패: " + error.Message);

            return false;
        }
    }

    /// <summary>
    /// 알림 수신을 취소하고 소유한 뮤텍스를 해제합니다. 획득한 UI 스레드에서 호출해야 합니다.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        stopped.Cancel();

        if (IsPrimary)
        {
            mutex.ReleaseMutex(); // Must be called on the acquiring (UI) thread.
        }

        mutex.Dispose();
    }

    /// <summary>
    /// 기존 앱 프로세스가 전면 창을 활성화할 수 있도록 Windows 권한을 전달합니다.
    /// </summary>
    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
}
