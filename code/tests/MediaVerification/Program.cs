using BigBlueFish.Windows;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

static class Program
{
    static int checks;
    static void Check(bool value,string description){if(!value)throw new Exception(description);checks++;Console.WriteLine("PASS "+description);}
    static void Pump(int ms){var f=new DispatcherFrame();var t=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(ms)};t.Tick+=(_,_)=>{t.Stop();f.Continue=false;};t.Start();Dispatcher.PushFrame(f);}
    static void Wait(Func<bool> condition,int seconds){var w=Stopwatch.StartNew();while(!condition()&&w.Elapsed.TotalSeconds<seconds)Pump(15);Check(condition(),"condition within "+seconds+" seconds");}
    [STAThread] static int Main(string[] args)
    {
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        var image=new CompanionSpriteImage{Width=480,Height=270,Stretch=Stretch.Fill};var window=new Window{Title="1.1.1 animation verification",Width=520,Height=330,Content=image,Background=Brushes.Lime};
        using var player=new CompanionVideoPlayer(image);string? error=null;player.Failed+=e=>error=e;bool ended=false;
        if(args.Contains("--region-check"))
        {
            try
            {
                window.Show();Pump(100);string regionFile=Path.Combine(AppContext.BaseDirectory,"assets/fish/待机呼吸休闲.webm");
                player.Play(regionFile,false,()=>{});Wait(()=>player.Frames>10||error!=null,12);Check(error==null,"region check uses actual GPU player");CompositionRegionVerification.Check(image,false);
                player.Play(regionFile,true,()=>{});Wait(()=>player.Frames>10||error!=null,12);Check(error==null,"mirrored region uses actual GPU player");CompositionRegionVerification.Check(image,true);
                image.Width=360;image.Height=202.5;var before=player.Frames;Wait(()=>player.Frames>before+2||error!=null,5);CompositionRegionVerification.Check(image,true);
                Console.WriteLine("RESULT actual GPU HWND region verified at normal, mirrored and resized layouts");return 0;
            }
            finally{player.Dispose();window.Close();Pump(200);app.Shutdown();}
        }
        if(args.Contains("--bench-only")||args.Contains("--baseline"))
        {window.WindowStyle=WindowStyle.None;window.AllowsTransparency=true;window.Background=Brushes.Transparent;window.ShowInTaskbar=false;window.ShowActivated=false;window.Topmost=true;}
        if(args.Contains("--bench-only"))
        {
            window.Show();Pump(100);player.Play(Path.Combine(AppContext.BaseDirectory,"assets/fish/待机呼吸休闲.webm"),false,()=>{});
            Wait(()=>player.PositionSeconds>=2||error!=null,12);Check(error==null&&player.Backend.StartsWith("GPU"),"isolated GPU playback");
            var proc=Process.GetCurrentProcess();var cpu=proc.TotalProcessorTime;var before=player.RenderedFrames;var clock=Stopwatch.StartNew();Pump(6000);proc.Refresh();
            Console.WriteLine($"GPU BENCH fps={(player.RenderedFrames-before)/clock.Elapsed.TotalSeconds:F2} cpu={(proc.TotalProcessorTime-cpu).TotalSeconds/clock.Elapsed.TotalSeconds/Environment.ProcessorCount*100:F3}% memory={proc.WorkingSet64/1048576d:F1}MiB");
            player.Dispose();window.Close();Pump(300);app.Shutdown();return 0;
        }
        if(args.Contains("--receiver"))
        {
            var button=new Button{Content="Underlying window: 0 clicks",Background=Brushes.Lime};int clicks=0;
            window.Title="Underlying input receiver";window.Left=100;window.Top=100;window.Width=600;window.Height=400;window.Content=button;
            button.Click+=(_,_)=>{clicks++;button.Content="Underlying window: "+clicks+" clicks";};window.Show();Pump(180000);window.Close();app.Shutdown();return 0;
        }
        if(args.Contains("--interactive"))
        {
            window.Left=100;window.Top=100;window.WindowStyle=WindowStyle.None;window.AllowsTransparency=true;window.Background=Brushes.Transparent;
            int clicks=0;image.MouseLeftButtonDown+=(_,e)=>{clicks++;window.Title="Fish input clicks="+clicks;if(e.ClickCount==1)window.DragMove();};
            image.MouseRightButtonDown+=(_,_)=>{image.ContextMenu=new ContextMenu();image.ContextMenu.Items.Add(new MenuItem{Header="Input forwarded correctly"});image.ContextMenu.IsOpen=true;};
            window.Show();Pump(100);void Repeat()=>player.Play(Path.Combine(AppContext.BaseDirectory,"assets/fish/待机呼吸休闲.webm"),false,Repeat);Repeat();
            Pump(180000);Console.WriteLine("INPUT CLICKS="+clicks+" error="+error);player.Dispose();window.Close();app.Shutdown();return 0;
        }
        if(args.Contains("--baseline"))
        {
            if(!File.Exists(Path.Combine(AppContext.BaseDirectory,"media/ffmpeg.exe")))throw new InvalidOperationException("Run tools/Setup-Media.ps1 and rebuild this verification project to prepare the historical benchmark.");
            window.Show();Pump(100);
            var type=typeof(Program).Assembly.GetType("BigBlueFish.Windows.LegacyVideoPlayer")!;
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var baseline=Activator.CreateInstance(type,flags,null,new object[]{image},null)!;
            string original=Path.Combine(AppContext.BaseDirectory,"assets/fish/compat/待机呼吸休闲.webm");
            long frames=0;
            Action start=null!;start=()=>type.GetMethod("Play",flags)!.Invoke(baseline,new object?[]{original,false,start,0d,null,1d});
            type.GetEvent("FramePresented",flags)!.AddMethod!.Invoke(baseline,new object[]{(Action<double>)(_=>frames++)});
            start();Pump(2000);var proc=Process.GetCurrentProcess();var child=(Process)type.GetField("decoder",flags)!.GetValue(baseline)!;
            var cpu=proc.TotalProcessorTime;var childCpu=child.TotalProcessorTime;long before=frames;var clock=Stopwatch.StartNew();Pump(6000);proc.Refresh();child.Refresh();
            Console.WriteLine($"LEGACY BENCH fps={(frames-before)/clock.Elapsed.TotalSeconds:F2} cpu={((proc.TotalProcessorTime-cpu)+(child.TotalProcessorTime-childCpu)).TotalSeconds/clock.Elapsed.TotalSeconds/Environment.ProcessorCount*100:F3}% memory={(proc.WorkingSet64+child.WorkingSet64)/1048576d:F1}MiB (parent + decoder)");
            ((IDisposable)baseline).Dispose();window.Close();Pump(300);app.Shutdown();return 0;
        }
        try
        {
            window.Show();Pump(100);
            string file=Path.Combine(AppContext.BaseDirectory,"assets/fish/待机呼吸休闲.webm");
            string[] environmentKeys=["GST_PLUGIN_SYSTEM_PATH_1_0","GST_PLUGIN_PATH_1_0","GST_REGISTRY_1_0","GST_REGISTRY_FORK"];
            var originalEnvironment=environmentKeys.Select(Environment.GetEnvironmentVariable).ToArray();
            var watch=Stopwatch.StartNew();player.Play(file,false,()=>ended=true);Wait(()=>player.PositionSeconds>=.9||error!=null,12);
            Check(error==null,"real bundled decoder succeeds: "+error);Check(player.Backend.StartsWith("GPU"),"real GPU backend: "+player.Backend);
            Check(environmentKeys.Select(Environment.GetEnvironmentVariable).SequenceEqual(originalEnvironment),"multimedia environment preserved for child agents");
            Check(player.OutputWidth<=1280&&player.OutputWidth>=480&&player.OutputHeight==player.OutputWidth*9/16,"DPI-aware output size: "+player.OutputWidth+"x"+player.OutputHeight);
            Check(player.PositionSeconds>.7&&player.PositionSeconds<3,"actual buffer timestamp "+player.PositionSeconds);
            var bmp=(BitmapSource)image.Source;var bytes=new byte[bmp.PixelWidth*bmp.PixelHeight*4];bmp.CopyPixels(bytes,bmp.PixelWidth*4,0);
            Check(bytes.Where((_,i)=>i%4==3).Any(x=>x==0)&&bytes.Where((_,i)=>i%4==3).Any(x=>x==255),"opaque and transparent hit-test regions");
            var snapshot=player.CaptureSnapshotAsync().GetAwaiter().GetResult()!;var rgba=new byte[1280*720*4];snapshot.CopyPixels(rgba,1280*4,0);
            Check(snapshot.PixelWidth==1280&&rgba.Where((_,i)=>i%4!=3).Any(v=>v>0),"diagnostic snapshot contains real GPU-decoded color pixels");
            Check(rgba.Where((_,i)=>i%4==3).Any(v=>v==0)&&rgba.Where((_,i)=>i%4==3).Any(v=>v==255),"GPU-decoded snapshot retains actual transparency");
            player.Paused=true;Pump(250);var position=player.PositionSeconds;var count=player.Frames;Pump(500);Check(player.Frames==count&&player.PositionSeconds==position,"pause stops frame and time advancement");
            player.Paused=false;Wait(()=>player.PositionSeconds>=position+.5,3);Check(player.PositionSeconds>position+.4,"resume advances media time");
            int completions=0;var segmentWatch=Stopwatch.StartNew();player.Play(file,false,()=>completions++,1.5,3,1.5);Wait(()=>completions==1||error!=null,5);Check(error==null&&player.PositionSeconds>=2.9&&player.PositionSeconds<3,$"trimmed segment exact media end: position={player.PositionSeconds}, error={error}");Check(segmentWatch.Elapsed.TotalSeconds>=.9&&segmentWatch.Elapsed.TotalSeconds<2.2,"trimmed 1.5x wall duration");
            var held=player.Frames;Pump(350);Check(player.Frames==held,"completed segment holds last frame");
            player.Play(file,false,()=>completions++);Wait(()=>player.Frames>10,3);player.Play(file,true,()=>completions++);Wait(()=>player.Frames>10,3);Check(((ScaleTransform)image.RenderTransform).ScaleX==-1,"mirror preserved");
            player.Stop();Pump(300);count=player.Frames;Pump(200);Check(player.Frames==count,"cancelled decoder cannot publish stale frames");
            player.ForceSoftware=true;error=null;player.Play(file,false,()=>ended=true);Wait(()=>player.Frames>24||error!=null,8);Check(error==null&&player.Backend.StartsWith("CPU"),"CPU compatibility playback works");Check(player.OutputWidth==640&&player.OutputHeight==360,"CPU uses bounded original 24fps assets");
            player.ForceSoftware=false;error=null;ended=false;watch.Restart();player.Play(file,false,()=>ended=true);Wait(()=>ended||error!=null,14);Check(error==null&&watch.Elapsed.TotalSeconds>9.8&&watch.Elapsed.TotalSeconds<12,"full clip plays complete wall duration");
            player.Play(file,false,()=>{});Wait(()=>player.PositionSeconds>=1,3);var proc=Process.GetCurrentProcess();var cpu=proc.TotalProcessorTime;ulong before=player.RenderedFrames;watch.Restart();Pump(6000);proc.Refresh();
            Console.WriteLine($"BENCH visible-fps={(player.RenderedFrames-before)/watch.Elapsed.TotalSeconds:F2} cpu={(proc.TotalProcessorTime-cpu).TotalSeconds/watch.Elapsed.TotalSeconds/Environment.ProcessorCount*100:F3}% memory={proc.WorkingSet64/1048576d:F1}MiB backend={player.Backend}");
            Check(player.Backend.StartsWith("GPU"),"GPU remains active after trim, rate and switching checks");
            Console.WriteLine($"Media playback: {checks} checks passed.");return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        finally{player.Dispose();window.Close();Pump(300);app.Shutdown();}
    }
}
