using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;

namespace BigBlueFish.Windows;

// The native surface renders the GPU swapchain. WPF retains input routing and window ownership.
internal sealed class CompanionComposition : IDisposable
{
    readonly Image image;
    readonly Window owner;
    readonly InputCallback inputCallback;
    IntPtr handle;
    bool mirrored, ready;
    Rect lastBounds=Rect.Empty;
    bool lastMirror;
    int lastVisible=-1;
    internal CompanionComposition(Image image)
    {
        this.image=image;owner=Window.GetWindow(image)??throw new InvalidOperationException("动画窗口尚未连接。");
        inputCallback=Input;Check(Create(new WindowInteropHelper(owner).Handle,inputCallback,out handle));
        owner.LocationChanged+=Layout;owner.SizeChanged+=Layout;owner.IsVisibleChanged+=VisibilityChanged;image.LayoutUpdated+=Layout;
    }
    static void Check(int hr){if(hr<0)Marshal.ThrowExceptionForHR(hr);}
    void Input(uint message,uint buttons)
    {
        var sprite=image as CompanionSpriteImage;
        try
        {
            if(message==0x215){sprite?.CancelNativeCapture();return;}
            if(sprite!=null){sprite.IsNativeInput=true;sprite.NativeLeftPressed=(buttons&1)!=0;}
            bool down=message is 0x201 or 0x203 or 0x204 or 0x206;
            bool up=message is 0x202 or 0x205;
            if(down||up)
            {
                var args=new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,message>=0x204?MouseButton.Right:MouseButton.Left){RoutedEvent=down?Mouse.PreviewMouseDownEvent:Mouse.PreviewMouseUpEvent};
                image.RaiseEvent(args);
                if(!args.Handled){args.RoutedEvent=down?Mouse.MouseDownEvent:Mouse.MouseUpEvent;image.RaiseEvent(args);}
            }
            else if(message==0x200)image.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=Mouse.PreviewMouseMoveEvent});
        }
        catch(Exception e){try{DiagnosticLog.Write(DiagnosticEvent.AnimationFailed, kind: "permission", error: e);}catch{}}
        finally{if(sprite!=null)sprite.IsNativeInput=false;try{if(owner is MainWindow fish)fish.TraceCompanionPointer(message,buttons);}catch{}}
    }
    internal void Attach(IntPtr swapchain,bool mirror)
    {mirrored=mirror;Check(AttachNative(handle,swapchain));image.Opacity=0;Update();}
    internal void Present(CompanionMediaFrame frame)
    {Update();Check(Shape(handle,frame.Data,frame.Width,frame.Height,frame.Stride,mirrored?1:0));ready=true;SetVisible(owner.IsVisible);}
    void Layout(object? sender,EventArgs e)=>Update();
    void VisibilityChanged(object sender,DependencyPropertyChangedEventArgs e)=>Update();
    void Update()
    {
        if(handle==IntPtr.Zero)return;
        if(!owner.IsVisible||PresentationSource.FromVisual(image)==null){SetVisible(false);return;}
        var a=image.PointToScreen(new Point());var b=image.PointToScreen(new Point(image.ActualWidth,image.ActualHeight));
        var bounds=new Rect(a,b);
        if(bounds!=lastBounds||mirrored!=lastMirror){lastBounds=bounds;lastMirror=mirrored;Bounds(handle,(int)bounds.Left,(int)bounds.Top,Math.Max(1,(int)Math.Round(bounds.Width)),Math.Max(1,(int)Math.Round(bounds.Height)),mirrored?1:0);}
        SetVisible(ready);
    }
    void SetVisible(bool visible){int value=visible?1:0;if(value!=lastVisible&&handle!=IntPtr.Zero){Show(handle,value);lastVisible=value;}}
    internal void Hide(){SetVisible(false);image.Opacity=1;ready=false;}
    public void Dispose()
    {
        owner.LocationChanged-=Layout;owner.SizeChanged-=Layout;owner.IsVisibleChanged-=VisibilityChanged;image.LayoutUpdated-=Layout;
        if(handle!=IntPtr.Zero)Destroy(handle);handle=IntPtr.Zero;image.Opacity=1;
    }
    // Resolved alongside the bundled GStreamer libraries by CompanionMedia.
    const string Library="bbf-composition";
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void InputCallback(uint message,uint buttons);
    [DllImport(Library,EntryPoint="surface_create",CallingConvention=CallingConvention.Cdecl)]static extern int Create(IntPtr owner,InputCallback callback,out IntPtr surface);
    [DllImport(Library,EntryPoint="surface_attach",CallingConvention=CallingConvention.Cdecl)]static extern int AttachNative(IntPtr surface,IntPtr swapchain);
    [DllImport(Library,EntryPoint="surface_bounds",CallingConvention=CallingConvention.Cdecl)]static extern void Bounds(IntPtr surface,int x,int y,int width,int height,int mirror);
    [DllImport(Library,EntryPoint="surface_shape",CallingConvention=CallingConvention.Cdecl)]static extern int Shape(IntPtr surface,IntPtr pixels,int width,int height,int stride,int mirror);
    [DllImport(Library,EntryPoint="surface_show",CallingConvention=CallingConvention.Cdecl)]static extern void Show(IntPtr surface,int visible);
    [DllImport(Library,EntryPoint="surface_destroy",CallingConvention=CallingConvention.Cdecl)]static extern void Destroy(IntPtr surface);
}
