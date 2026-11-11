using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Upscaler.Core;
using Upscaler.PythonBridge;
using Upscaler.Wpf;

internal static class Program
{
    static int passed, failed;
    static readonly string Root = FindRoot();
    static string FindRoot() { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d is not null && !File.Exists(Path.Combine(d.FullName,"python","worker.py"))) d=d.Parent; return d!.FullName; }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static async Task Throws<T>(Func<Task> action) where T:Exception { try { await action(); } catch(T) {return;} throw new Exception($"Expected {typeof(T).Name}"); }
    static async Task Test(string name, Func<Task> test) { try { await test(); passed++; Console.WriteLine("PASS "+name); } catch(Exception e) { failed++;Console.WriteLine("FAIL "+name+"\n"+e); } }
    static WorkerOptions FaultOptions(string script="fault_worker.py") => new() { WorkerPath=Path.Combine(Root,"tests","fixtures",script),CancelTimeout=TimeSpan.FromMilliseconds(250),StartupTimeout=TimeSpan.FromSeconds(5),JobTimeout=TimeSpan.FromSeconds(8)};
    static string Temp() { var p=Path.Combine(Root,"artifacts","tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(p);return p; }
    static void MakeImage(string path, int width=8, int height=6) { var pixels=new byte[width*height*4]; for(int y=0;y<height;y++) for(int x=0;x<width;x++){var i=(y*width+x)*4;pixels[i]=(byte)(70+x*150/width);pixels[i+1]=(byte)(60+y*150/height);pixels[i+2]=(byte)(40+x*80/width);pixels[i+3]=255;} var image=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using var f=File.Create(path);png.Save(f); }

    [STAThread]
    static int Main()
    {
        var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        app.Dispatcher.BeginInvoke(async()=> { try { if (Environment.GetCommandLineArgs().Contains("--render-ui")) await RenderUi(); else if (Environment.GetCommandLineArgs().Contains("--ui-integration")) await UiIntegration(); else await Run(); } catch(Exception e) {failed++;Console.WriteLine(e);} finally {Console.WriteLine($"{passed} passed, {failed} failed");app.Shutdown(failed==0?0:1);} });
        return app.Run();
    }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool IsWindowEnabled(IntPtr window);

    static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject
    {
        for (var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);
            if(child is T item) yield return item;
            foreach(var nested in Descendants<T>(child)) yield return nested;
        }
    }
    static async Task UiIntegration()
    {
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Upscaler.Wpf;component/Themes/Dark.xaml") });
        var window=new MainWindow(true);window.Show();
        var vm=(MainViewModel)window.DataContext;
        var dest=Temp();var input=Path.Combine(dest,"ui sample.png");MakeImage(input);
        try
        {
            await Test("UI preview, zoom and fit",async()=> {
                await vm.NavigateAsync(dest);vm.Selected=vm.Files.First();await vm.LoadPreviewAsync(vm.Selected);
                window.UpdateLayout();var image=(System.Windows.Controls.Image)window.FindName("PreviewImage");var width=image.Width;
                Descendants<Button>(window).Single(b=>b.Content?.ToString()=="+").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(image.Width>width,"zoom did not enlarge");
                Descendants<Button>(window).Single(b=>b.Content?.ToString()=="화면 맞춤").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(Math.Abs(image.Width-width)<1,"fit did not restore");
            });
            await Test("Native modal disables owner and restores after real inference",async()=> {
                await using var worker=new PythonWorker(Root);await worker.StartAsync();
                var modal=new ProgressWindow(worker,[input],new Settings {Output=dest,Device="cpu"}) {Owner=window};
                var ownerHandle=new WindowInteropHelper(window).Handle;var sawDisabled=false;var timedOut=false;
                var started=DateTime.UtcNow;
                var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(30)};
                timer.Tick+=(_,_)=> {sawDisabled|=!IsWindowEnabled(ownerHandle);if(modal.IsFinished){timer.Stop();modal.Close();}else if(DateTime.UtcNow-started>TimeSpan.FromSeconds(90)){timedOut=true;timer.Stop();Application.Current.Shutdown(1);}};
                timer.Start();modal.ShowDialog();timer.Stop();
                Check(!timedOut&&sawDisabled,"owner was not disabled");Check(IsWindowEnabled(ownerHandle),"owner stayed disabled");
                Check(modal.Summary.Contains("완료 1")&&modal.LastOutput is not null&&File.Exists(modal.LastOutput),modal.Summary);
            });
            await Test("Native modal survives worker crash and continues",async()=> {
                var output=Temp();await using var worker=new PythonWorker(Root,FaultOptions());
                var modal=new ProgressWindow(worker,["crash.png","ok.png"],new Settings{Output=output}){Owner=window};
                var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(30)};
                timer.Tick+=(_,_)=>{if(modal.IsFinished){timer.Stop();modal.Close();}};
                timer.Start();modal.ShowDialog();timer.Stop();
                Check(modal.Summary.Contains("완료 1")&&modal.Summary.Contains("실패 1"),modal.Summary);
                Check(!Directory.EnumerateFiles(output,".upscaler-*").Any(),"crash partial file");
            });
            await Test("Native modal waits for forced cancellation cleanup",async()=> {
                var output=Temp();await using var worker=new PythonWorker(Root,FaultOptions());using var cts=new CancellationTokenSource();
                worker.Progress+=_=>cts.Cancel();
                var modal=new ProgressWindow(worker,["hang.png","never.png"],new Settings{Output=output},cts.Token){Owner=window};
                var ownerHandle=new WindowInteropHelper(window).Handle;var stayedDisabled=true;
                var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(30)};
                timer.Tick+=(_,_)=>{if(modal.IsFinished){timer.Stop();modal.Close();}else stayedDisabled &= !IsWindowEnabled(ownerHandle);};
                timer.Start();modal.ShowDialog();timer.Stop();
                Check(stayedDisabled&&modal.Summary.Contains("취소 2"),modal.Summary);
                Check(!Directory.EnumerateFiles(output,".upscaler-*").Any(),"cancel partial file");
            });
        }
        finally
        {
            window.Close();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(!window.IsVisible,"window did not close cleanly");
        }
    }
    static async Task RenderUi()
    {
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Upscaler.Wpf;component/Themes/Dark.xaml") });
        var window = new MainWindow(true);
        var vm = (MainViewModel)window.DataContext;
        var fixtures = Path.Combine(Root, "artifacts", "ui-fixtures"); Directory.CreateDirectory(fixtures);
        MakeImage(Path.Combine(fixtures, "sample.png"),512,384);
        await vm.NavigateAsync(fixtures);
        vm.Selected = vm.Files.First();
        await vm.LoadPreviewAsync(vm.Selected);
        var content = (FrameworkElement)window.Content;
        content.Margin = new Thickness(0);
        // Render the actual WPF visual at each pixel density without changing OS display settings.
        var output = Path.Combine(Root, "artifacts", "ui"); Directory.CreateDirectory(output);
        foreach (var size in new[] { new Size(1100,700), new Size(1440,900) })
        {
            foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0 })
            {
                content.Width = size.Width - 32; content.Height = size.Height - 32;
                content.Measure(new Size(content.Width,content.Height));
                content.Arrange(new Rect(0,0,content.Width,content.Height)); content.UpdateLayout();
                await vm.LoadThumbnailAsync(vm.Files.First()); content.UpdateLayout();
                var visual = new DrawingVisual();
                using (var draw = visual.RenderOpen())
                {
                    draw.DrawRectangle(new SolidColorBrush(Color.FromRgb(20,24,32)),null,new Rect(size));
                    draw.DrawRectangle(new VisualBrush(content),null,new Rect(16,16,content.Width,content.Height));
                }
                var bitmap = new RenderTargetBitmap((int)(size.Width*scale),(int)(size.Height*scale),96*scale,96*scale,PixelFormats.Pbgra32);
                bitmap.Render(visual);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                var path = Path.Combine(output,$"layout-{size.Width}x{size.Height}-{scale*100:F0}.png");
                using var file = File.Create(path); encoder.Save(file);
                Check(bitmap.PixelWidth==(int)(size.Width*scale),"DPI raster size");
                passed++;Console.WriteLine("PASS render "+Path.GetFileName(path));
            }
        }
        vm.Stop(); window.Close();
    }
    static async Task Run()
    {
        await Test("Settings roundtrip and invalid fallback",()=> {
            var p=Path.Combine(Temp(),"settings.json");var value=new Settings {Output=Temp(),Device="cpu",Tile=128,TreeWidth=333};value.Save(p);
            var read=Settings.Load(p);Check(read.Tile==128&&read.TreeWidth==333,"roundtrip");
            File.WriteAllText(p,"{\"Device\":\"invalid\"}");Check(Settings.Load(p).Device=="auto","invalid semantic settings");
            File.WriteAllText(p,"broken");Check(Settings.Load(p).Tile==0,"corrupt settings");return Task.CompletedTask;
        });
        await Test("LRU evicts oldest and stays bounded",()=> {
            var cache=new LruCache<int,string>(3);cache.Put(1,"a");cache.Put(2,"b");cache.Put(3,"c");cache.TryGet(1,out _);cache.Put(4,"d");
            Check(!cache.TryGet(2,out _),"wrong eviction");for(int i=0;i<10000;i++)cache.Put(i,"x");Check(cache.Count==3,"unbounded cache");return Task.CompletedTask;
        });
        await Test("Folder snapshot excludes subdirectories and remains fixed",()=> {
            var p=Temp();File.WriteAllText(Path.Combine(p,"b.PNG"),"");File.WriteAllText(Path.Combine(p,"a.jpg"),"");File.WriteAllText(Path.Combine(p,"x.txt"),"");Directory.CreateDirectory(Path.Combine(p,"nested"));File.WriteAllText(Path.Combine(p,"nested","hidden.png"),"");
            var list=Images.Scan(p);File.WriteAllText(Path.Combine(p,"new.png"),"");Check(list.Length==2&&Path.GetFileName(list[0])=="a.jpg","scan snapshot");return Task.CompletedTask;
        });
        await Test("Latest navigation wins even if old scan ignores cancellation",async()=> {
            var old=new TaskCompletionSource<string[]>();var fresh=new TaskCompletionSource<string[]>();
            var vm=new MainViewModel((p,t)=>p.EndsWith("old")?old.Task:fresh.Task,new Settings());
            var a=vm.NavigateAsync(Path.Combine(Root,"old"));var b=vm.NavigateAsync(Path.Combine(Root,"fresh"));
            fresh.SetResult([Path.Combine(Root,"fresh","new.png")]);Check(await b,"fresh navigation");old.SetResult(["old.png"]);Check(!await a,"stale accepted");Check(vm.Folder.EndsWith("fresh")&&vm.Files[0].Name=="new.png","stale overwrite");vm.Stop();
        });
        await Test("Navigation failure preserves previous folder",async()=> {
            var vm=new MainViewModel((p,t)=>p.EndsWith("bad")?Task.FromException<string[]>(new UnauthorizedAccessException("denied")):Task.FromResult(new[]{"valid.png"}),new Settings());
            await vm.NavigateAsync(Path.Combine(Root,"good"));var previous=vm.Folder;Check(!await vm.NavigateAsync(Path.Combine(Root,"bad")),"accepted inaccessible");Check(vm.Folder==previous&&vm.Address==previous,"lost old folder");vm.Stop();
        });
        await Test("Large folder does not eagerly decode thumbnails",async()=> {
            var vm=new MainViewModel((p,t)=>Task.FromResult(Enumerable.Range(0,10000).Select(i=>Path.Combine(p,$"{i}.png")).ToArray()),new Settings());
            await vm.NavigateAsync(Root);Check(vm.Files.Count==10000&&vm.Files.All(x=>x.Thumbnail is null)&&vm.CachedThumbnailCount==0,"eager thumbnail memory");vm.Filter="9999";Check(vm.Files.Count==1,"filter");vm.Stop();
        });
        await Test("Large preview is bounded and original dimensions retained",async()=> {
            var dest=Temp();var path=Path.Combine(dest,"large.png");MakeImage(path,5000,3000);
            var vm=new MainViewModel();var decoded=await vm.DecodeAsync(path,4096,CancellationToken.None);
            Check(decoded.Dimensions=="5000x3000"&&decoded.Image.PixelWidth==4096&&decoded.Image.PixelHeight<=4096,"unbounded preview");vm.Stop();
        });
        await Test("Invalid tile text is rejected",()=> {
            var vm=new MainViewModel(settings:new Settings());vm.TileText="nonsense";try {vm.CaptureSettings();throw new Exception("accepted");}catch(ArgumentException){}vm.Stop();return Task.CompletedTask;
        });
        await Test("Batch continues after per-file failure",async()=> {
            var engine=new FakeEngine();var runner=new BatchRunner(engine);var result=await runner.RunAsync(["ok","bad","ok2"],new Settings(),CancellationToken.None);
            Check(result.Completed==2&&result.Failed==1&&result.Cancelled==0,"batch counts");Check(engine.Calls==3,"stopped on failure");
        });
        await Test("Batch stops remaining files on cancellation",async()=> {
            using var cts=new CancellationTokenSource();var engine=new FakeEngine {After=()=>cts.Cancel()};var runner=new BatchRunner(engine);var result=await runner.RunAsync(["ok","next","last"],new Settings(),cts.Token);
            Check(result.Completed==1&&result.Cancelled==2&&engine.Calls==1,"cancelled count");
        });
        await Test("Hung worker is killed and its temporary output removed",async()=> {
            var dest=Temp();await using var worker=new PythonWorker(Root,FaultOptions());using var cts=new CancellationTokenSource();
            var progress=new TaskCompletionSource();worker.Progress+=_=>progress.TrySetResult();
            var run=worker.UpscaleAsync("hang.png",new Settings{Output=dest},cts.Token);await progress.Task.WaitAsync(TimeSpan.FromSeconds(10));cts.Cancel();
            await Throws<OperationCanceledException>(async()=>await run);Check(!Directory.EnumerateFiles(dest,".upscaler-*").Any(),"partial remains");
            var next=await worker.UpscaleAsync("ok.png",new Settings{Output=dest},CancellationToken.None);Check(next.GetProperty("type").GetString()=="completed","restart failed");
        });
        await Test("Worker crash does not poison restart",async()=> {
            var dest=Temp();await using var worker=new PythonWorker(Root,FaultOptions());await Throws<IOException>(async()=>await worker.UpscaleAsync("crash.png",new Settings{Output=dest},CancellationToken.None));
            Check(!Directory.EnumerateFiles(dest,".upscaler-*").Any(),"crash cleanup");var response=await worker.UpscaleAsync("ok.png",new Settings{Output=dest},CancellationToken.None);Check(response.GetProperty("type").GetString()=="completed","restart");
        });
        await Test("Job timeout cancels and removes temporary output",async()=> {
            var dest=Temp();await using var worker=new PythonWorker(Root,FaultOptions() with {JobTimeout=TimeSpan.FromMilliseconds(200)});
            await Throws<TimeoutException>(async()=>await worker.UpscaleAsync("hang.png",new Settings{Output=dest},CancellationToken.None));
            Check(!Directory.EnumerateFiles(dest,".upscaler-*").Any(),"timeout partial");Check(worker.ProcessId is null,"timeout orphan");
        });
        await Test("Startup timeout stops the child",async()=> {
            await using var worker=new PythonWorker(Root,FaultOptions("slow_worker.py") with {StartupTimeout=TimeSpan.FromMilliseconds(150)});
            await Throws<TimeoutException>(()=>worker.StartAsync());Check(worker.ProcessId is null,"startup orphan");
        });
        await Test("Dispose cancels active job and waits for cleanup",async()=> {
            var dest=Temp();var worker=new PythonWorker(Root,FaultOptions());var progress=new TaskCompletionSource();worker.Progress+=_=>progress.TrySetResult();
            var run=worker.UpscaleAsync("hang.png",new Settings{Output=dest},CancellationToken.None);await progress.Task.WaitAsync(TimeSpan.FromSeconds(10));await worker.DisposeAsync();
            await Throws<OperationCanceledException>(async()=>await run);Check(!Directory.EnumerateFiles(dest,".upscaler-*").Any(),"dispose partial");Check(worker.ProcessId is null,"orphan process");
        });
        await Test("Real C# bridge CPU/CUDA and preview decoder",async()=> {
            var dest=Temp();var input=Path.Combine(dest,"한글 sample.png");MakeImage(input);await using var worker=new PythonWorker(Root);
            await worker.StartAsync();var devices=worker.DeviceInfo.GetProperty("cuda").GetBoolean()?new[]{"cpu","cuda"}:new[]{"cpu"};
            foreach(var device in devices) {var response=await worker.UpscaleAsync(input,new Settings{Output=dest,Device=device},CancellationToken.None);Check(response.GetProperty("type").GetString()=="completed",response.ToString());Check(response.GetProperty("output_width").GetInt32()==32,"scale");}
            var vm=new MainViewModel();var decoded=await vm.DecodeAsync(input,160,CancellationToken.None);Check(decoded.Dimensions=="8x6"&&decoded.Image.IsFrozen,"decoder");vm.Stop();
        });
    }
    sealed class FakeEngine:IUpscaleEngine
    {
        public event Action<JsonElement>? Progress {add{} remove{}}
        public int Calls;public Action? After;
        public Task<JsonElement> UpscaleAsync(string input,Settings settings,CancellationToken token)
        {Calls++;After?.Invoke();return Task.FromResult(JsonSerializer.SerializeToElement(input=="bad"?new {type="failed",output="",message="bad image"}:new {type="completed",output=input,message=""}));}
    }
}
