using System.Diagnostics;
using System.IO;
using System.Text.Json;
using QuietPC.Core;
namespace QuietPC.App;
sealed class MonitorPreferences {
 public int MonitorIntervalSeconds{get;set;}=2;
 public HashSet<string> MonitoringHidden{get;set;}=[];
 public int HistorySeconds{get;set;}=60;
 public string[] Graphs{get;set;}=["cpu.load","gpu.current","ram.load","live.fps"];
 public bool NetworkProbeEnabled{get;set;}=true;
 public string NetworkHost{get;set;}="";
 public bool AdvancedSensors{get;set;}
 public void Validate(){if(!new[]{1,2,5,10}.Contains(MonitorIntervalSeconds)||MonitoringHidden is null||MonitoringHidden.Count>10000||!new[]{30,60,120,300}.Contains(HistorySeconds)||Graphs is null||Graphs.Length>4||Graphs.Any(s=>s is null||s.Length>512)||NetworkHost is null||NetworkHost.Length>253)throw new IOException("Неверные настройки мониторинга");}
}
sealed class SensorProcess:IDisposable {
 Process? process;readonly CancellationTokenSource stop=new();
 int disposed;
 public DateTimeOffset ReceivedAt{get;private set;}
 public MonitorMetric[] Metrics{get;private set;}=[];
 public string Status{get;private set;}="Подключение датчиков…";
 sealed record Frame(MonitorMetric[] Metrics,string Status);
 public SensorProcess(bool advanced,int interval=2){
  try{
   var start=new ProcessStartInfo(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","monitor","VSP.Monitor.exe"))){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
   start.ArgumentList.Add("--parent");start.ArgumentList.Add(Environment.ProcessId.ToString());if(advanced)start.ArgumentList.Add("--advanced");
   start.ArgumentList.Add("--interval");start.ArgumentList.Add(interval.ToString());
   process=Process.Start(start)??throw new IOException("Помощник мониторинга не запущен");
   _=Read();_=process.StandardError.ReadToEndAsync(stop.Token);
  }catch(Exception ex){Status="Дополнительные датчики недоступны: "+ex.Message;}
 }
 async Task Read(){
  try{while(!stop.IsCancellationRequested){var line=await process!.StandardOutput.ReadLineAsync(stop.Token);if(line is null){Status="Помощник датчиков завершился. Базовый мониторинг продолжает работать.";Metrics=[];break;}if(line.Length>1000000)throw new IOException("Слишком большой ответ датчиков");var frame=JsonSerializer.Deserialize<Frame>(line);if(frame is not null){Metrics=frame.Metrics;Status=frame.Status;ReceivedAt=DateTimeOffset.UtcNow;}}}
  catch(OperationCanceledException){}catch(Exception ex){Metrics=[];Status="Дополнительные датчики недоступны: "+ex.Message;}
 }
 public void Dispose(){if(Interlocked.Exchange(ref disposed,1)!=0)return;stop.Cancel();try{if(process is {HasExited:false})process.Kill();}catch{}process?.Dispose();stop.Dispose();}
}
public partial class MainWindow {
 MonitoringWindow? monitoring;
 void Monitoring(){
  if(monitoring is not null){monitoring.Activate();return;}
  string path=Path.Combine(PortableMode.DataDirectory,"monitoring.json");MonitorPreferences settings;
  try{settings=AtomicJson.Read<MonitorPreferences>(path)??new();settings.Validate();}catch{settings=new();}
  monitoring=new MonitoringWindow(this,settings,sensors,()=>AtomicJson.Write(path,settings),()=>gamePaths.Length>0?string.Join(", ",gamePaths.Select(Path.GetFileNameWithoutExtension)):null);
  monitoring.Closed+=(_,_)=>monitoring=null;monitoring.Show();
 }
}
