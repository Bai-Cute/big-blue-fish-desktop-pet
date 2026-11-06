using System.Diagnostics;
using System.Runtime.InteropServices;
using BigBlueFish.Windows;

internal static class CompositionRegionVerification
{
    delegate bool EnumProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr parameter);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int capacity);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll")] static extern int GetWindowRgn(IntPtr window, IntPtr region);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] static extern bool PtInRegion(IntPtr region, int x, int y);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr value);
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left,Top,Right,Bottom; }

    internal static void Check(CompanionSpriteImage sprite, bool mirror)
    {
        IntPtr surface=IntPtr.Zero;
        EnumWindows((window, _) => {
            GetWindowThreadProcessId(window, out uint pid);
            var name=new System.Text.StringBuilder(128);GetClassName(window,name,name.Capacity);
            if(pid==Environment.ProcessId&&name.ToString()=="BigBlueFish.Composition")surface=window;
            return true;
        },IntPtr.Zero);
        if(surface==IntPtr.Zero)throw new Exception("GPU composition window missing");
        var region=CreateRectRgn(0,0,0,0);
        try
        {
            if(GetWindowRgn(surface,region)<=1)throw new Exception("Composition has no usable HWND region");
            GetClientRect(surface,out var bounds);
            var pixels=sprite.InputPixels??throw new Exception("Input alpha unavailable");
            int opaque=0;
            for(int y=0;y<sprite.InputHeight;y+=3)
                for(int x=0;x<sprite.InputWidth;x+=3)
                    if(pixels[(y*sprite.InputWidth+x)*4+3]>=16)
                    {
                        int sx=mirror?sprite.InputWidth-1-x:x;
                        int px=(int)((sx+.5)*bounds.Right/sprite.InputWidth),py=(int)((y+.5)*bounds.Bottom/sprite.InputHeight);
                        if(!PtInRegion(region,px,py))throw new Exception($"Opaque sample clipped by region: {x},{y}, mirror={mirror}");
                        opaque++;
                    }
            if(opaque==0||PtInRegion(region,2,2))throw new Exception("Region must preserve body and exclude transparent corner");
            GetWindowRect(surface,out var screen);
            for(int y=0;y<bounds.Bottom;y+=17)
                for(int x=0;x<bounds.Right;x+=17)
                {
                    int sx=screen.Left+x,sy=screen.Top+y;
                    long packed=((long)(sy&65535)<<16)|(uint)(sx&65535);
                    long hit=SendMessage(surface,0x84,IntPtr.Zero,new IntPtr(packed)).ToInt64();
                    if((hit==1)!=PtInRegion(region,x,y))throw new Exception($"Region/hit-test disagreement at {x},{y}");
                }
            Console.WriteLine($"PASS actual HWND region excludes background and preserves {opaque} opaque samples (mirror={mirror})");
        }
        finally{DeleteObject(region);}
    }
}
