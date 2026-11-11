using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Upscaler.Core;
using Upscaler.PythonBridge;
namespace Upscaler.Wpf;

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

    public MainWindow() : this(false) { }
    public MainWindow(bool diagnostic)
    {
        this.diagnostic = diagnostic;
        InitializeComponent(); DataContext = vm; worker = new(vm.Root);
        TreeColumn.Width = new(vm.Settings.TreeWidth);
        if (vm.Settings.PreviewLayout == "horizontal") HorizontalLayoutButton.IsChecked = true; else VerticalLayoutButton.IsChecked = true;
        foreach (var drive in DriveInfo.GetDrives()) Folders.Items.Add(Node(drive.RootDirectory.FullName));
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Preview) && vm.Preview is not null) FitPreview(); };
        worker.StatusChanged += value => Dispatcher.BeginInvoke(() => vm.EngineStatus = value.Replace("Worker", "엔진"));
        UpdateNavigationButtons();
        Loaded += async (_, _) =>
        {
            if (diagnostic) return;
            _ = InitializeWorkerAsync();
            var args = Environment.GetCommandLineArgs();
            var folder = args.Length > 1 && Directory.Exists(args[1]) ? args[1] : vm.Settings.Folder;
            await Navigate(folder);
        };
        PreviewScroll.SizeChanged += (_, _) => { if (fitMode) FitPreview(); };
    }
    private void ToggleThumbnailSortDirection(object sender, RoutedEventArgs e) => vm.SortDescending = !vm.SortDescending;
    private void LayoutChanged(object sender, RoutedEventArgs e)
    {
        if (PreviewLayoutGrid is null) return;
        bool horizontal = ReferenceEquals(sender, HorizontalLayoutButton);
        if (vm.Settings.PreviewLayout == "vertical" && ThumbnailRow.ActualHeight >= 110)
            vm.Settings.ThumbnailHeight = ThumbnailRow.ActualHeight;
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
        Grid.SetRow(ThumbnailPane, horizontal ? 0 : 2); Grid.SetColumn(ThumbnailPane, horizontal ? 2 : 0);
        Grid.SetRow(PreviewSplitter, horizontal ? 0 : 1); Grid.SetColumn(PreviewSplitter, horizontal ? 1 : 0);
        PreviewSplitter.ResizeDirection = horizontal ? GridResizeDirection.Columns : GridResizeDirection.Rows;
        PreviewSplitter.Width = horizontal ? 4 : double.NaN; PreviewSplitter.Height = horizontal ? double.NaN : 4;
        PreviewSplitter.HorizontalAlignment = horizontal ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        PreviewSplitter.VerticalAlignment = horizontal ? VerticalAlignment.Stretch : VerticalAlignment.Center;
        Dispatcher.BeginInvoke(() => { if (vm.Selected is not null) Thumbnails.ScrollIntoView(vm.Selected); });
    }
    private async Task InitializeWorkerAsync()
    {
        try { await worker.StartAsync(); vm.MarkEngineReady(worker.DeviceInfo.GetProperty("cuda").GetBoolean()); }
        catch (Exception error) { if (!closing) vm.EngineStatus = "엔진 오류: " + error.Message; AppLog.Write(error.ToString()); }
    }
    private TreeViewItem Node(string path)
    {
        var node = new TreeViewItem { Header = System.IO.Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } name ? name : path, Tag = path };
        node.Items.Add(new TreeViewItem { Header = "…" });
        node.Expanded += async (_, e) =>
        {
            if (e.OriginalSource == node && node.Items.Count == 1 && node.Items[0] is TreeViewItem { Tag: null })
                await LoadNodeAsync(node, path);
        };
        return node;
    }
    private async Task LoadNodeAsync(TreeViewItem node, string path)
    {
        if (!loadingNodes.Add(node)) return;
        try
        {
            var paths = await Task.Run(() => Directory.GetDirectories(path));
            var expanded = node.Items.OfType<TreeViewItem>().Where(x => x.IsExpanded).Select(x => x.Tag as string).ToHashSet();
            node.Items.Clear();
            foreach (var child in paths.Order()) { var item = Node(child); node.Items.Add(item); if (expanded.Contains(child)) item.IsExpanded = true; }
        }
        catch (Exception error)
        {
            vm.Status = "폴더를 읽을 수 없습니다: " + error.Message;
            node.Items.Clear(); node.Items.Add(new TreeViewItem { Header = "접근할 수 없음 · 다시 펼쳐 재시도" });
        }
        finally { loadingNodes.Remove(node); }
    }
    private static TreeViewItem? FindNode(ItemsControl parent, string path)
    {
        foreach (var node in parent.Items.OfType<TreeViewItem>())
        {
            if (node.Tag is string candidate && string.Equals(candidate.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return node;
            if (FindNode(node, path) is { } nested) return nested;
        }
        return null;
    }
    private void UpdateNavigationButtons()
    {
        BackButton.IsEnabled = historyIndex > 0;
        ForwardButton.IsEnabled = historyIndex + 1 < history.Count;
        try { UpButton.IsEnabled = !string.IsNullOrEmpty(vm.Folder) && Directory.GetParent(vm.Folder) is not null; }
        catch { UpButton.IsEnabled = false; }
    }
    private async Task RefreshCurrentFolderAsync()
    {
        if (busy || string.IsNullOrEmpty(vm.Folder)) return;
        var path = vm.Folder;
        await Navigate(path, false);
        if (FindNode(Folders, path) is { } node) await LoadNodeAsync(node, path);
    }
    private async void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5) { e.Handled = true; await RefreshCurrentFolderAsync(); }
    }
    private void ClearSearch(object sender, RoutedEventArgs e) { vm.Filter = ""; SearchBox.Focus(); }
    private async Task<bool> Navigate(string path, bool record = true)
    {
        if (!await vm.NavigateAsync(path)) return false;
        if (record && (historyIndex < 0 || !string.Equals(history[historyIndex], vm.Folder, StringComparison.OrdinalIgnoreCase)))
        {
            if (historyIndex + 1 < history.Count) history.RemoveRange(historyIndex + 1, history.Count - historyIndex - 1);
            history.Add(vm.Folder); historyIndex = history.Count - 1;
        }
        UpdateNavigationButtons();
        return true;
    }
    private async void RefreshFolder(object s, RoutedEventArgs e) => await RefreshCurrentFolderAsync();
    private async void GoBack(object s, RoutedEventArgs e) { if (historyIndex > 0 && await Navigate(history[historyIndex - 1], false)) { historyIndex--; UpdateNavigationButtons(); } }
    private async void GoForward(object s, RoutedEventArgs e) { if (historyIndex + 1 < history.Count && await Navigate(history[historyIndex + 1], false)) { historyIndex++; UpdateNavigationButtons(); } }
    private async void GoUp(object s, RoutedEventArgs e) { try { if (Directory.GetParent(vm.Folder) is { } p) await Navigate(p.FullName); } catch (Exception ex) { vm.Status = ex.Message; } }
    private async void AddressKey(object s, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; await Navigate(vm.Address); } }
    private async void FolderSelected(object s, RoutedPropertyChangedEventArgs<object> e) { if (e.NewValue is TreeViewItem { Tag: string path }) await Navigate(path); }
    private void ChooseOutput(object s, RoutedEventArgs e) { var d = new OpenFolderDialog(); if (d.ShowDialog(this) == true) vm.Output = d.FolderName; }
    private void Fit(object s, RoutedEventArgs e) => FitPreview();
    private void FitPreview()
    {
        fitMode = true;
        if (vm.Preview is null) return;
        var w = Math.Max(1, PreviewScroll.ActualWidth - 22); var h = Math.Max(1, PreviewScroll.ActualHeight - 22);
        zoom = Math.Min(1, Math.Min(w / Math.Max(1, vm.OriginalWidth), h / Math.Max(1, vm.OriginalHeight)));
        ApplyZoom(); PreviewScroll.ScrollToHorizontalOffset(0); PreviewScroll.ScrollToVerticalOffset(0);
    }
    private void ApplyZoom()
    {
        PreviewImage.Width = vm.OriginalWidth * zoom; PreviewImage.Height = vm.OriginalHeight * zoom;
        ZoomLabel.Text = $"{zoom * 100:0}%";
    }
    private void SetZoom(double factor) { if (vm.Preview is null) return; fitMode = false; zoom = Math.Clamp(zoom * factor, 0.01, 8); ApplyZoom(); }
    private void ZoomIn(object s, RoutedEventArgs e) => SetZoom(1.25);
    private void ZoomOut(object s, RoutedEventArgs e) => SetZoom(0.8);
    private void PreviewWheel(object s, MouseWheelEventArgs e) { SetZoom(e.Delta > 0 ? 1.15 : 1 / 1.15); e.Handled = true; }
    private void PreviewDown(object s, MouseButtonEventArgs e)
    {
        if (vm.Preview is null) return;
        dragStart = e.GetPosition(PreviewScroll); dragX = PreviewScroll.HorizontalOffset; dragY = PreviewScroll.VerticalOffset;
        PreviewScroll.CaptureMouse(); PreviewScroll.Cursor = Cursors.Hand; e.Handled = true;
    }
    private void PreviewMove(object s, MouseEventArgs e)
    {
        if (dragStart is not { } start) return;
        var at = e.GetPosition(PreviewScroll);
        PreviewScroll.ScrollToHorizontalOffset(dragX + start.X - at.X); PreviewScroll.ScrollToVerticalOffset(dragY + start.Y - at.Y);
    }
    private void PreviewUp(object s, MouseButtonEventArgs e) { dragStart = null; PreviewScroll.ReleaseMouseCapture(); PreviewScroll.Cursor = Cursors.Arrow; }
    private void PreviewCaptureLost(object sender, MouseEventArgs e) { dragStart = null; PreviewScroll.Cursor = Cursors.Arrow; }
    private async void ThumbnailLoaded(object s, RoutedEventArgs e) { if (s is FrameworkElement { DataContext: ImageItem item }) await vm.LoadThumbnailAsync(item); }
    private void ThumbnailUnloaded(object s, RoutedEventArgs e) { if (s is FrameworkElement { DataContext: ImageItem item }) vm.ReleaseThumbnail(item); }
    private async void ThumbnailChanged(object s, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ImageItem old) vm.ReleaseThumbnail(old);
        if (s is FrameworkElement { IsLoaded: true } && e.NewValue is ImageItem item) await vm.LoadThumbnailAsync(item);
    }
    private async void OnDrop(object s, DragEventArgs e)
    {
        if (busy || e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths) return;
        var path = paths[0];
        if (Directory.Exists(path)) await Navigate(path);
        else if (File.Exists(path) && Images.Extensions.Contains(System.IO.Path.GetExtension(path)))
        { vm.Filter = ""; if (await Navigate(System.IO.Path.GetDirectoryName(path)!)) vm.Selected = vm.Files.FirstOrDefault(x => x.Path == path); }
    }
    private async void RunImage(object s, RoutedEventArgs e) { if (!preparing && vm.Selected is { } item) await Run([item.Path]); }
    private async void RunFolder(object s, RoutedEventArgs e)
    {
        if (busy || preparing) return;
        preparing = true;
        try { var folder = vm.Folder; var paths = await Task.Run(() => Images.Scan(folder)); await Run(paths); }
        catch (Exception error) { vm.Status = error.Message; }
        finally { preparing = false; }
    }
    private Task Run(string[] paths)
    {
        if (busy || paths.Length == 0) return Task.CompletedTask;
        Settings settings;
        try { settings = vm.CaptureSettings(); }
        catch (Exception error) { AppDialog.Inform(this, "설정 확인", error.Message); return Task.CompletedTask; }
        if (!AppDialog.Confirm(this, "업스케일링 시작", $"대상: {paths.Length}개 이미지\n출력: {settings.Output}\n\n원본을 보존하고 4배 확대 결과를 저장합니다.", "시작")) return Task.CompletedTask;
        busy = true;
        try
        {
            progress = new(worker, paths, settings) { Owner = this }; progress.ShowDialog();
            lastResult = progress.LastOutput ?? lastResult; ResultButton.IsEnabled = lastResult is not null; vm.Status = progress.Summary;
            if (worker.ProcessId is not null) vm.MarkEngineReady(worker.DeviceInfo.GetProperty("cuda").GetBoolean());
            else vm.EngineStatus = "엔진 대기 · 다음 실행 시 준비";
        }
        finally { progress = null; busy = false; if (closeAfterJob) { closeAfterJob = false; _ = Dispatcher.BeginInvoke(Close); } }
        return Task.CompletedTask;
    }
    private async void ShowResult(object s, RoutedEventArgs e)
    {
        if (lastResult is null) { vm.Status = "아직 완료된 결과가 없습니다."; return; }
        vm.Filter = "";
        if (await Navigate(System.IO.Path.GetDirectoryName(lastResult)!)) vm.Selected = vm.Files.FirstOrDefault(x => x.Path == lastResult);
    }
    private async void OnClosing(object? s, CancelEventArgs e)
    {
        if (closeReady) return;
        e.Cancel = true;
        if (busy) { closeAfterJob = progress?.RequestCancellation() ?? false; return; }
        if (closing) return;
        closing = true; IsEnabled = false; vm.Stop();
        vm.Settings.TreeWidth = TreeColumn.ActualWidth; if (vm.Settings.PreviewLayout == "vertical") vm.Settings.ThumbnailHeight = ThumbnailRow.ActualHeight;
        try { if (!diagnostic) vm.Settings.Save(); } catch (Exception error) { AppLog.Write(error.ToString()); }
        try { await worker.DisposeAsync(); } catch (Exception error) { AppLog.Write(error.ToString()); }
        closeReady = true; _ = Dispatcher.BeginInvoke(Close);
    }
}
