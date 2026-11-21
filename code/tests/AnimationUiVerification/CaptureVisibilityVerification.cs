using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using BigBlueFish.Windows;

internal static partial class Program
{
    [DllImport("user32.dll")] static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sx, int sy, uint operation);
    static uint Affinity(Window window)
    {
        if (!GetWindowDisplayAffinity(new WindowInteropHelper(window).Handle, out uint value)) throw new Exception("Cannot read display affinity");
        return value;
    }
    static void VerifyCaptureVisibility(MainWindow w)
    {
        Wait(()=>(string)Read(w,"animationPhase")!="Startup", 12, "startup finishes before capture test");
        var prefs = Read(w,"preferences");
        Check(!Property<bool>(prefs,"HideFromCapture") && Affinity(w)==0, "capture exclusion defaults off");
        Invoke(w,"OpenCompanionSettings"); Pump(100);
        var settings = (Window)Read(w,"companionSettings");
        var toggle = Children<CheckBox>(settings).Single(c=>c.Content?.ToString()=="截屏时隐藏大肥鱼");
        toggle.IsChecked = true; toggle.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent)); Pump(100);
        Check(Property<bool>(prefs,"HideFromCapture") && Affinity(w)==0x11, "actual settings enables native character capture exclusion");
        Check(Affinity(settings)==0, "settings remain capturable ordinary window");
        Render((Visual)settings.Content,"capture-settings.png",(int)settings.ActualWidth,(int)settings.ActualHeight);
        settings.Close();
        Invoke(w,"ShowCompanionNotice","截屏排除验证",false); Pump(250);
        var bubble = w.OwnedWindows.Cast<Window>().Single(x=>x.Title=="蓝色大肥鱼 · 对话");
        Check(Affinity(bubble)==0x11, "bubble follows enabled exclusion");
        Set(prefs,"AgentEnabled",true); Invoke(w,"TryOpenMcpInput"); Pump(100);
        var input = (Window)Read(w,"mcpInput");
        Check(Affinity(input)==0x11, "new chat input inherits exclusion"); input.Close(); Set(prefs,"AgentEnabled",false);
        // A controlled opaque window prevents personal desktop pixels entering evidence.
        using var background = new CloseWindow(new Window { Left=w.Left-20, Top=w.Top-200, Width=w.Width+40,
            Height=w.Height+240, Topmost=true, Background=Brushes.Lime, WindowStyle=WindowStyle.None, ShowInTaskbar=false });
        background.Window.Show(); w.Topmost=false; w.Topmost=true; Pump(500);
        var sprite = (Image)Read(w,"videoImage");
        var bitmapSource = (System.Windows.Media.Imaging.BitmapSource)sprite.Source;
        var bytes = new byte[bitmapSource.PixelWidth*bitmapSource.PixelHeight*4]; bitmapSource.CopyPixels(bytes, bitmapSource.PixelWidth*4, 0);
        int px=bitmapSource.PixelWidth/2, py=bitmapSource.PixelHeight/2;
        while(py<bitmapSource.PixelHeight-1 && bytes[(py*bitmapSource.PixelWidth+px)*4+3]<240) py++;
        Check(bytes[(py*bitmapSource.PixelWidth+px)*4+3]>=240, "sample point lies inside opaque character");
        var point = sprite.PointToScreen(new Point((px+.5)*sprite.ActualWidth/bitmapSource.PixelWidth,(py+.5)*sprite.ActualHeight/bitmapSource.PixelHeight));
        System.Drawing.Color Sample() {
            using var bitmap = new System.Drawing.Bitmap(3,3);
            using (var g = System.Drawing.Graphics.FromImage(bitmap)) {
                IntPtr source=GetDC(IntPtr.Zero), target=g.GetHdc();
                try { Check(BitBlt(target,0,0,3,3,source,(int)point.X,(int)point.Y,0x40cc0020), "screen capture including layered windows succeeds"); }
                finally { g.ReleaseHdc(target); ReleaseDC(IntPtr.Zero,source); }
            }
            return bitmap.GetPixel(1,1);
        }
        var hidden = Sample(); Console.WriteLine("Capture hidden pixel: " + hidden + " at " + point + " background=" + background.Window.Left + "," + background.Window.Top);
        Check(hidden.G>240 && hidden.R<10 && hidden.B<10, "real GDI screen capture reveals controlled background through excluded pet");
        Set(prefs,"HideFromCapture",false);
        typeof(App).Assembly.GetType("BigBlueFish.Windows.CompanionCaptureVisibility")!.GetMethod("Refresh",Access)!.Invoke(null,null); Pump(250);
        Check(Affinity(w)==0 && Affinity(bubble)==0, "disabling restores character and bubble capture");
        var visible = Sample(); Console.WriteLine("Capture visible pixel: " + visible); Check(visible.ToArgb()!=hidden.ToArgb(), "same real screen capture contains pet after disabling");
        Invoke(w,"SaveCompanion");
        Check(!System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText("preferences.json")).RootElement.GetProperty("HideFromCapture").GetBoolean(), "capture preference persists");
        Console.WriteLine($"RESULT {checks} actual WPF/native/screen capture checks passed");
    }
}
