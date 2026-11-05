using System;
using System.IO;
using System.Runtime.Intrinsics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BigBlueFish.Windows;

internal sealed class CompanionVideoPlayer : IDisposable
{
    readonly Image image;
    readonly SemaphoreSlim serial = new(1);
    WriteableBitmap? bitmap;
    CompanionComposition? composition;
    CancellationTokenSource? cancellation;
    volatile bool paused;
    long generation;
    bool disposed, hardwareFailed;
    string? displayedFile;
    double displayedSeconds;
    internal long Frames { get; private set; }
    internal ulong RenderedFrames {get;private set;}
    internal double PositionSeconds { get; private set; }
    internal double PlaybackRate { get; private set; } = 1;
    internal double StartSeconds { get; private set; }
    internal string Backend { get; private set; } = "未运行";
    internal int OutputWidth { get; private set; }
    internal int OutputHeight { get; private set; }
    internal bool ForceSoftware { get; set; }
    internal bool Paused { get => paused; set => paused=value; }
    internal event Action<string>? Failed;
    internal event Action<double>? FramePresented;
    internal CompanionVideoPlayer(Image target) => image=target;
    internal void Play(string file,bool mirrored,Action completed,double startSeconds=0,double? endSeconds=null,double playbackRate=1)
    {
        if(!double.IsFinite(startSeconds)||startSeconds<0||!double.IsFinite(playbackRate)||playbackRate<=0||endSeconds.HasValue&&(!double.IsFinite(endSeconds.Value)||endSeconds<=startSeconds))
            throw new ArgumentOutOfRangeException(nameof(startSeconds));
        Stop();if(disposed)return;
        image.RenderTransformOrigin=new(.5,.5);image.RenderTransform=mirrored?new ScaleTransform(-1,1):Transform.Identity;
        Frames=0;RenderedFrames=0;PositionSeconds=StartSeconds=startSeconds;PlaybackRate=playbackRate;
        var pixels=image.Width*VisualTreeHelper.GetDpi(image).DpiScaleX;
        int width=Math.Clamp((int)Math.Ceiling((double.IsFinite(pixels)?pixels:640)/128)*128,256,1280),height=width*9/16;
        cancellation=new();var token=cancellation.Token;var id=++generation;
        _=Task.Run(async ()=>
        {
            try
            {
                await serial.WaitAsync(token);
                try
                {
                    CompanionMedia.Initialize();
                    bool hardware=!ForceSoftware&&!hardwareFailed&&CompanionMedia.HardwareAvailable;
                    while(true)
                    {
                        try { await Decode(file,hardware,width,height,mirrored,id,token,completed,startSeconds,endSeconds,playbackRate);break; }
                        catch(Exception ex) when(hardware&&!token.IsCancellationRequested)
                        {
                            hardwareFailed=true;hardware=false;
                            DiagnosticLog.Write(DiagnosticEvent.AnimationFailed, state: "hardware_fallback", error: ex);
                        }
                    }
                }
                finally {serial.Release();}
            }
            catch(OperationCanceledException) { }
            catch(Exception ex) {await image.Dispatcher.InvokeAsync(()=>{if(id==generation&&!disposed)Failed?.Invoke(ex.Message);});}
        });
    }
    async Task Decode(string file,bool hardware,int width,int height,bool mirror,long id,CancellationToken token,Action completed,double start,double? end,double rate)
    {
        if(!hardware)
        {
            file=Path.Combine(Path.GetDirectoryName(file)!,"compat",Path.GetFileName(file));width=640;height=360;
            if(!File.Exists(file))throw new IOException("兼容动画素材缺失："+Path.GetFileName(file));
        }
        if(hardware){width=320;height=180;}
        using var stream=new CompanionMediaStream(file,hardware,width,height,start,end,rate,composition:hardware);
        token.ThrowIfCancellationRequested();
        Backend=stream.Description;OutputWidth=hardware?1280:width;OutputHeight=hardware?720:height;
        await image.Dispatcher.InvokeAsync(()=>
        {
            if(id!=generation||disposed)return;
            if(hardware){composition??=new(image);composition.Attach(stream.Swapchain,mirror);}
            else composition?.Hide();
        });
        stream.SetPaused(paused);bool wasPaused=paused;long presented=0;
        while(true)
        {
            token.ThrowIfCancellationRequested();
            if(wasPaused!=paused){wasPaused=paused;stream.SetPaused(paused);}
            if(paused){await Task.Delay(40,token);continue;}
            using var frame=stream.Read();
            if(frame==null){if(stream.Ended)break;continue;}
            await image.Dispatcher.InvokeAsync(()=>
            {
                if(id!=generation||disposed)return;
                if(hardware&&image is CompanionSpriteImage sprite)
                {
                    if(bitmap==null||bitmap.PixelWidth!=frame.Width){Present(frame);}
                    sprite.InputWidth=frame.Width;sprite.InputHeight=frame.Height;sprite.InputPixels=frame.ManagedPixels;
                    composition!.Present(frame);
                }
                else{if(image is CompanionSpriteImage softwareSprite)softwareSprite.InputPixels=null;Present(frame);}
                Frames=++presented;PositionSeconds=frame.Seconds;FramePresented?.Invoke(PositionSeconds);
                displayedFile=file;displayedSeconds=frame.Seconds;
            },System.Windows.Threading.DispatcherPriority.Render,token);
            if(hardware){RenderedFrames=stream.RenderedFrames;await Task.Delay(45,token);}
        }
        if(presented==0)throw new IOException("动画没有生成可显示的帧。");
        await image.Dispatcher.InvokeAsync(()=>{if(id==generation&&!disposed)completed();});
    }
    unsafe void Present(CompanionMediaFrame frame)
    {
        if(bitmap==null||bitmap.PixelWidth!=frame.Width||bitmap.PixelHeight!=frame.Height)
        {bitmap=new(frame.Width,frame.Height,96,96,PixelFormats.Bgra32,null);image.Source=bitmap;}
        bitmap.Lock();
        try
        {
            for(int y=0;y<frame.Height;y++)
            {
                byte* row=(byte*)bitmap.BackBuffer+y*bitmap.BackBufferStride;
                uint* source=(uint*)((byte*)frame.Data+y*frame.Stride);uint* target=(uint*)row;int x=0;
                if(Vector256.IsHardwareAccelerated)
                    for(;x<=frame.Width-8;x+=8)
                    {
                        var pixels=Vector256.Load(source+x);
                        var transparent=Vector256.LessThan(Vector256.ShiftRightLogical(pixels,24),Vector256.Create(8u));
                        (pixels&~(transparent&Vector256.Create(0xff000000u))).Store(target+x);
                    }
                for(;x<frame.Width;x++){uint p=source[x];target[x]=(p>>24)<8?p&0x00ffffff:p;}
            }
            bitmap.AddDirtyRect(new Int32Rect(0,0,frame.Width,frame.Height));
        }
        finally {bitmap.Unlock();}
    }
    internal void Stop(){generation++;cancellation?.Cancel();cancellation?.Dispose();cancellation=null;}
    internal Task<BitmapSource?> CaptureSnapshotAsync()
    {
        if(!Backend.StartsWith("GPU")||displayedFile==null)
        {var copy=bitmap?.Clone();copy?.Freeze();return Task.FromResult<BitmapSource?>(copy);}
        string file=displayedFile;double seconds=displayedSeconds;
        // Diagnostic capture only: ordinary playback never downloads video pixels.
        return Task.Run<BitmapSource?>(()=>
        {
            using var capture=new CompanionMediaStream(file,true,1280,720,seconds,null,1);
            capture.SetPaused(false);
            var deadline=DateTime.UtcNow.AddSeconds(5);
            while(DateTime.UtcNow<deadline)
            {
                using var frame=capture.Read();if(frame==null){if(capture.Ended)break;continue;}
                var pixels=new byte[frame.Width*frame.Height*4];
                for(int y=0;y<frame.Height;y++)Marshal.Copy(frame.Data+y*frame.Stride,pixels,y*frame.Width*4,frame.Width*4);
                var result=BitmapSource.Create(frame.Width,frame.Height,96,96,PixelFormats.Bgra32,null,pixels,frame.Width*4);result.Freeze();return result;
            }
            throw new IOException("未能取得动画截图。");
        });
    }
    internal void RenderSnapshot(RenderTargetBitmap target,FrameworkElement layout,BitmapSource? snapshot)
    {
        if(!Backend.StartsWith("GPU")||snapshot==null||!image.IsVisible){target.Render(layout);return;}
        // The GPU surface is a separate HWND, so draw its real snapshot explicitly.
        var visual=new DrawingVisual();
        using(var drawing=visual.RenderOpen())
        {
            drawing.DrawRectangle(new VisualBrush(layout),null,new Rect(layout.RenderSize));
            var transform=image.TransformToAncestor(layout);
            var origin=transform.Transform(new Point());var x=transform.Transform(new Point(1,0));var y=transform.Transform(new Point(0,1));
            drawing.PushTransform(new MatrixTransform(new Matrix(x.X-origin.X,x.Y-origin.Y,y.X-origin.X,y.Y-origin.Y,origin.X,origin.Y)));
            drawing.DrawImage(snapshot,new Rect(0,0,image.ActualWidth,image.ActualHeight));drawing.Pop();
        }
        target.Render(visual);
    }
    public void Dispose(){if(disposed)return;disposed=true;Stop();composition?.Dispose();composition=null;}
}
