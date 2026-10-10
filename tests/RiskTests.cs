using System;
using NinjaTrader.NinjaScript.AddOns.Kinfx;
class RiskTests {
 static int Main(string[] args) {
  try { Eq(false,DashboardSafety.CanExecute("start")); Eq(false,DashboardSafety.CanExecute("flattenSelected")); Eq(false,DashboardSafety.CanExecute("flattenAll")); Eq(false,DashboardSafety.CanExecute("batch")); Eq(true,DashboardSafety.CanExecute("refresh")); Quantity(); Console.WriteLine("PASS quantity"); Risk(); Console.WriteLine("PASS risk latch / exits / finite / DST"); Persistence(); Console.WriteLine("PASS XML atomic roundtrip"); Worker(); Console.WriteLine("PASS serial reentrancy + ownership"); Ledger(); Console.WriteLine("PASS lifecycle decisions / dedupe / reservation"); Presets(); Console.WriteLine("PASS sourced editable presets"); ChangePolicy(); Console.WriteLine("PASS locked reductions / partial exit totals"); DurableLock(); Console.WriteLine("PASS durable global latches / unlock recheck / advisory"); return 0; }
  catch(Exception ex) { Console.WriteLine("FAIL "+ex.Message); return 1; }
 }
 static void Eq(object want,object got) { if(!want.Equals(got)) throw new Exception("expected "+want+" got "+got); }
 static void Risk() {
  var s = new RiskSettings { Target=100, Loss=50, MaxContracts=4 };
  var l = new RiskLatch();
  Eq("",RiskPolicy.Evaluate(s,l,99,50000,true));
  Eq("Target hit",RiskPolicy.Evaluate(s,l,100,50000,true));
  Eq("Target hit",RiskPolicy.Evaluate(s,l,0,50000,true));
  Eq(false,RiskPolicy.CanEnter(s,l,0,0,1));
  Eq(2,RiskPolicy.ExitRemaining(3,1,5)); Eq(0,RiskPolicy.ExitRemaining(3,0,-2));
  var bad = new RiskSettings { Ratio=double.NaN }; Eq(false,bad.Valid());
  Eq("Telemetry invalid",RiskPolicy.Evaluate(s,new RiskLatch(),double.NaN,50000,true));
  Eq("Daily loss",RiskPolicy.Evaluate(s,new RiskLatch(),-50,50000,true));
  s.Floor=49000; Eq("Drawdown floor",RiskPolicy.Evaluate(s,new RiskLatch(),0,48999,true));
  Eq(false,RiskPolicy.CanEnter(s,new RiskLatch(),3,1,1));
  Eq("2026-07-01",RiskPolicy.TradingDay(new DateTime(2026,7,1,22,0,0,DateTimeKind.Utc),"Central Standard Time",17));
  Eq("2026-06-30",RiskPolicy.TradingDay(new DateTime(2026,7,1,21,59,0,DateTimeKind.Utc),"Central Standard Time",17));
 }
 static void Persistence() {
  var record = new SavedAccount { Name="Sim<&>", Settings=new RiskSettings {Target=100,Loss=75,CopyAtm=false, Baseline=7, BaselineSet=true}, Latch=new RiskLatch {Locked=true,Reason="Target hit"} };
  var x=SettingsStore.ToXml(new[]{record},"MES 12-26","Central Standard Time",17);
  var back=SettingsStore.FromXml(x)[0]; Eq(record.Name,back.Name); Eq(true,back.Latch.Locked); Eq(100.0,back.Settings.Target); Eq(7.0,back.Settings.Baseline);Eq(75.0,back.Settings.Loss);Eq(false,back.Settings.CopyAtm);
  x.Element("Account").Attribute("CopyAtm").Remove();Eq(true,SettingsStore.FromXml(x)[0].Settings.CopyAtm);
  string p=System.IO.Path.Combine(Environment.GetEnvironmentVariable("TMPDIR"),"kinfx-settings-test.xml");
  SettingsStore.AtomicSave(p,x); SettingsStore.AtomicSave(p,x); Eq(1,SettingsStore.FromXml(System.Xml.Linq.XElement.Load(p)).Count); System.IO.File.Delete(p);
  var map=new OcoMap(); Eq(map.Get("a","bracket"),map.Get("a","bracket")); Eq(false,map.Get("a","bracket").Equals(map.Get("b","bracket")));
 }
 static void Worker() {
  var done=new System.Threading.ManualResetEvent(false); var events=new System.Collections.Generic.List<int>();
  using(var w=new SerialWorker(ex=> { throw ex; })) {
   w.Post(()=>{events.Add(1); w.Post(()=>{events.Add(3);done.Set();}); events.Add(2);});
   Eq(true,done.WaitOne(3000)); Eq("1,2,3",string.Join(",",events));
  }
  var owner=new object(); Eq(true,SessionOwnership.Acquire(new[]{"A","B"},owner)); Eq(false,SessionOwnership.Acquire(new[]{"B"},new object())); SessionOwnership.Release(owner);
 }
 static void Ledger() {
  var l=new CopyLedger(); Eq(true,l.First("o")); Eq(false,l.First("o"));
  Eq(0,CopyLedger.ExitBudget(3,0,2)); Eq(1,CopyLedger.ExitBudget(3,2,1));
  Eq(false,CopyLedger.AllowNew(false,false)); Eq(true,CopyLedger.AllowNew(false,true));
 }
 static void Presets() {
  var p=PresetCatalog.All.Find(x=>x.Firm=="Lucid" && x.Plan=="Pro" && x.Size=="50K");
  var s=p.Apply(new RiskSettings(),"MES",false); Eq(0.0,s.Loss); Eq(40,s.MaxContracts); Eq(0.0,s.Floor);
  Eq(1200.0,p.Apply(new RiskSettings(),"ES",true).Loss);
  Eq(1,p.Apply(new RiskSettings(),"CL",false).MaxContracts);
  Eq(0.0,PresetCatalog.All.Find(x=>x.Firm=="Tradeify"&&x.Plan=="→ Select Daily"&&x.Size=="50K").Apply(new RiskSettings(),"ES",false).Loss);
  Eq(false,PresetCatalog.All.Exists(x=>x.Plan.Contains("停售")));
  Eq(250.0,PresetCatalog.All.Find(x=>x.Firm=="Tradeify"&&x.Plan=="Growth"&&x.Size=="50K").Apply(new RiskSettings {Target=250},"MES",false).Target);
 }
 static void ChangePolicy() {
  Eq(2,RiskPolicy.ChangeTotal(4,0,2,false,1,0,5,true));
  Eq(4,RiskPolicy.ChangeTotal(4,0,6,false,1,0,5,false));
  Eq(3,RiskPolicy.ChangeTotal(4,1,5,true,2,0,5,false));
  Eq(0,RiskPolicy.ChangeTotal(3,0,3,true,0,0,5,false));
  Eq(3,RiskPolicy.ChangeTotal(4,0,3,false,1,0,5,false));
 }
 static void DurableLock() {
  string p=System.IO.Path.Combine(Environment.GetEnvironmentVariable("TMPDIR"),"kinfx-latch-test.xml");
  SettingsStore.SaveLatch(p,"A","Daily loss"); Eq("Daily loss",SettingsStore.ReadLatches(p)["A"]);
  SettingsStore.SaveLatch(p,"B","Disconnected");Eq(2,SettingsStore.ReadLatches(p).Count);SettingsStore.RemoveLatch(p,"A");Eq(false,SettingsStore.ReadLatches(p).ContainsKey("A"));System.IO.File.Delete(p);
  var s=new RiskSettings {LocalTrail=true,HighWater=50000,TrailDistance=2000};Eq(false,RiskPolicy.CanUnlock(s,100,47000,true));
  Eq("History required",RiskPolicy.ConsistencyAdvice(new RiskSettings {Consistency=40}));
  Eq("Over advisory limit: 50.00%",RiskPolicy.ConsistencyAdvice(new RiskSettings {Consistency=40,BestDay=500,TotalProfit=1000}));
 }
 static void Quantity() {
  Eq(3,RiskPolicy.Quantity(5,0.75)); Eq(0,RiskPolicy.Quantity(1,0.1));
  Eq(0,RiskPolicy.Quantity(5,-1)); Eq(0,RiskPolicy.Quantity(5,double.NaN)); Eq(0,RiskPolicy.Quantity(int.MaxValue,2));
 }
}
