using QuietPC.App;
int passed=0;
void Test(string label,Action action){action();passed++;Console.WriteLine("PASS "+label);}
void Throws(Action action){try{action();}catch{return;}throw new Exception("Expected validation failure");}
Test("monitor configuration rejects invalid refresh intervals",()=>{new MonitorPreferences().Validate();Throws(()=>new MonitorPreferences{MonitorIntervalSeconds=0}.Validate());Throws(()=>new MonitorPreferences{MonitorIntervalSeconds=3600}.Validate());});
Test("closing monitoring after pause is safe (double disposal)",()=>{var helper=new SensorProcess(false);helper.Dispose();helper.Dispose();});
Test("missing optional sensor helper leaves basic monitoring available",()=>{using var helper=new SensorProcess(false);if(!helper.Status.Contains("недоступны"))throw new Exception("Expected unavailable optional helper in isolated test folder");if(helper.Metrics.Length!=0)throw new Exception("Invented sensor values");});
Console.WriteLine($"TOTAL {passed} application tests passed");
