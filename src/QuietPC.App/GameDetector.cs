using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
namespace QuietPC.App;
sealed record DetectedGame(int Id,string Path);
static class GameDetector {
 static readonly HashSet<string> Known=new(StringComparer.OrdinalIgnoreCase){"cs2","dota2","deadlock","cyberpunk2077","valorant-win64-shipping","league of legends"};
 static readonly string[] Tools={"launcher","crash","helper","obs","wallpaper","blender","steamvr","3dmark","sdk"};
 static readonly int Session=CurrentSession();
 static int CurrentSession(){using var current=Process.GetCurrentProcess();return current.SessionId;}
 delegate bool EnumWindow(nint window,nint data);
 [DllImport("user32.dll")]static extern bool EnumWindows(EnumWindow callback,nint data);
 [DllImport("user32.dll")]static extern bool IsWindowVisible(nint window);
 [DllImport("user32.dll")]static extern nint GetWindow(nint window,uint command);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(nint window,out uint processId);
 [DllImport("kernel32.dll",SetLastError=true)]static extern nint OpenProcess(uint access,bool inherit,int processId);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool QueryFullProcessImageName(nint process,uint flags,StringBuilder path,ref int size);
 [DllImport("kernel32.dll")]static extern bool CloseHandle(nint handle);
 static HashSet<int> VisibleProcesses(){
  var result=new HashSet<int>();
  // Enumerate top-level windows once, rather than EnumWindows for each process.
  EnumWindows((window,_)=>{if(IsWindowVisible(window)&&GetWindow(window,4)==0){GetWindowThreadProcessId(window,out uint id);if(id<=int.MaxValue)result.Add((int)id);}return true;},0);
  return result;
 }
 public static string? Image(int id){
  var handle=OpenProcess(0x1000,false,id);if(handle==0)return null;
  try{int size=1024;var path=new StringBuilder(size);if(QueryFullProcessImageName(handle,0,path,ref size))return path.ToString();if(Marshal.GetLastWin32Error()!=122)return null;size=32768;path=new StringBuilder(size);return QueryFullProcessImageName(handle,0,path,ref size)?path.ToString():null;}
  finally{CloseHandle(handle);}
 }
 public static bool Running(IReadOnlyCollection<string> configured)=>Ids(configured).Length>0;
 public static int[] BackgroundIds(IReadOnlyCollection<string> configured)=>Find(configured,false,[]).Select(p=>p.Id).ToArray();
 public static int[] Ids(IReadOnlyCollection<string> configured)=>Detect(configured,[]).Select(p=>p.Id).ToArray();
 public static DetectedGame[] Detect(IReadOnlyCollection<string> configured,IReadOnlyList<QuietPC.Core.SteamGame> steam)=>Find(configured,true,steam);
 static DetectedGame[] Find(IReadOnlyCollection<string> configured,bool games,IReadOnlyList<QuietPC.Core.SteamGame> steam){
  if(!games&&configured.Count==0)return [];
  var paths=new HashSet<string>(configured,StringComparer.OrdinalIgnoreCase);
  var names=new HashSet<string>(configured.Select(Path.GetFileNameWithoutExtension).OfType<string>(),StringComparer.OrdinalIgnoreCase);
  var visible=games?VisibleProcesses():[];var result=new List<DetectedGame>();
  foreach(var process in Process.GetProcesses()){using(process){try{
   if(process.SessionId!=Session)continue;
   string name=process.ProcessName;bool known=games&&Known.Contains(name),manual=names.Contains(name);
   if(!known&&!manual&&(!games||(!visible.Contains(process.Id)&&steam.Count==0)))continue;
   string? path=Image(process.Id);if(path is null)continue;
   if(paths.Contains(path)||(known&&Known.Contains(Path.GetFileNameWithoutExtension(path)))){result.Add(new(process.Id,path));continue;}
   if(games&&steam.Any(g=>QuietPC.Core.SteamLibrary.Contains(g,path))&&!Tools.Any(t=>Path.GetFileName(path).Contains(t,StringComparison.OrdinalIgnoreCase))){result.Add(new(process.Id,path));continue;}
   if(games&&visible.Contains(process.Id)&&path.Contains("\\steamapps\\common\\",StringComparison.OrdinalIgnoreCase)&&!Tools.Any(t=>path.Contains(t,StringComparison.OrdinalIgnoreCase)))result.Add(new(process.Id,path));
  }catch{}}}
  return result.Take(200).ToArray();
 }
}
