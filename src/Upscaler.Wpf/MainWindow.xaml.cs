using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Upscaler.Core;
using Upscaler.PythonBridge;

namespace Upscaler.Wpf;

/// <summary>
/// 탐색과 이미지 조작 이벤트를 연결하고 모달 작업의 소유권·실행 상태를 관리합니다.
/// </summary>
public partial class MainWindow : ThemedWindow
{
    private readonly MainViewModel vm = new();
    private readonly PythonWorker worker;
    private readonly List<string> history = [];
    private int historyIndex = -1;
    private string? lastResult;
    private bool busy, preparing, closing, closeReady, closeAfterJob, fitMode = true;
    private readonly HashSet<TreeViewItem> loadingNodes = [];
    private double zoom = 1;
    private Point? dragStart;
    private double dragX, dragY;
    private ProgressWindow? progress;
    private readonly bool diagnostic;

    /// <summary>
    /// 메인 창·탐색·Worker 이벤트를 연결합니다. 진단 모드는 자동 준비와 사용자 설정 저장을 생략합니다.
    /// </summary>
    public MainWindow() : this(false)
    {
    }

    /// <summary>
    /// 메인 창·탐색·Worker 이벤트를 연결합니다. 진단 모드는 자동 준비와 사용자 설정 저장을 생략합니다.
    /// </summary>
    public MainWindow(bool diagnostic)
    {
        this.diagnostic = diagnostic;
        InitializeComponent();
        DataContext = vm;
        worker = new(vm.Root);
        TreeColumn.Width = new(vm.Settings.TreeWidth);

        if (vm.Settings.PreviewLayout == "horizontal")
        {
            HorizontalLayoutButton.IsChecked = true;
        }
        else
        {
            VerticalLayoutButton.IsChecked = true;
        }

        foreach (var drive in DriveInfo.GetDrives())
        {
            Folders.Items.Add(Node(drive.RootDirectory.FullName));
        }

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.Preview) && vm.Preview is not null)
            {
                FitPreview();
            }
        };
        worker.StatusChanged += value => Dispatcher.BeginInvoke(() => vm.EngineStatus = value.Replace("Worker", "엔진"));
        UpdateNavigationButtons();
        Loaded += async (_, _) =>
        {
            if (diagnostic)
            {
                return;
            }

            _ = InitializeWorkerAsync();
            var args = Environment.GetCommandLineArgs();
            var folder = args.Length > 1 && Directory.Exists(args[1]) ? args[1] : vm.Settings.Folder;
            await Navigate(folder);
        };
        PreviewScroll.SizeChanged += (_, _) =>
        {
            if (fitMode)
            {
                FitPreview();
            }
        };
    }

    /// <summary>
    /// 현재 정렬 기준을 유지하면서 오름차순과 내림차순을 전환합니다.
    /// </summary>
    private void ToggleThumbnailSortDirection(object sender, RoutedEventArgs e)
    {
        vm.SortDescending = !vm.SortDescending;
    }

    // 동일 컨트롤의 행과 열만 바꿔 이미지 선택과 캐시를 유지한다.

    /// <summary>
    /// 기존 프리뷰·썸네일 컨트롤을 재배치해 선택과 캐시를 유지하며 분할 방향을 바꿉니다.
    /// </summary>
    private void LayoutChanged(object sender, RoutedEventArgs e)
    {
        if (PreviewLayoutGrid is null)
        {
            return;
        }

        bool horizontal = ReferenceEquals(sender, HorizontalLayoutButton);

        if (vm.Settings.PreviewLayout == "vertical" && ThumbnailRow.ActualHeight >= 110)
        {
            vm.Settings.ThumbnailHeight = ThumbnailRow.ActualHeight;
        }

        PreviewLayoutGrid.ColumnDefinitions[0].MinWidth = 210;
        PreviewLayoutGrid.ColumnDefinitions[2].MinWidth = horizontal ? 144 : 0;
        PreviewLayoutGrid.RowDefinitions[0].MinHeight = 150;
        ThumbnailRow.MinHeight = horizontal ? 0 : 190;
        vm.Settings.PreviewLayout = horizontal ? "horizontal" : "vertical";
        PreviewLayoutGrid.ColumnDefinitions[1].Width = new(horizontal ? 10 : 0);
        PreviewLayoutGrid.ColumnDefinitions[2].Width = horizontal ? new GridLength(1.5, GridUnitType.Star) : new(0);
        PreviewLayoutGrid.ColumnDefinitions[0].Width = new(horizontal ? 2 : 1, GridUnitType.Star);
        PreviewLayoutGrid.RowDefinitions[1].Height = new(horizontal ? 0 : 10);
        ThumbnailRow.Height = new(horizontal ? 0 : vm.Settings.ThumbnailHeight);
        Grid.SetRow(ThumbnailPane, horizontal ? 0 : 2);
        Grid.SetColumn(ThumbnailPane, horizontal ? 2 : 0);
        Grid.SetRow(PreviewSplitter, horizontal ? 0 : 1);
        Grid.SetColumn(PreviewSplitter, horizontal ? 1 : 0);
        PreviewSplitter.ResizeDirection = horizontal ? GridResizeDirection.Columns : GridResizeDirection.Rows;
        PreviewSplitter.Width = horizontal ? 4 : double.NaN;
        PreviewSplitter.Height = horizontal ? double.NaN : 4;
        PreviewSplitter.HorizontalAlignment = horizontal ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        PreviewSplitter.VerticalAlignment = horizontal ? VerticalAlignment.Stretch : VerticalAlignment.Center;
        Dispatcher.BeginInvoke(() =>
        {
            if (vm.Selected is not null)
            {
                Thumbnails.ScrollIntoView(vm.Selected);
            }
        });
    }

    /// <summary>
    /// 백그라운드 Worker를 준비하고 창이 닫히는 동안의 오류 UI 갱신을 피합니다.
    /// </summary>
    private async Task InitializeWorkerAsync()
    {
        try
        {
            await worker.StartAsync();
            vm.MarkEngineReady(worker.DeviceInfo.GetProperty("cuda").GetBoolean());
        }
        catch (Exception error)
        {
            if (!closing)
            {
                vm.EngineStatus = "엔진 오류: " + error.Message;
            }

            AppLog.Write(error.ToString());
        }
    }

    /// <summary>
    /// 필요할 때만 하위 폴더를 조회하도록 지연 확장 노드를 만듭니다.
    /// </summary>
    private TreeViewItem Node(string path)
    {
        var node = new TreeViewItem
        {
            Header = System.IO.Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } name ? name : path,
            Tag = path
        };
        node.Items.Add(new TreeViewItem { Header = "…" });
        node.Expanded += async (_, e) =>
        {
            if (e.OriginalSource == node && node.Items.Count == 1 && node.Items[0] is TreeViewItem { Tag: null })
            {
                await LoadNodeAsync(node, path);
            }
        };

        return node;
    }

    /// <summary>
    /// 동일 노드의 중복 조회를 막고 백그라운드에서 얻은 하위 폴더를 트리에 반영합니다.
    /// </summary>
    private async Task LoadNodeAsync(TreeViewItem node, string path)
    {
        if (!loadingNodes.Add(node))
        {
            return;
        }

        try
        {
            var paths = await Task.Run(() => Directory.GetDirectories(path));
            var expanded = node.Items.OfType<TreeViewItem>().Where(x => x.IsExpanded).Select(x => x.Tag as string).ToHashSet();
            node.Items.Clear();

            foreach (var child in paths.Order())
            {
                var item = Node(child);
                node.Items.Add(item);

                if (expanded.Contains(child))
                {
                    item.IsExpanded = true;
                }
            }
        }
        catch (Exception error)
        {
            vm.Status = "폴더를 읽을 수 없습니다: " + error.Message;
            node.Items.Clear();
            node.Items.Add(new TreeViewItem { Header = "접근할 수 없음 · 다시 펼쳐 재시도" });
        }
        finally
        {
            loadingNodes.Remove(node);
        }
    }

    /// <summary>
    /// 이미 생성된 트리 항목에서 지정 경로와 일치하는 노드를 찾습니다.
    /// </summary>
    private static TreeViewItem? FindNode(ItemsControl parent, string path)
    {
        foreach (var node in parent.Items.OfType<TreeViewItem>())
        {
            if (node.Tag is string candidate
                && string.Equals(candidate.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            if (FindNode(node, path) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    /// <summary>
    /// 탐색 이력 위치와 상위 폴더 존재 여부에 맞춰 이동 버튼 상태를 갱신합니다.
    /// </summary>
    private void UpdateNavigationButtons()
    {
        BackButton.IsEnabled = historyIndex > 0;
        ForwardButton.IsEnabled = historyIndex + 1 < history.Count;

        try
        {
            UpButton.IsEnabled = !string.IsNullOrEmpty(vm.Folder) && Directory.GetParent(vm.Folder) is not null;
        }
        catch
        {
            UpButton.IsEnabled = false;
        }
    }

    /// <summary>
    /// 현재 폴더를 다시 조회하고 선택·탐색 버튼·새로고침 상태를 복원합니다.
    /// </summary>
    private async Task RefreshCurrentFolderAsync()
    {
        if (busy || string.IsNullOrEmpty(vm.Folder))
        {
            return;
        }

        var path = vm.Folder;
        await Navigate(path, false);

        if (FindNode(Folders, path) is { } node)
        {
            await LoadNodeAsync(node, path);
        }
    }

    /// <summary>
    /// 창 단축키를 해석하고 해당 탐색 또는 새로고침 동작으로 전달합니다.
    /// </summary>
    private async void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            e.Handled = true;
            await RefreshCurrentFolderAsync();
        }
    }

    /// <summary>
    /// 검색 필터를 비우고 검색 입력의 포커스를 처리합니다.
    /// </summary>
    private void ClearSearch(object sender, RoutedEventArgs e)
    {
        vm.Filter = "";
        SearchBox.Focus();
    }

    /// <summary>
    /// 폴더 이동 성공 시에만 탐색 이력을 갱신하고 현재 노드·버튼 상태를 맞춥니다.
    /// </summary>
    private async Task<bool> Navigate(string path, bool record = true)
    {
        if (!await vm.NavigateAsync(path))
        {
            return false;
        }

        if (record
            && (historyIndex < 0
                || !string.Equals(history[historyIndex], vm.Folder, StringComparison.OrdinalIgnoreCase)))
        {
            if (historyIndex + 1 < history.Count)
            {
                history.RemoveRange(historyIndex + 1, history.Count - historyIndex - 1);
            }

            history.Add(vm.Folder);
            historyIndex = history.Count - 1;
        }

        UpdateNavigationButtons();

        return true;
    }

    /// <summary>
    /// 새로고침 버튼 이벤트를 현재 폴더 재조회로 전달합니다.
    /// </summary>
    private async void RefreshFolder(object s, RoutedEventArgs e)
    {
        await RefreshCurrentFolderAsync();
    }

    /// <summary>
    /// 이전 이력으로 이동하고 성공한 경우에만 이력 위치를 갱신합니다.
    /// </summary>
    private async void GoBack(object s, RoutedEventArgs e)
    {
        if (historyIndex > 0 && await Navigate(history[historyIndex - 1], false))
        {
            historyIndex--;
            UpdateNavigationButtons();
        }
    }

    /// <summary>
    /// 다음 이력으로 이동하고 성공한 경우에만 이력 위치를 갱신합니다.
    /// </summary>
    private async void GoForward(object s, RoutedEventArgs e)
    {
        if (historyIndex + 1 < history.Count && await Navigate(history[historyIndex + 1], false))
        {
            historyIndex++;
            UpdateNavigationButtons();
        }
    }

    /// <summary>
    /// 현재 경로의 상위 폴더가 있으면 해당 경로로 이동합니다.
    /// </summary>
    private async void GoUp(object s, RoutedEventArgs e)
    {
        try
        {
            if (Directory.GetParent(vm.Folder) is { } p)
            {
                await Navigate(p.FullName);
            }
        }
        catch (Exception ex)
        {
            vm.Status = ex.Message;
        }
    }

    /// <summary>
    /// 주소 입력의 Enter 키를 실제 폴더 탐색 요청으로 처리합니다.
    /// </summary>
    private async void AddressKey(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await Navigate(vm.Address);
        }
    }

    /// <summary>
    /// 트리에서 선택한 실제 폴더 경로로 이미지 목록을 이동합니다.
    /// </summary>
    private async void FolderSelected(object s, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem { Tag: string path })
        {
            await Navigate(path);
        }
    }

    /// <summary>
    /// 폴더 선택 창에서 승인된 경로만 출력 설정에 반영합니다.
    /// </summary>
    private void ChooseOutput(object s, RoutedEventArgs e)
    {
        var d = new OpenFolderDialog();

        if (d.ShowDialog(this) == true)
        {
            vm.Output = d.FolderName;
        }
    }

    /// <summary>
    /// 화면 맞춤 버튼 이벤트를 프리뷰 크기 계산으로 전달합니다.
    /// </summary>
    private void Fit(object s, RoutedEventArgs e)
    {
        FitPreview();
    }

    /// <summary>
    /// 이미지와 뷰포트 비율로 화면 맞춤 배율을 계산하고 스크롤 위치를 맞춥니다.
    /// </summary>
    private void FitPreview()
    {
        fitMode = true;

        if (vm.Preview is null)
        {
            return;
        }

        var w = Math.Max(1, PreviewScroll.ActualWidth - 22);
        var h = Math.Max(1, PreviewScroll.ActualHeight - 22);
        zoom = Math.Min(1, Math.Min(w / Math.Max(1, vm.OriginalWidth), h / Math.Max(1, vm.OriginalHeight)));
        ApplyZoom();
        PreviewScroll.ScrollToHorizontalOffset(0);
        PreviewScroll.ScrollToVerticalOffset(0);
    }

    /// <summary>
    /// 현재 배율을 프리뷰 표시 크기와 UI 안내에 반영합니다.
    /// </summary>
    private void ApplyZoom()
    {
        PreviewImage.Width = vm.OriginalWidth * zoom;
        PreviewImage.Height = vm.OriginalHeight * zoom;
        ZoomLabel.Text = $"{zoom * 100:0}%";
    }

    /// <summary>
    /// 배율을 제한 범위 안에서 변경하고 화면 맞춤 모드를 해제합니다.
    /// </summary>
    private void SetZoom(double factor)
    {
        if (vm.Preview is null)
        {
            return;
        }

        fitMode = false;
        zoom = Math.Clamp(zoom * factor, 0.01, 8);
        ApplyZoom();
    }

    /// <summary>
    /// 확대 버튼에서 현재 배율에 확대 비율을 적용합니다.
    /// </summary>
    private void ZoomIn(object s, RoutedEventArgs e)
    {
        SetZoom(1.25);
    }

    /// <summary>
    /// 축소 버튼에서 현재 배율에 축소 비율을 적용합니다.
    /// </summary>
    private void ZoomOut(object s, RoutedEventArgs e)
    {
        SetZoom(0.8);
    }

    /// <summary>
    /// 프리뷰의 마우스 휠을 배율 변경으로 처리합니다.
    /// </summary>
    private void PreviewWheel(object s, MouseWheelEventArgs e)
    {
        SetZoom(e.Delta > 0 ? 1.15 : 1 / 1.15);
        e.Handled = true;
    }

    /// <summary>
    /// 드래그 시작 위치와 스크롤 오프셋을 보관하고 마우스를 캡처합니다.
    /// </summary>
    private void PreviewDown(object s, MouseButtonEventArgs e)
    {
        if (vm.Preview is null)
        {
            return;
        }

        dragStart = e.GetPosition(PreviewScroll);
        dragX = PreviewScroll.HorizontalOffset;
        dragY = PreviewScroll.VerticalOffset;
        PreviewScroll.CaptureMouse();
        PreviewScroll.Cursor = Cursors.Hand;
        e.Handled = true;
    }

    /// <summary>
    /// 드래그 시작점과 현재 포인터 차이만큼 프리뷰를 패닝합니다.
    /// </summary>
    private void PreviewMove(object s, MouseEventArgs e)
    {
        if (dragStart is not { } start)
        {
            return;
        }

        var at = e.GetPosition(PreviewScroll);
        PreviewScroll.ScrollToHorizontalOffset(dragX + start.X - at.X);
        PreviewScroll.ScrollToVerticalOffset(dragY + start.Y - at.Y);
    }

    /// <summary>
    /// 드래그 상태를 해제하고 마우스 캡처를 반환합니다.
    /// </summary>
    private void PreviewUp(object s, MouseButtonEventArgs e)
    {
        dragStart = null;
        PreviewScroll.ReleaseMouseCapture();
        PreviewScroll.Cursor = Cursors.Arrow;
    }

    /// <summary>
    /// 외부 요인으로 마우스 캡처를 잃었을 때 드래그 상태를 정리합니다.
    /// </summary>
    private void PreviewCaptureLost(object sender, MouseEventArgs e)
    {
        dragStart = null;
        PreviewScroll.Cursor = Cursors.Arrow;
    }

    /// <summary>
    /// 화면에 나타난 항목의 썸네일 로드를 요청합니다.
    /// </summary>
    private async void ThumbnailLoaded(object s, RoutedEventArgs e)
    {
        if (s is FrameworkElement { DataContext: ImageItem item })
        {
            await vm.LoadThumbnailAsync(item);
        }
    }

    /// <summary>
    /// 화면에서 사라진 재활용 항목의 썸네일 요청과 참조를 해제합니다.
    /// </summary>
    private void ThumbnailUnloaded(object s, RoutedEventArgs e)
    {
        if (s is FrameworkElement { DataContext: ImageItem item })
        {
            vm.ReleaseThumbnail(item);
        }
    }

    // 재활용 컨테이너는 Loaded 없이 DataContext만 바뀔 수 있어 별도로 처리한다.

    /// <summary>
    /// 재활용 컨테이너의 항목이 바뀌면 이전 요청을 해제하고 새 항목을 로드합니다.
    /// </summary>
    private async void ThumbnailChanged(object s, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ImageItem old)
        {
            vm.ReleaseThumbnail(old);
        }

        if (s is FrameworkElement { IsLoaded: true } && e.NewValue is ImageItem item)
        {
            await vm.LoadThumbnailAsync(item);
        }
    }

    /// <summary>
    /// 끌어온 폴더 또는 이미지의 위치로 이동하고 이미지인 경우 해당 항목을 선택합니다.
    /// </summary>
    private async void OnDrop(object s, DragEventArgs e)
    {
        if (busy || e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths)
        {
            return;
        }

        var path = paths[0];

        if (Directory.Exists(path))
        {
            await Navigate(path);
        }
        else if (File.Exists(path) && Images.Extensions.Contains(System.IO.Path.GetExtension(path)))
        {
            vm.Filter = "";

            if (await Navigate(System.IO.Path.GetDirectoryName(path)!))
            {
                vm.Selected = vm.Files.FirstOrDefault(x => x.Path == path);
            }
        }
    }

    /// <summary>
    /// 목록 준비 중이 아닐 때 선택한 이미지 한 장을 실행 대상으로 확정합니다.
    /// </summary>
    private async void RunImage(object s, RoutedEventArgs e)
    {
        if (!preparing && vm.Selected is { } item)
        {
            await Run([item.Path]);
        }
    }

    /// <summary>
    /// 현재 입력 폴더를 먼저 캡처하고 하위 폴더를 제외한 목록을 확정해 실행합니다.
    /// </summary>
    private async void RunFolder(object s, RoutedEventArgs e)
    {
        if (busy || preparing)
        {
            return;
        }

        preparing = true;

        try
        {
            var folder = vm.Folder;
            var paths = await Task.Run(() => Images.Scan(folder));
            await Run(paths, folder);
        }
        catch (Exception error)
        {
            vm.Status = error.Message;
        }
        finally
        {
            preparing = false;
        }
    }

    /// <summary>
    /// 작업 설정과 유효 출력 경로를 확인받은 뒤 진행 모달을 열고 완료·취소 후 실행 상태를 복원합니다.
    /// </summary>
    private Task Run(string[] paths, string? sourceFolder = null)
    {
        if (busy || paths.Length == 0)
        {
            return Task.CompletedTask;
        }

        Settings settings;

        try
        {
            settings = vm.CaptureSettings(sourceFolder);
        }
        catch (Exception error)
        {
            AppDialog.Inform(this, "설정 확인", error.Message);

            return Task.CompletedTask;
        }

        if (!AppDialog.Confirm(
                this,
                "업스케일링 시작",
                $"대상: {paths.Length}개 이미지\n출력: {settings.Output}\n\n원본을 보존하고 4배 확대 결과를 저장합니다.",
                "시작"
            ))
        {
            return Task.CompletedTask;
        }

        busy = true;

        try
        {
            progress = new(worker, paths, settings)
            {
                Owner = this
            };
            progress.ShowDialog();
            lastResult = progress.LastOutput ?? lastResult;
            ResultButton.IsEnabled = lastResult is not null;
            vm.Status = progress.Summary;

            if (worker.ProcessId is not null)
            {
                vm.MarkEngineReady(worker.DeviceInfo.GetProperty("cuda").GetBoolean());
            }
            else
            {
                vm.EngineStatus = "엔진 대기 · 다음 실행 시 준비";
            }
        }
        finally
        {
            progress = null;
            busy = false;

            if (closeAfterJob)
            {
                closeAfterJob = false;
                _ = Dispatcher.BeginInvoke(Close);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 최근 성공 결과가 있는 폴더로 이동하고 해당 이미지를 선택합니다.
    /// </summary>
    private async void ShowResult(object s, RoutedEventArgs e)
    {
        if (lastResult is null)
        {
            vm.Status = "아직 완료된 결과가 없습니다.";

            return;
        }

        vm.Filter = "";

        if (await Navigate(System.IO.Path.GetDirectoryName(lastResult)!))
        {
            vm.Selected = vm.Files.FirstOrDefault(x => x.Path == lastResult);
        }
    }

    /// <summary>
    /// 작업 취소·Worker 종료·설정 저장이 끝날 때까지 닫기를 지연해 자원 정리를 보장합니다.
    /// </summary>
    private async void OnClosing(object? s, CancelEventArgs e)
    {
        if (closeReady)
        {
            return;
        }

        e.Cancel = true;

        if (busy)
        {
            closeAfterJob = progress?.RequestCancellation() ?? false;

            return;
        }

        if (closing)
        {
            return;
        }

        closing = true;
        IsEnabled = false;
        vm.Stop();
        vm.Settings.TreeWidth = TreeColumn.ActualWidth;

        if (vm.Settings.PreviewLayout == "vertical")
        {
            vm.Settings.ThumbnailHeight = ThumbnailRow.ActualHeight;
        }

        try
        {
            if (!diagnostic)
            {
                vm.Settings.Save();
            }
        }
        catch (Exception error)
        {
            AppLog.Write(error.ToString());
        }

        try
        {
            await worker.DisposeAsync();
        }
        catch (Exception error)
        {
            AppLog.Write(error.ToString());
        }

        closeReady = true;
        _ = Dispatcher.BeginInvoke(Close);
    }
}
