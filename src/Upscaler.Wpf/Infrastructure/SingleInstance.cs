using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
namespace Upscaler.Wpf;

/// <summary>One GUI per user/session. A second process only sends an activation request.</summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly string pipeName;
    private readonly CancellationTokenSource stopped = new();
    private Task? listener;
    private bool disposed;
    public bool IsPrimary { get; }
    public static string DefaultName
    {
        get
        {
            var sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sid)))[..16];
            return $"LocalAIUpscaler-{hash}-{Process.GetCurrentProcess().SessionId}";
        }
    }
    public SingleInstance(string? name = null)
    {
        pipeName = name ?? DefaultName;
        mutex = new Mutex(false, "Local\\" + pipeName);
        try { IsPrimary = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { IsPrimary = true; }
    }
    public void Listen(Action activated)
    {
        if (!IsPrimary || listener is not null) throw new InvalidOperationException("Only the primary instance can listen.");
        listener = ListenAsync(activated);
    }
    private async Task ListenAsync(Action activated)
    {
        while (!stopped.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(stopped.Token).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopped.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                using var reader = new StreamReader(server, Encoding.UTF8, false, 1024, true);
                using var writer = new StreamWriter(server, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
                await writer.WriteLineAsync(Environment.ProcessId.ToString()).ConfigureAwait(false);
                if (await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false) == "activate")
                {
                    activated();
                    await writer.WriteLineAsync("ok").ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException error) { Upscaler.Core.AppLog.Write(error.Message); }
            catch (Exception error) when (!stopped.IsCancellationRequested)
            {
                Upscaler.Core.AppLog.Write(error.ToString());
                try { await Task.Delay(200, stopped.Token).ConfigureAwait(false); } catch (OperationCanceledException) { }
            }
        }
    }
    public async Task<bool> NotifyExistingAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(timeout.Token).ConfigureAwait(false);
            using var reader = new StreamReader(client, Encoding.UTF8, false, 1024, true);
            using var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
            if (int.TryParse(await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false), out var pid))
                _ = AllowSetForegroundWindow(pid);
            await writer.WriteLineAsync("activate").ConfigureAwait(false);
            return await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false) == "ok";
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or UnauthorizedAccessException)
        { Upscaler.Core.AppLog.Write("기존 창 활성화 요청 실패: " + error.Message); return false; }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; stopped.Cancel();
        if (IsPrimary) mutex.ReleaseMutex(); // Must be called on the acquiring (UI) thread.
        mutex.Dispose();
    }
    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
}
