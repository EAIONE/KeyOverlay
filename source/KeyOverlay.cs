using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Microsoft.Win32;

// Windows port inspired by brookhong/KeyCastOW; see THIRD-PARTY.txt.
public class KeyOverlay : Window {
    [DllImport("user32.dll", SetLastError=true)] static extern IntPtr SetWindowsHookEx(int n, Hook h, IntPtr m, uint t);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h,int c,IntPtr w,IntPtr l);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string s);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h,int n);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h,int n,int v);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h,int id,uint mod,uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h,int id);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int k);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [StructLayout(LayoutKind.Sequential)] struct MouseData {public int X,Y;public uint MouseDataValue,Flags,Time;public IntPtr Extra;}
    delegate IntPtr Hook(int c,IntPtr w,IntPtr l);
    Hook hook,mouseHook; IntPtr hookHandle,mouseHandle, hwnd; Forms.NotifyIcon tray;
    HashSet<int> held = new HashSet<int>(); Border card; TextBlock label;
    Window settings; DispatcherTimer expiry; bool paused, closing, locked=true,hidden;
    readonly bool startup; bool recording; TextBox recordingBox;
    OverlayOptions options=new OverlayOptions();
    Rect cardScreen=Rect.Empty;double cardRadius=18;
    CheckBox lockCheck,visibleCheck;TextBlock stateText,saveStatus;
    readonly TripleClickTracker triple=new TripleClickTracker(Forms.SystemInformation.DoubleClickTime,Math.Max(4,Forms.SystemInformation.DoubleClickSize.Width));
    static string Executable {get{return Process.GetCurrentProcess().MainModule.FileName;}}
    double size=32, alpha=.88, duration=1.4;
    string config=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"KeyOverlayRounded","settings.txt");
    static Dictionary<int,string> names = new Dictionary<int,string>();
    public static string Format(IEnumerable<int> keys) {
        var list=keys.Distinct().ToList(); var parts=new List<string>();
        if(list.Any(k=>k==17||k==162||k==163)) parts.Add("Ctrl");
        if(list.Any(k=>k==18||k==164||k==165)) parts.Add("Alt");
        if(list.Any(k=>k==16||k==160||k==161)) parts.Add("Shift");
        if(list.Any(k=>k==91||k==92)) parts.Add("Win");
        foreach(int k in list.Where(k=>!Modifier(k)).OrderBy(k=>k)) {
            string s; if(names.TryGetValue(k,out s)) parts.Add(s);
            else if(k>=48&&k<=90) parts.Add(((char)k).ToString());
            else if(k>=96&&k<=105) parts.Add("Num "+(k-96));
            else parts.Add(((Forms.Keys)k).ToString());
        }
        return String.Join(" + ",parts);
    }
    static bool Modifier(int k) { return k==16||k==17||k==18||k==91||k==92||(k>=160&&k<=165); }
    static void InitNames() {
        names=UpstreamKeys.Create();
        names[17]="Ctrl"; names[18]="Alt"; names[32]="Space";
        string[] symbols={";","=",",","-",".","/","`"};
        for(int i=0;i<symbols.Length;i++) names[186+i]=symbols[i];
        names[219]="[";names[220]="\\";names[221]="]";names[222]="'";
    }
    public KeyOverlay() : this(false) {}
    public KeyOverlay(bool isStartup) {
        startup=isStartup; Icon=System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(System.Drawing.Icon.ExtractAssociatedIcon(Executable).Handle,Int32Rect.Empty,System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
        Title="按键屏显"; Width=460; Height=110; WindowStyle=WindowStyle.None;
        AllowsTransparency=true; Background=Brushes.Transparent; Topmost=true;
        ShowInTaskbar=false; ShowActivated=false; ResizeMode=ResizeMode.NoResize;
        Left=Math.Max(0,SystemParameters.WorkArea.Width/2-230); Top=SystemParameters.WorkArea.Bottom-160;
        LoadConfig();
        label=new TextBlock {Text="按键屏显",Foreground=Brushes.White,FontFamily=new FontFamily("Segoe UI"),FontWeight=FontWeights.SemiBold,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        card=new Border {Background=new SolidColorBrush(Color.FromRgb(27,30,38)),CornerRadius=new CornerRadius(18),Padding=new Thickness(24,12,24,12),Child=label,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        Content=card; Apply();if(hidden)card.Visibility=Visibility.Hidden;
        MouseLeftButtonDown+=(s,e)=>{if(!locked&&e.ClickCount==1){DragMove();SaveConfig();}};
        MouseRightButtonUp+=(s,e)=>OpenSettings();
        LocationChanged+=(s,e)=>UpdateCardBounds();SizeChanged+=(s,e)=>UpdateCardBounds();
        card.LayoutUpdated+=(s,e)=>UpdateCardBounds();
        SourceInitialized+=(s,e)=>{
            hwnd=new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd,-20,GetWindowLong(hwnd,-20)|0x08000000|0x80);
            ApplyLock(); HwndSource.FromHwnd(hwnd).AddHook(Messages);
            string hotkeyError;RegisterBindings(out hotkeyError);
            if(hotkeyError!=null) Dispatcher.BeginInvoke(new Action(()=>tray.ShowBalloonTip(5000,"快捷键不可用",hotkeyError,Forms.ToolTipIcon.Warning)));
            hook=OnKey; hookHandle=SetWindowsHookEx(13,hook,GetModuleHandle(null),0);
            if(hookHandle==IntPtr.Zero) {MessageBox.Show("无法监听键盘，错误："+Marshal.GetLastWin32Error());Close();}
            mouseHook=OnMouse;mouseHandle=SetWindowsHookEx(14,mouseHook,GetModuleHandle(null),0);
            if(mouseHandle==IntPtr.Zero)Dispatcher.BeginInvoke(new Action(()=>tray.ShowBalloonTip(5000,"鼠标三击不可用","可使用自定义快捷键或托盘菜单打开设置。",Forms.ToolTipIcon.Warning)));
        };
        expiry=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(80)};
        expiry.Tick+=(s,e)=>{
            held.RemoveWhere(k=>(GetAsyncKeyState(k)&0x8000)==0);
            if(hidden||(held.Count==0 && (DateTime.UtcNow-last).TotalSeconds>duration && locked)) card.Visibility=Visibility.Hidden;
        }; expiry.Start();
        tray=new Forms.NotifyIcon {Icon=System.Drawing.Icon.ExtractAssociatedIcon(Executable),Text="按键屏显 · 双击设置",Visible=true};
        var menu=new Forms.ContextMenuStrip();
        menu.Items.Add("设置",null,(s,e)=>Dispatcher.Invoke(new Action(OpenSettings)));
        menu.Items.Add("锁定 / 解锁",null,(s,e)=>Dispatcher.Invoke(new Action(ToggleLock)));
        menu.Items.Add("显示 / 隐藏",null,(s,e)=>Dispatcher.Invoke(new Action(ToggleHidden)));
        menu.Items.Add("暂停 / 恢复",null,(s,e)=>Dispatcher.Invoke(new Action(TogglePause)));
        menu.Items.Add("恢复按键显示与监听",null,(s,e)=>Dispatcher.Invoke(new Action(RecoverListening)));
        menu.Items.Add("退出",null,(s,e)=>Dispatcher.Invoke(new Action(Close)));
        tray.ContextMenuStrip=menu;tray.DoubleClick+=(s,e)=>Dispatcher.Invoke(new Action(OpenSettings));
        Closed+=(s,e)=>{closing=true;SaveConfig();expiry.Stop();if(settings!=null)settings.Close();UnhookWindowsHookEx(hookHandle);UnhookWindowsHookEx(mouseHandle);for(int i=1;i<=4;i++)UnregisterHotKey(hwnd,i);tray.Dispose();};
        Loaded+=(s,e)=>{UpdateCardBounds();if(!startup)OpenSettings();else Display("按键屏显已启动");};
    }
    DateTime last=DateTime.UtcNow;
    IntPtr OnKey(int code,IntPtr w,IntPtr l) {
        if(code>=0) {
            int key=Marshal.ReadInt32(l), msg=w.ToInt32();
            if(msg==0x100||msg==0x104) {
                if(!paused && !recording && held.Add(key)) {string text=Format(held);Dispatcher.BeginInvoke(new Action(()=>Display(text)));}
            } else if(msg==0x101||msg==0x105) held.Remove(key);
        }
        return CallNextHookEx(hookHandle,code,w,l);
    }
    void Display(string text) {if(hidden)return;label.Text=text;card.Visibility=Visibility.Visible;last=DateTime.UtcNow;}
    void Apply() {if(card==null)return;label.FontSize=size;card.Opacity=alpha;Width=Math.Max(460,size*17);Height=Math.Max(110,size*2.6);}
    void ApplyLock() {if(hwnd==IntPtr.Zero)return;int v=GetWindowLong(hwnd,-20);SetWindowLong(hwnd,-20,locked?v|0x20:v&~0x20);}
    void ToggleLock(){locked=!locked;ApplyLock();Display(locked?"已锁定":"拖动此处调整位置");SyncChecks();SaveConfig();}
    void ToggleHidden(){hidden=!hidden;if(hidden)NotifyState("屏显已隐藏");if(hidden)card.Visibility=Visibility.Hidden;else Display("按键屏显");SyncChecks();SaveConfig();}
    void TogglePause(){paused=!paused;if(paused)NotifyState("屏显已暂停");held.Clear();Display(paused?"已暂停":"已恢复");SyncChecks();}
    void SyncChecks(){if(lockCheck!=null)lockCheck.IsChecked=locked;if(visibleCheck!=null)visibleCheck.IsChecked=!hidden;if(stateText!=null)stateText.Text=recording?"正在录制快捷键；切换窗口会自动恢复显示":hidden?"当前屏显已隐藏":paused?"当前已暂停按键显示":"当前正在显示按键";if(tray!=null)tray.Text=recording?"按键屏显 · 录制快捷键":hidden?"按键屏显 · 已隐藏":paused?"按键屏显 · 已暂停":"按键屏显 · 正在显示";}
    IntPtr Messages(IntPtr h,int m,IntPtr w,IntPtr l,ref bool handled){if(m==0x312){handled=true;if(recording||!options.HotkeysEnabled)return IntPtr.Zero;switch(w.ToInt32()){case 1:ToggleLock();break;case 2:OpenSettings();break;case 3:TogglePause();break;case 4:ToggleHidden();break;}}return IntPtr.Zero;}
    void LoadConfig(){try{if(File.Exists(config))options=OverlayOptions.Load(File.ReadAllLines(config));size=options.Size;alpha=options.Alpha;locked=options.Locked;duration=options.Duration;hidden=options.Hidden;if(File.Exists(config)){Left=options.Left;Top=options.Top;}Rect desktop=new Rect(SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenTop,SystemParameters.VirtualScreenWidth,SystemParameters.VirtualScreenHeight);if(!desktop.IntersectsWith(new Rect(Left,Top,460,110))){Left=100;Top=100;}}catch{size=32;alpha=.88;locked=true;Left=100;Top=100;}}
    void SaveConfig(){try{options.Size=size;options.Alpha=alpha;options.Locked=locked;options.Duration=duration;options.Left=Left;options.Top=Top;options.Hidden=hidden;Directory.CreateDirectory(Path.GetDirectoryName(config));string temp=config+".tmp";File.WriteAllLines(temp,options.Serialize());if(File.Exists(config))File.Replace(temp,config,null);else File.Move(temp,config);}catch(Exception ex){if(saveStatus!=null)saveStatus.Text="配置未保存："+ex.Message;}}
    void UpdateCardBounds(){if(card==null||!IsLoaded||card.ActualWidth<=0||card.ActualHeight<=0)return;try{Point a=card.PointToScreen(new Point(0,0)),b=card.PointToScreen(new Point(card.ActualWidth,card.ActualHeight));cardScreen=new Rect(a,b);cardRadius=18*(b.X-a.X)/card.ActualWidth;}catch{cardScreen=Rect.Empty;}}
    bool InsideCard(int x,int y){if(hidden||card==null||!card.IsVisible||cardScreen.IsEmpty||!cardScreen.Contains(new Point(x,y)))return false;double r=Math.Min(cardRadius,Math.Min(cardScreen.Width,cardScreen.Height)/2);double dx=Math.Max(cardScreen.Left+r-x,Math.Max(0,x-(cardScreen.Right-r))),dy=Math.Max(cardScreen.Top+r-y,Math.Max(0,y-(cardScreen.Bottom-r)));return dx*dx+dy*dy<=r*r;}
    IntPtr OnMouse(int code,IntPtr w,IntPtr l){if(code>=0&&w.ToInt32()==0x201){var d=(MouseData)Marshal.PtrToStructure(l,typeof(MouseData));bool inside=InsideCard(d.X,d.Y);if(inside)last=DateTime.UtcNow;if(triple.Add(unchecked((uint)d.Time),d.X,d.Y,inside))Dispatcher.BeginInvoke(new Action(OpenSettings));}return CallNextHookEx(mouseHandle,code,w,l);}
    string[] BindingTexts(){return new[]{options.LockKey,options.SettingsKey,options.PauseKey,options.VisibilityKey};}
    bool RegisterBindings(out string error){error=null;if(!options.HotkeysEnabled||hwnd==IntPtr.Zero)return true;var texts=BindingTexts();for(int i=0;i<texts.Length;i++){HotkeySpec k=HotkeySpec.Parse(texts[i]);if(!RegisterHotKey(hwnd,i+1,k.Modifiers|0x4000,k.Key))error=(error==null?"":error+"\n")+texts[i]+" 被占用";}return error==null;}
    void UnregisterBindings(){for(int i=1;i<=4;i++)UnregisterHotKey(hwnd,i);}
    bool StartupEnabled(){try{using(var k=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))return k!=null&&String.Equals(k.GetValue(StartupEntry.ValueName) as string,StartupEntry.Command(Executable),StringComparison.OrdinalIgnoreCase);}catch{return false;}}
    void SetStartup(bool enabled){using(var k=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")){if(enabled)k.SetValue(StartupEntry.ValueName,StartupEntry.Command(Executable));else k.DeleteValue(StartupEntry.ValueName,false);}}
    TextBox AddHotkey(StackPanel panel,string title,string value){panel.Children.Add(new TextBlock{Text=title,Margin=new Thickness(0,10,0,4)});var box=new TextBox {Text=value,IsReadOnly=true,Padding=new Thickness(9,7,9,7),ToolTip="点击后直接按下组合键；按 Esc 取消。"};panel.Children.Add(box);box.PreviewMouseDown+=(s,e)=>BeginRecording(box);box.GotKeyboardFocus+=(s,e)=>{if(System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.Tab))BeginRecording(box);};box.LostKeyboardFocus+=(s,e)=>{if(recordingBox==box)EndRecording();};box.PreviewKeyDown+=(s,e)=>{
        if(!recording||recordingBox!=box)return;e.Handled=true;var key=e.Key==System.Windows.Input.Key.System?e.SystemKey:e.Key;if(key==System.Windows.Input.Key.Escape){System.Windows.Input.Keyboard.ClearFocus();EndRecording();return;}
        int vk=System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);if(Modifier(vk))return;var mods=System.Windows.Input.Keyboard.Modifiers;var parts=new List<string>();if((mods&System.Windows.Input.ModifierKeys.Control)!=0)parts.Add("Ctrl");if((mods&System.Windows.Input.ModifierKeys.Alt)!=0)parts.Add("Alt");if((mods&System.Windows.Input.ModifierKeys.Shift)!=0)parts.Add("Shift");if((mods&System.Windows.Input.ModifierKeys.Windows)!=0)parts.Add("Win");parts.Add(((Forms.Keys)vk).ToString());HotkeySpec spec;if(HotkeySpec.TryParse(String.Join("+",parts),out spec)){box.Text=spec.ToString();if(saveStatus!=null)saveStatus.Text="快捷键尚未保存，请点“应用快捷键”。";EndRecording();System.Windows.Input.Keyboard.ClearFocus();if(settings!=null)System.Windows.Input.FocusManager.SetFocusedElement(settings,null);}else if(saveStatus!=null)saveStatus.Text="请使用 Ctrl、Alt 或 Shift 加一个按键。";
      };return box;}
    void BeginRecording(TextBox box){if(recordingBox==box)return;EndRecording();recording=true;recordingBox=box;UnregisterBindings();held.Clear();box.SelectAll();SyncChecks();}
    void EndRecording(){if(!recording)return;recording=false;recordingBox=null;held.Clear();string error;RegisterBindings(out error);if(error!=null&&saveStatus!=null)saveStatus.Text="快捷键恢复失败："+error;SyncChecks();}
    void SettingsDeactivated(object sender,EventArgs e){EndRecording();if(settings!=null)System.Windows.Input.FocusManager.SetFocusedElement(settings,null);}
    void NotifyState(string text){if(tray!=null)tray.ShowBalloonTip(4000,text,"右击托盘图标，选择“恢复按键显示与监听”。",Forms.ToolTipIcon.Info);}
    void RecoverListening(){EndRecording();paused=false;hidden=false;held.Clear();UnhookWindowsHookEx(hookHandle);UnhookWindowsHookEx(mouseHandle);hook=OnKey;mouseHook=OnMouse;hookHandle=SetWindowsHookEx(13,hook,GetModuleHandle(null),0);mouseHandle=SetWindowsHookEx(14,mouseHook,GetModuleHandle(null),0);UnregisterBindings();string error;RegisterBindings(out error);Display(hookHandle==IntPtr.Zero?"键盘监听恢复失败":"已恢复按键显示与监听");SyncChecks();SaveConfig();if(hookHandle==IntPtr.Zero)NotifyState("键盘监听恢复失败，请退出后重新打开");else if(error!=null&&tray!=null)tray.ShowBalloonTip(4000,"快捷键不可用",error,Forms.ToolTipIcon.Warning);}

    void ApplyHotkeys(TextBox[] boxes){System.Windows.Input.Keyboard.ClearFocus();EndRecording();var keys=new List<string>();foreach(var box in boxes){HotkeySpec k;if(!HotkeySpec.TryParse(box.Text,out k)){saveStatus.Text="快捷键无效。";return;}keys.Add(k.ToString());}if(keys.Distinct().Count()!=4){saveStatus.Text="四项操作不能使用相同的快捷键。";return;}var previous=BindingTexts();UnregisterBindings();options.LockKey=keys[0];options.SettingsKey=keys[1];options.PauseKey=keys[2];options.VisibilityKey=keys[3];string error;if(!RegisterBindings(out error)){UnregisterBindings();options.LockKey=previous[0];options.SettingsKey=previous[1];options.PauseKey=previous[2];options.VisibilityKey=previous[3];string restore;RegisterBindings(out restore);saveStatus.Text="保存失败，保留原快捷键："+error;return;}SaveConfig();saveStatus.Text="快捷键已应用并保存。";}
    void Slider(StackPanel p,string title,double min,double max,double val,Action<double> change){var t=new TextBlock {Text=title,Margin=new Thickness(0,14,0,6)};p.Children.Add(t);var s=new Slider {Minimum=min,Maximum=max,Value=val,TickFrequency=1};s.ValueChanged+=(a,e)=>{change(s.Value);Apply();Display("Ctrl + Shift + A");SaveConfig();};p.Children.Add(s);}
    void OpenSettings(){
        if(closing)return;if(settings!=null){settings.WindowState=WindowState.Normal;settings.Topmost=true;settings.Show();settings.Activate();SetForegroundWindow(new WindowInteropHelper(settings).Handle);return;}
        settings=new Window {Title="按键屏显 · 设置 v2.1",Width=460,Height=Math.Min(850,SystemParameters.WorkArea.Height-60),MinHeight=420,ResizeMode=ResizeMode.CanResizeWithGrip,Topmost=true,WindowStartupLocation=WindowStartupLocation.CenterScreen,Background=new SolidColorBrush(Color.FromRgb(246,247,250)),FontFamily=new FontFamily("Microsoft YaHei UI"),FontSize=14};
        var p=new StackPanel {Margin=new Thickness(26),Focusable=true};settings.Content=new ScrollViewer{Content=p,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        p.Children.Add(new TextBlock {Text="按键屏显",FontSize=25,FontWeight=FontWeights.SemiBold});
        p.Children.Add(new TextBlock {Text="简洁圆角 · 三击色块打开设置",Foreground=Brushes.Gray,Margin=new Thickness(0,6,0,6)});
        Slider(p,"显示大小",16,80,size,v=>size=v);
        Slider(p,"不透明度（向左更透明）",15,100,alpha*100,v=>alpha=v/100);
        Slider(p,"松开后保留时间（秒）",.3,5,duration,v=>duration=v);
        lockCheck=new CheckBox {Content="锁定位置并让鼠标穿透",IsChecked=locked,Margin=new Thickness(0,20,0,12)};
        lockCheck.Checked+=(s,e)=>{if(!locked)ToggleLock();};lockCheck.Unchecked+=(s,e)=>{if(locked)ToggleLock();};p.Children.Add(lockCheck);
        visibleCheck=new CheckBox {Content="显示屏显（取消后保持隐藏）",IsChecked=!hidden,Margin=new Thickness(0,0,0,12)};
        visibleCheck.Checked+=(s,e)=>{if(hidden)ToggleHidden();};visibleCheck.Unchecked+=(s,e)=>{if(!hidden)ToggleHidden();};p.Children.Add(visibleCheck);
        var start=new CheckBox {Content="开机启动（登录 Windows 后静默运行）",IsChecked=StartupEnabled(),Margin=new Thickness(0,0,0,14)};
        start.Click+=(s,e)=>{try{SetStartup(start.IsChecked==true);if(saveStatus!=null)saveStatus.Text=start.IsChecked==true?"已启用开机启动。":"已关闭开机启动。";}catch(Exception ex){start.IsChecked=StartupEnabled();if(saveStatus!=null)saveStatus.Text="开机启动修改失败："+ex.Message;}};p.Children.Add(start);
        p.Children.Add(new TextBlock {Text="自定义快捷键",FontSize=18,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,8,0,0)});
        var enableHotkeys=new CheckBox {Content="启用全局快捷键（取消可避免误触暂停或隐藏）",IsChecked=options.HotkeysEnabled,Margin=new Thickness(0,10,0,0)};
        enableHotkeys.Click+=(s,e)=>{EndRecording();options.HotkeysEnabled=enableHotkeys.IsChecked==true;UnregisterBindings();string error;RegisterBindings(out error);SaveConfig();if(saveStatus!=null)saveStatus.Text=error??(options.HotkeysEnabled?"全局快捷键已启用。":"全局快捷键已关闭；仍可用托盘或三击打开设置。");};p.Children.Add(enableHotkeys);
        p.Children.Add(new TextBlock {Text="点击输入框，直接按组合键，再应用。",Foreground=Brushes.Gray,FontSize=12,Margin=new Thickness(0,5,0,0)});
        var boxes=new[]{AddHotkey(p,"锁定 / 解锁",options.LockKey),AddHotkey(p,"打开设置",options.SettingsKey),AddHotkey(p,"暂停 / 恢复",options.PauseKey),AddHotkey(p,"隐藏 / 显示",options.VisibilityKey)};
        var apply=new Button {Content="应用快捷键",Margin=new Thickness(0,14,0,0),Padding=new Thickness(10,7,10,7)};apply.Click+=(s,e)=>ApplyHotkeys(boxes);p.Children.Add(apply);
        saveStatus=new TextBlock {Text="外观调整自动保存。",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.SteelBlue,FontSize=12,Margin=new Thickness(0,9,0,0)};p.Children.Add(saveStatus);
        stateText=new TextBlock {Margin=new Thickness(0,14,0,6)};p.Children.Add(stateText);SyncChecks();
        var pause=new Button {Content="暂停 / 恢复按键显示",Padding=new Thickness(10,6,10,6)};pause.Click+=(s,e)=>TogglePause();p.Children.Add(pause);
        p.Children.Add(new TextBlock {Text="快速三击可见色块打开设置，锁定时同样有效。\n穿透时点击也会传给下方软件。隐藏后用快捷键或托盘恢复。\n解锁后可拖动色块；右下角托盘菜单可退出。",Foreground=Brushes.DimGray,FontSize=12,Margin=new Thickness(0,14,0,0),TextWrapping=TextWrapping.Wrap});
        var done=new Button {Content="完成，收起设置",Margin=new Thickness(0,18,0,0),Padding=new Thickness(10,6,10,6)};done.Click+=(s,e)=>settings.Close();p.Children.Add(done);
        settings.ContentRendered+=(s,e)=>p.Focus();
        settings.Deactivated+=SettingsDeactivated;
        settings.Closed+=(s,e)=>{EndRecording();settings=null;lockCheck=null;visibleCheck=null;stateText=null;saveStatus=null;SaveConfig();};Display("Ctrl + Shift + A");settings.Show();settings.Activate();SetForegroundWindow(new WindowInteropHelper(settings).Handle);
    }
    [STAThread] public static int Main(string[] args){
        InitNames();
        if(args.Contains("--test")) {
            if(Format(new[]{162,160,65})!="Ctrl + Shift + A")return 1;
            if(Format(new[]{163,162,67})!="Ctrl + C")return 2;
            if(Format(new[]{37})!="←")return 3;
            if(Format(new[]{91,164,112})!="Alt + Win + F1")return 4;
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-result.txt"),"PASS: modifier combinations, duplicate modifiers, arrows, Win/Alt/F1");return 0;
        }
        bool created;using(var mutex=new System.Threading.Mutex(true,"Local\\KeyOverlayRounded",out created)) {
            if(!created){MessageBox.Show("按键屏显已经运行，请双击右下角托盘图标，或三击可见色块打开设置。");return 0;}
            var app=new Application {ShutdownMode=ShutdownMode.OnMainWindowClose};app.Run(new KeyOverlay(args.Contains("--startup")));
        }return 0;
    }
}

