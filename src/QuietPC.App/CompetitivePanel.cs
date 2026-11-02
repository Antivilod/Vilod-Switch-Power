using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Button=System.Windows.Controls.Button;
namespace QuietPC.App;
public partial class MainWindow {
 void CompetitivePanel(){
  var window=Panel("VSP · Соревновательный режим",520,760,out var body);window.MaxHeight=SystemParameters.WorkArea.Height-24;
  body.Children.Add(new TextBlock{Text="Соревновательный",FontSize=24,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,10)});
  Paragraph(body,"400 FPS · приоритет игровой производительности. Это верхний предел кадров, а не обещание 400 FPS на любых настройках графики.");
  Paragraph(body,"При включении:\n• CPU: политика производительности EPP 0 и разрешён штатный Turbo.\n• GPU: исходный бюджет мощности + 5 Вт в допустимом диапазоне драйвера. Понижающий эколимит игрового профиля не используется.\n• Распознанная игра получает приоритет выше обычного. Выбранные вами фоновые приложения — ниже обычного, пока идёт игра.\n• В NVIDIA задаётся глобальный FPS-потолок профиля. Привязка Steam к 120 FPS не заменит выбранный соревновательный профиль.");
  Paragraph(body,"При включённой автоматике после выхода из игры VSP возвращается в «Работа», а при следующем запуске распознанной игры — в соревновательный профиль. Выключение или выход восстанавливают управляемые настройки.");
  var apply=new Button{Content="Включить · Соревновательный 400 FPS",Margin=new Thickness(0,0,0,14)};body.Children.Add(apply);
  var result=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};body.Children.Add(result);
  apply.Click+=async(_,_)=>{
   if(busy||calibrating){result.Text="Подождите завершения текущего применения или замера.";return;}
   if(!prefs.Profiles.Any(p=>p.Id=="competitive")){result.Text="В списке нет профиля: освободите место в профилях и откройте VSP заново.";return;}
   try{ready=false;try{Profiles.SelectedValue="competitive";}finally{ready=true;}Save();await Apply();if(enabled&&applied=="competitive")window.Close();else result.Text=Detail.Text;}catch(Exception ex){result.Text="Не удалось включить: "+ex.Message;}
  };
  using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar")){
   object? value=key?.GetValue("AutoGameModeEnabled");Paragraph(body,"Игровой режим Windows: "+(value is int flag?(flag!=0?"включён":"выключен"):"проверьте в настройках Windows")+". Его переключатель управляется Windows отдельно.");
  }
  ActionButton(body,"Игровой режим Windows",()=>Process.Start(new ProcessStartInfo("ms-settings:gaming-gamemode"){UseShellExecute=true}));
  ActionButton(body,"Выбрать фоновые приложения",()=>{window.Close();BackgroundOptimizer();});
  ActionButton(body,"Проверить нагрузку компьютера",()=>{window.Close();GamingAudit();});
  ActionButton(body,"Проверить FPS и время кадра",()=>{window.Close();Monitoring();});
  Paragraph(body,"Выбирайте профиль до запуска игры. Собственный ограничитель игры, V-Sync или отдельный профиль драйвера могут задавать меньший FPS. Reflex включается в самой поддерживаемой игре. VSP не измеряет полную задержку мышь → экран и не меняет графику игры. Системные службы, защита и античиты не отключаются.");
  window.Show();
 }
}
