using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Upscaler.Core;
using Upscaler.PythonBridge;
namespace Upscaler.Wpf;

public partial class MainWindow : Window
{
    private readonly MainViewModel vm = new();
    private readonly PythonWorker worker;
    private readonly List<string> history = [];
    private int historyIndex = -1;
    private string? lastResult;
    private bool busy, closing, closeReady, closeAfterJob, fitMode = true;
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
        TreeColumn.Width = new(vm.Settings.TreeWidth); ThumbnailRow.Height = new(vm.Settings.ThumbnailHeight);
        foreach (var drive in DriveInfo.GetDrives()) Folders.Items.Add(Node(drive.RootDirectory.FullName));
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Preview) && vm.Preview is not null) FitPreview(); };
        worker.StatusChanged += value => Dispatcher.BeginInvoke(() => vm.EngineStatus = value);
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
    private async Task InitializeWorkerAsync()
    {
        try { await worker.StartAsync(); }
        catch (Exception error) { if (!closing) vm.EngineStatus = "Worker 오류: " + error.Message; AppLog.Write(error.ToString()); }
    }
    private TreeViewItem Node(string path)
    {
        var node = new TreeViewItem { Header = System.IO.Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } name ? name : path, Tag = path };
        node.Items.Add(new TreeViewItem { Header = "…" });
        node.Expanded += async (_, e) =>
        {
            if (e.OriginalSource != node) return;
            if (node.Items.Count == 1 && node.Items[0] is TreeViewItem { Tag: null })
            {
                node.Items.Clear();
                try { var paths = await Task.Run(() => Directory.GetDirectories(path)); foreach (var child in paths.Order()) node.Items.Add(Node(child)); }
                catch (Exception error) { vm.Status = error.Message; node.Items.Add(new TreeViewItem { Header = "접근할 수 없음 · 다시 펼쳐 재시도" }); }
            }
        };
        return node;
    }
    private async Task<bool> Navigate(string path, bool record = true)
    {
        if (!await vm.NavigateAsync(path)) return false;
        if (record && (historyIndex < 0 || !string.Equals(history[historyIndex], vm.Folder, StringComparison.OrdinalIgnoreCase)))
        {
            if (historyIndex + 1 < history.Count) history.RemoveRange(historyIndex + 1, history.Count - historyIndex - 1);
            history.Add(vm.Folder); historyIndex = history.Count - 1;
        }
        return true;
    }
    private async void OpenFolder(object s, RoutedEventArgs e) { var d = new OpenFolderDialog(); if (d.ShowDialog(this) == true) await Navigate(d.FolderName); }
    private async void RefreshFolder(object s, RoutedEventArgs e) => await Navigate(vm.Folder, false);
    private async void GoBack(object s, RoutedEventArgs e) { if (historyIndex > 0 && await Navigate(history[historyIndex - 1], false)) historyIndex--; }
    private async void GoForward(object s, RoutedEventArgs e) { if (historyIndex + 1 < history.Count && await Navigate(history[historyIndex + 1], false)) historyIndex++; }
    private async void GoUp(object s, RoutedEventArgs e) { try { if (Directory.GetParent(vm.Folder) is { } p) await Navigate(p.FullName); } catch (Exception ex) { vm.Status = ex.Message; } }
    private async void AddressKey(object s, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; await Navigate(vm.Address); } }
    private async void FolderSelected(object s, RoutedPropertyChangedEventArgs<object> e) { if (e.NewValue is TreeViewItem { Tag: string path }) await Navigate(path); }
    private void ChooseOutput(object s, RoutedEventArgs e) { var d = new OpenFolderDialog(); if (d.ShowDialog(this) == true) vm.Output = d.FolderName; }
    private void ShowSettings(object s, RoutedEventArgs e) { SettingsScroll.ScrollToTop(); SettingsScroll.Focus(); }
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
    private async void RunImage(object s, RoutedEventArgs e) { if (vm.Selected is { } item) await Run([item.Path]); }
    private async void RunFolder(object s, RoutedEventArgs e)
    {
        try { var folder = vm.Folder; var paths = await Task.Run(() => Images.Scan(folder)); await Run(paths); }
        catch (Exception error) { vm.Status = error.Message; }
    }
    private Task Run(string[] paths)
    {
        if (busy || paths.Length == 0) return Task.CompletedTask;
        Settings settings;
        try { settings = vm.CaptureSettings(); }
        catch (Exception error) { MessageBox.Show(this, error.Message, "설정 확인"); return Task.CompletedTask; }
        if (MessageBox.Show(this, $"{paths.Length}개 이미지\n출력: {settings.Output}\n\n업스케일링을 시작할까요?", "작업 확인", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return Task.CompletedTask;
        busy = true;
        try
        {
            progress = new(worker, paths, settings) { Owner = this }; progress.ShowDialog();
            lastResult = progress.LastOutput ?? lastResult; vm.Status = progress.Summary;
            vm.EngineStatus = "Worker 대기 · 다음 실행 시 자동 준비";
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
        vm.Settings.TreeWidth = TreeColumn.ActualWidth; vm.Settings.ThumbnailHeight = ThumbnailRow.ActualHeight;
        try { if (!diagnostic) vm.Settings.Save(); } catch (Exception error) { AppLog.Write(error.ToString()); }
        try { await worker.DisposeAsync(); } catch (Exception error) { AppLog.Write(error.ToString()); }
        closeReady = true; _ = Dispatcher.BeginInvoke(Close);
    }
}
