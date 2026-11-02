using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using QuietPC.Core;
using Button=System.Windows.Controls.Button;
using TextBox=System.Windows.Controls.TextBox;
using ListBox=System.Windows.Controls.ListBox;
namespace QuietPC.App;
public sealed class ProfileEditor:Window {
 readonly List<Profile> items;readonly ListBox list=new(){DisplayMemberPath="Name",Height=150};
 readonly CheckBox boost=new(){Content="Game Boost · приоритет игры и исходный лимит + 5 Вт",Margin=new Thickness(0,12,0,8),Foreground=Theme.Brush("TextPrimary")};
 readonly TextBox name=new(),fps=new(),gpu=new();readonly ComboBox strategy=new(){ItemsSource=new[]{"Авто","Больше экономии","Больше плавности"},SelectedIndex=0};
 bool refreshing;Profile? editing;
 public List<Profile>? Result {get;private set;}public string SelectedId {get;private set;}
 public ProfileEditor(Window owner,Preferences prefs){
  Theme.ConfigureWindow(this);Owner=owner;Title="Профили";Width=400;Height=660;MinHeight=580;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=Theme.Brush("AppBg");
  var hardware=HardwareCapabilities.Read();gpu.IsEnabled=hardware.NvidiaPower;gpu.ToolTip=hardware.NvidiaPower?"Диапазон ограничен видеодрайвером":"Управление мощностью GPU на этом ПК не подтверждено";items=[..prefs.Profiles];SelectedId=prefs.Selected;
  var body=new StackPanel{Margin=new Thickness(24)};Content=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  body.Children.Add(new TextBlock{Text="Профили",FontSize=24,Margin=new Thickness(0,0,0,16)});body.Children.Add(list);
  Label("Название");body.Children.Add(name);Label("FPS · 20–500, в том числе 30");body.Children.Add(fps);Label("Стратегия CPU");body.Children.Add(strategy);
  Label("Лимит GPU, % исходного (0 — по режиму)");body.Children.Add(gpu);body.Children.Add(boost);
  body.Children.Add(new TextBlock{Text="Разрешено 30–100%. Реальное значение ограничено диапазоном видеокарты. Игровым профилям добавляется 5 Вт; Работа остаётся без добавки. В Game Boost используется 100% исходного лимита + 5 Вт. Лимит не выходит за диапазон, разрешённый драйвером.",TextWrapping=TextWrapping.Wrap,Foreground=Theme.Brush("TextSecondary"),Margin=new Thickness(0,12,0,12)});
  var row=new StackPanel{Orientation=System.Windows.Controls.Orientation.Horizontal};body.Children.Add(row);Add(row,"Новый",()=>{var p=new Profile(Guid.NewGuid().ToString("N"),"Мой профиль",30,50);items.Add(p);Refresh(p.Id);});
  Add(row,"Копия",()=>{if(list.SelectedItem is Profile p){Store();p=items.First(x=>x.Id==p.Id);var copy=p with{Id=Guid.NewGuid().ToString("N"),Name=p.Name+" — копия"};items.Add(copy);Refresh(copy.Id);}});
  Add(row,"Удалить",()=>{if(list.SelectedItem is Profile p){if(Profile.Defaults.Any(d=>d.Id==p.Id)){System.Windows.MessageBox.Show("Встроенный профиль сохраняется. Можно удалить собственный.");return;}items.Remove(p);Refresh("60");}});
  Add(body,"Сохранить и применить",()=>{try{Store();Result=items;SelectedId=((Profile)list.SelectedItem).Id;DialogResult=true;}catch(Exception e){System.Windows.MessageBox.Show(e.Message);}});
  list.SelectionChanged+=(_,_)=>{if(!refreshing&&editing is not null){try{Store(editing);}catch(Exception e){System.Windows.MessageBox.Show(e.Message);}}if(list.SelectedItem is Profile selected){var p=items.First(x=>x.Id==selected.Id);editing=p;name.Text=p.Name;fps.Text=p.Fps?.ToString()??"";gpu.Text=p.GpuPercent.ToString();boost.IsChecked=p.Boost;fps.IsReadOnly=p.Id is "60" or "120" or "200" or "400" or "work";strategy.SelectedItem=p.Strategy;}};
  Refresh(prefs.Selected);
  void Label(string text)=>body.Children.Add(new TextBlock{Text=text,Margin=new Thickness(0,10,0,5)});
 }
 void Store(Profile? source=null){var old=source??editing;if(old is null||!items.Any(p=>p.Id==old.Id))return;int? cap=old.Fps is null?null:int.TryParse(fps.Text,out var n)?n:throw new ArgumentException("Введите целый FPS");if(!uint.TryParse(gpu.Text,out uint limit))throw new ArgumentException("Введите процент GPU или 0");var chosen=(string)strategy.SelectedItem;uint epp=chosen switch{"Больше экономии"=>80,"Больше плавности"=>30,_=>old.Epp};var updated=old with{Name=name.Text,Fps=cap,Strategy=chosen,Epp=epp,GpuPercent=limit,Boost=boost.IsChecked==true};updated.Validate();items[items.FindIndex(p=>p.Id==old.Id)]=updated;}
 void Refresh(string id){if(editing is not null&&items.Any(p=>p.Id==editing.Id))Store(editing);refreshing=true;list.ItemsSource=null;list.ItemsSource=items;list.SelectedItem=items.FirstOrDefault(p=>p.Id==id)??items[0];refreshing=false;}
 static void Add(Panel parent,string title,Action action){var b=new Button{Content=title,Margin=new Thickness(0,4,8,4),Padding=new Thickness(9)};b.Click+=(_,_)=>{try{action();}catch(Exception e){System.Windows.MessageBox.Show(e.Message);}};parent.Children.Add(b);}
}
