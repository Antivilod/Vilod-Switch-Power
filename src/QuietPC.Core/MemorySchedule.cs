namespace QuietPC.Core;
public sealed class MemorySchedule {
 DateTimeOffset? started,lastAttempt,pressureSince;
 public DateTimeOffset? NextAt(int minutes)=>started is null?null:(lastAttempt is {} last&&last>started.Value?last:started.Value).AddMinutes(minutes);
 public bool Due(bool enabled,bool automatic,string trigger,int threshold,int minutes,bool pauseInGames,bool game,uint load,DateTimeOffset now){
  if(!enabled||!automatic){started=null;pressureSince=null;return false;}
  started??=now;
  if(load>=threshold)pressureSince??=now;else pressureSince=null;
  if(pauseInGames&&game)return false;
  bool pressure=pressureSince is {} since&&now-since>=TimeSpan.FromSeconds(60)&&(lastAttempt is null||now-lastAttempt>=TimeSpan.FromMinutes(minutes));
  bool timer=now-((lastAttempt is {} last&&last>started.Value)?last:started.Value)>=TimeSpan.FromMinutes(minutes);
  return trigger switch{"Threshold"=>pressure,"Timer"=>timer,"Either"=>pressure||timer,_=>false};
 }
 public void Attempted(DateTimeOffset now){lastAttempt=now;pressureSince=null;}
}
