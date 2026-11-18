using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace Upscaler.Wpf;

// Each slot owns one lazy, persistent decoder. Queue cancellation never kills a
// healthy decoder: finish the current image and discard its result if obsolete.

/// <summary>
/// 썸네일 전용 Python 디코더 세션을 대여해 프로세스 생성 비용을 줄입니다.
/// </summary>
internal sealed class ThumbnailDecoder(string root) : IDisposable
{
    // 슬롯 하나가 요청 한 개와 디코더 한 개를 독점하여 JSONL 응답 순서를 보장한다.
    private readonly SemaphoreSlim available = new(3, 3);
    private readonly Stack<Session> idle = new();
    private readonly HashSet<Session> sessions = new();
    private readonly object gate = new();
    private bool disposed;

    public int Starts
    {
        get;
        private set;
    }

    /// <summary>
    /// 유휴 세션을 대여해 썸네일을 읽고 요청·수명 취소나 통신 실패 시 해당 세션을 폐기합니다.
    /// </summary>
    public async Task<BitmapSource> DecodeAsync(string path, CancellationToken request, CancellationToken lifetime)
    {
        await available.WaitAsync(request);
        Session? session = null;

        try
        {
            request.ThrowIfCancellationRequested();

            return await Task.Run(
                async () =>
                {
                    lock (gate)
                    {
                        ObjectDisposedException.ThrowIf(disposed, this);

                        if (idle.Count > 0)
                        {
                            session = idle.Pop();
                        }
                        else
                        {
                            session = new Session(root);
                            sessions.Add(session);
                            Starts++;
                        }
                    }

                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
                    deadline.CancelAfter(TimeSpan.FromSeconds(30));

                    try
                    {
                        await session.Process.StandardInput.WriteLineAsync(
                            JsonSerializer.Serialize(new
                            {
                                path
                            }).AsMemory(),
                            deadline.Token
                        );
                        var line = await session.Process.StandardOutput.ReadLineAsync(deadline.Token)
                            ?? throw new IOException("썸네일 디코더가 종료되었습니다.");
                        using var response = JsonDocument.Parse(line);

                        if (response.RootElement.TryGetProperty("error", out var error))
                        {
                            throw new InvalidDataException(error.GetString());
                        }

                        using var buffer = new MemoryStream(Convert.FromBase64String(response.RootElement.GetProperty("png").GetString()!));
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = buffer;
                        bitmap.EndInit();
                        bitmap.Freeze();

                        return (BitmapSource)bitmap;
                    }
                    catch (InvalidDataException)
                    {
                        throw;
                    } // Per-image errors leave the protocol synchronized.
                    catch
                    {
                        lock (gate)
                        {
                            sessions.Remove(session);
                            session.Dispose();
                            session = null;
                        }

                        throw;
                    }
                },
                request
            );
        }
        finally
        {
            lock (gate)
            {
                if (session is not null && !disposed)
                {
                    idle.Push(session);
                }
            }

            available.Release();
        }
    }

    /// <summary>
    /// 풀의 수명을 취소하고 소유한 디코더 세션을 종료합니다.
    /// </summary>
    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;

            foreach (var session in sessions)
            {
                session.Dispose();
            }

            sessions.Clear();
            idle.Clear();
        }
    }

    /// <summary>
    /// 재사용 가능한 썸네일 디코더 프로세스와 입출력 스트림의 소유권을 보관합니다.
    /// </summary>
    private sealed class Session : IDisposable
    {
        private int disposed;

        public Process Process
        {
            get;
        }

        /// <summary>
        /// 썸네일 서버 모드의 Python 프로세스를 시작하고 stderr 배출을 연결합니다.
        /// </summary>
        public Session(string root)
        {
            var start = new ProcessStartInfo(Path.Combine(root, ".venv", "Scripts", "python.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8
            };
            start.Environment["PYTHONUTF8"] = "1";
            start.ArgumentList.Add(Path.Combine(root, "python", "decode.py"));
            start.ArgumentList.Add("--thumbnail-server");
            Process = Process.Start(start) ?? throw new IOException("썸네일 디코더를 시작하지 못했습니다.");
            Process.ErrorDataReceived += (_, _) =>
            {
            };
            Process.BeginErrorReadLine();
        }

        /// <summary>
        /// 디코더 프로세스를 종료하고 스트림과 프로세스 핸들을 해제합니다.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            try
            {
                if (!Process.HasExited)
                {
                    Process.Kill(true);
                }
            }
            catch (InvalidOperationException)
            {
            }

            Process.Dispose();
        }
    }
}
