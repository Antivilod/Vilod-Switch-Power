using System.Diagnostics;
using System.IO;
using QuietPC.Core;
namespace QuietPC.App;
sealed class FrameCapture:IDisposable {
 readonly Process process;readonly string path;readonly string session;
 public FrameCapture(int pid,int seconds,bool elevated){
  string folder=Path.Combine(PortableMode.DataDirectory,"Measurements");Directory.CreateDirectory(folder);session="VSP_"+Guid.NewGuid().ToString("N");path=Path.Combine(folder,session+".csv");
  string tool=Environment.ProcessPath??throw new IOException("Не найден процесс VSP");
  var start=new ProcessStartInfo(tool){UseShellExecute=elevated,CreateNoWindow=!elevated,WindowStyle=ProcessWindowStyle.Hidden};if(elevated)start.Verb="runas";
  foreach(string argument in new[]{"--vsp-capture",pid.ToString(),seconds.ToString(),session})start.ArgumentList.Add(argument);
  process=Process.Start(start)??throw new IOException("Не удалось запустить PresentMon");
 }
 public FrameSample[] Read(int pid){
  if(File.Exists(path+".error"))throw new IOException(File.ReadAllText(path+".error"));
  if(!File.Exists(path)){if(process.HasExited)throw new IOException("PresentMon завершился без данных. Нужны права администратора или доступ к журналам производительности Windows.");return [];}
  try{return ReadShared(path,pid);}catch(IOException ex)when(!process.HasExited&&((ex.HResult&0xffff)==32||(ex.HResult&0xffff)==33)){return [];}
 }
 internal static FrameSample[] ReadShared(string path,int pid){using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);if(file.Length>100_000_000)throw new IOException("Слишком большой файл замера");using var reader=new StreamReader(file);string text=reader.ReadToEnd();if(text.Length==0||!text.Contains('\n'))return [];return FrameAnalysis.Parse(text,pid);}
 public bool HasExited=>process.HasExited;
 public string CsvPath=>path;
 public static async Task Worker(string[] args){
  if(args.Length!=4||!int.TryParse(args[1],out int pid)||pid<=0||!int.TryParse(args[2],out int seconds)||seconds is <10 or >180||!args[3].StartsWith("VSP_")||!Guid.TryParseExact(args[3][4..],"N",out _))return;
  string folder=Path.Combine(PortableMode.DataDirectory,"Measurements");Directory.CreateDirectory(folder);string output=Path.Combine(folder,args[3]+".csv");
  try{
   string tool=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","tools","PresentMon.exe"));
   var start=new ProcessStartInfo(tool){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Path.GetDirectoryName(tool)!};
   foreach(string argument in new[]{"--process_id",pid.ToString(),"--output_stdout","--timed",seconds.ToString(),"--terminate_after_timed","--session_name",args[3],"--v1_metrics","--no_console_stats","--no_track_input","--no_track_gpu"})start.ArgumentList.Add(argument);
   using var child=Process.Start(start)??throw new IOException("Не удалось запустить PresentMon");
   var error=child.StandardError.ReadToEndAsync();
   using var file=new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.ReadWrite|FileShare.Delete);
   using var writer=new StreamWriter(file,new System.Text.UTF8Encoding(false)){AutoFlush=true};
   var pump=Task.Run(async()=>{while(await child.StandardOutput.ReadLineAsync() is string line)await writer.WriteLineAsync(line);});
   var clock=Stopwatch.StartNew();
   while(!child.HasExited&&!File.Exists(output+".stop")&&clock.Elapsed.TotalSeconds<seconds+10)await Task.Delay(100);
   if(!child.HasExited){
    var stopInfo=new ProcessStartInfo(tool){UseShellExecute=false,CreateNoWindow=true};
    foreach(string argument in new[]{"--session_name",args[3],"--terminate_existing_session"})stopInfo.ArgumentList.Add(argument);
    using var stop=Process.Start(stopInfo);if(stop is not null)await Task.WhenAny(stop.WaitForExitAsync(),Task.Delay(2000));
    await Task.WhenAny(child.WaitForExitAsync(),Task.Delay(3000));
    if(!child.HasExited){child.Kill();await child.WaitForExitAsync();}
   }await pump;
   string detail=await error;if(child.ExitCode!=0&&!File.Exists(output+".stop"))throw new IOException("PresentMon: "+(string.IsNullOrWhiteSpace(detail)?"ошибка сбора кадров":detail.Trim()));
  }catch(Exception ex){try{File.WriteAllText(output+".error",ex.Message);}catch{}}
 }
 public void Dispose(){
  // The capture worker owns exactly one child and polls only its own stop marker.
  try{if(!process.HasExited)File.WriteAllText(path+".stop","");}
  catch{}process.Dispose();
 }
}
