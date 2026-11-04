using System.IO;
using System.Windows;
using System.Windows.Controls;
using QuietPC.Core;
namespace QuietPC.App;
public partial class MainWindow {
 SteamGame[] steamGames=[];
 DateTimeOffset steamChecked=DateTimeOffset.MinValue;
 bool steamScanning,steamFirst=true;
 string steamStatus="Список ещё не обновлён";
 static string[] SteamRoots(){
  var paths=new List<string>();
  foreach(var hive in new[]{Microsoft.Win32.Registry.CurrentUser,Microsoft.Win32.Registry.LocalMachine}){
   foreach(string sub in new[]{@"Software\Valve\Steam",@"Software\WOW6432Node\Valve\Steam"}){
    using var key=hive.OpenSubKey(sub);foreach(string value in new[]{"SteamPath","InstallPath"})if(key?.GetValue(value) is string path&&Path.IsPathFullyQualified(path))paths.Add(path);
   }
  }
  string fallback=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam");if(Directory.Exists(fallback))paths.Add(fallback);
  return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
 }
 async Task RefreshSteam(bool force=false){
  if(steamScanning||(!force&&(!prefs.DiscoverSteamGames||DateTimeOffset.UtcNow-steamChecked<TimeSpan.FromMinutes(1))))return;
  steamScanning=true;steamChecked=DateTimeOffset.UtcNow;
  try{
   var result=await Task.Run(()=>SteamLibrary.Scan(SteamRoots()));
   var old=steamGames;
   // A disconnected disk / incomplete Steam write is not an uninstall.
   steamGames=SteamLibrary.Merge(old,result);
   string fallback=prefs.GamingDefault();bool changed=false;
   if(fallback.Length>0)foreach(var game in steamGames)if(!prefs.SteamPresets.Any(p=>p.AppId==game.AppId)){prefs.SteamPresets.Add(new(game.AppId,fallback));changed=true;}
   if(changed)Save();
   steamStatus=$"Найдено игр: {steamGames.Length} · проверено {DateTime.Now:HH:mm}"+(result.Errors.Length>0?"\nЧасть библиотек недоступна. Сохранён предыдущий список.":"");
   int added=steamGames.Count(g=>!old.Any(p=>p.AppId==g.AppId));
   if(!steamFirst&&added>0){tray.BalloonTipTitle="VSP · Игры Steam";tray.BalloonTipText=$"Добавлено игр: {added}. Профиль: {DefaultGaming?.Name??"не выбран"}.";tray.ShowBalloonTip(4000);}
   steamFirst=false;
  }catch(Exception e){steamStatus="Не удалось обновить Steam: "+e.Message;}
  finally{steamScanning=false;}
 }
 void SteamPanel(){
  var win=Panel("VSP · Игры Steam",560,700,out var body);win.MaxHeight=SystemParameters.WorkArea.Height;win.MaxWidth=SystemParameters.WorkArea.Width;
  Paragraph(body,"Игры добавляются из всех библиотек Steam при запуске VSP и раз в минуту. Личные настройки сохраняются после переустановки игры. Steam и учётная запись не изменяются.");
  var discovery=new System.Windows.Controls.CheckBox{Content="Автоматически находить игры Steam",IsChecked=prefs.DiscoverSteamGames,Foreground=Theme.Brush("TextPrimary")};body.Children.Add(discovery);
  Paragraph(body,"Профиль для новых игр");var fallback=new ComboBox{ItemsSource=prefs.Profiles.Where(p=>p.Fps!=null).ToArray(),SelectedValuePath="Id",SelectedValue=prefs.GamingDefault()};body.Children.Add(fallback);
  var status=new TextBlock{Text=steamStatus,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,14,0,8)};body.Children.Add(status);
  var list=new ListBox{Height=200,DisplayMemberPath="Name"};body.Children.Add(list);
  var path=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new Thickness(0,8,0,8)};body.Children.Add(path);
  Paragraph(body,"Профиль выбранной игры");var profile=new ComboBox{ItemsSource=prefs.Profiles.Where(p=>p.Fps!=null).ToArray(),SelectedValuePath="Id"};body.Children.Add(profile);
  var edits=prefs.SteamPresets.ToDictionary(p=>p.AppId,p=>p.ProfileId);string? selected=null;
  void Capture(){if(selected!=null&&profile.SelectedValue is string id)edits[selected]=id;}
  list.SelectionChanged+=(_,_)=>{Capture();selected=(list.SelectedItem as SteamGame)?.AppId;if(list.SelectedItem is SteamGame g){path.Text=g.Directory;profile.SelectedValue=edits.GetValueOrDefault(g.AppId,prefs.GamingDefault());}};
  void Reload(){Capture();list.ItemsSource=steamGames;status.Text=steamStatus;if(list.Items.Count>0)list.SelectedIndex=0;}
  Reload();
  var refresh=new System.Windows.Controls.Button{Content="Обновить сейчас",Margin=new Thickness(0,8,0,0)};body.Children.Add(refresh);
  refresh.Click+=async(_,_)=>{refresh.IsEnabled=false;try{await RefreshSteam(true);Reload();}finally{refresh.IsEnabled=true;}};
  Paragraph(body,"Автопереключение действует при включённом VSP и включённом автоматическом режиме: вне игр - Работа, в игре - её профиль. Профиль не меняет графику игры и не гарантирует рост FPS. Для нового FPS-лимита иногда нужен перезапуск игры.");
  ActionButton(body,"Сохранить",()=>{Capture();if(fallback.SelectedValue is not string id)throw new ArgumentException("Создайте игровой профиль");prefs.DiscoverSteamGames=discovery.IsChecked==true;prefs.DefaultGameProfile=id;foreach(var game in steamGames)if(!edits.ContainsKey(game.AppId))edits[game.AppId]=id;prefs.SteamPresets=edits.Select(p=>new SteamPreset(p.Key,p.Value)).ToList();Save();activeGameKey="";steamChecked=DateTimeOffset.MinValue;win.Close();});
  win.ShowDialog();
 }
}
