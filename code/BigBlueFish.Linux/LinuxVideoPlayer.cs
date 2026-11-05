using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
namespace BigBlueFish.Windows;
internal sealed class LinuxVideoPlayer(Image image) : IDisposable
{
    readonly SemaphoreSlim serial=new(1);
    WriteableBitmap? bitmap;
    CancellationTokenSource? current;
    long generation;
    bool disposed,hardwareFailed;
    internal long Frames {get;private set;}
    internal double PositionSeconds {get;private set;}
    internal string Backend {get;private set;}="未运行";
    internal double LastReadMs,LastUiMs,LastCopyMs,LastShapeMs;
    internal volatile bool Paused;
    internal event Action<double>? FramePresented;
    internal event Action<byte[],int,int,bool>? PixelsPresented;
    internal event Action<string>? Failed;
    internal void Play(string path,bool mirror,Action completed,double startSeconds=0,double? endSeconds=null,double rate=1)
    {
        if(!double.IsFinite(startSeconds)||startSeconds<0||!double.IsFinite(rate)||rate<=0||endSeconds.HasValue&&(!double.IsFinite(endSeconds.Value)||endSeconds<=startSeconds))throw new ArgumentOutOfRangeException(nameof(startSeconds));
        current?.Cancel();current?.Dispose();if(disposed)return;
        current=new();var token=current.Token;var id=++generation;
        Frames=0;PositionSeconds=startSeconds;
        image.RenderTransformOrigin=new(.5,.5,RelativeUnit.Relative);image.RenderTransform=new Avalonia.Media.ScaleTransform(mirror?-1:1,1);
        double pixels=image.Width*(TopLevel.GetTopLevel(image)?.RenderScaling??1);
        int width=Math.Clamp((int)Math.Ceiling((double.IsFinite(pixels)?pixels:640)/128)*128,256,1280);
        _=Task.Run(async()=>
        {
            try
            {
                await serial.WaitAsync(token);
                try
                {
                    CompanionMedia.Initialize();bool hardware=!hardwareFailed&&CompanionMedia.HardwareAvailable;
                    while(true)
                    {
                        try{await Decode(path,hardware,width,id,token,mirror,completed,startSeconds,endSeconds,rate);break;}
                        catch(Exception e)when(hardware&&!token.IsCancellationRequested)
                        {hardwareFailed=true;hardware=false;DiagnosticLog.Write(DiagnosticEvent.AnimationFailed, state: "hardware_fallback", error: e);}
                    }
                }
                finally{serial.Release();}
            }
            catch(OperationCanceledException){}
            catch(Exception e){await Dispatcher.UIThread.InvokeAsync(()=>{if(id==generation&&!disposed)Failed?.Invoke(e.Message);});}
        });
    }
    async Task Decode(string file,bool hardware,int width,long id,CancellationToken token,bool mirror,Action completed,double start,double? end,double rate)
    {
        if(!hardware){file=Path.Combine(Path.GetDirectoryName(file)!,"compat",Path.GetFileName(file));width=640;}
        int height=width*9/16;
        using var stream=new CompanionMediaStream(file,hardware,width,height,start,end,rate);
        Backend=stream.Description;stream.SetPaused(Paused);bool wasPaused=Paused;long count=0;byte[] pixels=new byte[width*height*4];
        while(true)
        {
            token.ThrowIfCancellationRequested();
            if(wasPaused!=Paused){wasPaused=Paused;stream.SetPaused(Paused);}
            if(Paused){await Task.Delay(40,token);continue;}
            var clock=Stopwatch.StartNew();using var frame=stream.Read();LastReadMs=clock.Elapsed.TotalMilliseconds;
            if(frame==null){if(stream.Ended)break;continue;}
            for(int y=0;y<height;y++)Marshal.Copy(frame.Data+y*frame.Stride,pixels,y*width*4,width*4);
            for(int i=3;i<pixels.Length;i+=4)if(pixels[i]<8)pixels[i]=0;
            clock.Restart();
            await Dispatcher.UIThread.InvokeAsync(()=>
            {
                if(id!=generation||disposed)return;
                var copy=Stopwatch.StartNew();
                if(bitmap==null||bitmap.PixelSize.Width!=width||bitmap.PixelSize.Height!=height)
                {bitmap?.Dispose();bitmap=new(new PixelSize(width,height),new Vector(96,96),PixelFormat.Bgra8888,AlphaFormat.Unpremul);image.Source=bitmap;}
                using(var target=bitmap.Lock())for(int y=0;y<height;y++)Marshal.Copy(pixels,y*width*4,target.Address+y*target.RowBytes,width*4);
                LastCopyMs=copy.Elapsed.TotalMilliseconds;copy.Restart();image.InvalidateVisual();Frames=++count;PositionSeconds=frame.Seconds;
                PixelsPresented?.Invoke(pixels,width,height,mirror);LastShapeMs=copy.Elapsed.TotalMilliseconds;FramePresented?.Invoke(PositionSeconds);
            });
            LastUiMs=clock.Elapsed.TotalMilliseconds;
        }
        if(count==0)throw new IOException("动画没有生成可显示的帧。");
        await Dispatcher.UIThread.InvokeAsync(()=>{if(id==generation&&!disposed)completed();});
    }
    public void Dispose(){if(disposed)return;disposed=true;generation++;current?.Cancel();current?.Dispose();bitmap?.Dispose();}
}
