using System.Runtime.InteropServices;
using QuietPC.Windows;
namespace QuietPC.App;
public partial class MainWindow {
 [DllImport("powrprof.dll")]static extern uint PowerReadACValueIndex(nint root,ref Guid scheme,ref Guid group,ref Guid setting,out uint value);
 void PowerCheck(){
  if(!enabled){TextDialog("VSP · Самопроверка CPU","Сначала включите нужный профиль. Проверка только читает настройки питания и не запускает стресс-тест.");return;}
  try{
   var profile=prefs.Profiles.FirstOrDefault(p=>p.Id==applied)??throw new InvalidOperationException("Активный профиль не определён");
   var power=new WindowsPower();var id=power.Active();var actual=power.Read(id);uint expected=profile.Boost?0:profile.Epp;
   var group=new Guid("54533251-82be-4824-96c1-47b60b740d00");var setting=new Guid("be337238-0d82-4146-a960-4f3749d470c7");uint rc=PowerReadACValueIndex(0,ref id,ref group,ref setting,out uint boost);
   bool epp=actual.Ac==expected;bool turbo=rc==0&&(expected>=80?boost==0:expected==0?boost==2:true);
   TextDialog("VSP · Самопроверка CPU",$"Профиль: {profile.Name}\nПолитика энергосбережения CPU: {(epp?"совпадает с профилем":"НЕ совпадает")}\nEPP: {actual.Ac} из 100 (ожидается {expected})\nУскорение CPU: {(rc!=0?"проверка недоступна":boost==0?"отключено":"разрешено")}\nРезультат: {(epp&&turbo?"настройки CPU подтверждены":"проверьте активную схему питания")}\n\nПроверка читает значения Windows. Она не измеряет мощность CPU в ваттах и не доказывает предел потребления всего компьютера.");
  }catch(Exception ex){TextDialog("VSP · Самопроверка CPU","Проверка не завершена: "+ex.Message);}
 }
}
