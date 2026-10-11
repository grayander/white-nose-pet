using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Globalization;
using System.Xml.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace WhiteNosePet {
public static class Program {
    public static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
    public const string EventName = @"Local\WhiteNoseDesktopPet.Quit.v1";
    public const string MutexName = @"Local\WhiteNoseDesktopPet.Instance.v1";
    [STAThread] public static int Main(string[] args) {
        bool test = args.Contains("--self-test") || args.Contains("--smoke-test");
        bool stop = args.Contains("--stop");
        Mutex singleton = null;
        EventWaitHandle quit = null;
        try {
            if (stop) { try { using (var e = EventWaitHandle.OpenExisting(EventName)) e.Set(); } catch (WaitHandleCannotBeOpenedException) {} return 0; }
            if (!test) {
                bool created;
                singleton = new Mutex(true, MutexName, out created);
                if (!created) { try { using (var e = EventWaitHandle.OpenExisting(EventName)) e.Set(); } catch (WaitHandleCannotBeOpenedException) {} singleton.Dispose(); return 0; }
                quit = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
            }
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            var pet = new PetWindow(test, args.Contains("--smoke-test"));
            if (quit != null) {
                var q = quit;
                var thread = new Thread(delegate() { q.WaitOne(); app.Dispatcher.BeginInvoke(new Action(pet.Close)); });
                thread.IsBackground = true;
                thread.Start();
            }
            app.Run(pet);
            if (singleton != null) singleton.ReleaseMutex();
            return 0;
        } catch (Exception ex) {
            try { Directory.CreateDirectory(Path.Combine(Root,"data")); File.WriteAllText(Path.Combine(Root,"data","error.log"), ex.ToString()); } catch {}
            if (!test) MessageBox.Show("桌宠未能启动。请确保 assets 文件夹和程序放在一起。\n\n" + ex.Message,"白鼻子桌宠");
            return 1;
        } finally { if (singleton != null) singleton.Dispose(); }
    }
}

public class PetWindow : Window {
    readonly Canvas stage = new Canvas { Background = null, ClipToBounds = false };
    readonly Image sprite = new Image { Stretch = Stretch.Fill, Cursor = Cursors.Hand };
    readonly TextBlock speech = new TextBlock { Foreground = Brushes.White, FontSize = 13, FontFamily = new FontFamily("Microsoft YaHei"), TextAlignment = TextAlignment.Center };
    readonly Border bubble = new Border { Background = new SolidColorBrush(Color.FromArgb(232,36,40,55)), CornerRadius = new CornerRadius(12), Padding = new Thickness(12,7,12,7), IsHitTestVisible = false };
    readonly TextBlock zzz = new TextBlock { Text = "z Z", FontSize = 24, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(104,156,210)), IsHitTestVisible = false };
    readonly BitmapSource[] frames = new BitmapSource[14];
    readonly Rect[] spriteBounds = new Rect[14];
    readonly Image sportsEffects = new Image { Width=400, Height=480, IsHitTestVisible=false };
    readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
    readonly Stopwatch watch = Stopwatch.StartNew();
    readonly Random random = new Random();
    Forms.NotifyIcon tray;
    string action = "idle";
    double actionStart, actionLength, nextAction, previousTick, bubbleUntil, smokeNext;
    double size = 220, homeX, homeY, dragX, dragY, originalX, originalY;
    bool wander = true, paused, dragging, moved, quitting, testMode, smoke, loading = true;
    int frame = -1, walkDirection = 1, lastIdle = -1, testStep, greetingIndex;
    readonly string[] idleActions = { "wave", "jump", "tongue", "walk", "sleep", "bow", "smash", "save" };
    const double StageWidth = 400, StageHeight = 480, BaseLine = 460;
    const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    public PetWindow(bool testing, bool smokeTest) {
        testMode = testing; smoke = smokeTest;
        Title = "白鼻子桌宠"; Width = StageWidth; Height = StageHeight;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; ShowActivated = false;
        Content = stage;
        RenderOptions.SetBitmapScalingMode(sprite, BitmapScalingMode.HighQuality);
        stage.Children.Add(sprite); stage.Children.Add(sportsEffects); stage.Children.Add(zzz);
        bubble.Child = speech; stage.Children.Add(bubble);
        bubble.Visibility = Visibility.Hidden; zzz.Visibility = Visibility.Hidden;
        LoadFrames(); LoadSettings();
        SourceInitialized += delegate {
            var h = new WindowInteropHelper(this).Handle;
            SetWindowLong(h,GWL_EXSTYLE,GetWindowLong(h,GWL_EXSTYLE)|WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE);
        };
        Loaded += delegate {
            loading = false;
            if (double.IsNaN(homeX)) PlaceDefault(); else { Left = homeX; Top = homeY; KeepVisible(); }
            if (!testMode) CreateTray();
            SetFrame(0); nextAction = watch.Elapsed.TotalSeconds + 7;
            previousTick = watch.Elapsed.TotalSeconds;
            if (!testMode) {
                if(Environment.GetCommandLineArgs().Contains("--show-new-action")) StartAction("smash",4.4,"看这一拍！");
                else StartAction("wave", 2.5, "你好呀！右键看看我会什么");
            }
            timer.Start();
            if (testMode && !smoke) Dispatcher.BeginInvoke(new Action(RunSelfTest), DispatcherPriority.ApplicationIdle);
        };
        timer.Tick += Tick;
        sprite.MouseLeftButtonDown += MouseDownPet;
        sprite.MouseMove += MouseMovePet;
        sprite.MouseLeftButtonUp += MouseUpPet;
        sprite.LostMouseCapture += delegate { if(dragging) { dragging=false; KeepVisible(); SaveSettings(); } };
        sprite.MouseRightButtonUp += delegate(object sender, MouseButtonEventArgs e) { ShowMenu(); e.Handled=true; };
        sprite.MouseWheel += delegate(object sender, MouseWheelEventArgs e) { ChangeSize(size + (e.Delta > 0 ? 15 : -15)); e.Handled=true; };
        Closing += delegate { quitting=true; timer.Stop(); if(!testMode) SaveSettings(); if(tray != null) { tray.Visible=false; tray.Dispose(); } };
    }

    void LoadFrames() {
        var atlas = new BitmapImage();
        atlas.BeginInit(); atlas.CacheOption = BitmapCacheOption.OnLoad;
        atlas.UriSource = new Uri(Path.Combine(Program.Root,"assets","pet-atlas.png")); atlas.EndInit(); atlas.Freeze();
        if (atlas.PixelWidth % 4 != 0 || atlas.PixelHeight % 2 != 0) throw new InvalidDataException("角色图集尺寸不正确。");
        int w=atlas.PixelWidth/4, h=atlas.PixelHeight/2;
        // The sleep cell has a few pixels from the neighbouring raised hand at its far right.
        // Use a tighter source rectangle for that cell, keeping its original position and scale.
        for(int i=0;i<8;i++) { frames[i] = new CroppedBitmap(atlas,new Int32Rect((i%4)*w,(i/4)*h,i==6?w-28:w,h)); frames[i].Freeze(); }
        var sports = new BitmapImage();
        sports.BeginInit(); sports.CacheOption=BitmapCacheOption.OnLoad;
        sports.UriSource=new Uri(Path.Combine(Program.Root,"assets","pingpong-atlas.png")); sports.EndInit(); sports.Freeze();
        if(sports.PixelWidth!=1536 || sports.PixelHeight!=1024) throw new InvalidDataException("乒乓动作图集尺寸不正确。");
        // Source rectangles follow the transparent gaps around each generated pose.
        Int32Rect[] regions={new Int32Rect(0,0,512,512),new Int32Rect(512,0,512,512),new Int32Rect(1024,0,512,512),new Int32Rect(0,512,568,512),new Int32Rect(568,512,492,512),new Int32Rect(1060,512,476,512)};
        for(int i=0;i<6;i++) { frames[i+8]=new CroppedBitmap(sports,regions[i]); frames[i+8].Freeze(); }
        for(int i=0;i<frames.Length;i++) spriteBounds[i]=AlphaBounds(frames[i]);
    }
    static Rect AlphaBounds(BitmapSource source) {
        var b=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);
        int w=b.PixelWidth,h=b.PixelHeight,minX=w,minY=h,maxX=0,maxY=0;
        byte[] pixels=new byte[w*h*4]; b.CopyPixels(pixels,w*4,0);
        for(int y=0;y<h;y++) for(int x=0;x<w;x++) if(pixels[(y*w+x)*4+3]>30) { minX=Math.Min(minX,x); minY=Math.Min(minY,y); maxX=Math.Max(maxX,x); maxY=Math.Max(maxY,y); }
        if(minX>maxX||minY>maxY) throw new InvalidDataException("Empty sprite");
        return new Rect(minX,minY,maxX-minX+1,maxY-minY+1);
    }
    void SetFrame(int i) { if(frame==i) return; frame=i; sprite.Source=frames[i]; }
    void LoadSettings() {
        homeX=double.NaN;
        if(testMode) return;
        try {
            var x=XElement.Load(Path.Combine(Program.Root,"data","settings.xml"));
            size=Math.Max(150,Math.Min(270,(double)x.Element("Size")));
            homeX=(double)x.Element("X"); homeY=(double)x.Element("Y");
            wander=(bool)x.Element("Wander");
            if(double.IsNaN(homeX)||double.IsInfinity(homeX)||double.IsNaN(homeY)||double.IsInfinity(homeY)||double.IsNaN(size)||double.IsInfinity(size)) { homeX=double.NaN; size=220; }
        } catch { homeX=double.NaN; }
    }
    void SaveSettings() {
        if(testMode||loading) return;
        try {
            string dir=Path.Combine(Program.Root,"data"); Directory.CreateDirectory(dir);
            var x=new XElement("Pet",new XElement("Size",size),new XElement("X",Left),new XElement("Y",Top),new XElement("Wander",wander));
            x.Save(Path.Combine(dir,"settings.xml"));
        } catch {}
    }
    Rect WorkingArea() {
        var source=PresentationSource.FromVisual(this);
        Matrix m=source==null?Matrix.Identity:source.CompositionTarget.TransformFromDevice;
        var monitor=Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
        var r=monitor.WorkingArea;
        Point a=m.Transform(new Point(r.Left,r.Top)), b=m.Transform(new Point(r.Right,r.Bottom));
        return new Rect(a,b);
    }
    void PlaceDefault() {
        Rect r=WorkingArea(); Left=r.Right-StageWidth-35; Top=r.Bottom-BaseLine-8; KeepVisible();
    }
    void KeepVisible() {
        Rect r=WorkingArea();
        double margin=(StageWidth-size)/2;
        Left=Math.Max(r.Left-margin,Math.Min(r.Right-margin-size,Left));
        Top=Math.Max(r.Top-(BaseLine-size*4/3-85),Math.Min(r.Bottom-BaseLine-4,Top));
    }
    void ChangeSize(double value) { size=Math.Max(150,Math.Min(270,value)); KeepVisible(); SaveSettings(); }
    void CreateTray() {
        tray=new Forms.NotifyIcon();
        try { tray.Icon=System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location); } catch { tray.Icon=System.Drawing.SystemIcons.Information; }
        tray.Text="白鼻子桌宠 · 双击挥手 / 右键菜单";
        var menu=new Forms.ContextMenuStrip();
        menu.Items.Add("挥挥手",null,delegate { StartAction("wave",3,"嗨！"); });
        menu.Items.Add("跳一跳",null,delegate { StartAction("jump",2.4,"起飞！"); });
        menu.Items.Add("闭眼吐舌头",null,delegate { StartAction("tongue",3,"略略略～"); });
        menu.Items.Add("侧身爆冲 · 撑地回位",null,delegate { StartAction("smash",4.4,"看这一拍！"); });
        menu.Items.Add("滑步救球 · 单手撑地",null,delegate { StartAction("save",4.6,"这球我来！"); });
        menu.Items.Add("回到屏幕右下角",null,delegate { PlaceDefault(); SaveSettings(); });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出桌宠",null,delegate { Close(); });
        tray.ContextMenuStrip=menu;
        tray.DoubleClick+=delegate { StartAction("wave",3,"我在这儿！"); };
        tray.Visible=true;
    }
    void ShowMenu() {
        var menu=new ContextMenu { FontFamily=new FontFamily("Microsoft YaHei"), FontSize=13 };
        AddItem(menu,"👋  挥挥手",delegate { StartAction("wave",3,"嗨！"); });
        AddItem(menu,"↑   跳一跳",delegate { StartAction("jump",2.4,"起飞！"); });
        AddItem(menu,"😝  闭眼吐舌头",delegate { StartAction("tongue",3,"略略略～"); });
        AddItem(menu,"🏓  侧身爆冲 · 撑地回位",delegate { StartAction("smash",4.4,"看这一拍！"); });
        AddItem(menu,"🏓  滑步救球 · 单手撑地",delegate { StartAction("save",4.6,"这球我来！"); });
        AddItem(menu,"☾   打个盹",delegate { StartAction("sleep",8,"眯一小会儿…"); });
        menu.Items.Add(new Separator());
        var walk=AddItem(menu,"允许自己散步",delegate { wander=!wander; if(!wander&&action=="walk") StartAction("idle",0,null); SaveSettings(); }); walk.IsCheckable=true; walk.IsChecked=wander;
        var pause=AddItem(menu,paused?"继续活动":"暂停动作",delegate { paused=!paused; StartAction("idle",0,null); nextAction=watch.Elapsed.TotalSeconds+5; }); pause.IsChecked=paused;
        var scale=new MenuItem { Header="桌宠大小" };
        foreach(int s in new int[]{160,220,270}) { int v=s; var item=new MenuItem{Header=s==160?"小号":s==220?"标准":"大号",IsCheckable=true,IsChecked=Math.Abs(size-s)<1}; item.Click+=delegate { ChangeSize(v); }; scale.Items.Add(item); }
        menu.Items.Add(scale);
        AddItem(menu,"回到屏幕右下角",delegate { PlaceDefault(); SaveSettings(); });
        menu.Items.Add(new Separator());
        AddItem(menu,"退出桌宠",Close);
        menu.PlacementTarget=sprite; menu.IsOpen=true;
    }
    MenuItem AddItem(ContextMenu menu,string label,Action a) { var item=new MenuItem { Header=label }; item.Click+=delegate { a(); }; menu.Items.Add(item); return item; }
    void MouseDownPet(object sender,MouseButtonEventArgs e) {
        if(e.ClickCount==2) { StartAction("jump",2.4,"蹦！"); e.Handled=true; return; }
        var p=Forms.Cursor.Position; dragX=p.X; dragY=p.Y; originalX=Left; originalY=Top;
        dragging=true; moved=false; sprite.CaptureMouse(); e.Handled=true;
    }
    void MouseMovePet(object sender,MouseEventArgs e) {
        if(!dragging) return;
        var p=Forms.Cursor.Position; var source=PresentationSource.FromVisual(this);
        Matrix m=source==null?Matrix.Identity:source.CompositionTarget.TransformFromDevice;
        Vector delta=m.Transform(new Vector(p.X-dragX,p.Y-dragY));
        if(Math.Abs(delta.X)+Math.Abs(delta.Y)>5) moved=true;
        if(moved) { Left=originalX+delta.X; Top=originalY+delta.Y; action="idle"; bubble.Visibility=Visibility.Hidden; }
    }
    void MouseUpPet(object sender,MouseButtonEventArgs e) {
        if(!dragging) return;
        dragging=false; sprite.ReleaseMouseCapture();
        KeepVisible(); SaveSettings();
        if(!moved) {
            string greeting=greetingIndex==0?"你好呀":"98-05-11";
            greetingIndex=1-greetingIndex;
            StartAction("wave",2.5,greeting);
        }
        nextAction=watch.Elapsed.TotalSeconds+8; e.Handled=true;
    }
    void StartAction(string name,double duration,string text) {
        action=name; actionStart=watch.Elapsed.TotalSeconds; actionLength=duration;
        if(name=="walk") walkDirection=random.Next(2)==0?-1:1;
        if(text!=null) { speech.Text=text; bubbleUntil=actionStart+Math.Min(duration,3); bubble.Visibility=Visibility.Visible; }
    }
    void Tick(object sender,EventArgs e) {
        if(quitting) return;
        double now=watch.Elapsed.TotalSeconds, dt=Math.Min(.08,now-previousTick); previousTick=now;
        if(smoke) {
            string[] seq={"wave","jump","tongue","walk","sleep","bow","smash","save","idle"};
            if(testStep<seq.Length && now>=smokeNext) { double duration=testStep==6?4.4:testStep==7?4.6:1; StartAction(seq[testStep],duration,null); smokeNext=now+duration; testStep++; }
            if(testStep==seq.Length && now>smokeNext+.3) { File.WriteAllText(Path.Combine(Program.Root,"data","smoke-test.txt"),"PASS: WPF transparent window, 9 actions including both complete sports sequences, animation timer and clean shutdown."); Close(); return; }
        }
        if(!dragging && action!="idle" && now-actionStart>=actionLength) { action="idle"; nextAction=now+random.Next(5,11); }
        if(!testMode && !paused && !dragging && action=="idle" && now>=nextAction) {
            int i; do { i=random.Next(idleActions.Length); } while(i==lastIdle || (!wander && idleActions[i]=="walk")); lastIdle=i;
            string name=idleActions[i];
            StartAction(name,name=="sleep"?7:name=="walk"?4:name=="jump"?2.4:name=="smash"?4.4:name=="save"?4.6:3,null);
        }
        if(!paused && !dragging && action=="walk") { Left+=walkDirection*30*dt; double before=Left; KeepVisible(); if(Math.Abs(before-Left)>.1) walkDirection=-walkDirection; }
        RenderPose(action,Math.Max(0,now-actionStart),now);
        if(now>bubbleUntil) bubble.Visibility=Visibility.Hidden;
    }
    void RenderPose(string state,double t,double now) {
        if((state=="smash"||state=="save") && !(dragging&&moved)) { RenderSports(state,t); return; }
        sportsEffects.Visibility=Visibility.Hidden;
        double bob=paused?0:Math.Sin(now*2.3)*1.5, angle=0,sx=1,sy=1,y=0;
        int f=0;
        if(state=="wave") { f=((int)(t/.18)%2==0)?1:2; angle=Math.Sin(t*5)*2; }
        if(state=="tongue") { f=3; angle=Math.Sin(t*5.5)*4; sx=1+Math.Sin(t*5)*.01; }
        if(state=="sleep") { f=6; sy=1+Math.Sin(t*2)*.015; }
        if(state=="walk") { f=walkDirection<0?4:5; bob=-Math.Abs(Math.Sin(t*9))*8; angle=Math.Sin(t*9)*3; }
        if(state=="bow") { f=6; sy=1-Math.Sin(Math.Min(1,t/3)*Math.PI)*.13; angle=Math.Sin(Math.Min(1,t/3)*Math.PI)*5; }
        if(state=="jump") {
            double phase=t%1.2;
            if(phase<.2) { double q=Math.Sin(phase/.2*Math.PI); sy=1-.12*q; sx=1+.08*q; }
            else if(phase<.95) { double q=(phase-.2)/.75; y=-Math.Sin(q*Math.PI)*78; f=7; sy=1.02; sx=.98; }
            else { double q=Math.Sin((phase-.95)/.25*Math.PI); sy=1-.1*q; sx=1+.06*q; }
        }
        if(dragging&&moved) { f=7; angle=Math.Sin(now*6)*3; }
        SetFrame(f);
        double h=size*4/3;
        sprite.Width=size*frames[f].PixelWidth/frames[0].PixelWidth; sprite.Height=h;
        Canvas.SetLeft(sprite,(StageWidth-size)/2); Canvas.SetTop(sprite,BaseLine-h+y+bob);
        sprite.RenderTransformOrigin=new Point(.5,.92);
        var tr=new TransformGroup(); tr.Children.Add(new ScaleTransform(sx,sy)); tr.Children.Add(new RotateTransform(angle)); sprite.RenderTransform=tr;
        zzz.Visibility=state=="sleep"?Visibility.Visible:Visibility.Hidden;
        zzz.Opacity=.55+.4*Math.Sin(t*2)*Math.Sin(t*2);
        Canvas.SetLeft(zzz,StageWidth/2+size*.32); Canvas.SetTop(zzz,BaseLine-h+20-Math.Sin(t*2)*5);
        bubble.Measure(new Size(320,70));
        Canvas.SetLeft(bubble,(StageWidth-bubble.DesiredSize.Width)/2);
        Canvas.SetTop(bubble,Math.Max(0,BaseLine-h-38+y));
    }
    static double Smooth(double t) { t=Math.Max(0,Math.Min(1,t)); return t*t*(3-2*t); }
    void RenderSports(string name,double t) {
        bool save=name=="save";
        double[] times=save?new double[]{0,.4,.85,1.1,1.6,2.4,3.15,3.75,4.6}:new double[]{0,.45,1.0,1.25,1.7,2.4,3.1,3.65,4.4};
        int[] poses=save?new int[]{8,9,10,11,12,12,13,8}:new int[]{8,9,10,11,12,12,13,8};
        double[] offsets=save?new double[]{0,-14,0,24,30,25,14,0,0}:new double[]{0,-10,16,24,25,20,10,0,0};
        double[] tilts=save?new double[]{0,-4,5,8,3,0,-2,0,0}:new double[]{0,-6,6,8,3,0,-2,0,0};
        int k=0; while(k<7 && t>=times[k+1]) k++;
        double u=Smooth((t-times[k])/(times[k+1]-times[k]));
        int f=poses[k]; SetFrame(f);
        double offset=offsets[k]+(offsets[k+1]-offsets[k])*u;
        double angle=tilts[k]+(tilts[k+1]-tilts[k])*u;
        double scale=size*1.25/512, width=frames[f].PixelWidth*scale, height=frames[f].PixelHeight*scale;
        sprite.Width=width; sprite.Height=height; sprite.RenderTransformOrigin=new Point(0,0);
        var rotation=new RotateTransform(angle,width/2,height/2); sprite.RenderTransform=rotation;
        Rect a=spriteBounds[f]; Rect rect=new Rect(a.X*scale,a.Y*scale,a.Width*scale,a.Height*scale);
        Rect bounds=rotation.TransformBounds(rect);
        double center=Math.Max(bounds.Width/2+7,Math.Min(StageWidth-bounds.Width/2-7,StageWidth/2+offset));
        Canvas.SetLeft(sprite,center-(bounds.Left+bounds.Right)/2);
        double hop=k==6?-Math.Sin(u*Math.PI)*9:0;
        Canvas.SetTop(sprite,BaseLine-20-bounds.Bottom+hop);
        zzz.Visibility=Visibility.Hidden;
        bubble.Measure(new Size(320,70)); Canvas.SetLeft(bubble,(StageWidth-bubble.DesiredSize.Width)/2);
        Canvas.SetTop(bubble,Math.Max(0,BaseLine-20-bounds.Height-43));
        DrawSportsEffects(t,times[2],center);
    }
    void DrawSportsEffects(double t,double hit,double center) {
        var drawing=new DrawingGroup();
        using(var dc=drawing.Open()) {
            // Fix the DrawingImage viewport so moving strokes do not stretch to fill it.
            dc.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,400,480));
            double flight=t-hit;
            if(flight>=0 && flight<.38) {
                double q=flight/.38, x=center+45+q*120, y=BaseLine-size*.42-q*38;
                var pen=new Pen(new SolidColorBrush(Color.FromArgb((byte)(160*(1-q)),255,230,180)),3);
                dc.DrawLine(pen,new Point(x-35,y+7),new Point(x-7,y+1));
                dc.DrawEllipse(Brushes.White,new Pen(Brushes.DarkOrange,1),new Point(x,y),4,4);
            }
        }
        drawing.Freeze(); sportsEffects.Source=new DrawingImage(drawing); sportsEffects.Visibility=Visibility.Visible;
    }
    void RunSelfTest() {
        try {
            for(int i=0;i<frames.Length;i++) {
                var bitmap=new FormatConvertedBitmap(frames[i],PixelFormats.Bgra32,null,0);
                byte[] bytes=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4]; bitmap.CopyPixels(bytes,bitmap.PixelWidth*4,0);
                int clear=0,solid=0;
                for(int p=3;p<bytes.Length;p+=4) { if(bytes[p]<10) clear++; if(bytes[p]>240) solid++; }
                if(clear<bytes.Length/4*.08 || solid<bytes.Length/4*.12) throw new Exception("Sprite alpha validation failed: "+i);
            }
            string[] poses={"idle","wave","wave","tongue","walk","walk","sleep","jump"};
            var visual=new DrawingVisual();
            using(var dc=visual.RenderOpen()) {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(229,235,242)),null,new Rect(0,0,1200,960));
                for(int i=0;i<8;i++) {
                    walkDirection=i==4?-1:1; RenderPose(poses[i],i==2?.2:i==7?.55:.1,0);
                    bubble.Visibility=Visibility.Hidden; stage.UpdateLayout();
                    var bmp=new RenderTargetBitmap(400,480,96,96,PixelFormats.Pbgra32); bmp.Render(stage);
                    dc.DrawImage(bmp,new Rect((i%4)*300,(i/4)*480,300,360));
                    var label=new FormattedText(new string[]{"待机","挥手 A","挥手 B","闭眼吐舌头","向左散步","向右散步","打盹","跳跃"}[i],CultureInfo.GetCultureInfo("zh-CN"),FlowDirection.LeftToRight,new Typeface("Microsoft YaHei"),18,Brushes.DarkSlateGray);
                    dc.DrawText(label,new Point((i%4)*300+96,(i/4)*480+390));
                }
            }
            var sheet=new RenderTargetBitmap(1200,960,96,96,PixelFormats.Pbgra32); sheet.Render(visual);
            var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(sheet));
            Directory.CreateDirectory(Path.Combine(Program.Root,"data"));
            using(var stream=File.Create(Path.Combine(Program.Root,"data","动作预览.png"))) encoder.Save(stream);
            ExportSportsPreview();
            File.WriteAllText(Path.Combine(Program.Root,"data","self-test.txt"),"PASS: 14 sprites loaded and alpha masks validated; old actions rendered; both sports sequences sampled at 30 fps at 3 sizes for viewport and ground bounds; sports preview rendered.");
            Close();
        } catch(Exception ex) { File.WriteAllText(Path.Combine(Program.Root,"data","self-test.txt"),"FAIL: "+ex); Environment.ExitCode=1; Close(); }
    }
    void ExportSportsPreview() {
        double originalSize=size;
        foreach(double s in new double[]{150,220,270}) {
            size=s;
            foreach(string state in new string[]{"smash","save"}) for(int n=0;n<138;n++) {
                RenderSports(state,n/30.0);
                Rect a=spriteBounds[frame]; double scale=size*1.25/512;
                Rect b=sprite.RenderTransform.TransformBounds(new Rect(a.X*scale,a.Y*scale,a.Width*scale,a.Height*scale));
                b.Offset(Canvas.GetLeft(sprite),Canvas.GetTop(sprite));
                if(b.Left<0||b.Right>StageWidth||b.Top<0||b.Bottom>BaseLine-19) throw new Exception("Sports animation exceeds viewport/ground: "+state+" "+s+" "+n);
            }
        }
        size=originalSize;
        double[] samples={.1,.65,1.1,1.45,1.95,2.7,3.35,4.1};
        string[] labels={"凝神准备","侧身蓄力","爆冲挥拍","压低重心","单手撑地","稳住回位","撑地起身","恢复站姿"};
        var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen()) {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(229,235,242)),null,new Rect(0,0,1200,820));
            for(int i=0;i<8;i++) {
                RenderSports("smash",samples[i]); bubble.Visibility=Visibility.Hidden; stage.UpdateLayout();
                var bmp=new RenderTargetBitmap(400,480,96,96,PixelFormats.Pbgra32); bmp.Render(stage);
                dc.DrawImage(bmp,new Rect(i%4*300,i/4*410,300,360));
                dc.DrawText(new FormattedText(labels[i],CultureInfo.GetCultureInfo("zh-CN"),FlowDirection.LeftToRight,new Typeface("Microsoft YaHei"),18,Brushes.DarkSlateGray),new Point(i%4*300+95,i/4*410+375));
            }
        }
        var sheet=new RenderTargetBitmap(1200,820,96,96,PixelFormats.Pbgra32); sheet.Render(visual);
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(sheet));
        using(var stream=File.Create(Path.Combine(Program.Root,"data","乒乓动作预览.png"))) encoder.Save(stream);
    }
}
}

