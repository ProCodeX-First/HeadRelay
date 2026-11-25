// SPDX-License-Identifier: MIT
using Microsoft.Win32;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace HeadRelay;
public static class Entry
{
 [STAThread]public static int Main(string[] args)
 {
 try{
 if(args.Contains("--test"))return SelfTests.Run();
 try{using var legacy=Mutex.OpenExisting("Local\\AxisStudio.App");MessageBox.Show("Bitte die bisherige Axis-/Preview-Version zuerst schließen und HeadRelay danach erneut starten.","HeadRelay");return 0;}catch(WaitHandleCannotBeOpenedException){}
 using var instance=new Mutex(true,"Local\\HeadRelay.App",out var first);if(!first){MessageBox.Show("HeadRelay läuft bereits.","HeadRelay");return 0;}
 CultureInfo.DefaultThreadCurrentCulture=CultureInfo.GetCultureInfo("de-DE");
 var app=new Application();var window=(Window)XamlReader.Load(File.OpenRead(Path.Combine(AppContext.BaseDirectory,"Interface.xaml")));
 window.Icon=System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(Path.Combine(AppContext.BaseDirectory,"Assets","HeadRelay.ico")));
 var controller=new StudioController(window);app.Run(window);return 0;
 }catch(Exception ex){try{File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"Startfehler.txt"),ex.ToString());}catch{}MessageBox.Show("HeadRelay konnte nicht starten.\n\n"+ex.Message,"HeadRelay");return 1;}
 }
}
public sealed class StudioController
{
 readonly GameIntegration integration = new(Registry.CurrentUser, AppContext.BaseDirectory);
 bool integrationInstalled;
 readonly EventWaitHandle recenterRequest=new(false,EventResetMode.AutoReset,"Local\\HeadRelay.Recenter");
 readonly Window w;readonly SensorReader reader=new();readonly MotionFilter filter=new();readonly HeadPreview preview=new();readonly AppSettings settings=AppSettings.Load();
 readonly List<AdapterDescriptor> adapters;readonly List<string> events=new();List<DeviceInfo> devices=new();readonly DispatcherTimer tick=new(){Interval=TimeSpan.FromMilliseconds(16)};readonly DispatcherTimer refresh=new(){Interval=TimeSpan.FromSeconds(5)};
 DeviceInfo? device;AxisFrame? frame;Q center=Q.Identity,calCenter=Q.Identity;V3 calRight;int step;bool outputActive,scanning,capturing,closed,updating,gotSignal,calibrating;string page="Dashboard",lastError="";double lastTick=Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;DateTime footerUntil=DateTime.MinValue;UdpOutput? udp;DirectOutput? direct;HwndSource? hwndSource;
 T E<T>(string name) where T:FrameworkElement=>(T)w.FindName(name);
 TextBlock T(string name)=>E<TextBlock>(name);Button B(string name)=>E<Button>(name);
 public StudioController(Window window)
 {
 w=window;adapters=AdapterDescriptor.Load(Path.Combine(AppContext.BaseDirectory,"Adapters"),Log);E<Grid>("PreviewHost").Children.Add(preview);
 foreach(var name in new[]{"Dashboard","Devices","Calibration","Output","Settings","Diagnostics"}){var n=name;B("Nav"+n).Click+=(_,_)=>Show(n);}
 B("ConnectButton").Click+=async(_,_)=>await ToggleConnect();B("RefreshButton").Click+=async(_,_)=>await Scan();B("SetupButton").Click+=async(_,_)=>{if(!reader.Running)await Connect();BeginCalibration();};B("RecenterButton").Click+=(_,_)=>Recenter();B("StartButton").Click+=(_,_)=>ToggleOutput();B("CaptureButton").Click+=async(_,_)=>await Capture();B("ResetCalibrationButton").Click+=(_,_)=>BeginCalibration();
 B("InstallButton").Click+=(_,_)=>ConfigureIntegration();
 B("RestoreButton").Click+=(_,_)=>{try{StopOutput();integration.Restore();Log("Vorherige Spielanbindung wiederhergestellt.");Toast("Die vorherige Spielanbindung wurde wiederhergestellt.");}catch(Exception ex){Toast(ex.Message);}UpdateIntegration();};
 E<ComboBox>("DeviceChoice").SelectionChanged+=(_,_)=>{if(scanning)return;SelectDevice(E<ComboBox>("DeviceChoice").SelectedItem as DeviceInfo);};
 E<ListBox>("DevicesList").SelectionChanged+=(_,_)=>{if(scanning||reader.Running)return;if(E<ListBox>("DevicesList").SelectedItem is DeviceInfo d){E<ComboBox>("DeviceChoice").SelectedItem=devices.FirstOrDefault(x=>x.Path==d.Path);Show("Dashboard");}};
 B("ExportButton").Click+=(_,_)=>Export();B("OpenFolderButton").Click+=(_,_)=>Process.Start(new ProcessStartInfo(AppContext.BaseDirectory){UseShellExecute=true});
 E<RadioButton>("DirectRadio").Checked+=(_,_)=>SetOutput("direct");E<RadioButton>("UdpRadio").Checked+=(_,_)=>SetOutput("udp");
 B("DefaultsButton").Click+=(_,_)=>{settings.Motion=new();LoadMotionControls();filter.Reset();Save();Toast("Bewegung auf Standardwerte zurückgesetzt.");};
 LoadMotionControls();foreach(var name in new[]{"GainYaw","GainPitch","Smoothing","DeadZone","Curve"})E<Slider>(name+"Slider").ValueChanged+=(_,_)=>ReadMotionControls();
 foreach(var name in new[]{"InvertYaw","InvertPitch"}){E<CheckBox>(name+"Check").Checked+=(_,_)=>ReadMotionControls();E<CheckBox>(name+"Check").Unchecked+=(_,_)=>ReadMotionControls();}
 E<RadioButton>(settings.Output=="udp"?"UdpRadio":"DirectRadio").IsChecked=true;
 w.SourceInitialized+=(_,_)=>RegisterKeys();w.Closed+=(_,_)=>Close();w.PreviewKeyDown+=async(_,e)=>{if(e.Key==Key.Space&&page=="Calibration"){e.Handled=true;await Capture();}};
 tick.Tick+=(_,_)=>Update();refresh.Tick+=async(_,_)=>{if(!closed)await Scan();};tick.Start();refresh.Start();Show("Dashboard");UpdateIntegration();Log("HeadRelay 0.2.1 gestartet. Zwei integrierte JBL-Adapter.");w.Loaded+=async(_,_)=>await Scan();
 }
 void SelectDevice(DeviceInfo? d)
 {
 device=d;gotSignal=false;frame=null;filter.Reset();center=Q.Identity;
 if(d?.Adapter is not null){settings.LastDevice=d.Adapter.Id;if(settings.Frames.TryGetValue(d.Adapter.Id,out var f))frame=AxisFrame.From(f);else if(!d.Adapter.NeedsCalibration)frame=AxisFrame.Create(new(0,0,1),new(0,1,0));Save();}
 T("DeviceHint").Text=d is null?"Kein passendes Gerät gefunden. USB-Basisstation anschließen.":d.Supported?"USB erkannt · Verbinde das Headset für Sensordaten.":"Gerät erkannt. Für dieses Modell fehlt noch ein Signaladapter.";
 B("ConnectButton").IsEnabled=d?.Supported==true;UpdateCalibrationSummary();
 }
 async Task Scan()
 {
 if(scanning||closed)return;scanning=true;try{var found=await Task.Run(()=>Hid.Scan(adapters));if(closed)return;devices=found;var selected=device?.Path;E<ComboBox>("DeviceChoice").ItemsSource=devices;E<ListBox>("DevicesList").ItemsSource=devices;
 var choice=devices.FirstOrDefault(x=>x.Path==selected)??(string.IsNullOrWhiteSpace(settings.LastDevice)?null:devices.FirstOrDefault(x=>x.Adapter?.Id==settings.LastDevice))??devices.FirstOrDefault(x=>x.Supported)??devices.FirstOrDefault();E<ComboBox>("DeviceChoice").SelectedItem=choice;
 if(!reader.Running&&device?.Path!=choice?.Path)SelectDevice(choice);if(device is null&&choice is null)SelectDevice(null);
 T("SupportText").Text=$"{adapters.Count} Adapter geladen · {devices.Count(x=>x.Supported)} kompatible Geräte angeschlossen. Integriert: JBL Quantum 910 und 950. Weitere Modelle benötigen auslesbare Sensordaten und einen passenden Adapter; Anlernen ordnet anschließend die Bewegungsachsen zu.";
 }catch(Exception ex){Log("Gerätesuche: "+ex.Message);}finally{scanning=false;}
 }
 async Task ToggleConnect(){if(reader.Running){StopOutput();await reader.Stop();E<ComboBox>("DeviceChoice").IsEnabled=true;B("ConnectButton").Content="Headset verbinden";gotSignal=false;Toast("Headset getrennt.");}else await Connect();}
 async Task Connect()
 {
 if(device?.Adapter is null){Toast("Wähle ein unterstütztes Headset aus.");return;}if(reader.Running)return;
 await reader.Stop();reader.Connect(device);E<ComboBox>("DeviceChoice").IsEnabled=false;B("ConnectButton").Content="Verbindung trennen";lastError="";gotSignal=false;Log("Sensor öffnen: "+device.Name);if(frame is null){BeginCalibration();Toast("Sobald Sensordaten ankommen, kannst du dein Headset anlernen.");}
 }
 void BeginCalibration(){StopOutput();calibrating=true;step=0;Show("Calibration");UpdateWizard();}
 void UpdateWizard()
 {
 var titles=new[]{"Geradeaus schauen","Nach rechts drehen","Nach oben schauen","Dein Headset ist angelernt."};
 var descriptions=new[]{"Setze dich bequem hin. Schaue auf die Mitte deines Bildschirms und halte den Kopf aufrecht.","Drehe deinen Kopf etwa 20–40° nach rechts. Deine Nase zeigt nach rechts. Nicht das Ohr zur Schulter kippen.","Kehre zur Mitte zurück. Hebe dann das Kinn und schaue etwa 15–30° nach oben. Halte diese Position.","Kehre in deine normale Sitzposition zurück. Zentriere den Blick und prüfe die Bewegung in der Live-Vorschau."};
 T("StepNumber").Text=step<3?(step+1).ToString("00"):"✓";T("StepTitle").Text=titles[step];T("StepDescription").Text=descriptions[step];T("StepProgress").Text=step>=3?"01  ✓     02  ✓     03  ✓":$"SCHRITT {step+1} VON 3";B("CaptureButton").Content=step<3?"Position aufnehmen · 3 Sekunden":"Zentrieren und Vorschau öffnen";T("CaptureNote").Text=step<3?"Nach dem Klick hast du 3 Sekunden Zeit.":"Die Zuordnung ist für dieses Headset gespeichert.";
 }
 async Task Capture()
 {
 if(capturing)return;if(!reader.Running||reader.Snapshot().Time<DateTime.UtcNow.AddMilliseconds(-500)){Toast("Verbinde zuerst das Headset. Es müssen aktuelle Sensordaten ankommen.");return;}
 if(step==3){Recenter();Show("Dashboard");return;}StopOutput();calibrating=true;capturing=true;B("CaptureButton").IsEnabled=false;B("ResetCalibrationButton").IsEnabled=false;
 try{for(int n=3;n>0;n--){T("CaptureNote").Text=$"Position halten … {n}";await Task.Delay(1000);if(closed)return;}
 if(!reader.StableSample(out var sample)){T("CaptureNote").Text="Noch zu viel Bewegung oder kein Signal. Kurz ruhig halten und erneut aufnehmen.";return;}
 if(step==0){calCenter=sample;step=1;Log("Kalibrierung: neutrale Position aufgenommen.");}
 else if(step==1){if(!AxisFrame.Movement(calCenter,sample,out calRight)){T("CaptureNote").Text="Bitte deutlicher nach rechts drehen (10–85°) und erneut aufnehmen.";return;}step=2;Log("Kalibrierung: Rechtsdrehung aufgenommen.");}
 else{if(!AxisFrame.Movement(calCenter,sample,out var up)){T("CaptureNote").Text="Bitte deutlicher nach oben schauen und erneut aufnehmen.";return;}var result=AxisFrame.Create(calRight,up);if(result is null){T("CaptureNote").Text="Die Bewegungen ähneln sich zu sehr. Bitte von vorne beginnen und Drehen von Nicken trennen.";return;}
 frame=result;calibrating=false;center=calCenter;filter.Reset();if(device?.Adapter is not null)settings.Frames[device.Adapter.Id]=frame.Data();Save();step=3;Log("Kalibrierung gespeichert: Dreh-, Nick- und Kippachse getrennt.");UpdateCalibrationSummary();}
 UpdateWizard();
 }finally{capturing=false;B("CaptureButton").IsEnabled=true;B("ResetCalibrationButton").IsEnabled=true;}
 }
 void Recenter(){var s=reader.Snapshot();if(s.Time<DateTime.UtcNow.AddMilliseconds(-500)){Toast("Zentrieren braucht ein aktuelles Sensorsignal.");return;}center=s.Pose;filter.Reset();Toast("Blick zentriert.");Log("Blick zentriert.");}
 void ToggleOutput()
 {
 if(outputActive){StopOutput();return;}if(!reader.Running||reader.Snapshot().Time<DateTime.UtcNow.AddMilliseconds(-500)){Toast("Zuerst das Headset verbinden und auf Sensordaten warten.");return;}if(frame is null||calibrating){Show("Calibration");Toast("Schließe zuerst das Anlernen der Bewegungen ab.");return;}
 try{if(settings.Output=="direct"){
 if(Process.GetProcessesByName("opentrack").Length>0){Show("Output");Toast("Ein anderer Tracker läuft. Bitte vor der direkten Ausgabe schließen.");return;}
 if(!integration.Installed()){Show("Output");Toast("Richte zuerst die direkte Spielanbindung ein.");return;}
 direct=new DirectOutput(Path.Combine(AppContext.BaseDirectory,"Profiles","games.json"));
 }else udp=new UdpOutput();outputActive=true;B("StartButton").Content="Tracking pausieren";Log("Tracking gestartet: "+settings.Output);Toast(settings.Output=="direct"?"Direkte Ausgabe aktiv. Das Spiel kann jetzt auf TrackIR zugreifen.":"UDP-Ausgabe aktiv.");
 }catch(Exception ex){StopOutput();Toast("Ausgabe konnte nicht starten: "+ex.Message);Log(ex.ToString());}
 }
 void StopOutput(){if(outputActive)Log("Tracking pausiert.");outputActive=false;try{direct?.Dispose();}catch{}direct=null;try{udp?.Write(0,0);udp?.Dispose();}catch{}udp=null;B("StartButton").Content="Tracking starten";}
 void SetOutput(string name){if(updating)return;StopOutput();settings.Output=name;Save();UpdateIntegration();}
 void ConfigureIntegration()
 {
     try
     {
         string? modules = integration.FindOpenTrack();
         if (modules == null)
         {
             var picker = new OpenFileDialog
             {
                 Title = "OpenTrack auswählen · installierte opentrack.exe öffnen",
                 Filter = "OpenTrack (opentrack.exe)|opentrack.exe",
                 CheckFileExists = true
             };
             if (picker.ShowDialog(w) != true) return;
             if (!string.Equals(Path.GetFileName(picker.FileName), "opentrack.exe", StringComparison.OrdinalIgnoreCase))
                 throw new InvalidDataException("Wähle die opentrack.exe deiner OpenTrack-Installation.");
             modules = Path.Combine(Path.GetDirectoryName(picker.FileName)!, "modules");
         }
         integration.Install(modules);
         Log("OpenTrack-Spielanbindung eingerichtet. Vorherige Pfade gesichert.");
         Toast("Jetzt Tracking starten und danach das Spiel neu starten.");
     }
     catch (Exception ex) { Toast(ex.Message); Log(ex.Message); }
     finally { UpdateIntegration(); }
 }
 void UpdateIntegration(){integrationInstalled=integration.Installed();T("IntegrationState").Text=integrationInstalled?"✓ Direkte Spielanbindung eingerichtet":"Noch nicht eingerichtet · einmaliger Schritt";T("OutputSummary").Text=settings.Output=="udp"?"UDP-Ausgabe":"Direkt zum Spiel";B("InstallButton").Content=integrationInstalled?"Spielanbindung erneut einrichten":"Spielanbindung einrichten";}
 void LoadMotionControls(){updating=true;var m=settings.Motion;E<Slider>("GainYawSlider").Value=m.GainYaw;E<Slider>("GainPitchSlider").Value=m.GainPitch;E<Slider>("SmoothingSlider").Value=m.SmoothingMs;E<Slider>("DeadZoneSlider").Value=m.DeadZone;E<Slider>("CurveSlider").Value=m.Curve;E<CheckBox>("InvertYawCheck").IsChecked=m.InvertYaw;E<CheckBox>("InvertPitchCheck").IsChecked=m.InvertPitch;updating=false;MotionLabels();}
 void ReadMotionControls(){if(updating)return;var m=settings.Motion;m.GainYaw=E<Slider>("GainYawSlider").Value;m.GainPitch=E<Slider>("GainPitchSlider").Value;m.SmoothingMs=E<Slider>("SmoothingSlider").Value;m.DeadZone=E<Slider>("DeadZoneSlider").Value;m.Curve=E<Slider>("CurveSlider").Value;m.InvertYaw=E<CheckBox>("InvertYawCheck").IsChecked==true;m.InvertPitch=E<CheckBox>("InvertPitchCheck").IsChecked==true;MotionLabels();Save();}
 void MotionLabels(){var m=settings.Motion;T("GainYawLabel").Text=$"Links / Rechts · {m.GainYaw:F2}×";T("GainPitchLabel").Text=$"Oben / Unten · {m.GainPitch:F2}×";T("SmoothingLabel").Text=$"Glättung · {m.SmoothingMs:F0} ms";T("DeadZoneLabel").Text=$"Ruhezone · {m.DeadZone:F1}°";T("CurveLabel").Text=$"Bewegungskurve · {m.Curve:F2}";}
 void Update()
 {
 if(closed)return;if(recenterRequest.WaitOne(0))Recenter();var now=Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;var dt=now-lastTick;lastTick=now;var sample=reader.Snapshot();bool live=reader.Running&&sample.Time>DateTime.UtcNow.AddMilliseconds(-750);
 if(live&&!gotSignal){gotSignal=true;center=sample.Pose;filter.Reset();Log("Gültige Rotationsdaten empfangen.");}
 if(!string.IsNullOrEmpty(reader.Error)&&lastError!=reader.Error){lastError=reader.Error;StopOutput();Log("Sensorfehler: "+lastError);Toast("Sensor konnte nicht gelesen werden: "+lastError);E<ComboBox>("DeviceChoice").IsEnabled=true;B("ConnectButton").Content="Erneut verbinden";}
 var yaw=0d;var pitch=0d;if(live&&frame is not null){var mapped=frame.Map((center.Conjugate()*sample.Pose).Unit()).Euler();(yaw,pitch)=filter.Apply(mapped.Yaw*180/Math.PI*(frame.InvertYaw?-1:1),mapped.Pitch*180/Math.PI*(frame.InvertPitch?-1:1),dt,settings.Motion);}
 preview.SetPose(yaw,pitch,live&&frame is not null);T("YawValue").Text=$"{yaw:F1}°";T("PitchValue").Text=$"{pitch:F1}°";T("RateValue").Text=live?$"{reader.Hz} Hz":"— Hz";
 T("PreviewBadge").Text=live?(frame is null?"SENSOR LIVE":"LIVE VORSCHAU"):"KEIN SIGNAL";
 T("PreviewHint").Text=live?(frame is null?"Lerne die Achsen deines Headsets an.":"Ausgabe nach Kalibrierung und Glättung."):"Verbinde dein Headset für eine Live-Vorschau.";
 T("CalibrationSignal").Text=live?$"● Sensorsignal aktiv · {reader.Hz} Hz":"Kein aktuelles Sensorsignal. USB-Basisstation und Headtracking prüfen.";
 T("TopStatus").Text=live?(outputActive?"Tracking aktiv · "+device?.Name:"Sensor verbunden · "+device?.Name):reader.Running?"Warte auf Sensorsignal":device is not null?"Headset erkannt · noch nicht verbunden":"Bereit für dein Headset";
 E<System.Windows.Shapes.Ellipse>("StatusDot").Fill=new SolidColorBrush(live?Color.FromRgb(148,227,194):Color.FromRgb(126,139,146));
 T("DeviceHint").Text=live?$"Rotationsdaten empfangen · {reader.Hz} Hz":reader.Running?"Warte auf Sensordaten. Headtracking in der Herstellersoftware aktivieren.":device?.Supported==true?"USB erkannt · Headset verbinden.":device is null?"USB-Basisstation anschließen und Geräte suchen.":"Für dieses Modell fehlt ein Signaladapter.";
 B("RecenterButton").IsEnabled=live;B("StartButton").IsEnabled=live&&frame is not null&&!calibrating;T("GameSummary").Text=direct?.GameName??(settings.Output=="udp"?(outputActive?"Lokal an Port 5555":"UDP-Empfänger an Port 5555"):(integrationInstalled?"TrackIR-Brücke bereit":"Spielanbindung noch einrichten"));
 if(outputActive){if(!live){StopOutput();Toast("Sensorsignal unterbrochen. Tracking wurde pausiert.");}else try{direct?.Write(yaw,pitch);udp?.Write(yaw,pitch);}catch(Exception ex){StopOutput();Toast("Ausgabefehler: "+ex.Message);Log(ex.ToString());}}
 if(DateTime.UtcNow>footerUntil)T("FooterText").Text=live?(frame is null?"Headset anlernen, um die Bewegungsachsen zuzuordnen.":outputActive?"Tracking aktiv · F9 zentriert · F10 pausiert":"Vorschau aktiv · Tracking starten, wenn du bereit bist."):"Headset verbinden, dann die Kalibrierung starten.";
 }
 void UpdateCalibrationSummary(){T("CalibrationSummary").Text=frame is null?"Noch keine Kalibrierung":device?.Adapter?.NeedsCalibration==false&&!settings.Frames.ContainsKey(device.Adapter.Id)?"Standardausrichtung bereit":"Persönlich kalibriert";}
 void Show(string name){page=name;foreach(var n in new[]{"Dashboard","Devices","Calibration","Output","Settings","Diagnostics"}){E<Grid>("Page"+n).Visibility=n==name?Visibility.Visible:Visibility.Collapsed;B("Nav"+n).Background=n==name?new SolidColorBrush(Color.FromRgb(37,51,47)):Brushes.Transparent;B("Nav"+n).Foreground=n==name?new SolidColorBrush(Color.FromRgb(148,227,194)):new SolidColorBrush(Color.FromRgb(169,184,191));}if(name=="Calibration")UpdateWizard();if(name=="Output")UpdateIntegration();}
 void Toast(string s){T("FooterText").Text=s;footerUntil=DateTime.UtcNow.AddSeconds(9);}
 void Log(string s){events.Add($"{DateTime.Now:HH:mm:ss}  {s}");if(events.Count>250)events.RemoveAt(0);if(w.FindName("LogText") is TextBox box){box.Text=string.Join(Environment.NewLine,events);box.ScrollToEnd();}}
 void Save(){try{settings.Save();}catch(Exception ex){Log("Einstellungen konnten nicht gespeichert werden: "+ex.Message);}}
 void Export(){try{Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory,"Diagnose"));var path=Path.Combine(AppContext.BaseDirectory,"Diagnose","Axis-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".json");var report=new{App="HeadRelay 0.2.1",Time=DateTimeOffset.Now,Devices=devices.Select(d=>new{d.Name,d.Product,Adapter=d.Adapter?.Id,d.ReportLength}),Sensor=new{reader.Running,reader.Hz,reader.Error,reader.Rejected},Output=settings.Output,OutputActive=outputActive,Calibration=frame?.Data(),Motion=settings.Motion,Events=events};File.WriteAllText(path,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Toast("Diagnose gespeichert: "+Path.GetFileName(path));Log("Diagnose gespeichert: "+path);}catch(Exception ex){Toast(ex.Message);}}
 void RegisterKeys(){var handle=new WindowInteropHelper(w).Handle;int dark=1;try{DwmSetWindowAttribute(handle,20,ref dark,4);}catch{}hwndSource=HwndSource.FromHwnd(handle);hwndSource?.AddHook(Hook);if(!RegisterHotKey(handle,901,0x4000,0x78))Log("F9 ist bereits belegt; Zentrieren über die Schaltfläche möglich.");if(!RegisterHotKey(handle,902,0x4000,0x79))Log("F10 ist bereits belegt; Pause über die Schaltfläche möglich.");}
 IntPtr Hook(IntPtr h,int msg,IntPtr wp,IntPtr lp,ref bool handled){if(msg==0x0312){if(wp.ToInt32()==901)Recenter();else if(wp.ToInt32()==902)ToggleOutput();handled=true;}return IntPtr.Zero;}
 void Close(){closed=true;recenterRequest.Dispose();tick.Stop();refresh.Stop();StopOutput();reader.Dispose();Save();var h=new WindowInteropHelper(w).Handle;UnregisterHotKey(h,901);UnregisterHotKey(h,902);if(hwndSource is not null)hwndSource.RemoveHook(Hook);}
 [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
 [DllImport("user32.dll",SetLastError=true)]static extern bool RegisterHotKey(IntPtr h,int id,uint modifiers,uint key);
 [DllImport("user32.dll")]static extern bool UnregisterHotKey(IntPtr h,int id);
}
public sealed class HeadPreview:FrameworkElement
{
 double yaw,pitch;bool active;
 public void SetPose(double y,double p,bool a){if(Math.Abs(y-yaw)<.03&&Math.Abs(p-pitch)<.03&&a==active)return;yaw=y;pitch=p;active=a;InvalidateVisual();}
 protected override void OnRender(DrawingContext dc)
 {
 base.OnRender(dc);var width=ActualWidth;var height=ActualHeight;if(width<10||height<10)return;dc.PushClip(new RectangleGeometry(new Rect(0,0,width,height)));
 var scale=Math.Min(width/4.4,height/3.8);var cy=height*.48;double ry=(yaw+18)*Math.PI/180,rx=(pitch-7)*Math.PI/180;
 Point Project(double x,double y,double z,bool rotate=true){if(rotate){var xx=x*Math.Cos(ry)+z*Math.Sin(ry);z=-x*Math.Sin(ry)+z*Math.Cos(ry);x=xx;var yy=y*Math.Cos(rx)-z*Math.Sin(rx);z=y*Math.Sin(rx)+z*Math.Cos(rx);y=yy;}var p=4.5/(4.5+z);return new(width*.5+x*scale*p,cy-y*scale*p);}
 var grid=new Pen(new SolidColorBrush(Color.FromRgb(37,51,53)),1);for(int i=-3;i<=3;i++){dc.DrawLine(grid,Project(i*.7,-1.5,-2,false),Project(i*.7,-1.5,2,false));dc.DrawLine(grid,Project(-2.5,-1.5,i*.65,false),Project(2.5,-1.5,i*.65,false));}
 var wire=new Pen(new SolidColorBrush(active?Color.FromRgb(93,153,135):Color.FromRgb(73,99,96)),.8);var accent=new Pen(new SolidColorBrush(active?Color.FromRgb(148,227,194):Color.FromRgb(118,158,145)),2.3);
 for(int lat=1;lat<11;lat++){double a=Math.PI*lat/11;Point? prev=null;for(int i=0;i<=64;i++){double b=2*Math.PI*i/64;var p=Project(.73*Math.Sin(a)*Math.Cos(b),1.04*Math.Cos(a),.65*Math.Sin(a)*Math.Sin(b));if(prev is Point v)dc.DrawLine(wire,v,p);prev=p;}}
 for(int lon=0;lon<16;lon++){double b=2*Math.PI*lon/16;Point? prev=null;for(int i=0;i<=36;i++){double a=Math.PI*i/36;var p=Project(.73*Math.Sin(a)*Math.Cos(b),1.04*Math.Cos(a),.65*Math.Sin(a)*Math.Sin(b));if(prev is Point v)dc.DrawLine(wire,v,p);prev=p;}}
 for(int side=-1;side<=1;side+=2){Point? prev=null;for(int i=0;i<=48;i++){double a=2*Math.PI*i/48;var p=Project(side*.84,.05+.38*Math.Cos(a),.02+.24*Math.Sin(a));if(prev is Point v)dc.DrawLine(accent,v,p);prev=p;}}
 Point? last=null;for(int i=0;i<=48;i++){double a=Math.PI*i/48;var p=Project(.88*Math.Cos(a),.14+1.15*Math.Sin(a),.02);if(last is Point v)dc.DrawLine(accent,v,p);last=p;}
 var nose=new[]{Project(-.12,.05,-.64),Project(0,-.02,-.97),Project(.12,.05,-.64)};dc.DrawLine(accent,nose[0],nose[1]);dc.DrawLine(accent,nose[1],nose[2]);dc.DrawLine(accent,Project(-.16,-.4,-.61),Project(.16,-.4,-.61));dc.Pop();
 }
}
