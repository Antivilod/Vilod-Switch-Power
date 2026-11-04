using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using QuietPC.Core;
using Button=System.Windows.Controls.Button;
using TextBox=System.Windows.Controls.TextBox;
namespace QuietPC.App;
sealed class EnergyPreferences {
 public decimal? RubPerKwh{get;set;}
 public void Validate(){if(RubPerKwh is decimal value&&(value<0||value>10000))throw new IOException("Неверный тариф");}
}
public partial class MainWindow {
 readonly GpuEnergy gpuEnergy=new();readonly Stopwatch energyClock=Stopwatch.StartNew();readonly DateTimeOffset energyStarted=DateTimeOffset.UtcNow;
 Window? energyWindow;
 void EnergyPanel(){
  if(energyWindow is not null){energyWindow.Activate();return;}
  string path=Path.Combine(PortableMode.DataDirectory,"energy-settings.json");EnergyPreferences options;
  try{options=AtomicJson.Read<EnergyPreferences>(path)??new();options.Validate();}catch{options=new();}
  var window=Panel("VSP · Энергия GPU",480,740,out var body);energyWindow=window;
  body.Children.Add(new TextBlock{Text="Энергия GPU",FontSize=24,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,10)});
  Paragraph(body,"Только видеокарта GPU 0. Расчёт энергии по периодическим замерам мощности, а не потребление всего ПК из розетки.");
  var info=new StackPanel();var energy=new TextBlock{FontSize=30,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,10)};energy.SetResourceReference(TextBlock.ForegroundProperty,"Accent");info.Children.Add(energy);
  var summary=new TextBlock{TextWrapping=TextWrapping.Wrap,LineHeight=24};info.Children.Add(summary);
  var card=new Border{Child=info,CornerRadius=new CornerRadius(12),Padding=new Thickness(20),Margin=new Thickness(0,4,0,16),BorderThickness=new Thickness(1)};card.SetResourceReference(Border.BackgroundProperty,"PanelBg");card.SetResourceReference(Border.BorderBrushProperty,"Edge");body.Children.Add(card);
  body.Children.Add(new TextBlock{Text="Тариф, ₽ / кВт·ч · необязательно",Margin=new Thickness(0,0,0,8)});
  var field=new TextBox{Text=options.RubPerKwh?.ToString(CultureInfo.CurrentCulture)??"",MaxLength=16,ToolTip="Например: 7,5. Пустое поле - без расчёта стоимости."};body.Children.Add(field);
  var feedback=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};body.Children.Add(feedback);
  var save=new Button{Content="Сохранить тариф",Margin=new Thickness(0,0,0,16)};body.Children.Add(save);
  var cost=new TextBlock{FontSize=18,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14)};body.Children.Add(cost);
  var coverage=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14)};coverage.SetResourceReference(TextBlock.ForegroundProperty,"TextSecondary");body.Children.Add(coverage);
  var export=new Button{Content="Сохранить отчёт JSON",Margin=new Thickness(0,0,0,16)};body.Children.Add(export);
  Paragraph(body,"Пропуски данных и паузы дольше 15 с не заполняются предполагаемыми ваттами. Учитывается время работы интерфейса VSP, включая свёрнутое окно. Новый запуск - новый сеанс. Мониторы, CPU и остальные компоненты в эти числа не входят.");
  void Refresh(){
   var value=gpuEnergy.Snapshot();bool measured=value.ObservedSeconds>0;energy.Text=measured?$"{value.WattHours:F3} Вт·ч":"Накопление данных…";
   summary.Text=$"Средняя мощность: {(value.AverageWatts is double average?$"{average:F1} Вт":"-")}\nПиковая мощность: {(value.PeakWatts is double peak?$"{peak:F1} Вт":"-")}\nНаблюдали: {TimeSpan.FromSeconds(value.ObservedSeconds):hh\\:mm\\:ss}";
   cost.Text=measured&&options.RubPerKwh is decimal tariff?$"Расчётная стоимость GPU: {(decimal)value.WattHours/1000*tariff:F4} ₽":measured?"Стоимость: тариф не задан":"Стоимость: ожидаю первые замеры";
   double elapsed=energyClock.Elapsed.TotalSeconds;coverage.Text=$"Сеанс {TimeSpan.FromSeconds(elapsed):hh\\:mm\\:ss} · данных {value.Samples} · покрытие {(elapsed>0?Math.Clamp(value.ObservedSeconds/elapsed*100,0,100):0):F0}%"+(value.Samples==0?"\nИсточник мощности GPU недоступен или ещё не получен.":"");export.IsEnabled=measured;
  }
  save.Click+=(_,_)=>{string text=field.Text.Trim();decimal? tariff=null;
   if(text.Length>0){if(!decimal.TryParse(text.Replace(',','.'),NumberStyles.AllowDecimalPoint|NumberStyles.AllowLeadingWhite|NumberStyles.AllowTrailingWhite,CultureInfo.InvariantCulture,out decimal number)||number<0||number>10000){feedback.SetResourceReference(TextBlock.ForegroundProperty,"MemoryCritical");feedback.Text="Введите число от 0 до 10 000 или оставьте поле пустым.";return;}tariff=number;}
   try{var next=new EnergyPreferences{RubPerKwh=tariff};next.Validate();AtomicJson.Write(path,next);options=next;feedback.SetResourceReference(TextBlock.ForegroundProperty,"Accent");feedback.Text="Тариф сохранён. Расчёт относится только к видеокарте.";Refresh();}catch(Exception ex){feedback.SetResourceReference(TextBlock.ForegroundProperty,"MemoryCritical");feedback.Text="Не удалось сохранить: "+ex.Message;}
  };
  export.Click+=(_,_)=>{var pick=new Microsoft.Win32.SaveFileDialog{Filter="Отчёт энергии JSON|*.json",FileName=$"VSP-GPU-energy-{DateTime.Now:yyyyMMdd-HHmmss}.json"};if(pick.ShowDialog()!=true)return;
   try{var value=gpuEnergy.Snapshot();File.WriteAllText(pick.FileName,JsonSerializer.Serialize(new{Product="VSP",Version=typeof(MainWindow).Assembly.GetName().Version?.ToString(),Scope="GPU 0 only; not whole-PC wall power",StartedUtc=energyStarted,CapturedUtc=DateTimeOffset.UtcNow,SessionSeconds=energyClock.Elapsed.TotalSeconds,Energy=value,TariffRubPerKwh=options.RubPerKwh,EstimatedCostRub=options.RubPerKwh is decimal tariff?(decimal?)(value.WattHours/1000)*(tariff):null,Method="Trapezoidal integration of adjacent valid power samples; gaps over 15 seconds excluded"},new JsonSerializerOptions{WriteIndented=true}));feedback.Text="Отчёт сохранён.";}catch(Exception ex){feedback.Text="Не удалось сохранить отчёт: "+ex.Message;}
  };
  var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};timer.Tick+=(_,_)=>Refresh();window.Closed+=(_,_)=>{timer.Stop();energyWindow=null;};Refresh();timer.Start();window.Show();
 }
}
