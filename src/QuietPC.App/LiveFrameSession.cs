using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using QuietPC.Core;
namespace QuietPC.App;

sealed class LiveFrameSession:IDisposable {
 readonly NamedPipeServerStream pipe;readonly string pipeName="vsp.frames."+Guid.NewGuid().ToString("N");readonly CancellationTokenSource stop=new();Process? worker;int disposed;
 LiveFramePacket packet=new(null,"Ожидание разрешения Windows…");long received;
 public LiveFramePacket Packet=>packet.Stats is not null&&Stopwatch.GetElapsedTime(Interlocked.Read(ref received)).TotalSeconds>4?new(null,"Источник кадров не обновляется"):packet;
 public bool IsRunning=>Volatile.Read(ref disposed)==0&&worker is {HasExited:false};
 public LiveFrameSession(){pipe=new(pipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);}
 public async Task Start(CaptureTarget target){
  using var identity=WindowsIdentity.GetCurrent();bool elevate=!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
  using var parent=Process.GetCurrentProcess();
  var start=new ProcessStartInfo(Environment.ProcessPath??throw new IOException("Не найден файл VSP")){UseShellExecute=elevate,CreateNoWindow=!elevate,WindowStyle=ProcessWindowStyle.Hidden};if(elevate)start.Verb="runas";
  foreach(string argument in new[]{"--vsp-live-capture",target.Id.ToString(),target.Started.ToString(),pipeName,parent.Id.ToString(),parent.StartTime.ToUniversalTime().Ticks.ToString()})start.ArgumentList.Add(argument);
  try{
   worker=await Task.Run(()=>Process.Start(start)??throw new IOException("Сборщик кадров не запущен"));
   if(Volatile.Read(ref disposed)!=0){worker.Dispose();return;}
   packet=new(null,"Подключение PresentMon…");_=Read();
  }catch(System.ComponentModel.Win32Exception ex)when(ex.NativeErrorCode==1223){packet=new(null,"Запрос Windows отменён. Сбор FPS не включён.");DisposePipe();}
  catch(Exception ex){packet=new(null,"Сбор FPS недоступен: "+ex.Message);DisposePipe();}
 }
 async Task Read(){
  try{
   using var connect=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);connect.CancelAfter(TimeSpan.FromSeconds(15));
   await pipe.WaitForConnectionAsync(connect.Token);
   if(!GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(),out uint pid)||pid!=worker!.Id)throw new IOException("Не удалось проверить сборщик кадров");
   using var reader=new StreamReader(pipe,Encoding.UTF8,false,8192,true);
   while(!stop.IsCancellationRequested){string? line=await reader.ReadLineAsync(stop.Token);if(line is null)break;if(line.Length>65536)throw new IOException("Неверный размер ответа сборщика");var value=JsonSerializer.Deserialize<LiveFramePacket>(line);if(value is not null){packet=value;Interlocked.Exchange(ref received,Stopwatch.GetTimestamp());}}
   if(Volatile.Read(ref disposed)==0)packet=new(null,"Сбор кадров завершён. Можно включить снова.");
  }catch(OperationCanceledException){if(Volatile.Read(ref disposed)==0)packet=new(null,"Сборщик не подключился или не получил разрешение Windows.");}
  catch(Exception ex){if(Volatile.Read(ref disposed)==0)packet=new(null,"Сбор FPS остановлен: "+ex.Message);}
  finally{DisposePipe();}
 }
 void DisposePipe(){try{pipe.Dispose();}catch{}}
 public void Dispose(){if(Interlocked.Exchange(ref disposed,1)!=0)return;stop.Cancel();DisposePipe();worker?.Dispose();}
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetNamedPipeClientProcessId(nint pipe,out uint pid);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetNamedPipeServerProcessId(nint pipe,out uint pid);

 public static async Task Worker(string[] args){
  if(args.Length!=6||!int.TryParse(args[1],out int targetId)||targetId<=0||!long.TryParse(args[2],out long targetStarted)||!args[3].StartsWith("vsp.frames.",StringComparison.Ordinal)||!Guid.TryParseExact(args[3][11..],"N",out _)||!int.TryParse(args[4],out int parentId)||parentId<=0||!long.TryParse(args[5],out long parentStarted))return;
  using var connection=new NamedPipeClientStream(".",args[3],PipeDirection.InOut,PipeOptions.Asynchronous,TokenImpersonationLevel.Identification);
  Process? child=null;Task? pump=null;Task<string>? stderr=null;using var lifetime=new CancellationTokenSource(TimeSpan.FromHours(8));
  string session="VSP_live_"+Guid.NewGuid().ToString("N");string tool=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","tools","PresentMon.exe"));
  try{
   await connection.ConnectAsync(10000,lifetime.Token);
   if(!SameProcess(parentId,parentStarted)||!GetNamedPipeServerProcessId(connection.SafePipeHandle.DangerousGetHandle(),out uint server)||server!=parentId)return;
   using var writer=new StreamWriter(connection,new UTF8Encoding(false),8192,true){AutoFlush=true};
   async Task Send(LiveFramePacket value)=>await writer.WriteLineAsync(JsonSerializer.Serialize(value));
   if(!SameProcess(targetId,targetStarted)){await Send(new(null,"Выбранное приложение уже завершилось. Обновите список."));return;}
   var start=new ProcessStartInfo(tool){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Path.GetDirectoryName(tool)!};
   foreach(string argument in new[]{"--process_id",targetId.ToString(),"--output_stdout","--timed","28800","--terminate_after_timed","--session_name",session,"--v1_metrics","--no_console_stats","--no_track_input","--no_track_gpu","--terminate_on_proc_exit"})start.ArgumentList.Add(argument);
   try{child=Process.Start(start)??throw new IOException("PresentMon не запущен");}
   catch(Exception ex){await Send(new(null,"PresentMon недоступен: "+ex.Message));return;}
   stderr=child.StandardError.ReadToEndAsync();var buffer=new LiveFrameWindow();object gate=new();long lastFrame=0;FrameCsvDecoder? decoder=null;string? parseError=null;
   pump=Task.Run(async()=>{try{while(await child.StandardOutput.ReadLineAsync(lifetime.Token) is string line){if(line.Length>8192)continue;if(decoder is null){if(line.TrimStart('\uFEFF').StartsWith("Application,",StringComparison.Ordinal))decoder=new(line,targetId);continue;}var frame=decoder.Parse(line);if(frame is not null){lock(gate)buffer.Add(frame);Interlocked.Exchange(ref lastFrame,Stopwatch.GetTimestamp());}}}catch(OperationCanceledException){}catch(Exception ex){parseError=ex.Message;}});
   var disconnected=connection.ReadAsync(new byte[1],lifetime.Token).AsTask();
   while(!lifetime.IsCancellationRequested&&!disconnected.IsCompleted&&!child.HasExited){
    if(!SameProcess(parentId,parentStarted))break;
    if(!SameProcess(targetId,targetStarted)){await Send(new(null,"Приложение завершилось. Обновите список и включите сбор снова."));break;}
    LiveFrameStats? stats;lock(gate)stats=buffer.Snapshot();
    long updated=Interlocked.Read(ref lastFrame);bool fresh=updated!=0&&Stopwatch.GetElapsedTime(updated).TotalSeconds<3;
    await Send(new(fresh?stats:null,parseError is not null?"Ошибка потока кадров: "+parseError:fresh&&stats is not null?"PresentMon · окно статистики до 30 с · время кадра не равно полной задержке ввода":"Ожидание кадров. Выведите выбранное приложение из свёрнутого состояния."));
    await Task.Delay(500,lifetime.Token);
   }
   if(child.HasExited){string detail=await stderr;if(child.ExitCode!=0)await Send(new(null,"PresentMon завершился: "+(string.IsNullOrWhiteSpace(detail)?"нет доступа к сбору кадров":detail.Trim()[..Math.Min(1000,detail.Trim().Length)])));}
  }catch(OperationCanceledException){}catch(IOException){}catch(UnauthorizedAccessException){}
  finally{
   try{if(child is {HasExited:false}){
    var start=new ProcessStartInfo(tool){UseShellExecute=false,CreateNoWindow=true};foreach(string argument in new[]{"--session_name",session,"--terminate_existing_session"})start.ArgumentList.Add(argument);
    using var end=Process.Start(start);if(end is not null)await Task.WhenAny(end.WaitForExitAsync(),Task.Delay(2000));await Task.WhenAny(child.WaitForExitAsync(),Task.Delay(3000));if(!child.HasExited){child.Kill();await child.WaitForExitAsync();}
   }}catch{}
   lifetime.Cancel();if(pump is not null)try{await pump;}catch{}child?.Dispose();
  }
 }
 static bool SameProcess(int id,long started){try{using var process=Process.GetProcessById(id);return !process.HasExited&&process.StartTime.ToUniversalTime().Ticks==started;}catch{return false;}}
}
