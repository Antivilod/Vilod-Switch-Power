using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json;
using QuietPC.Core;
using QuietPC.Windows;
namespace QuietPC.Service;
static class Program {
 static void Main(string[] args) {if(args.Length==5&&args[0]=="--portable"){PortableHost.Run(args[1],args[2],int.Parse(args[3]),long.Parse(args[4]));return;}if(args.Length!=1 || !args[0].StartsWith("S-1-5-21-",StringComparison.Ordinal))throw new ArgumentException("Требуется SID владельца");ServiceBase.Run(new Host(args[0]));}
}
sealed class Host:ServiceBase {
 readonly string owner;readonly string pipeName;readonly string? portableDir;
 public Host(string owner,string pipeName="QuietPC.v1",string? portableDir=null){this.owner=owner;this.pipeName=pipeName;this.portableDir=portableDir;ServiceName="QuietPC";CanShutdown=true;AutoLog=false;}
 readonly SemaphoreSlim gate=new(1,1);
 readonly CancellationTokenSource stop=new();
 RecoveryEngine? engine;
 NvidiaFpsRecovery? fps;
 GpuPowerRecovery? gpu;
 GameBoostRecovery? boost;bool boosting;bool maintenance;string powerDetail="";
 DateTimeOffset lastTrim=DateTimeOffset.MinValue;
 string? appliedId;
 DateTimeOffset heartbeat;
 Reply current=new(true,false,"Выключено","Собственных изменений нет");
 string Dir=>portableDir??StateDirectory.PathName;
 public void StartPortable()=>OnStart([]);
 public void StopPortable()=>OnStop();
 protected override void OnStart(string[] args){StateDirectory.Prepare(Dir);engine=new(new WindowsPower(),Path.Combine(Dir,"recovery.json"));fps=new(Path.Combine(Dir,"fps-recovery.json"),()=>new NvidiaDrs());gpu=new(Path.Combine(Dir,"gpu-recovery.json"),()=>new NvidiaPower());boost=new(new OwnedProcessPriority(owner),Path.Combine(Dir,"boost-recovery.json"));heartbeat=DateTimeOffset.UtcNow;_ = Run();}
 void Log(string s){try{string p=Path.Combine(Dir,"service.log");if(File.Exists(p)&&new FileInfo(p).Length>1_000_000)File.Move(p,p+".old",true);File.AppendAllText(p,$"{DateTimeOffset.UtcNow:O} {s}\n");}catch{}}
 Reply Restore(){
  boosting=false;var errors=new List<string>();try{boost!.Restore();}catch(Exception e){errors.Add(e.Message);Log(e.ToString());}try{gpu!.Restore();}catch(Exception e){errors.Add(e.Message);Log(e.ToString());}
  try{fps!.Restore();}catch(Exception e){errors.Add(e.Message);Log(e.ToString());}
  Outcome r;try{r=engine!.Restore();if(r.State=="Конфликт")errors.Add(r.Detail);}catch(Exception e){errors.Add(e.Message);Log(e.ToString());r=new(false,"Конфликт",e.Message);}
  if(!r.Enabled)appliedId=null;if(errors.Count>0)return new(false,false,"Конфликт",string.Join("; ",errors));
  return new(true,r.Enabled,r.State,r.Detail);
 }
 async Task Run(){
  await gate.WaitAsync();try{current=Restore();}catch(Exception e){current=new(false,false,"Ошибка восстановления",e.Message);Log(e.ToString());}finally{gate.Release();}
  _=Watch();
  while(!stop.IsCancellationRequested){
   try {
    var acl=new PipeSecurity();acl.SetAccessRuleProtection(true,false);
    acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid,null),PipeAccessRights.FullControl,AccessControlType.Deny));
    acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(owner),PipeAccessRights.ReadWrite,AccessControlType.Allow));
    acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),PipeAccessRights.FullControl,AccessControlType.Allow));
    acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null),PipeAccessRights.ReadWrite,AccessControlType.Allow));
    using var pipe=NamedPipeServerStreamAcl.Create(pipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,4096,4096,acl);
    await pipe.WaitForConnectionAsync(stop.Token);
    using var deadline=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);deadline.CancelAfter(TimeSpan.FromSeconds(3));
    // Strict bounded frame: UTF8 JSON <= 65536 bytes, one request per connection.
    byte[] len=new byte[4];await pipe.ReadExactlyAsync(len,deadline.Token);int n=BitConverter.ToInt32(len);if(n is <1 or >65536)continue;
    byte[] buf=new byte[n];await pipe.ReadExactlyAsync(buf,deadline.Token);var request=JsonSerializer.Deserialize<Request>(buf)??throw new IOException("Пустая команда");
    string? sid=null;bool administrator=false;pipe.RunAsClient(()=>{using var identity=WindowsIdentity.GetCurrent();sid=identity.User?.Value;administrator=new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);});
    // An elevated uninstaller may only inspect state and request restoration.
    if(!BackendAccess.Allows(owner,sid,administrator,request.Command))continue;
    deadline.CancelAfter(TimeSpan.FromSeconds(20));
    await gate.WaitAsync(deadline.Token);
    Reply response;
    try {
     heartbeat=DateTimeOffset.UtcNow;
     switch(request.Command){
      case "heartbeat":
       if(boosting&&engine!.Enabled){try{var summary=boost!.Apply(request.GamePids??[],request.BackgroundPids??[]);current=current with{Detail=powerDetail+" · "+summary};}catch(Exception e){Log(e.ToString());current=current with{State="Частично применено",Detail=powerDetail+" · Game Boost: "+e.Message};}}
       response=current;break;
      case "memorytrim":
       if(DateTimeOffset.UtcNow-lastTrim<TimeSpan.FromMinutes(1))throw new InvalidOperationException("Повторная очистка доступна через минуту");
       var trim=MemoryCleaner.TrimFileCache();lastTrim=DateTimeOffset.UtcNow;response=new(true,engine!.Enabled,"Память",trim,appliedId);break;
      case "off":current=Restore();response=current;break;
      case "prepareuninstall":
       if(!administrator)throw new UnauthorizedAccessException("Administrator rights are required for removal");
       maintenance=true;current=Restore();response=current;break;
      case "apply":
       if(maintenance)throw new InvalidOperationException("VSP service is preparing for removal");
       if((engine!.Current is not null||fps!.Pending||gpu!.Pending||boost!.Pending)&&!engine.Enabled)throw new InvalidOperationException("Сначала восстановите незавершённую транзакцию");
       var p=request.Profile??throw new ArgumentException("Профиль отсутствует");p.Validate();var games=request.Games??[];if(games.Length>200)throw new ArgumentException("Слишком много игр");
       boost!.Restore();boosting=false;
       var effective=p.Boost?p with{Epp=0,GpuPercent=100}:p;
       var r=engine.Apply(effective);string detail=r.Detail,state=r.State;
       try {
        fps!.Restore();
        if(request.IdleGamingFps is <20 or >500)throw new ArgumentException("Недопустимый FPS ожидания");
        if((p.Fps??request.IdleGamingFps) is int cap){
         try{detail="CPU применён. "+fps!.Apply(games,cap,request.GlobalFps);state=detail.Contains("DRS FPS записан")?"Требуется перезапуск игры":"Частично применено";}
         catch(Exception ex)when(!fps!.Pending){detail="CPU EPP применён. FPS недоступен: "+ex.Message;state="Частично применено";}
        }
        uint percent=effective.GpuPercent!=0?effective.GpuPercent:p.Fps switch{null=>60,<=60=>70,<=120=>85,_=>95};
        try{detail+=" "+gpu!.Apply(percent,p.Fps is null?0u:5u);}catch(Exception ex){gpu!.Restore();detail+=" GPU недоступен: "+ex.Message;state="Частично применено";}
       }catch{current=Restore();throw;}
       powerDetail=detail;boosting=p.Boost;
       if(boosting){try{detail+=" · "+boost!.Apply(request.GamePids??[],request.BackgroundPids??[]);}catch(Exception ex){state="Частично применено";detail+=" · Game Boost: "+ex.Message;}}
       appliedId=p.Id;current=new(true,r.Enabled,state,detail,appliedId);response=current;break;
      default:throw new ArgumentException("Неизвестная команда");
     }
    }catch(Exception e){Log(e.ToString());current=new(false,engine!.Enabled,"Ошибка",e.Message,engine.Enabled?appliedId:null);response=current;}finally{gate.Release();}
    byte[] output=JsonSerializer.SerializeToUtf8Bytes(response);await pipe.WriteAsync(BitConverter.GetBytes(output.Length),deadline.Token);await pipe.WriteAsync(output,deadline.Token);
   }catch(OperationCanceledException){}catch(Exception e){Log(e.ToString());await Task.Delay(250,stop.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);}
  }
 }
 async Task Watch(){while(!stop.IsCancellationRequested){await Task.Delay(1000,stop.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);await gate.WaitAsync();try{if((engine!.Enabled||fps!.Pending||gpu!.Pending||boost!.Pending) && DateTimeOffset.UtcNow-heartbeat>TimeSpan.FromSeconds(15))current=Restore();}catch(Exception e){Log(e.ToString());current=new(false,false,"Ошибка восстановления",e.Message);}finally{gate.Release();}}}
 protected override void OnStop(){stop.Cancel();gate.Wait();try{if(engine!=null){current=Restore();Log(current.State+": "+current.Detail);}}finally{gate.Release();}}
 protected override void OnShutdown()=>OnStop();
}
