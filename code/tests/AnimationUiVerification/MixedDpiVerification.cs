using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using BigBlueFish.Windows;

internal static partial class Program
{
    [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr handle);
    static NativeRect PhysicalRect(IntPtr handle)
    {
        var prior=SetThreadDpiAwarenessContext(new IntPtr(-4));
        try { if(!GetWindowRect(handle,out var rect))throw new Exception("GetWindowRect failed");return rect; }
        finally { if(prior!=IntPtr.Zero)SetThreadDpiAwarenessContext(prior); }
    }
    static void VerifyMixedDpi(MainWindow w)
    {
        foreach(var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var area=screen.WorkingArea;
            Check(SetWindowPos(Handle(w),IntPtr.Zero,area.Left+area.Width/2-320,area.Top+area.Height/2-180,0,0,0x15),"move pet to monitor "+screen.DeviceName);Pump(200);
            Invoke(w,"ClampCompanion");Invoke(w,"ShowCompanionNotice","主人，鱼鱼陪你检查不同缩放的屏幕呀~",false);Pump(180);
            var popup=w.OwnedWindows.Cast<Window>().Single(p=>p.Title=="蓝色大肥鱼 · 对话");
            var pet=PhysicalRect(Handle(w));var bubble=PhysicalRect(Handle(popup));
            Check(popup.IsVisible&&bubble.Right>bubble.Left&&bubble.Bottom>bubble.Top,"bubble physically visible "+screen.DeviceName);
            Check(bubble.Left<pet.Right&&bubble.Right>pet.Left&&Math.Abs(bubble.Top-pet.Top)<800,"bubble physically follows pet on monitor "+screen.DeviceName);
            var menu=(ContextMenu)Read(w,"companionContextMenu");
            menu.PlacementTarget=w;menu.Placement=PlacementMode.RelativePoint;menu.HorizontalOffset=300;menu.VerticalOffset=160;menu.IsOpen=true;Pump(100);
            var source=(HwndSource)PresentationSource.FromVisual(menu)!;var rect=PhysicalRect(source.Handle);
            var dismissal=Read(w,"companionMenuDismissal");
            Invoke(dismissal,"DismissPhysicalPoint",new Point((rect.Left+rect.Right)/2.0,(rect.Top+rect.Bottom)/2.0));Pump();
            Check(menu.IsOpen,"physical click inside menu survives on "+screen.DeviceName);
            Invoke(dismissal,"DismissPhysicalPoint",new Point(rect.Right+80,rect.Bottom+80));Pump();
            Check(!menu.IsOpen&&!Property<bool>(dismissal,"IsListening"),"physical outside click dismisses menu on "+screen.DeviceName);
            Console.WriteLine($"MONITOR {screen.DeviceName} logical={area} dpi={GetDpiForWindow(Handle(w))} petPhysical={pet.Left},{pet.Top},{pet.Right},{pet.Bottom} bubblePhysical={bubble.Left},{bubble.Top},{bubble.Right},{bubble.Bottom}");
        }
        Console.WriteLine($"RESULT {checks} mixed-DPI window checks passed across {System.Windows.Forms.Screen.AllScreens.Length} actual monitors");
    }
}
