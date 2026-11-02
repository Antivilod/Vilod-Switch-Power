namespace QuietPC.Core;
public sealed record GpuState(string Id,uint Current,uint Minimum,uint Maximum);
public interface IGpuPower:IDisposable {GpuState[] Devices();uint Read(string id);void Set(string id,uint milliwatts);}
public sealed record GpuChange(string Id,uint Original,uint Last,uint? Previous=null);
public sealed class GpuJournal {public int Schema{get;set;}=1;public List<GpuChange> Changes{get;set;}=[];}
public sealed class GpuPowerRecovery(string path,Func<IGpuPower> factory){
 GpuJournal? journal=AtomicJson.Read<GpuJournal>(path);
 public bool Pending=>journal?.Changes.Count>0;
 void Save()=>AtomicJson.Write(path,journal);
 public string Apply(uint percent,uint extraWatts=0){
  if(extraWatts>5)throw new ArgumentOutOfRangeException(nameof(extraWatts));
  if(percent is <30 or >100)throw new ArgumentOutOfRangeException(nameof(percent));
  using var api=factory();var devices=api.Devices();if(devices.Length==0)return "GPU: управление мощностью недоступно";
  var notes=new List<string>();
  foreach(var d in devices){
   journal??=new();if(journal.Schema!=1)throw new IOException("Неизвестный журнал GPU");
   var old=journal.Changes.FirstOrDefault(c=>c.Id==d.Id);uint before=api.Read(d.Id),baseline=old?.Original??before;
   if(old!=null&&before!=old.Last)throw new IOException("GPU: лимит изменён извне");
   // Add to the original baseline, never to a previous VSP target. All units here are milliwatts.
   uint target=(uint)Math.Clamp((ulong)baseline*percent/100+extraWatts*1000UL,d.Minimum,d.Maximum);
   if(before==target){notes.Add($"GPU {target/1000.0:F0} Вт");continue;}
   var change=new GpuChange(d.Id,baseline,target,old?.Last);if(old!=null)journal.Changes.Remove(old);journal.Changes.Add(change);Save();
   api.Set(d.Id,target);if(api.Read(d.Id)!=target)throw new IOException("GPU: проверка лимита не пройдена");notes.Add($"GPU до {target/1000.0:F0} Вт");
  }
  return string.Join("; ",notes);
 }
 public void Restore(){
  if(journal==null)return;if(journal.Schema!=1)throw new IOException("Неизвестный журнал GPU");
  if(journal.Changes.Count>0){using var api=factory();foreach(var c in journal.Changes.ToArray()){
   uint current=api.Read(c.Id);if(current!=c.Original){if(current!=c.Last&&current!=c.Previous)throw new IOException("GPU: внешний лимит сохранён, нужен разбор конфликта");api.Set(c.Id,c.Original);if(api.Read(c.Id)!=c.Original)throw new IOException("GPU: исходный лимит не восстановлен");}
   journal.Changes.Remove(c);Save();
  }}
  File.Delete(path);journal=null;
 }
}
