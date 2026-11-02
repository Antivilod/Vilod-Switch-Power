using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
namespace QuietPC.App;
public partial class MainWindow {
 void GameBoostPanel(){
  var window=Panel("VSP · Game Boost",470,650,out var body);
  Paragraph(body,"Game Boost отдаёт игре приоритет CPU выше обычного, разрешает штатный Turbo и устанавливает исходный лимит мощности GPU + 5 Вт в пределах диапазона драйвера. У обычного Game Boost потолок 200 FPS; отдельный «Соревновательный» использует 400 FPS. Потребление может вырасти.");
  Paragraph(body,"Программы ниже получают пониженный приоритет только пока запущена игра и включён профиль Game Boost. При выходе из игры или выключении VSP приоритеты восстанавливаются.");
  ActionButton(body,"Соревновательный режим · 400 FPS",()=>{window.Close();CompetitivePanel();});
  var list=new System.Windows.Controls.ListBox{Height=145};body.Children.Add(list);
  void Refresh()=>list.ItemsSource=prefs.BackgroundApps.Select(Path.GetFileName).ToArray();Refresh();
  ActionButton(body,"Добавить фоновую программу…",()=>{var pick=new Microsoft.Win32.OpenFileDialog{Filter="Программы|*.exe"};if(pick.ShowDialog()==true&&!prefs.BackgroundApps.Contains(pick.FileName,StringComparer.OrdinalIgnoreCase)){prefs.BackgroundApps.Add(pick.FileName);Save();Refresh();scanned=DateTimeOffset.MinValue;}});
  ActionButton(body,"Убрать выбранную",()=>{if(list.SelectedIndex>=0){prefs.BackgroundApps.RemoveAt(list.SelectedIndex);Save();Refresh();scanned=DateTimeOffset.MinValue;}});
  ActionButton(body,"Выбрать Game Boost",()=>{Profiles.SelectedValue="boost";window.Close();});
  Paragraph(body,"Одна основная кнопка включает выбранный профиль. При сворачивании VSP продолжает работать в трее. Разгон и завершение фоновых приложений не используются.");
  string gigabyte=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Gigabyte","GameBoost","GBoost.exe");
  if(File.Exists(gigabyte)){Paragraph(body,"На компьютере также найден Gigabyte GameBoost. Он работает отдельно и может менять системные службы.");ActionButton(body,"Открыть Gigabyte GameBoost",()=>Process.Start(new ProcessStartInfo(gigabyte){UseShellExecute=true}));}
  window.ShowDialog();
 }
}
