using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace Upscaler.Wpf;

// Each slot owns one lazy, persistent decoder. Queue cancellation never kills a
// healthy decoder: finish the current image and discard its result if obsolete.
internal sealed class ThumbnailDecoder(string root) : IDisposable
{
    private readonly SemaphoreSlim available = new(3, 3);
    private readonly Stack<Session> idle = new();
    private readonly HashSet<Session> sessions = new();
    private readonly object gate = new();
    private bool disposed;
    public int Starts { get; private set; }

    public async Task<BitmapSource> DecodeAsync(string path, CancellationToken request, CancellationToken lifetime)
    {
        await available.WaitAsync(request);
        Session? session = null;
        try
        {
            request.ThrowIfCancellationRequested();
            return await Task.Run(async () =>
            {
                lock (gate)
                {
                    ObjectDisposedException.ThrowIf(disposed, this);
                    if (idle.Count > 0) session = idle.Pop();
                    else { session = new Session(root); sessions.Add(session); Starts++; }
                }
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
                deadline.CancelAfter(TimeSpan.FromSeconds(30));
                try
                {
                    await session.Process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { path }).AsMemory(), deadline.Token);
                    var line = await session.Process.StandardOutput.ReadLineAsync(deadline.Token)
                        ?? throw new IOException("썸네일 디코더가 종료되었습니다.");
                    using var response = JsonDocument.Parse(line);
                    if (response.RootElement.TryGetProperty("error", out var error))
                        throw new InvalidDataException(error.GetString());
                    using var buffer = new MemoryStream(Convert.FromBase64String(response.RootElement.GetProperty("png").GetString()!));
                    var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = buffer; bitmap.EndInit(); bitmap.Freeze();
                    return (BitmapSource)bitmap;
                }
                catch (InvalidDataException) { throw; } // Per-image errors leave the protocol synchronized.
                catch
                {
                    lock (gate) { sessions.Remove(session); session.Dispose(); session = null; }
                    throw;
                }
            }, request);
        }
        finally
        {
            lock (gate) { if (session is not null && !disposed) idle.Push(session); }
            available.Release();
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            foreach (var session in sessions) session.Dispose();
            sessions.Clear(); idle.Clear();
        }
    }

    private sealed class Session : IDisposable
    {
        private int disposed;
        public Process Process { get; }
        public Session(string root)
        {
            var start = new ProcessStartInfo(Path.Combine(root, ".venv", "Scripts", "python.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8
            };
            start.Environment["PYTHONUTF8"] = "1";
            start.ArgumentList.Add(Path.Combine(root, "python", "decode.py"));
            start.ArgumentList.Add("--thumbnail-server");
            Process = Process.Start(start) ?? throw new IOException("썸네일 디코더를 시작하지 못했습니다.");
            Process.ErrorDataReceived += (_, _) => { }; Process.BeginErrorReadLine();
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            try { if (!Process.HasExited) Process.Kill(true); } catch (InvalidOperationException) { }
            Process.Dispose();
        }
    }
}
