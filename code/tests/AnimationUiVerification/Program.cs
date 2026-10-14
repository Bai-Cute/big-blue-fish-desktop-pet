using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BigBlueFish.Windows;

internal static class Program
{
    const BindingFlags Access = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static int checks;
    static object Read(object o,string n)=>o.GetType().GetField(n,Access)!.GetValue(o)!;
    static void Field(object o,string n,object v)=>o.GetType().GetField(n,Access)!.SetValue(o,v);
    static T Property<T>(object o,string n)=>(T)o.GetType().GetProperty(n,Access)!.GetValue(o)!;
    static void Set(object o,string n,object v)=>o.GetType().GetProperty(n,Access)!.SetValue(o,v);
    static object? Invoke(object o,string n,params object[] a)=>o.GetType().GetMethod(n,Access)!.Invoke(o,a);
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
    static void Pump(int ms=30)
    { var f=new DispatcherFrame();var t=new DispatcherTimer(DispatcherPriority.Send){Interval=TimeSpan.FromMilliseconds(ms)};t.Tick+=(_,_)=>{t.Stop();f.Continue=false;};t.Start();Dispatcher.PushFrame(f); }
    static void Wait(Func<bool> condition,int seconds,string message)
    {var watch=Stopwatch.StartNew();while(!condition()&&watch.Elapsed.TotalSeconds<seconds)Pump();Check(condition(),message);}
    static IEnumerable<T> Children<T>(DependencyObject root) where T:DependencyObject
    {for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T target)yield return target;foreach(var nested in Children<T>(child))yield return nested;}}
    static void Render(Visual visual,string file,int width,int height)
    {var b=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);b.Render(visual);var e=new PngBitmapEncoder();e.Frames.Add(BitmapFrame.Create(b));using var s=File.Create(file);e.Save(s);}
    static void VerifyBubbleScale(MainWindow w)
    {
        var prefs=Read(w,"preferences");
        var readPrefs=prefs.GetType().GetMethod("Read",Access)!;
        Check(Property<double>(prefs,"BubbleScale")==1,"old settings default bubble to 100 percent");
        Invoke(w,"OpenCompanionSettings");Pump(100);
        var settings=(Window)Read(w,"companionSettings");
        Children<Button>(settings).Single(b=>b.Content?.ToString()=="说话与天气").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        var slider=Children<Slider>(settings).Single(s=>System.Windows.Automation.AutomationProperties.GetName(s)=="文字气泡大小");
        Check(slider.IsVisible&&slider.Minimum==.5&&slider.Maximum==2&&slider.TickFrequency==.05,"independent bubble slider visible with range and steps");
        var bubble=(Border)Read(w,"bubble");var text=(TextBlock)Read(w,"bubbleText");
        var sprite=(Image)Read(w,"videoImage");double petWidth=sprite.Width;
        double baseWidth=0,baseHeight=0;
        foreach(var scale in new[]{1.0,.5,2.0,1.35})
        {
            slider.Value=scale;Invoke(w,"ShowCompanionNotice","主人，鱼鱼陪你呀~",false);Pump(120);
            var popup=w.OwnedWindows.Cast<Window>().Single(p=>p.Title=="蓝色大肥鱼 · 对话");
            if(scale==1){baseWidth=popup.Width;baseHeight=popup.Height;}
            Check(Math.Abs(popup.Width-baseWidth*scale)<1&&Math.Abs(popup.Height-baseHeight*scale)<1,"complete bubble scales proportionally "+scale);
            Check(sprite.Width==petWidth&&Property<double>(prefs,"Scale")==1,"bubble scaling leaves pet unchanged "+scale);
            Check(Math.Abs(Property<double>(readPrefs.Invoke(null,null)!,"BubbleScale")-scale)<.001,"bubble size survives settings reload "+scale);
            Check(((ScaleTransform)bubble.LayoutTransform).ScaleX==scale&&text.FontSize==17,"font button padding and outline share one transform "+scale);
            Render((Visual)popup.Content,$"bubble-{scale:0.00}.png",(int)Math.Ceiling(popup.Width),(int)Math.Ceiling(popup.Height));
        }
        foreach(var petScale in new[]{.5,1.0,2.0})
        foreach(var bubbleScale in new[]{.5,1.0,2.0})
        foreach(var corner in new[]{(X:-5000.0,Y:-5000.0),(X:5000.0,Y:-5000.0),(X:-5000.0,Y:5000.0),(X:5000.0,Y:5000.0)})
        {
            Set(prefs,"Scale",petScale);Invoke(w,"ApplyCompanionScale",false);slider.Value=bubbleScale;
            w.Left=corner.X;w.Top=corner.Y;Invoke(w,"ClampCompanion");
            Invoke(w,"ShowCompanionNotice","主人，鱼鱼看到你在认真研究新的功能啦，先伸个懒腰再慢慢来好不好呀？小尾巴也跟着主人一起加油哦~",false);Pump(80);
            var popup=w.OwnedWindows.Cast<Window>().Single(p=>p.Title=="蓝色大肥鱼 · 对话");var area=CompanionEdgeLayout.WorkArea(w);
            Check(popup.IsVisible&&popup.Left>=area.Left-1&&popup.Top>=area.Top-1&&popup.Left+popup.Width<=area.Right+1&&popup.Top+popup.Height<=area.Bottom+1,"scaled bubble stays inside work area");
            Check(Math.Abs(Property<double>(prefs,"BubbleScale")-bubbleScale)<.001,"pet resize preserves bubble setting");
            var bounds=text.TransformToAncestor(popup).TransformBounds(new Rect(0,0,text.ActualWidth,text.ActualHeight));
            Check(bounds.Right<=popup.Width+1&&bounds.Bottom<=popup.Height+1,$"full text fits scaled popup pet={petScale} bubble={bubbleScale} corner={corner} bounds={bounds} popup={popup.Width}x{popup.Height}");
        }
        Children<Button>(bubble).Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        Check(!w.OwnedWindows.Cast<Window>().Single(p=>p.Title=="蓝色大肥鱼 · 对话").IsVisible,"scaled close button dismisses bubble");
        settings.Close();
        foreach(var sample in new[]{(Value:.1,Expected:.5),(Value:9.0,Expected:2.0)})
        {
            File.WriteAllText("preferences.json","{\"BubbleScale\":"+sample.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)+"}");
            Check(Property<double>(readPrefs.Invoke(null,null)!,"BubbleScale")==sample.Expected,"loaded bubble scale clamps to range");
        }
        Console.WriteLine($"Bubble scale: {checks} real WPF checks passed.");
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")]
    static extern IntPtr GetWindowStyle(IntPtr handle,int index);
    static IntPtr Handle(Window window)=>new System.Windows.Interop.WindowInteropHelper(window).Handle;
    static void VerifySettingsFocus(MainWindow w)
    {
        using var closeFixture=new CloseWindow(new Window { Title="大肥鱼设置前台回归样例",Width=460,Height=220,ShowInTaskbar=true,Content=new TextBlock {Text="普通窗口切换检查",Margin=new Thickness(30)} });
        var other=closeFixture.Window;other.Show();other.Activate();Pump(100);
        Console.WriteLine("FOCUS_FIXTURE_READY: click the normal regression window to grant genuine foreground input.");
        Wait(()=>GetForegroundWindow()==Handle(other),60,"ordinary fixture receives genuine foreground input before opening settings");
        var menu=(ContextMenu)Read(w,"companionContextMenu");
        var item=menu.Items.Cast<MenuItem>().Single(i=>i.Header?.ToString()=="设置");
        menu.IsOpen=true;Pump();item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        // Reproduce late menu focus restoration after its click handler returns.
        w.Dispatcher.BeginInvoke(DispatcherPriority.Input,(Action)(()=>other.Activate()));Pump(120);
        var settings=(Window)Read(w,"companionSettings");
        Check(GetForegroundWindow()==Handle(settings)&&settings.IsActive,$"new settings become foreground after menu focus restoration: foreground={GetForegroundWindow()}, settings={Handle(settings)}, other={Handle(other)}, active={settings.IsActive}, menu={menu.IsOpen}");
        Check(!settings.Topmost&&settings.ShowActivated&&settings.ShowInTaskbar&&settings.Owner==null,"settings remain independent ordinary taskbar window");
        Check((GetWindowStyle(Handle(settings),-20).ToInt64()&0x08000000)==0,"settings do not inherit pet no-activate style");
        other.Activate();Pump();
        Check(GetForegroundWindow()==Handle(other)&&!settings.IsActive,"switching to ordinary window leaves settings behind normally");
        Invoke(w,"OpenCompanionSettings");Pump();
        Check(ReferenceEquals(settings,Read(w,"companionSettings"))&&GetForegroundWindow()==Handle(settings),"reopening existing settings brings same window forward");
        settings.WindowState=WindowState.Minimized;other.Activate();Pump();
        Invoke(w,"OpenCompanionSettings");Pump();
        Check(settings.WindowState==WindowState.Normal&&GetForegroundWindow()==Handle(settings),"reopening minimized settings restores and activates");
        settings.WindowState=WindowState.Maximized;other.Activate();Pump();
        Invoke(w,"OpenCompanionSettings");Pump();
        Check(settings.WindowState==WindowState.Maximized&&GetForegroundWindow()==Handle(settings),"reopening maximized settings preserves maximized state");
        settings.Close();other.Activate();Pump();
        var petStyle=GetWindowStyle(Handle(w),-20).ToInt64();
        Check(w.Topmost&&!w.ShowActivated&&!w.ShowInTaskbar&&(petStyle&0x08000080)==0x08000080,"pet remains topmost non-activating tool window");
        Invoke(w,"ShowCompanionNotice","主人，鱼鱼在这里呀~",false);Pump();
        var bubble=w.OwnedWindows.Cast<Window>().Single(p=>p.Title=="蓝色大肥鱼 · 对话");
        Check(bubble.IsVisible&&bubble.Topmost&&!bubble.ShowActivated&&!bubble.ShowInTaskbar,"bubble retains topmost no-activate behavior");
        Check(GetForegroundWindow()==Handle(other),"showing pet bubble does not take foreground from ordinary window");
        Invoke(w,"OpenCompanionSettings");((Window)Read(w,"companionSettings")).Close();other.Activate();Pump();
        Check(GetForegroundWindow()==Handle(other),"queued activation ignores settings already closed");
        Console.WriteLine($"Settings focus: {checks} real WPF/Win32 checks passed.");
    }
    sealed class CloseWindow(Window window):IDisposable
    {
        public Window Window=>window;
        public void Dispose()=>window.Close();
    }
    static void VerifyMenus(MainWindow w)
    {
        var menu=(ContextMenu)Read(w,"companionContextMenu");
        var items=menu.Items.Cast<MenuItem>().ToArray();
        Check(items.Select(i=>i.Header?.ToString()).SequenceEqual(new[]{"设置","天气","吐字测试","隐藏桌宠"}),"pet menu retains its four actions");
        menu.IsOpen=true;Pump(120);menu.UpdateLayout();
        var dismissal=Read(w,"companionMenuDismissal");
        Check(Property<bool>(dismissal,"IsListening"),"outside-click observer attaches only while menu is open");
        Invoke(dismissal,"DismissOutside",menu.PointToScreen(new Point(menu.ActualWidth/2,menu.ActualHeight/2)));Pump();
        Check(menu.IsOpen,"pointer inside menu does not dismiss it");
        Invoke(dismissal,"DismissOutside",menu.PointToScreen(new Point(-20,-20)));Pump();
        Check(!menu.IsOpen&&!Property<bool>(dismissal,"IsListening"),"outside pointer closes menu and detaches observer");
        menu.IsOpen=true;Pump();
        Check(Property<bool>(dismissal,"IsListening"),"reopening menu reattaches observer");
        Check(menu.Style!=null&&menu.FontSize==14&&menu.ActualWidth>=184,"modern menu template and readable sizing applied");
        var frame=Children<Border>(menu).First(b=>b.CornerRadius.TopLeft==12);
        Check(frame.Background is SolidColorBrush color&&color.Color==Colors.White,"menu uses white rounded settings-style panel");
        Check(items.All(i=>i.ActualHeight>=38&&i.Icon is TextBlock {Text.Length:1}),"all menu actions have full-height rows and system vector icons");
        var rows=items.Select(i=>i.TransformToAncestor(menu).TransformBounds(new Rect(0,0,i.ActualWidth,i.ActualHeight))).ToArray();
        Check(rows.Zip(rows.Skip(1)).All(pair=>pair.First.Bottom<=pair.Second.Top+.1),"menu rows do not overlap");
        Render(menu,"menu-normal.png",(int)Math.Ceiling(menu.ActualWidth),(int)Math.Ceiling(menu.ActualHeight));
        items[0].Focus();Pump(80);
        Render(menu,"menu-focused.png",(int)Math.Ceiling(menu.ActualWidth),(int)Math.Ceiling(menu.ActualHeight));
        items[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Pump(100);
        Check(!menu.IsOpen&&Read(w,"companionSettings") is Window {IsVisible:true},"styled settings action opens actual settings and dismisses menu");
        ((Window)Read(w,"companionSettings")).Close();
        menu.IsOpen=true;Pump();items[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Pump();
        Check(!menu.IsOpen&&((TextBlock)Read(w,"bubbleText")).Text.Contains("启用本地大模型"),"weather action retains model-off behavior");
        menu.IsOpen=true;Pump();items[2].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Pump();
        Check(!menu.IsOpen&&((TextBlock)Read(w,"bubbleText")).Text.Contains("当前设置暂停了模型"),"speech test action retains model-off behavior");
        menu.IsOpen=true;Pump();items[3].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Pump();
        Check(!w.IsVisible&&(bool)Read(w,"hiddenByUser"),"styled hide action uses existing hide lifecycle");
        Invoke(w,"RestoreCompanion");Pump();Check(w.IsVisible&&!(bool)Read(w,"hiddenByUser"),"existing restore still works");
        var tray=(System.Windows.Forms.ContextMenuStrip)((System.Windows.Forms.NotifyIcon)Read(w,"notifyIcon")).ContextMenuStrip!;
        Check(tray.Renderer.GetType().Name=="CompanionTrayMenuRenderer"&&!tray.ShowImageMargin,"tray uses matching palette without obsolete image gutter");
        Check(tray.Items.Cast<System.Windows.Forms.ToolStripItem>().Select(i=>i.Text).SequenceEqual(new[]{"显示桌宠","设置","天气","吐字测试","关闭桌宠"}),"tray retains all original actions");
        tray.Show(new System.Drawing.Point(500,300));System.Windows.Forms.Application.DoEvents();
        Console.WriteLine($"TRAY_METRICS dpi={tray.DeviceDpi} font={tray.Font.Size}/{tray.Font.Unit} size={tray.Size} client={tray.ClientSize} items="+string.Join(";",tray.Items.Cast<System.Windows.Forms.ToolStripItem>().Select(i=>$"{i.Text}:{i.Bounds},font={i.Font.Size}")));
        Check(tray.GetType().Name=="CompanionTrayMenu"&&tray.Region!=null
            &&!tray.Region.IsVisible(0,0)&&tray.Region.IsVisible(tray.Width/2,tray.Height/2),"native tray menu has a real rounded window region");
        Check(tray.Items.Cast<System.Windows.Forms.ToolStripItem>().All(i=>i.Tag is string{Length:1}
            &&i.Height>=38*tray.DeviceDpi/96),"all tray rows include vector glyphs and DPI-scaled comfortable height");
        tray.Items[1].Select();System.Windows.Forms.Application.DoEvents();
        using(var bitmap=new System.Drawing.Bitmap(tray.Width,tray.Height))
        {tray.DrawToBitmap(bitmap,new System.Drawing.Rectangle(0,0,tray.Width,tray.Height));bitmap.Save("tray-menu.png");}
        Check(tray.Items.Cast<System.Windows.Forms.ToolStripItem>().All(i=>
        {
            // WinForms may extend an item's paint surface into its hidden image
            // gutter. Check visible text/padding, not that internal paint surface.
            var visible=System.Drawing.Rectangle.Intersect(tray.ClientRectangle,i.Bounds);
            var textSize=System.Windows.Forms.TextRenderer.MeasureText(i.Text,i.Font);
            return visible.Height>=textSize.Height+i.Padding.Vertical&&visible.Width>=textSize.Width+56*tray.DeviceDpi/96;
        }),"tray labels and vertical padding fit visible rows");
        tray.Close();
        Invoke(w,"HideCompanion");tray.Show(new System.Drawing.Point(500,300));System.Windows.Forms.Application.DoEvents();
        Check(!w.IsVisible&&tray.Visible,"styled native tray remains usable while character is hidden");
        ((System.Windows.Forms.ToolStripMenuItem)tray.Items[0]).PerformClick();tray.Close();Pump();
        Check(w.IsVisible,"native tray display action restores hidden character");
        Console.WriteLine($"Menus: {checks} real WPF/WinForms checks passed.");
    }
    static void VerifyMenuOutsideClick(MainWindow w)
    {
        var area=CompanionEdgeLayout.WorkArea(w);
        int clicks=0;
        var button=new Button {Content="点这里验证外部点击",Margin=new Thickness(35),FontSize=18};
        button.Click+=(_,_)=>clicks++;
        using var fixture=new CloseWindow(new Window {Title="大肥鱼菜单外部点击验证",Width=420,Height=220,
            Left=area.Left+40,Top=area.Top+40,Content=button,ShowInTaskbar=true});
        fixture.Window.Show();fixture.Window.Activate();Pump();
        var menu=(ContextMenu)Read(w,"companionContextMenu");
        menu.Placement=System.Windows.Controls.Primitives.PlacementMode.AbsolutePoint;
        menu.HorizontalOffset=area.Left+500;menu.VerticalOffset=area.Top+150;menu.IsOpen=true;Pump(100);
        Check(Property<bool>(Read(w,"companionMenuDismissal"),"IsListening"),"native observer installed for real outside click");
        Console.WriteLine("OUTSIDE_CLICK_READY: click the button in 大肥鱼菜单外部点击验证.");
        Wait(()=>clicks>0,60,"outside target receives a real click");Pump(100);
        Check(!menu.IsOpen,"real click outside pet/menu closes context menu");
        Check(!Property<bool>(Read(w,"companionMenuDismissal"),"IsListening"),"native observer removed after outside click");
        Check(w.Topmost&&!w.ShowActivated&&!w.ShowInTaskbar,"outside-click handling preserves pet window policy");
        Console.WriteLine($"Outside click: {checks} real mouse/WPF checks passed.");
    }
    [STAThread] static int Main(string[] args)
    {
        MainWindow? w=null;
        try
        {
            string sandbox=Path.GetFullPath(args[0]);Directory.CreateDirectory(sandbox);
            var app=new App();app.InitializeComponent();typeof(Application).GetField("_startupUri",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(app,null);app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
            Directory.SetCurrentDirectory(sandbox);
            File.Copy(Path.Combine(AppContext.BaseDirectory,"BigBlueFish.ico"),"BigBlueFish.ico",true);
            File.WriteAllText("preferences.json","{\"ModelEnabled\":false,\"PublicInfo\":false,\"ForegroundEnabled\":false,\"Scale\":1,\"Left\":400,\"Top\":250,\"WeatherRegionCode\":\"340104\",\"BubbleDurationIndex\":5,\"AnimationIntervalSeconds\":40}");
            var native=typeof(App).Assembly.GetType("BigBlueFish.Windows.CompanionNative")!;
            native.GetField("TestPowerOverride",Access)!.SetValue(null,false);
            w=new MainWindow();w.Show();Wait(()=>(bool)Read(w,"companionReady"),10,"video load");
            ((DispatcherTimer)Read(w,"companionTimer")).Stop();
            if(args.Contains("--menu-dismiss"))
            {
                Wait(()=>(string)Read(w,"animationPhase")!="Startup",10,"startup finishes before outside-click verification");
                VerifyMenuOutsideClick(w);Field(w,"exitReady",true);w.Close();return 0;
            }
            if(args.Contains("--menus"))
            {
                Wait(()=>(string)Read(w,"animationPhase")!="Startup",10,"startup finishes before menu verification");
                VerifyMenus(w);Field(w,"exitReady",true);w.Close();return 0;
            }
            if(args.Contains("--settings-focus"))
            {
                Wait(()=>(string)Read(w,"animationPhase")!="Startup",10,"startup finishes before focus verification");
                VerifySettingsFocus(w);Field(w,"exitReady",true);w.Close();return 0;
            }
            if(args.Contains("--bubble-scale"))
            {
                Wait(()=>(string)Read(w,"animationPhase")!="Startup",10,"startup finishes before bubble verification");
                VerifyBubbleScale(w);Field(w,"exitReady",true);w.Close();return 0;
            }
            var player=Read(w,"videoPlayer");var prefs=Read(w,"preferences");var layout=(Grid)Read(w,"companionLayout");
            Check(Property<double>(player,"PlaybackRate")==1.5,"startup greeting uses 1.5x playback");
            var start=Stopwatch.StartNew();
            var welcome=(string)Invoke(w,"WelcomeText",DateTime.Now)!;
            Check(((TextBlock)Read(w,"bubbleText")).Text==welcome,"welcome appears immediately with model disabled");
            foreach(var sample in new[]{(Hour:5,Period:"早上"),(Hour:10,Period:"早上"),(Hour:11,Period:"中午"),(Hour:13,Period:"中午"),(Hour:14,Period:"下午"),(Hour:17,Period:"下午"),(Hour:18,Period:"晚上"),(Hour:0,Period:"晚上")})
                Check((string)Invoke(w,"WelcomeText",new DateTime(2026,10,14,sample.Hour,0,0))! == $"主人{sample.Period}好，鱼鱼给主人请安啦~","system time greeting boundary "+sample.Hour);
            Wait(()=>(string)Read(w,"animationPhase")!="Startup",9,"startup greeting completes at faster speed");
            Check(start.Elapsed.TotalSeconds>=6.4&&start.Elapsed.TotalSeconds<8,"full startup bow lasts about 6.7 seconds");
            Check(((Border)Read(w,"bubble")).Visibility!=Visibility.Visible,"welcome closes with bow, independent of 2 minute bubble setting");
            start.Restart();
            Invoke(w,"PlayCompanion","待机呼吸休闲");
            Wait(()=>Property<long>(player,"Frames")>=240,13,"240-frame playback");
            Check(start.Elapsed.TotalSeconds>=9.8&&start.Elapsed.TotalSeconds<12,"original fps playback: "+start.Elapsed.TotalSeconds);
            Render(layout,"pet-transparent.png",(int)w.Width,(int)w.Height);
            var bmp=(BitmapSource)((Image)Read(w,"videoImage")).Source;
            var pixels=new byte[bmp.PixelWidth*bmp.PixelHeight*4];bmp.CopyPixels(pixels,bmp.PixelWidth*4,0);
            Check(pixels.Where((_,i)=>i%4==3).Any(a=>a==0)&&pixels.Where((_,i)=>i%4==3).Any(a=>a==255),"real alpha pixels");
            var sprite=(Image)Read(w,"videoImage");
            Check(sprite.InputHitTest(new Point(1,1))==null,"transparent video margins ignore clicks");
            Check(sprite.InputHitTest(new Point(sprite.ActualWidth/2,sprite.ActualHeight*.45))!=null,"visible character accepts clicks");
            Invoke(w,"PlayCompanion","待机呼吸休闲");
            Console.WriteLine("24-fps playback and alpha verified.");
            var firstAction=(string)Read(w,"currentAction");
            Field(w,"nextAction",DateTime.UtcNow.AddSeconds(-1));Pump(300);
            Check((string)Read(w,"currentAction")==firstAction,"deadline did not truncate active clip");
            Wait(()=>(string)Read(w,"animationPhase")=="Event"||(string)Read(w,"animationPhase")=="Outbound",12,"due event at full boundary");
            Field(w,"nextAction",DateTime.UtcNow.AddMinutes(10));
            Invoke(w,"OpenCompanionSettings");Pump(150);
            var settings=(Window)Read(w,"companionSettings");
            var buttons=Children<Button>(settings).ToArray();
            var navigation=buttons.Where(b=>new[]{"陪伴与外观","说话与天气","模型与供电"}.Contains(b.Content?.ToString())).ToArray();
            Check(navigation.Length==3,"settings navigation");
            for(int i=0;i<3;i++)
            {
                navigation[i].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(100);
                Render((Visual)settings.Content,$"settings-{i}.png",(int)settings.ActualWidth,(int)settings.ActualHeight);
                Check(Children<ScrollViewer>(settings).Any(v=>v.IsVisible&&v.ActualWidth>450),"visible settings page "+i);
            }
            navigation[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(50);
            var interval=Children<Slider>(settings).Single(s=>s.Minimum==30&&s.Maximum==300);
            interval.Value=70;Pump();Check(Property<int>(prefs,"AnimationIntervalSeconds")==70,"interval saved live");
            Check(File.ReadAllText("preferences.json").Contains("\"AnimationIntervalSeconds\": 70"),"interval persisted");
            Check(Property<string>(prefs,"WeatherRegionCode")=="340104"&&Property<int>(prefs,"BubbleDurationIndex")==5,"settings opening preserved choices");
            foreach(var scale in new[]{.5,1.0,2.0})
            {
                Set(prefs,"Scale",scale);Invoke(w,"ApplyCompanionScale",false);Pump();
                w.Left=-800;w.Top=-800;Invoke(w,"ClampCompanion");Pump();
                var body=CompanionEdgeLayout.PetBounds(w);var work=CompanionEdgeLayout.WorkArea(w);
                Check(w.Left+body.Left>=work.Left-1&&w.Top+body.Top>=work.Top-1,"visible body clamps at scale "+scale);
                Invoke(w,"ShowCompanionBubble","主人，大肥鱼的小尾巴也陪着你认真工作呀～");Pump(100);
                Check(w.OwnedWindows.Cast<Window>().Any(p=>p.Title=="蓝色大肥鱼 · 对话"&&p.IsVisible),"separate bubble visible "+scale);
            }
            settings.Width=680;settings.Height=450;Pump(100);
            Render((Visual)settings.Content,"settings-small.png",(int)settings.ActualWidth,(int)settings.ActualHeight);
            Check(Children<ScrollViewer>(settings).Any(v=>v.IsVisible&&v.ExtentHeight>v.ViewportHeight),"small settings can scroll");
            settings.Close();Console.WriteLine("Settings navigation, persistence, scaling and scroll verified.");Set(prefs,"Scale",1.0);Invoke(w,"ApplyCompanionScale",false);w.Left=500;w.Top=250;Invoke(w,"ClampCompanion");
            Invoke(w,"ClickAnimation");Check(((string)Read(w,"currentAction")).StartsWith("点击回应-"),"click pool");
            Invoke(w,"PlayCompanion","drag");Check((string)Read(w,"animationPhase")=="Drag","drag starts");
            Check(Property<double>(player,"StartSeconds")==1.5,"drag begins directly in suspended segment");
            Wait(()=>Property<long>(player,"Frames")==108,7,"drag plays only 1.5-6 second segment");
            Check(Property<double>(player,"PositionSeconds")>=5.9&&Property<double>(player,"PositionSeconds")<6,"drag ends before landing segment");
            Check((string)Read(w,"animationPhase")=="Drag","drag holds until release");Invoke(w,"FinishDragAnimation");Check((string)Read(w,"animationPhase")=="Idle","drag release idle");
            var policy=Read(w,"animationPolicy");var rule=Invoke(policy,"Find","原地左转奔跑")!;
            var origin=w.Left;Invoke(w,"StartAnimationEvent",rule);
            Wait(()=>(string)Read(w,"animationPhase")=="BetweenMoves",13,"outbound completes");Check(Math.Abs(w.Left-origin)>20,"actual local movement");
            Wait(()=>(string)Read(w,"animationPhase")=="Return",13,"return within three animations");
            Wait(()=>(string)Read(w,"animationPhase")=="Idle",13,"return completes");Check(Math.Abs(w.Left-origin)<.1,"return exact origin");
            Console.WriteLine("Click, drag hold/release and paired movement verified.");
            // Model-free battery notices exercise the preserved production speech-display path.
            Set(prefs,"ModelEnabled",true);((Task)Invoke(w,"SpeakCompanionTest")!).GetAwaiter().GetResult();
            Pump(2000);var text=(TextBlock)Read(w,"bubbleText");Check(text.Text=="主人，当前设置为笔记本离电暂停模型，可以在设置里调节哦。","speech test battery notice");
            Check(Property<int?>(Read(w,"brain"),"RunnerPid")==null,"animation logic did not start model");
            var context=(ContextMenu)Read(w,"companionContextMenu");
            var hide=context.Items.Cast<MenuItem>().Single(m=>(string)m.Header=="隐藏桌宠");
            hide.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Pump();
            Check(!w.IsVisible&&(bool)Read(w,"hiddenByUser"),"direct pet context menu hides character");
            Invoke(w,"RestoreCompanion");Pump();Check(w.IsVisible&&!(bool)Read(w,"hiddenByUser"),"tray restore path still shows character");
            start.Restart();w.Close();Check((string)Read(w,"animationPhase")=="Exit"&&w.IsVisible,"closing farewell starts once");
            Check(Property<double>(player,"PlaybackRate")==1.5,"farewell greeting uses 1.5x playback");
            Check(((TextBlock)Read(w,"bubbleText")).Text=="主人再见~鱼鱼喜欢主人哦","farewell appears immediately with exact single tilde");
            Wait(()=>!w.IsVisible,9,"farewell completed and window closed");Check(start.Elapsed.TotalSeconds>=6.5&&start.Elapsed.TotalSeconds<8,"farewell full clip finishes at 1.5x: "+start.Elapsed.TotalSeconds);
            Console.WriteLine($"Animation UI: {checks} checks passed, settings snapshots and alpha render saved to {sandbox}.");return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);if(w!=null){Field(w,"exitReady",true);w.Close();}return 1;}
    }
}
