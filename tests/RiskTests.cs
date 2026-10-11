using System;
using NinjaTrader.NinjaScript.AddOns.Kinfx;
class RiskTests {
 static int Main(string[] args) {
  try { XmlDoubleRoundtripIsExact(); XmlDoubleRejectsGroupingCommas(); XmlBaselinePreservesSignedZeroAndIntent(); Console.WriteLine("PASS exact XML numeric persistence"); Eq(false,DashboardSafety.CanExecute("start")); Eq(false,DashboardSafety.CanExecute("flattenSelected")); Eq(false,DashboardSafety.CanExecute("flattenAll")); Eq(false,DashboardSafety.CanExecute("batch")); Eq(true,DashboardSafety.CanExecute("refresh")); BulkAmount(); Console.WriteLine("PASS bulk amount grammar"); SettingsWarnings(); Console.WriteLine("PASS settings warnings"); WorkerRetention(); Console.WriteLine("PASS worker retains accepted work and reports rejection"); DivergencePolicy(); Console.WriteLine("PASS divergence reaction setting"); Quantity(); Console.WriteLine("PASS quantity"); Risk(); Console.WriteLine("PASS risk latch / exits / finite / DST"); Persistence(); Console.WriteLine("PASS XML atomic roundtrip"); Worker(); Console.WriteLine("PASS serial reentrancy + ownership"); Ledger(); Console.WriteLine("PASS lifecycle decisions / dedupe / reservation"); Presets(); Console.WriteLine("PASS sourced editable presets"); ChangePolicy(); Console.WriteLine("PASS locked reductions / partial exit totals"); DurableLock(); Console.WriteLine("PASS durable global latches / unlock recheck / advisory"); return 0; }
  catch(Exception ex) { Console.WriteLine("FAIL "+ex.Message); return 1; }
 }
 static void Eq(object want,object got) { if(!want.Equals(got)) throw new Exception("expected "+want+" got "+got); }
 // Issue #5: exercise the actual persistence methods, not a second serializer.
 static void XmlDoubleRoundtripIsExact() {
  foreach(var field in typeof(RiskSettings).GetFields()) {
   if(field.FieldType!=typeof(double))continue;
   foreach(double value in new[]{0.12345678901234566,1.2345678901234567,12.345678901234567}) {
    var row=new SavedAccount{Name="Synthetic precision"};field.SetValue(row.Settings,value);
    var xml=SettingsStore.ToXml(new[]{row},"MES 12-26","UTC",0);
    var back=SettingsStore.FromXml(System.Xml.Linq.XElement.Parse(xml.ToString()))[0];
    Eq(BitConverter.DoubleToInt64Bits(value),BitConverter.DoubleToInt64Bits((double)field.GetValue(back.Settings)));
   }
  }
 }
 static void XmlDoubleRejectsGroupingCommas() {
  foreach(var field in typeof(RiskSettings).GetFields()) {
   if(field.FieldType!=typeof(double))continue;
   foreach(string value in new[]{"0,125","1,000","1,2,3"}) {
    var xml=SettingsStore.ToXml(new[]{new SavedAccount{Name="Synthetic grammar"}},"MES 12-26","UTC",0);
    xml.Element("Account").SetAttributeValue(field.Name,value);
    bool refused=false;try{SettingsStore.FromXml(xml);}catch(FormatException){refused=true;}
    Eq(true,refused);
   }
  }
 }
 static void XmlBaselinePreservesSignedZeroAndIntent() {
  foreach(bool baselineSet in new[]{false,true}) {
   var row=new SavedAccount{Name="Synthetic baseline"};
   row.Settings.Baseline=BitConverter.Int64BitsToDouble(long.MinValue);row.Settings.BaselineSet=baselineSet;
   var xml=SettingsStore.ToXml(new[]{row},"MES 12-26","UTC",0);
   var back=SettingsStore.FromXml(System.Xml.Linq.XElement.Parse(xml.ToString()))[0];
   Eq(long.MinValue,BitConverter.DoubleToInt64Bits(back.Settings.Baseline));Eq(baselineSet,back.Settings.BaselineSet);
  }
 }
 static void BulkAmount() {
  double v;
  Eq(true,BulkEdit.TryParseAmount("1000",out v)); Eq(1000.0,v);
  Eq(true,BulkEdit.TryParseAmount(" 250.5 ",out v)); Eq(250.5,v);
  Eq(true,BulkEdit.TryParseAmount("0",out v)); Eq(0.0,v);
  Eq(true,BulkEdit.TryParseAmount("-0",out v)); Eq(true,double.IsPositiveInfinity(1.0/v));
  foreach(string bad in new[]{"","  ","abc","-1","NaN","Infinity","1e400","1,000","0,125","1 000","12abc","--5"}) Eq(false,BulkEdit.TryParseAmount(bad,out v));
  Eq(false,BulkEdit.TryParseAmount(null,out v));
 }
 static void SettingsWarnings() {
  var ok=new RiskSettings{Ratio=1,Target=1000,Loss=500,MaxContracts=4};
  Eq("",SettingsAdvice.For(ok,"Follower",2));
  Eq("",SettingsAdvice.For(ok,"Leader",2));
  var noLimits=new RiskSettings{Ratio=1,Target=0,Loss=0,MaxContracts=4};
  Eq(true,SettingsAdvice.For(noLimits,"Follower",2).Contains("no daily"));
  var noLoss=new RiskSettings{Ratio=1,Target=1000,Loss=0,MaxContracts=4};
  Eq(true,SettingsAdvice.For(noLoss,"Follower",2).Contains("no daily loss"));
  var noTgt=new RiskSettings{Ratio=1,Target=0,Loss=500,MaxContracts=4};
  Eq(true,SettingsAdvice.For(noTgt,"Follower",2).Contains("no daily target"));
  var tiny=new RiskSettings{Ratio=0.2,Target=1000,Loss=500,MaxContracts=4};
  Eq(true,SettingsAdvice.For(tiny,"Follower",2).Contains("0 lots"));
  Eq(true,SettingsAdvice.For(tiny,"Follower",5).Equals(""));
  var cap=new RiskSettings{Ratio=3,Target=1000,Loss=500,MaxContracts=4};
  Eq(true,SettingsAdvice.For(cap,"Follower",2).Contains("capped"));
  Eq(true,SettingsAdvice.For(new RiskSettings{Ratio=double.NaN,Target=1,Loss=1,MaxContracts=4},"Follower",2).Contains("Ratio"));
  Eq("",SettingsAdvice.For(null,"Follower",2));
  // leader rows never get follower advice
  Eq("",SettingsAdvice.For(noLimits,"Leader",2));
  // reference lot size must be positive: unknown reference gives no lot warning
  Eq("",SettingsAdvice.For(tiny,"Follower",0));
 }
 // Mailbox contract: every task accepted by Post must run before Dispose returns, and a task refused after
 // Dispose must be visible to the caller (never a silent drop).
 static void WorkerRetention() {
  // 1) Accepted work queued behind a slow task still runs when Dispose is called.
  var ran=new System.Collections.Generic.List<int>();var started=new System.Threading.ManualResetEvent(false);var release=new System.Threading.ManualResetEvent(false);
  var w=new SerialWorker(ex=>{});
  Eq(true,w.Post(()=>{started.Set();release.WaitOne(5000);}));
  Eq(true,started.WaitOne(3000));
  for(int i=1;i<=5;i++){int n=i;Eq(true,w.Post(()=>{lock(ran)ran.Add(n);}));}
  var disposer=new System.Threading.Thread(()=>w.Dispose());disposer.Start();
  System.Threading.Thread.Sleep(100);release.Set();disposer.Join(6000);
  lock(ran)Eq("1,2,3,4,5",string.Join(",",ran));
  // 2) After Dispose a Post is refused AND reports it.
  Eq(false,w.Post(()=>{lock(ran)ran.Add(99);}));
  System.Threading.Thread.Sleep(100);lock(ran)Eq(false,ran.Contains(99));
  // 3) Accepted work posted from inside a running task during Dispose is still run (reentrant chain).
  var chain=new System.Collections.Generic.List<int>();var go=new System.Threading.ManualResetEvent(false);var w2=new SerialWorker(ex=>{});
  w2.Post(()=>{go.WaitOne(5000);bool ok=w2.Post(()=>{lock(chain)chain.Add(2);});lock(chain)chain.Add(ok?1:-1);});
  var d2=new System.Threading.Thread(()=>w2.Dispose());d2.Start();System.Threading.Thread.Sleep(100);go.Set();d2.Join(6000);
  // Post from inside a running task after Dispose began is REFUSED (-1), not retained: only work accepted BEFORE Dispose is guaranteed.
  lock(chain)Eq("-1",string.Join(",",chain));
  // 4) An exception in one task is reported and does not drop later accepted tasks.
  int errors=0;var later=new System.Collections.Generic.List<int>();var w3=new SerialWorker(ex=>System.Threading.Interlocked.Increment(ref errors));
  w3.Post(()=>{throw new InvalidOperationException("boom");});w3.Post(()=>{lock(later)later.Add(1);});w3.Dispose();
  Eq(1,errors);lock(later)Eq("1",string.Join(",",later));
 }
 // Issue #12: per-account reaction when a follower diverges. The DEFAULT is notify-only; flatten is an explicit opt-in.
 // This is a setting only: the engine has no automatic flatten, and Flatten stays disabled in this alpha.
 static void DivergencePolicy() {
  var s=new RiskSettings();
  Eq("Notify",s.OnDivergence);
  Eq(true,s.Valid());
  s.OnDivergence="FlattenOnce";Eq(true,s.Valid());
  s.OnDivergence="Notify";Eq(true,s.Valid());
  foreach(string bad in new[]{"","flatten","FlattenTwice","Retry",null,"Notify "}) { s.OnDivergence=bad; Eq(false,s.Valid()); }
  Eq(true,DivergenceChoice.IsValid("Notify")); Eq(true,DivergenceChoice.IsValid("FlattenOnce")); Eq(false,DivergenceChoice.IsValid("x"));
  // XML roundtrip keeps the choice per account, and an old file without the attribute loads as Notify.
  var rows=new System.Collections.Generic.List<SavedAccount>();
  var a=new SavedAccount{Name="A"};a.Settings.OnDivergence="FlattenOnce";var b=new SavedAccount{Name="B"};rows.Add(a);rows.Add(b);
  var xml=SettingsStore.ToXml(rows,"MES 12-26","UTC",0);
  var back=SettingsStore.FromXml(xml);
  Eq("FlattenOnce",back[0].Settings.OnDivergence);Eq("Notify",back[1].Settings.OnDivergence);
  foreach(var el in xml.Elements("Account")) { var at=el.Attribute("OnDivergence"); if(at!=null) at.Remove(); }
  var old=SettingsStore.FromXml(xml);
  Eq("Notify",old[0].Settings.OnDivergence);Eq("Notify",old[1].Settings.OnDivergence);
  // a corrupted value on disk must not silently become a flatten
  var bad2=SettingsStore.ToXml(rows,"MES 12-26","UTC",0);
  foreach(var el2 in bad2.Elements("Account")){el2.SetAttributeValue("OnDivergence","Retry");break;}
  bool refused=false; try{ SettingsStore.FromXml(bad2); }catch(Exception){ refused=true; }
  Eq(true,refused);
 }
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
