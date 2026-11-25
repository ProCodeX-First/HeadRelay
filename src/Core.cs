// SPDX-License-Identifier: MIT AND ISC
using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace HeadRelay;

public readonly record struct V3(double X,double Y,double Z)
{
 public double Length=>Math.Sqrt(X*X+Y*Y+Z*Z);
 public bool Finite=>double.IsFinite(X)&&double.IsFinite(Y)&&double.IsFinite(Z)&&double.IsFinite(Length);
 public V3 Unit()=>Scale(1/Length);
 public V3 Scale(double a)=>new(X*a,Y*a,Z*a);
 public V3 Minus(V3 b)=>new(X-b.X,Y-b.Y,Z-b.Z);
 public double Dot(V3 b)=>X*b.X+Y*b.Y+Z*b.Z;
 public V3 Cross(V3 b)=>new(Y*b.Z-Z*b.Y,Z*b.X-X*b.Z,X*b.Y-Y*b.X);
}
public readonly record struct Q(double W,double X,double Y,double Z)
{
 public static Q Identity=>new(1,0,0,0);
 public double Norm=>Math.Sqrt(W*W+X*X+Y*Y+Z*Z);
 public Q Unit(){var n=Norm;return n>1e-12&&double.IsFinite(n)?new(W/n,X/n,Y/n,Z/n):Identity;}
 public Q Conjugate()=>new(W,-X,-Y,-Z);
 public static Q operator*(Q a,Q b)=>new(a.W*b.W-a.X*b.X-a.Y*b.Y-a.Z*b.Z,a.W*b.X+a.X*b.W+a.Y*b.Z-a.Z*b.Y,a.W*b.Y-a.X*b.Z+a.Y*b.W+a.Z*b.X,a.W*b.Z+a.X*b.Y-a.Y*b.X+a.Z*b.W);
 public (double Yaw,double Pitch,double Roll) Euler()=>
 (Math.Atan2(2*(W*Z+X*Y),1-2*(Y*Y+Z*Z)),Math.Asin(Math.Clamp(2*(W*Y-Z*X),-1,1)),Math.Atan2(2*(W*X+Y*Z),1-2*(X*X+Y*Y)));
}
public sealed record FrameData(V3 Right,V3 Up,bool InvertYaw=false,bool InvertPitch=false);
public sealed class AxisFrame
{
 public V3 Right{get;} public V3 Up{get;} public V3 Tilt{get;}
 public bool InvertYaw{get;set;} public bool InvertPitch{get;set;}
 private AxisFrame(V3 r,V3 u){Right=r;Up=u;Tilt=u.Cross(r);}
 public static AxisFrame? Create(V3 r,V3 u)
 {if(!r.Finite||!u.Finite||r.Length<.1||u.Length<.1)return null;r=r.Unit();u=u.Unit();if(Math.Abs(r.Dot(u))>.6)return null;u=u.Minus(r.Scale(u.Dot(r))).Unit();return new(r,u);}
 public Q Map(Q q){var v=new V3(q.X,q.Y,q.Z);return new Q(q.W,v.Dot(Tilt),v.Dot(Up),v.Dot(Right)).Unit();}
 public FrameData Data()=>new(Right,Up,InvertYaw,InvertPitch);
 public static AxisFrame? From(FrameData? d){if(d is null)return null;var f=Create(d.Right,d.Up);if(f is not null){f.InvertYaw=d.InvertYaw;f.InvertPitch=d.InvertPitch;}return f;}
 public static bool Movement(Q center,Q sample,out V3 axis)
 {var q=(center.Conjugate()*sample).Unit();if(q.W<0)q=new(-q.W,-q.X,-q.Y,-q.Z);axis=new(q.X,q.Y,q.Z);var angle=2*Math.Atan2(axis.Length,Math.Abs(q.W))*180/Math.PI;if(!axis.Finite||angle<10||angle>85)return false;axis=axis.Unit();return true;}
}
public sealed class AdapterDescriptor
{
 public string Id{get;set;}=""; public string Name{get;set;}="";
 public string VendorId{get;set;}="";public string ProductId{get;set;}="";public string Interface{get;set;}="05";public string Collection{get;set;}="01";
 public int InputLength{get;set;}=64; public int ReportId{get;set;}
 public int HeaderOffset{get;set;} public string HeaderHex{get;set;}="";
 public int XOffset{get;set;} public int YOffset{get;set;} public int ZOffset{get;set;} public int WOffset{get;set;}
 public double Scale{get;set;}=16384;public bool NeedsCalibration{get;set;}=true;
 private byte[] Header=>Convert.FromHexString(HeaderHex);
 public bool Valid()
 {try{return Id.Length is >0 and <80&&Name.Length is >0 and <100&&new[]{VendorId,ProductId}.All(v=>v.Length==4&&v.All(Uri.IsHexDigit))&&new[]{Interface,Collection}.All(v=>v.Length==2&&v.All(Uri.IsHexDigit))&&InputLength is >=8 and <=512&&ReportId is >=0 and <=255&&HeaderOffset>=0&&HeaderOffset+Header.Length<=InputLength&&new[]{XOffset,YOffset,ZOffset,WOffset}.All(i=>i>=0&&i+2<=InputLength)&&new[]{XOffset,YOffset,ZOffset,WOffset}.Distinct().Count()==4&&double.IsFinite(Scale)&&Scale>0;}catch{return false;}}
 public bool Matches(string p)=>p.Contains($"vid_{VendorId}&pid_{ProductId}&mi_{Interface}&col{Collection}",StringComparison.OrdinalIgnoreCase);
 public bool Decode(ReadOnlySpan<byte> b,out Q q)
 {q=Q.Identity;if(b.Length!=InputLength||b[0]!=ReportId||!b.Slice(HeaderOffset,Header.Length).SequenceEqual(Header))return false;
 var raw=new Q(BinaryPrimitives.ReadInt16LittleEndian(b.Slice(WOffset,2))/Scale,BinaryPrimitives.ReadInt16LittleEndian(b.Slice(XOffset,2))/Scale,BinaryPrimitives.ReadInt16LittleEndian(b.Slice(YOffset,2))/Scale,BinaryPrimitives.ReadInt16LittleEndian(b.Slice(ZOffset,2))/Scale);
 if(raw.Norm<.97||raw.Norm>1.03)return false;q=raw.Unit();return true;}
 public static List<AdapterDescriptor> Load(string dir,Action<string> log)
 {
 var list=new List<AdapterDescriptor>{
 new(){Id="jbl-quantum-950",Name="JBL Quantum 950",VendorId="0ecb",ProductId="2127",Collection="04",ReportId=0x86,HeaderOffset=11,HeaderHex="031D14002903",XOffset=25,YOffset=27,ZOffset=29,WOffset=31},
 new(){Id="jbl-quantum-910",Name="JBL Quantum 910",VendorId="0ecb",ProductId="2088",ReportId=5,HeaderHex="050803",XOffset=11,YOffset=13,ZOffset=15,WOffset=17,NeedsCalibration=false}};
 if(Directory.Exists(dir))foreach(var path in Directory.GetFiles(dir,"*.json"))try{
 if(new FileInfo(path).Length>65536)throw new Exception("Adapterdatei zu groß");
 var a=JsonSerializer.Deserialize<AdapterDescriptor>(File.ReadAllText(path));if(a is null||!a.Valid()||list.Any(x=>x.Id==a.Id))throw new Exception("Ungültiger oder doppelter Adapter");list.Add(a);
 }catch(Exception ex){log(Path.GetFileName(path)+": "+ex.Message);}
 return list;
 }
}
public sealed record DeviceInfo(string Path,string Name,AdapterDescriptor? Adapter,string Product,int ReportLength)
{
 public string DisplayName=>Name+(Adapter is null?" · Adapter fehlt":" · bereit");
 public bool Supported=>Adapter is not null;
 public override string ToString()=>Name;
}
public static class Hid
{
 const uint Present=2,DeviceInterface=0x10;
 public static List<DeviceInfo> Scan(List<AdapterDescriptor> adapters)
 {
 var found=new List<DeviceInfo>();
 foreach(var path in Paths()){
 var adapter=adapters.FirstOrDefault(a=>a.Matches(path));
 using var handle=CreateFile(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero);
 var product=new StringBuilder(128);int len=adapter?.InputLength??0;ushort page=0;
 if(!handle.IsInvalid){HidD_GetProductString(handle,product,256);if(HidD_GetPreparsedData(handle,out var pp))try{if(HidP_GetCaps(pp,out var caps)>=0){len=caps.InputReportByteLength;page=caps.UsagePage;}}finally{HidD_FreePreparsedData(pp);}}
 if(adapter is not null)found.Add(new(path,adapter.Name,adapter,product.ToString(),len));
 else if(page>=0xFF00&&product.Length>0&&new[]{"quantum","headset","headphone","audeze","steelseries","wh-1000","wf-1000","arctis"}.Any(s=>product.ToString().Contains(s,StringComparison.OrdinalIgnoreCase)))
 found.Add(new(path,product.ToString(),null,product.ToString(),len));
 }
 string Hardware(string p){var i=p.IndexOf("&mi_",StringComparison.OrdinalIgnoreCase);return (i<0?p:p[..i]).ToLowerInvariant();}
 var supported=found.Where(d=>d.Supported).Select(d=>Hardware(d.Path)).ToHashSet();
 return found.Where(d=>d.Supported||!supported.Contains(Hardware(d.Path))).GroupBy(d=>d.Adapter?.Id??d.Product).Select(g=>g.First()).OrderByDescending(d=>d.Adapter?.Id=="jbl-quantum-950").ThenByDescending(d=>d.Supported).ToList();
 }
 public static IEnumerable<string> Paths()
 {
 HidD_GetHidGuid(out var guid);var set=SetupDiGetClassDevs(ref guid,null,IntPtr.Zero,Present|DeviceInterface);if(set==new IntPtr(-1))yield break;
 try{for(uint member=0;;member++){var data=new InterfaceData{Size=Marshal.SizeOf<InterfaceData>()};if(!SetupDiEnumDeviceInterfaces(set,IntPtr.Zero,ref guid,member,ref data)){if(Marshal.GetLastWin32Error()==259)yield break;continue;}
 SetupDiGetDeviceInterfaceDetail(set,ref data,IntPtr.Zero,0,out var size,IntPtr.Zero);var detail=Marshal.AllocHGlobal((int)size);try{Marshal.WriteInt32(detail,IntPtr.Size==8?8:6);if(SetupDiGetDeviceInterfaceDetail(set,ref data,detail,size,out _,IntPtr.Zero)){var path=Marshal.PtrToStringUni(detail+4);if(path is not null)yield return path;}}finally{Marshal.FreeHGlobal(detail);}}}finally{SetupDiDestroyDeviceInfoList(set);}
 }
 public static FileStream Open(DeviceInfo d)
 {var h=CreateFile(d.Path,0x80000000,3,IntPtr.Zero,3,0x40000000,IntPtr.Zero);if(h.IsInvalid){var e=new Win32Exception(Marshal.GetLastWin32Error());h.Dispose();throw e;}return new FileStream(h,FileAccess.Read,d.Adapter!.InputLength,true);}
 [StructLayout(LayoutKind.Sequential)]struct InterfaceData{public int Size;public Guid Class;public int Flags;public IntPtr Reserved;}
 [StructLayout(LayoutKind.Sequential)]struct Caps{public ushort Usage,UsagePage,InputReportByteLength,OutputReportByteLength,FeatureReportByteLength;[MarshalAs(UnmanagedType.ByValArray,SizeConst=17)]public ushort[] Reserved;public ushort A,B,C,D,E,F,G,H,I,J;}
 [DllImport("hid.dll")]static extern void HidD_GetHidGuid(out Guid g);
 [DllImport("hid.dll",CharSet=CharSet.Unicode)]static extern bool HidD_GetProductString(SafeFileHandle d,StringBuilder b,int len);
 [DllImport("hid.dll")]static extern bool HidD_GetPreparsedData(SafeFileHandle h,out IntPtr p);
 [DllImport("hid.dll")]static extern bool HidD_FreePreparsedData(IntPtr p);
 [DllImport("hid.dll")]static extern int HidP_GetCaps(IntPtr p,out Caps c);
 [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr SetupDiGetClassDevs(ref Guid g,string? e,IntPtr h,uint f);
 [DllImport("setupapi.dll",SetLastError=true)]static extern bool SetupDiEnumDeviceInterfaces(IntPtr s,IntPtr d,ref Guid g,uint n,ref InterfaceData i);
 [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s,ref InterfaceData d,IntPtr p,uint size,out uint need,IntPtr i);
 [DllImport("setupapi.dll")]static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle CreateFile(string n,uint a,uint share,IntPtr sec,uint create,uint flags,IntPtr t);
}
public sealed class SensorReader:IDisposable
{
 readonly object sync=new();CancellationTokenSource? cancel;Task? task;Q latest=Q.Identity;DateTime last=DateTime.MinValue;Queue<(DateTime Time,Q Pose)> history=new();int count;DateTime rateAt=DateTime.UtcNow;public int Hz{get;private set;}public string Error{get;private set;}="";public long Rejected{get;private set;}
 public bool Running=>task is not null&&!task.IsCompleted;
 public (Q Pose,DateTime Time) Snapshot(){lock(sync)return(latest,last);}
 public bool StableSample(out Q q)
 {
 lock(sync){q=latest;var recent=history.Where(p=>p.Time>DateTime.UtcNow.AddMilliseconds(-450)).Select(p=>p.Pose).ToArray();if(recent.Length<4||last<DateTime.UtcNow.AddMilliseconds(-300))return false;
 var first=recent[0];double w=0,x=0,y=0,z=0;foreach(var p in recent){var sign=p.W*first.W+p.X*first.X+p.Y*first.Y+p.Z*first.Z<0?-1:1;w+=p.W*sign;x+=p.X*sign;y+=p.Y*sign;z+=p.Z*sign;}var average=new Q(w,x,y,z).Unit();q=average;
 return recent.All(p=>2*Math.Acos(Math.Clamp(Math.Abs(p.W*average.W+p.X*average.X+p.Y*average.Y+p.Z*average.Z),0,1))*180/Math.PI<4);}
 }
 public void Connect(DeviceInfo d)
 {if(Running)throw new InvalidOperationException("Sensor läuft bereits.");Error="";Rejected=0;count=0;rateAt=DateTime.UtcNow;Hz=0;lock(sync){last=DateTime.MinValue;history.Clear();}cancel=new();var token=cancel.Token;task=Task.Run(async()=>{
 try{await using var stream=Hid.Open(d);var buffer=new byte[d.Adapter!.InputLength];while(!token.IsCancellationRequested){var n=await stream.ReadAsync(buffer,token);if(n==0)break;if(!d.Adapter.Decode(buffer.AsSpan(0,n),out var q)){Rejected++;continue;}var now=DateTime.UtcNow;lock(sync){latest=q;last=now;history.Enqueue((now,q));while(history.Count>100)history.Dequeue();}count++;if((now-rateAt).TotalSeconds>=1){Hz=(int)Math.Round(count/(now-rateAt).TotalSeconds);count=0;rateAt=now;}}}
 catch(OperationCanceledException){}catch(Exception ex){Error=ex.Message;}});}
 public async Task Stop(){cancel?.Cancel();if(task is not null)try{await task;}catch{}cancel?.Dispose();cancel=null;task=null;Hz=0;}
 public void Dispose(){cancel?.Cancel();}
}
public sealed class MotionOptions
{
 public double GainYaw{get;set;}=1.5;public double GainPitch{get;set;}=1.3;public double SmoothingMs{get;set;}=70;public double DeadZone{get;set;}=.5;public double Curve{get;set;}=1.15;
 public bool InvertYaw{get;set;}public bool InvertPitch{get;set;}
 public void Validate(){GainYaw=Safe(GainYaw,.2,5,1.5);GainPitch=Safe(GainPitch,.2,5,1.3);SmoothingMs=Safe(SmoothingMs,0,250,70);DeadZone=Safe(DeadZone,0,5,.5);Curve=Safe(Curve,.5,2,1.15);}
 static double Safe(double n,double min,double max,double fallback)=>double.IsFinite(n)?Math.Clamp(n,min,max):fallback;
}
public sealed class MotionFilter
{
 double y,p;bool ready;
 public void Reset(){y=0;p=0;ready=false;}
 public static double Curve(double degrees,double gain,double dead,double curve)
 {var a=Math.Max(0,Math.Abs(degrees)-dead);return Math.Clamp(Math.Sign(degrees)*60*Math.Pow(a/60,curve)*gain,-175,175);}
 public (double Yaw,double Pitch) Apply(double yaw,double pitch,double dt,MotionOptions o)
 {yaw=Curve(yaw,o.GainYaw,o.DeadZone,o.Curve)*(o.InvertYaw?-1:1);pitch=Curve(pitch,o.GainPitch,o.DeadZone,o.Curve)*(o.InvertPitch?-1:1);var alpha=o.SmoothingMs<=0?1:1-Math.Exp(-Math.Clamp(dt,0,.1)/(o.SmoothingMs/1000));if(!ready){y=yaw;p=pitch;ready=true;}else{y+=(yaw-y)*alpha;p+=(pitch-p)*alpha;}return(y,p);}
}
public sealed class AppSettings
{
 public int Version{get;set;}=1;public string Output{get;set;}="direct";public string? LastDevice{get;set;}
 public Dictionary<string,FrameData> Frames{get;set;}=new();public MotionOptions Motion{get;set;}=new();
 public static readonly string DataDir=Path.Combine(AppContext.BaseDirectory,"Data");
 public static AppSettings Load(){try{var s=JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path.Combine(DataDir,"settings.json")));if(s is null||s.Version!=1)return new();if(s.Output!="direct"&&s.Output!="udp")s.Output="direct";s.Frames??=new();s.Motion??=new();s.Motion.Validate();return s;}catch{return new();}}
 public void Save(){Directory.CreateDirectory(DataDir);var p=Path.Combine(DataDir,"settings.json");File.WriteAllText(p+".tmp",JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));File.Move(p+".tmp",p,true);}
}
public sealed class UdpOutput:IDisposable
{
 readonly UdpClient udp=new(AddressFamily.InterNetwork);readonly byte[] packet=new byte[50];readonly IPEndPoint destination;
 public UdpOutput(int port=5555){destination=new(IPAddress.Loopback,port);}
 public void Write(double yaw,double pitch){packet[1]=2;BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(2),(float)(yaw*Math.PI/180));BinaryPrimitives.WriteSingleLittleEndian(packet.AsSpan(6),(float)(pitch*Math.PI/180));udp.Send(packet,packet.Length,destination);}
 public void Dispose(){udp.Dispose();}
}
public sealed record GameProfile(int Id,string Name,string KeyHex);
public sealed record GameKey(int Id,string Name,byte[] Key);
public sealed class DirectOutput:IDisposable
{
 public const string Heap="FT_SharedMem";public const string MutexName="FT_Mutext";
 readonly MemoryMappedFile mem;readonly MemoryMappedViewAccessor view;readonly Mutex mutex;readonly Dictionary<int,GameKey> games;
 Process? helper;int sequence=1;public string GameName{get;private set;}="Warte auf Spiel";public int GameId{get;private set;}
 public DirectOutput(string gamesPath,string heap=Heap,string mutexName=MutexName,bool startHelper=true)
 {games=ReadGames(gamesPath);mutex=new Mutex(false,mutexName);mem=MemoryMappedFile.CreateOrOpen(heap,108,MemoryMappedFileAccess.ReadWrite);view=mem.CreateViewAccessor(0,108);view.Write(4,100);view.Write(8,250);if(startHelper&&heap==Heap&&Process.GetProcessesByName("TrackIR").Length==0){var helperPath=Path.Combine(AppContext.BaseDirectory,"Bridge","TrackIR.exe");if(File.Exists(helperPath))helper=Process.Start(new ProcessStartInfo(helperPath){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,Arguments=Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),WorkingDirectory=Path.GetDirectoryName(helperPath)});}}
 public static Dictionary<int,GameKey> ReadGames(string path)
 {
 // Numeric interoperability parameters; no copied third-party game database.
 var result=new Dictionary<int,GameKey>{{3450,new(3450,"Star Citizen",Convert.FromHexString("344ECEF4A2B4C63E"))}};
 if(!File.Exists(path))return result;
 if(new FileInfo(path).Length>65536)throw new InvalidDataException("Spieleprofile sind zu groß.");
 var profiles=JsonSerializer.Deserialize<List<GameProfile>>(File.ReadAllText(path))??new();
 foreach(var p in profiles){if(p.Id<1||p.Id>65535||string.IsNullOrWhiteSpace(p.Name)||p.Name.Length>120||p.KeyHex is null||p.KeyHex.Length!=16||!p.KeyHex.All(Uri.IsHexDigit))throw new InvalidDataException("Ungültiges Spieleprofil.");result[p.Id]=new(p.Id,p.Name,Convert.FromHexString(p.KeyHex));}
 return result;
 }
 public void Write(double yaw,double pitch)
 {bool owned=false;try{try{owned=mutex.WaitOne(10);}catch(AbandonedMutexException){owned=true;}if(!owned)return;
 var id=view.ReadInt32(92);if(id!=GameId||view.ReadInt32(104)!=id){GameId=id;if(games.TryGetValue(id,out var g)){GameName=g.Name;view.WriteArray(96,g.Key,0,8);}else{GameName=id==0?"Warte auf Spiel":"Unbekanntes Spiel ("+id+")";view.WriteArray(96,new byte[8],0,8);}view.Write(104,id);}
 var y=(float)(-yaw*Math.PI/180);var safePitch=Math.Abs(pitch-90)<.15?89.86:pitch;var p=(float)(-safePitch*Math.PI/180);view.Write(12,y);view.Write(16,p);view.Write(20,0f);view.Write(24,0f);view.Write(28,0f);view.Write(32,0f);view.Write(36,y);view.Write(40,-p);for(int i=44;i<92;i+=4)view.Write(i,0f);view.Write(0,sequence++);
 }finally{if(owned)mutex.ReleaseMutex();}}
 public void Dispose(){try{Write(0,0);}catch{}view.Dispose();mem.Dispose();mutex.Dispose();if(helper is not null){try{if(!helper.HasExited)helper.Kill();}catch{}helper.Dispose();helper=null;}}
}
