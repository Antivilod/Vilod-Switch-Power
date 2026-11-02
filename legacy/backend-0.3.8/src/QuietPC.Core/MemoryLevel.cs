namespace QuietPC.Core;
public enum MemoryLevel { Normal, Warning, Critical }
public static class MemoryLevels {
 public static void Validate(int warning,int critical,int minutes){
  if(critical is <70 or >95 || warning<50 || warning>=critical || minutes is <5 or >180)
   throw new ArgumentException("Опасный уровень: от 50% и ниже критического. Критический: 70–95%. Интервал: 5–180 минут.");
 }
 public static MemoryLevel Classify(uint used,int warning,int critical){
  Validate(warning,critical,20);
  return used>=critical?MemoryLevel.Critical:used>=warning?MemoryLevel.Warning:MemoryLevel.Normal;
 }
}
