// SPDX-License-Identifier: MIT
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Markup;
namespace HeadRelay;
public static class SelfTests
{
 static int count;static readonly List<string> notes=new();
 static void Check(bool c,string why){count++;if(!c)throw new Exception(why);}
 static Q Rotation(V3 axis,double angle){var v=axis.Unit().Scale(Math.Sin(angle/2));return new(Math.Cos(angle/2),v.X,v.Y,v.Z);}
 [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int RegisterProfile(ushort id);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int ReadPose(IntPtr p);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int NativeControl();
 public static int Run()
 {
 var root=AppContext.BaseDirectory;var report=Path.Combine(root,"Tests","TestResults.txt");Directory.CreateDirectory(Path.GetDirectoryName(report)!);
 try{
 var adapters=AdapterDescriptor.Load(Path.Combine(root,"Adapters"),_=>{});Check(adapters.Count>=2,"Built-in adapters present");Check(adapters.All(a=>a.Valid()),"Built-in descriptors valid");
 var a950=adapters.Single(a=>a.Id=="jbl-quantum-950");var a910=adapters.Single(a=>a.Id=="jbl-quantum-910");
 Check(a950.Matches(@"\\?\hid#vid_0ecb&pid_2127&mi_05&col04#test"),"950 device matching");Check(!a950.Matches("vid_0ecb&pid_2088&mi_05&col04"),"Do not confuse 910 and 950");
 var samples=File.ReadAllLines(Path.Combine(root,"Tests","950-packets.txt"));Check(samples.Length==10,"Ten real 950 samples");
 foreach(var hex in samples){var b=Convert.FromHexString(hex);Check(a950.Decode(b,out var q),"Decode observed 950 input");Check(Math.Abs(q.Norm-1)<1e-12,"Quaternion normalized");Check(!a910.Decode(b,out _),"Wrong model rejected");Check(!a950.Decode(b.AsSpan(0,32),out _),"Truncated sample rejected");var bad=(byte[])b.Clone();bad[0]=0;Check(!a950.Decode(bad,out _),"Invalid report rejected");bad=(byte[])b.Clone();Array.Clear(bad,25,8);Check(!a950.Decode(bad,out _),"Zero quaternion rejected");var old=new byte[64];old[0]=5;old[1]=8;old[2]=3;b.AsSpan(25,8).CopyTo(old.AsSpan(11));Check(a910.Decode(old,out var legacy)&&legacy==q,"Legacy decoder retained");Check((q.Conjugate()*q).Euler() is var zero&&Math.Abs(zero.Yaw)+Math.Abs(zero.Pitch)+Math.Abs(zero.Roll)<1e-12,"Center is neutral");}
 foreach(var axes in new[]{(R:new V3(0,0,1),U:new V3(0,1,0)),(R:new V3(0,0,1),U:new V3(1,0,0)),(R:new V3(0,-1,0),U:new V3(0,0,-1)),(R:new V3(1,0,1),U:new V3(0,1,0))})
 {
 var frame=AxisFrame.Create(axes.R,axes.U)!;Check(frame is not null,"Frame construction");var neutral=Rotation(new(1,2,3),.7);
 foreach(var item in new[]{(Axis:frame!.Right,Index:0),(Axis:frame.Up,Index:1),(Axis:frame.Tilt,Index:2)})
 {
 var motion=Rotation(item.Axis,.4);var sample=neutral*motion;
 Check(AxisFrame.Movement(neutral,sample,out var measured)&&measured.Dot(item.Axis)>.99999,"Calibration captures physical local axis");
 var e=frame.Map(neutral.Conjugate()*sample).Euler();var actual=new[]{e.Yaw,e.Pitch,e.Roll};for(int i=0;i<3;i++)Check(Math.Abs(actual[i]-(i==item.Index?.4:0))<1e-10,"Turning, nodding and tilting separated");
 Check(AxisFrame.Movement(neutral,new(-sample.W,-sample.X,-sample.Y,-sample.Z),out var flipped)&&flipped.Dot(item.Axis)>.99999,"Quaternion sign invariant");
 }
 var saved=System.Text.Json.JsonSerializer.Deserialize<FrameData>(System.Text.Json.JsonSerializer.Serialize(frame.Data()));var loaded=AxisFrame.From(saved);Check(loaded is not null&&loaded.Right.Dot(frame.Right)>.99999,"Calibration persists");
 }
 Check(AxisFrame.Create(new(1,0,0),new(1,0,0)) is null,"Repeated motion rejected");Check(AxisFrame.Create(new(0,0,0),new(0,1,0)) is null,"Missing axis rejected");Check(!AxisFrame.Movement(Q.Identity,Q.Identity,out _),"No-motion capture rejected");Check(!AxisFrame.Movement(Q.Identity,Rotation(new(0,0,1),.02),out _),"Tiny motion rejected");
 var m=new MotionOptions();m.Validate();Check(MotionFilter.Curve(.2,2,.5,1)==0,"Dead zone");Check(Math.Abs(MotionFilter.Curve(20,2,0,1)-40)<1e-10,"Gain");Check(MotionFilter.Curve(1000,4,0,1)==175,"Angle clamp");var filter=new MotionFilter();var first=filter.Apply(0,0,.016,m);var next=filter.Apply(60,0,.016,m);Check(next.Yaw>0&&next.Yaw<MotionFilter.Curve(60,m.GainYaw,m.DeadZone,m.Curve),"Time-aware smoothing");m.GainYaw=double.NaN;m.SmoothingMs=double.PositiveInfinity;m.Validate();Check(double.IsFinite(m.GainYaw)&&double.IsFinite(m.SmoothingMs),"Corrupt options sanitized");
 var games=DirectOutput.ReadGames(Path.Combine(root,"Profiles","games.json"));Check(games[3450].Name=="Star Citizen","Star Citizen game ID");Check(Convert.ToHexString(games[3450].Key)=="344ECEF4A2B4C63E","Encryption table byte order matches upstream");Check(games.Count>=1,"Built-in game profile retained without vendor database");
 var name="HeadRelay.Test."+Guid.NewGuid();using(var output=new DirectOutput(Path.Combine(root,"Profiles","games.json"),name,name+"Mutex",false))using(var map=MemoryMappedFile.OpenExisting(name))using(var view=map.CreateViewAccessor())
 {view.Write(92,3450);output.Write(30,15);Check(output.GameName=="Star Citizen","Game handshake");Check(view.ReadInt32(104)==3450,"Handshake ack");Check(Math.Abs(view.ReadSingle(12)+Math.PI/6)<1e-6,"Direct yaw sign and radians");Check(Math.Abs(view.ReadSingle(16)+Math.PI/12)<1e-6,"Direct pitch sign and radians");Check(view.ReadSingle(20)==0&&view.ReadSingle(24)==0,"Tilt and translation suppressed");var key=new byte[8];view.ReadArray(96,key,0,8);Check(key.SequenceEqual(games[3450].Key),"Key memory layout");var seq=view.ReadInt32(0);output.Write(5,5);Check(view.ReadInt32(0)>seq,"Frame sequence advances");}
 notes.Add("FreeTrack shared-memory layout, handshake and angle convention: PASS");
 // Native DLL test uses a unique heap: never touches a running tracking session.
 var nativeHeap="HeadRelay.NativeTest."+Guid.NewGuid();
 Environment.SetEnvironmentVariable("HEADRELAY_BRIDGE_HEAP",nativeHeap);
 var dll=NativeLibrary.Load(Path.Combine(root,"Bridge","NPClient64.dll"));try{
 var register=Marshal.GetDelegateForFunctionPointer<RegisterProfile>(NativeLibrary.GetExport(dll,"NP_RegisterProgramProfileID"));var read=Marshal.GetDelegateForFunctionPointer<ReadPose>(NativeLibrary.GetExport(dll,"NP_GetData"));
 var controls=new[]{"NP_StartDataTransmission","NP_StopDataTransmission","NP_ReCenter","NP_UnregisterWindowHandle"};foreach(var export in controls)Check(NativeLibrary.TryGetExport(dll,export,out _),"Native export "+export);
 using(var output=new DirectOutput(Path.Combine(root,"Profiles","games.json"),nativeHeap,nativeHeap+"Mutex",false)){
 register(3450);output.Write(30,15);var pointer=Marshal.AllocHGlobal(400);try{
 Check(read(pointer)==0,"Native client returns enabled status");var b=new byte[68];Marshal.Copy(pointer,b,0,68);var key=games[3450].Key;byte state=0x88;int k=0;for(int i=b.Length-1;i>=0;i--){var plain=(byte)(b[i]^key[k]^state);b[i]=plain;state=unchecked((byte)(state+i+plain));k=(k+1)%8;}
 Check(BitConverter.ToInt16(b,0)==0,"Encrypted status decoded");Check(Math.Abs(BitConverter.ToSingle(b,16)+2730.5)<.02,"Native yaw encryption and scale");Check(Math.Abs(BitConverter.ToSingle(b,12)+1365.25)<.02,"Native pitch encryption and scale");
 // Neutral pose stays enabled; moving is not a prerequisite for tracking.
 output.Write(0,0);Check(read(pointer)==0,"Neutral pose remains enabled");
 var signature=Marshal.GetDelegateForFunctionPointer<ReadPose>(NativeLibrary.GetExport(dll,"NP_GetSignature"));Check(signature(pointer)!=0,"Vendor signature is explicitly unsupported, not forged");
 notes.Add("HeadRelay native x64 bridge, unique heap, handshake and encrypted pose: PASS");
 }finally{Marshal.FreeHGlobal(pointer);}}
 }finally{NativeLibrary.Free(dll);Environment.SetEnvironmentVariable("HEADRELAY_BRIDGE_HEAP",null);}

 // UDP compatibility: inspect an actual loopback datagram independently.
 using(var receiver=new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback,0))){receiver.Client.ReceiveTimeout=1500;using(var sender=new UdpOutput(((System.Net.IPEndPoint)receiver.Client.LocalEndPoint!).Port))sender.Write(30,15);System.Net.IPEndPoint remote=new(System.Net.IPAddress.Any,0);var packet=receiver.Receive(ref remote);Check(packet.Length==50&&packet[0]==0&&packet[1]==2,"UDP packet format");Check(Math.Abs(BitConverter.ToSingle(packet,2)-Math.PI/6)<1e-6,"UDP yaw radians");Check(Math.Abs(BitConverter.ToSingle(packet,6)-Math.PI/12)<1e-6,"UDP pitch radians");Check(System.Net.IPAddress.IsLoopback(remote.Address),"UDP loopback only");}
 // The native API can request an actual center event.
 var recenterName="Local\\HeadRelay.RecenterTest."+Guid.NewGuid();
 Environment.SetEnvironmentVariable("HEADRELAY_BRIDGE_RECENTER",recenterName);
 using(var request=new EventWaitHandle(false,EventResetMode.AutoReset,recenterName)){
 var library=NativeLibrary.Load(Path.Combine(root,"Bridge","NPClient64.dll"));try{var re=Marshal.GetDelegateForFunctionPointer<NativeControl>(NativeLibrary.GetExport(library,"NP_ReCenter"));Check(re()==0&&request.WaitOne(500),"Native recenter event reaches application");}finally{NativeLibrary.Free(library);Environment.SetEnvironmentVariable("HEADRELAY_BRIDGE_RECENTER",null);}}

 var window=(Window)XamlReader.Load(File.OpenRead(Path.Combine(root,"Interface.xaml")));foreach(var n in new[]{"PageDashboard","PageDevices","PageCalibration","PageOutput","PageSettings","PageDiagnostics","CaptureButton","ConnectButton","StartButton","DeviceChoice","InstallButton","RestoreButton"})Check(window.FindName(n) is not null,"UI control: "+n);window.Close();
 Check(GameIntegration.NPKey==@"SOFTWARE\NaturalPoint\NATURALPOINT\NPClient Location","Registry path is correct");
 var result="PASS: "+count+" checks\n"+string.Join("\n",notes)+"\nNo registry values were changed by these tests.\n";File.WriteAllText(report,result);Console.WriteLine(result);return 0;
 }catch(Exception ex){var result="FAIL after "+count+" checks\n"+ex;File.WriteAllText(report,result);Console.Error.WriteLine(result);return 1;}
 }
}
