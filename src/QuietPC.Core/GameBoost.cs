namespace QuietPC.Core;

public sealed record ProcessState(int Id,long Started,int Priority);
public interface IProcessPriority {
 ProcessState? Read(int id);
 void Set(ProcessState expected,int priority);
}
public sealed record PriorityChange(ProcessState Original,int Last);
public sealed class PriorityJournal {
 public int Schema {get;set;}=1;
 public List<PriorityChange> Changes {get;set;}=[];
}
// A PID alone is not an identity: Windows reuses process IDs after exit.
public sealed class GameBoostRecovery(IProcessPriority processes,string path) {
 const int Normal=32,AboveNormal=32768,BelowNormal=16384;
 PriorityJournal journal=AtomicJson.Read<PriorityJournal>(path)??new();
 public bool Pending=>journal.Changes.Count>0;
 void Save(){if(Pending)AtomicJson.Write(path,journal);else File.Delete(path);}
 void RestoreOne(PriorityChange change){
  var actual=processes.Read(change.Original.Id);
  if(actual is not null&&actual.Started==change.Original.Started){
   if(actual.Priority!=change.Original.Priority){
    if(actual.Priority!=change.Last)throw new IOException("Приоритет изменён другой программой; значение сохранено");
    processes.Set(actual,change.Original.Priority);
    if(processes.Read(actual.Id) is {} check&&check.Started==actual.Started&&check.Priority!=change.Original.Priority)throw new IOException("Приоритет не восстановлен");
   }
  }
  journal.Changes.Remove(change);Save();
 }
 public void Restore(){
  if(journal.Schema!=1)throw new IOException("Неизвестный журнал Game Boost");
  var errors=new List<string>();foreach(var item in journal.Changes.ToArray()){try{RestoreOne(item);}catch(Exception e){errors.Add(e.Message);}}
  if(errors.Count>0)throw new IOException(string.Join("; ",errors));
 }
 public string Apply(int[] games,int[] background){
  if(games.Length>200||background.Length>200||games.Concat(background).Any(p=>p<=0))throw new ArgumentException("Недопустимый список процессов");
  if(journal.Schema!=1)throw new IOException("Неизвестный журнал Game Boost");
  // With no game running, background tasks immediately regain their priorities.
  var desired=new Dictionary<int,int>();if(games.Length>0){foreach(int p in background)desired[p]=BelowNormal;foreach(int p in games)desired[p]=AboveNormal;}
  foreach(var item in journal.Changes.ToArray()){
   var actual=processes.Read(item.Original.Id);
   if(!desired.TryGetValue(item.Original.Id,out int value)||value!=item.Last||actual?.Started!=item.Original.Started)RestoreOne(item);
  }
  int boosted=0,quiet=0;
  foreach(var (id,priority) in desired){
   var actual=processes.Read(id);if(actual is null)continue;
   var owned=journal.Changes.FirstOrDefault(c=>c.Original.Id==id&&c.Original.Started==actual.Started);
   if(owned is not null){if(actual.Priority!=owned.Last)throw new IOException("Приоритет изменён извне");}
   else {
    // Respect an existing manual performance choice; never use High or Realtime.
    if(actual.Priority==priority||actual.Priority is not (Normal or BelowNormal))continue;
    var entry=new PriorityChange(actual,priority);journal.Changes.Add(entry);Save();
    processes.Set(actual,priority);
    var check=processes.Read(id);if(check is not null&&check.Started==actual.Started&&check.Priority!=priority)throw new IOException("Windows не подтвердила приоритет");
   }
   if(priority==AboveNormal)boosted++;else quiet++;
  }
  return games.Length==0?"Game Boost ждёт игру":$"Game Boost: игр {boosted}, фоновых программ {quiet}";
 }
}
