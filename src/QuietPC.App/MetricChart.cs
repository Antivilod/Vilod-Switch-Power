using System.Globalization;
using System.Windows;
using System.Windows.Media;
using QuietPC.Core;
namespace QuietPC.App;
sealed class MetricChart:FrameworkElement {
 public TracePoint[] Points{get;set;}=[];public double Now{get;set;}public int Seconds{get;set;}=60;public string Unit{get;set;}="";
 public MetricChart(){Height=145;MinWidth=120;}
 protected override void OnRender(DrawingContext d){
  base.OnRender(d);double w=ActualWidth-12,h=ActualHeight-28;if(w<30||h<20)return;
  var valid=Points.Where(p=>p.Value.HasValue).ToArray();var secondary=Theme.Brush("TextSecondary");
  void Label(string s,double x,double y)=>d.DrawText(new FormattedText(s,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),10,secondary,VisualTreeHelper.GetDpi(this).PixelsPerDip),new Point(x,y));
  if(valid.Length==0){Label("Ожидание данных",6,h/2);return;}
  double min=Math.Min(0,valid.Min(p=>p.Value!.Value)),max=valid.Max(p=>p.Value!.Value);if(max<=min)max=min+1;max*=1.08;
  var grid=new Pen(secondary,.3);for(int i=0;i<=3;i++)d.DrawLine(grid,new Point(6,i*h/3+4),new Point(w,i*h/3+4));
  var geometry=new StreamGeometry();using(var ctx=geometry.Open()){
   bool begin=true;foreach(var p in Points){if(p.Value is not double value){begin=true;continue;}double x=6+Math.Clamp((p.Seconds-(Now-Seconds))/Seconds,0,1)*(w-6),y=4+h-(value-min)/(max-min)*h;if(begin){ctx.BeginFigure(new Point(x,y),false,false);begin=false;}else ctx.LineTo(new Point(x,y),true,false);}
  }geometry.Freeze();d.DrawGeometry(null,new Pen(Theme.Brush("Accent"),1.7),geometry);
  Label($"{valid.Last().Value:F2} {Unit}",6,h+9);Label($"макс. {valid.Max(p=>p.Value):F2}",Math.Max(120,w-100),h+9);
 }
}
