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
using VPet_Simulator.Windows;

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
    [STAThread] static int Main(string[] args)
    {
        MainWindow? w=null;
        try
        {
            string sandbox=Path.GetFullPath(args[0]);Directory.CreateDirectory(sandbox);
            var app=new App();app.InitializeComponent();typeof(Application).GetField("_startupUri",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(app,null);app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
            Directory.SetCurrentDirectory(sandbox);
            File.Copy(Path.Combine(AppContext.BaseDirectory,"vpeticon.ico"),"vpeticon.ico",true);
            File.WriteAllText("preferences.json","{\"ModelEnabled\":false,\"PublicInfo\":false,\"ForegroundEnabled\":false,\"Scale\":1,\"Left\":400,\"Top\":250,\"WeatherRegionCode\":\"340104\",\"BubbleDurationIndex\":5,\"AnimationIntervalSeconds\":40}");
            var native=typeof(App).Assembly.GetType("VPet_Simulator.Windows.CompanionNative")!;
            native.GetField("TestPowerOverride",Access)!.SetValue(null,false);
            w=new MainWindow();w.Show();Wait(()=>(bool)Read(w,"companionReady"),10,"video load");
            ((DispatcherTimer)Read(w,"companionTimer")).Stop();
            var player=Read(w,"videoPlayer");var prefs=Read(w,"preferences");var layout=(Grid)Read(w,"companionLayout");
            var start=Stopwatch.StartNew();
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
            foreach(var scale in new[]{.65,1.0,1.6})
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
            Wait(()=>Property<long>(player,"Frames")==241,13,"drag full clip"); // Original assets have 241 frames.
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
            start.Restart();w.Close();Check((string)Read(w,"animationPhase")=="Exit"&&w.IsVisible,"closing farewell starts once");
            Wait(()=>!w.IsVisible,13,"farewell completed and window closed");Check(start.Elapsed.TotalSeconds>=9.8,"farewell not truncated");
            Console.WriteLine($"Animation UI: {checks} checks passed, settings snapshots and alpha render saved to {sandbox}.");return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);if(w!=null){Field(w,"exitReady",true);w.Close();}return 1;}
    }
}
