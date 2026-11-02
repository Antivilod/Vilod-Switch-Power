namespace QuietPC.Core;
public sealed record DrsValue(bool Exists,uint Value,int Location,bool Predefined);
public sealed record DrsChange(string Profile,DrsValue Before,uint Last,uint? Previous=null);
public sealed class FpsJournal {public int Schema {get;set;}=1;public List<DrsChange> Changes {get;set;}=[];}
public interface INvidiaDrs:IDisposable {void Reload();void Save();string ProfileFor(string path);DrsValue Read(string name);void Write(string name,uint fps);void Revert(string name,DrsValue before);}
public sealed class NvidiaFpsRecovery(string path,Func<INvidiaDrs> factory){
 FpsJournal? journal=AtomicJson.Read<FpsJournal>(path);
 void Persist()=>AtomicJson.Write(path,journal);
 public bool Pending=>journal?.Changes.Count>0;
 static bool Own(DrsValue v,uint value)=>v.Exists&&v.Location==0&&!v.Predefined&&v.Value==value;
 public string Apply(IEnumerable<string> games,int fps,bool global=false){
  using var api=factory();var notes=new List<string>();int count=0;
  foreach(var game in (global?games.Prepend("::VSP_GLOBAL::"):games).Distinct(StringComparer.OrdinalIgnoreCase)){
   if(game!="::VSP_GLOBAL::"&&(!Path.IsPathFullyQualified(game)||!game.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)||!File.Exists(game)))throw new ArgumentException("Игра: нужен существующий полный путь .exe");
   string profile;
   try{profile=api.ProfileFor(game);}catch(Exception e){notes.Add(e.Message);continue;}
   api.Reload();var before=api.Read(profile);journal??=new();if(journal.Schema!=1)throw new IOException("Неизвестный журнал FPS");
   var existing=journal.Changes.FirstOrDefault(c=>c.Profile==profile);
   if(existing!=null&&!Own(before,existing.Last))throw new IOException("Конфликт внешнего изменения FPS");
   if(existing==null&&Own(before,(uint)fps)){count++;continue;}
   var change=new DrsChange(profile,existing?.Before??before,(uint)fps,existing?.Last);
   if(existing!=null)journal.Changes.Remove(existing);journal.Changes.Add(change);Persist();
   api.Write(profile,(uint)fps);api.Save();api.Reload();if(!Own(api.Read(profile),(uint)fps))throw new IOException("FPS: проверка чтением не пройдена");count++;
  }
  return count>0?$"DRS FPS записан и прочитан для {count} профиля(ей). Требуется перезапуск игры; действие лимита ещё не измерено. "+string.Join("; ",notes):"FPS не применён: нет подходящего отдельного DRS-профиля игры. "+string.Join("; ",notes);
 }
 public void Restore(){
  if(journal is null)return;if(journal.Schema!=1)throw new IOException("Неизвестный журнал FPS");
  if(journal.Changes.Count==0){File.Delete(path);journal=null;return;}
  using var api=factory();
  foreach(var change in journal.Changes.ToArray()){
   api.Reload();var current=api.Read(change.Profile);
   if(current!=change.Before){
    if(!Own(current,change.Last)&&!(change.Previous is uint prev&&Own(current,prev)))throw new IOException("Конфликт: FPS изменён извне; журнал сохранён");
    api.Revert(change.Profile,change.Before);api.Save();api.Reload();
    if(api.Read(change.Profile)!=change.Before)throw new IOException("Исходное состояние FPS не подтверждено");
   }
   journal.Changes.Remove(change);Persist();
  }
  File.Delete(path);journal=null;
 }
}
