using System.Diagnostics;
using System.IO;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Upscaler.Core;
using Upscaler.PythonBridge;

namespace Upscaler.Wpf;

/// <summary>
/// 배치 진행과 오류를 표시하고 취소 정리가 끝나기 전에는 소유 창으로 복귀하지 않는 모달입니다.
/// </summary>
public sealed class ProgressWindow : ThemedWindow
{
    private readonly PythonWorker worker;
    private readonly string[] paths;
    private readonly Settings settings;
    private readonly CancellationTokenSource cancel;
    private readonly TextBlock file = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Margin = new(0, 18, 0, 12)
    };
    private readonly TextBlock counts = new()
    {
        Margin = new(0, 12, 0, 8)
    };
    private readonly TextBlock stage = new()
    {
        Margin = new(0, 0, 0, 12)
    };
    private readonly TextBox details = new()
    {
        IsReadOnly = true,
        TextWrapping = TextWrapping.Wrap,
        MaxHeight = 100,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Visibility = Visibility.Collapsed
    };
    private readonly ProgressBar bar = new()
    {
        Height = 8,
        Minimum = 0,
        Maximum = 100
    };
    private readonly Button button = new()
    {
        Content = "작업 취소",
        HorizontalAlignment = HorizontalAlignment.Right
    };
    private readonly Button openOutput = new()
    {
        Content = "결과 폴더 열기",
        Visibility = Visibility.Collapsed
    };
    private bool finished, confirming;

    public bool IsFinished => finished;

    public string? LastOutput
    {
        get;
        private set;
    }

    public string Summary
    {
        get;
        private set;
    } = "";

    /// <summary>
    /// 진행 UI와 취소 이벤트를 구성하고 외부 취소 토큰을 창의 작업 수명에 연결합니다.
    /// </summary>
    public ProgressWindow(
        PythonWorker worker,
        string[] paths,
        Settings settings,
        CancellationToken externalCancellation = default
    )
    {
        this.worker = worker;
        this.paths = paths;
        this.settings = settings;
        cancel = CancellationTokenSource.CreateLinkedTokenSource(externalCancellation);
        Title = "이미지 업스케일링";
        Width = 580;
        SizeToContent = SizeToContent.Height;
        MinHeight = 300;
        MaxHeight = 560;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel
        {
            Margin = new(24)
        };
        panel.Children.Add(new TextBlock { Text = $"이미지 업스케일링 · {paths.Length}개", FontSize = 22 });
        panel.Children.Add(file);
        panel.Children.Add(bar);
        panel.Children.Add(counts);
        panel.Children.Add(stage);
        panel.Children.Add(details);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        actions.Children.Add(openOutput);
        actions.Children.Add(button);
        panel.Children.Add(actions);
        Content = panel;
        openOutput.Click += (_, _) =>
        {
            try
            {
                var folder = Path.GetDirectoryName(LastOutput!);

                if (folder is null || !Directory.Exists(folder))
                {
                    throw new DirectoryNotFoundException("결과 폴더가 존재하지 않습니다.");
                }

                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            }
            catch (Exception error)
            {
                AppDialog.Inform(this, "결과 폴더 열기", error.Message);
            }
        };
        button.Click += (_, _) =>
        {
            if (finished)
            {
                Close();
            }
            else
            {
                RequestCancellation();
            }
        };
        Closing += OnClosing;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;

                if (finished)
                {
                    Close();
                }
                else
                {
                    RequestCancellation();
                }
            }
        };
        Loaded += async (_, _) => await ExecuteAsync();
        Closed += (_, _) => cancel.Dispose();
    }

    /// <summary>
    /// 사용자 확인 후 취소를 요청합니다. 정리가 끝날 때까지 버튼과 닫기 상태를 잠급니다.
    /// </summary>
    /// <returns>종료 또는 취소 요청을 수락한 경우 true입니다. 확인 중이거나 사용자가 거절하면 false입니다.</returns>
    public bool RequestCancellation()
    {
        if (finished)
        {
            Close();

            return true;
        }

        if (confirming)
        {
            return false;
        }

        if (cancel.IsCancellationRequested)
        {
            return true;
        }

        confirming = true;

        try
        {
            if (AppDialog.Confirm(this, "작업 취소", "현재 이미지와 남은 이미지 처리를 중단할까요?\n이미 완료된 결과는 유지됩니다.", "작업 취소"))
            {
                cancel.Cancel();
                button.IsEnabled = false;
                button.Content = "작업 종료 확인 중…";
                stage.Text = "작업을 종료하고 임시 파일을 정리하고 있습니다.";

                return true;
            }
        }
        finally
        {
            confirming = false;
        }

        return false;
    }

    /// <summary>
    /// 작업이 끝나지 않았으면 닫기를 취소하고 기존 취소 확인 흐름으로 전달합니다.
    /// </summary>
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!finished)
        {
            e.Cancel = true;
            RequestCancellation();
        }
    }

    /// <summary>
    /// Worker의 실제 단계·진행률을 Dispatcher에서 표시하고 취소 이후의 늦은 갱신을 무시합니다.
    /// </summary>
    private void OnProgress(JsonElement value)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (finished || cancel.IsCancellationRequested)
            {
                return;
            }

            var name = value.GetProperty("stage").GetString();
            stage.Text = name switch
            {
                "decoding" => "이미지 확인 중",
                "loading" => "모델 준비 중",
                "saving" => "결과 저장 중",
                "retry" => "메모리 부족으로 작은 타일로 재시도 중",
                _ => "추론 중"
            };

            if (value.TryGetProperty("progress", out var p) && p.ValueKind == JsonValueKind.Number)
            {
                bar.IsIndeterminate = false;
                bar.Value = p.GetDouble();
                stage.Text += $" · {bar.Value:F0}%";
            }
            else
            {
                bar.IsIndeterminate = true;
            }
        });
    }

    /// <summary>
    /// 배치를 실행하고 최종 집계·최근 성공 경로를 반영한 뒤 완료 상태로 전환합니다.
    /// </summary>
    private async Task ExecuteAsync()
    {
        worker.Progress += OnProgress;
        var runner = new BatchRunner(worker);
        runner.Changed += value =>
        {
            if (value.Current.Length > 0)
            {
                file.Text = $"{Math.Min(paths.Length, value.Completed + value.Failed + 1)} / {paths.Length} · {System.IO.Path.GetFileName(value.Current)}";
                bar.IsIndeterminate = true;
            }

            counts.Text = $"완료 {value.Completed} · 실패 {value.Failed} · 남음 {paths.Length - value.Completed - value.Failed}";

            if (value.Error is not null)
            {
                details.Visibility = Visibility.Visible;
                details.AppendText($"{System.IO.Path.GetFileName(value.Current)}: {value.Error}\n");

                if (details.Text.Length > 16000)
                {
                    details.Text = details.Text[^12000..];
                }
            }
        };

        try
        {
            var result = await runner.RunAsync(paths, settings, cancel.Token);
            Summary = result.Summary;
            LastOutput = runner.LastOutput;
        }
        catch (Exception error)
        {
            Summary = "작업을 완료하지 못했습니다.";
            details.Text = error.Message;
            details.Visibility = Visibility.Visible;
            AppLog.Write(error.ToString());
        }
        finally
        {
            worker.Progress -= OnProgress;
            finished = true;
            bar.IsIndeterminate = false;
            bar.Value = cancel.IsCancellationRequested ? 0 : 100;
            counts.Text = Summary;
            file.Text = cancel.IsCancellationRequested ? "취소 처리가 완료되었습니다." : "작업이 끝났습니다.";
            stage.Text = "출력: " + settings.Output;
            stage.TextWrapping = TextWrapping.Wrap;
            button.Content = "닫기";
            button.IsEnabled = true;
            openOutput.Visibility = LastOutput is not null ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
