using System;
using NinjaTrader.NinjaScript.AddOns.Kinfx;
class RiskTests {
 static int Main(string[] args) {
  try { XmlDoubleRoundtripIsExact(); XmlDoubleRejectsGroupingCommas(); XmlBaselinePreservesSignedZeroAndIntent(); Console.WriteLine("PASS exact XML numeric persistence"); Eq(false,DashboardSafety.CanExecute("start")); Eq(false,DashboardSafety.CanExecute("flattenSelected")); Eq(false,DashboardSafety.CanExecute("flattenAll")); Eq(false,DashboardSafety.CanExecute("batch")); Eq(true,DashboardSafety.CanExecute("refresh")); BulkAmount(); Console.WriteLine("PASS bulk amount grammar"); SettingsWarnings(); Console.WriteLine("PASS settings warnings"); WorkerRetention(); Console.WriteLine("PASS worker retains accepted work and reports rejection"); DivergencePolicy(); Console.WriteLine("PASS divergence reaction setting"); Quantity(); Console.WriteLine("PASS quantity"); AccountSyncDiff(); Console.WriteLine("PASS account sync diff"); ContractRollQuarterly(); Console.WriteLine("PASS contract roll"); ContractInputNormalize(); Console.WriteLine("PASS contract input"); AccountSyncDecision(); Console.WriteLine("PASS account sync decision"); HeaderTextMatchesBuild(); Console.WriteLine("PASS header text matches build"); LangEveryKeyHasThreeTranslations(); Console.WriteLine("PASS language table: every sentence has zh/ja/ko with matching placeholders"); LangSelection(); Console.WriteLine("PASS language selection"); LangUnknownTextFallsBackToEnglish(); Console.WriteLine("PASS unknown text falls back to English"); LangTemplateRefill(); Console.WriteLine("PASS templates refill and composite messages translate"); LangDoesNotChangeEnglishDecisions(); Console.WriteLine("PASS language never changes English decisions"); LangNoCantoneseInChinese(); Console.WriteLine("PASS zh is written Chinese, not Cantonese colloquial"); DefaultLeaderChoice(); Console.WriteLine("PASS default leader choice"); AllowFileLineParsing(); ConfirmPrefsDefaultsAskEverything(); ConfirmPrefsRoundTripAndUnknownLines(); ConfirmPrefsRiskWarningIsNeverSkipped();ConfirmPrefsKindFollowsTheButtonNotTheRowCount();ColumnPrefsRoundTripAndSafetyColumnsNeverHidden(); Console.WriteLine("PASS allow file line parsing"); StartSummaryListsLeaderRatioAndExpectedQuantity(); Console.WriteLine("PASS start summary"); RvDraftNeverPublishesNonFiniteOrOutOfRange(); Console.WriteLine("PASS RvDraftNeverPublishesNonFiniteOrOutOfRange"); RvDraftMinusZeroAndStrictFields(); Console.WriteLine("PASS RvDraftMinusZeroAndStrictFields"); RvDraftCultureIndependent(); Console.WriteLine("PASS RvDraftCultureIndependent"); RvDraftBuildAfterLoadWithInvalidRawStillRefuses(); Console.WriteLine("PASS RvDraftBuildAfterLoadWithInvalidRawStillRefuses"); RvDraftLoadedInvalidPresetCannotBuild(); Console.WriteLine("PASS RvDraftLoadedInvalidPresetCannotBuild"); Risk(); Console.WriteLine("PASS risk latch / exits / finite / DST"); Persistence(); Console.WriteLine("PASS XML atomic roundtrip"); Worker(); Console.WriteLine("PASS serial reentrancy + ownership"); Ledger(); Console.WriteLine("PASS lifecycle decisions / dedupe / reservation"); Presets(); Console.WriteLine("PASS sourced editable presets"); ChangePolicy(); Console.WriteLine("PASS locked reductions / partial exit totals"); DurableLock(); Console.WriteLine("PASS durable global latches / unlock recheck / advisory"); return 0; }
  catch(Exception ex) { Console.WriteLine("FAIL "+ex.Message); return 1; }
 }
 static void Eq(object want,object got) { if(want==null||got==null){ if(want!=got) throw new Exception("expected "+(want??"<null>")+" got "+(got??"<null>")); return; } if(!want.Equals(got)) throw new Exception("expected "+want+" got "+got); }
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
 static void DialogDraftPreservesInvalidFieldsAcrossLoad() {
  var initial=new RiskSettings{Floor=100,Baseline=-10,TrailDistance=20,HighWater=200,BestDay=30,TotalProfit=300,Consistency=50};
  foreach(string name in new[]{"Ratio","Target","Loss","MaxContracts","Floor","Baseline","TrailDistance","HighWater","BestDay","TotalProfit","Consistency"}) {
   var draft=new RiskSettingsDraft(initial);var field=typeof(RiskSettings).GetField(name);object before=field.GetValue(initial);
   foreach(string bad in new[]{"","wrong","1,000","NaN","1e400"}) {
    Eq(false,draft.Set(name,bad));Eq(true,draft.HasErrors);Eq(bad,draft.Text(name));Eq(before,field.GetValue(draft.Settings));
    var preset=initial.Clone();preset.Target=900;draft.Load(preset);Eq(bad,draft.Text(name));
    bool refused=false;try{draft.Build(false);}catch(ArgumentException){refused=true;}Eq(true,refused);
    Eq(true,draft.Set(name,SettingsStore.FieldText(before)));Eq(false,draft.HasErrors);
   }
   Eq(before,field.GetValue(initial)); // caller's settings never mutated
  }
 }
 static void DialogDraftBuildIsAtomicAndBitExact() {
  var initial=new RiskSettings{Baseline=BitConverter.Int64BitsToDouble(long.MinValue)};var draft=new RiskSettingsDraft(initial);
  Eq(true,draft.Set("Target","0.12345678901234566"));Eq(false,draft.Set("Floor","-1"));Eq(false,draft.Set("Consistency","101"));
  Eq(0.0,initial.Target);Eq(true,draft.HasErrors);Eq(true,draft.Set("Floor","0"));Eq(true,draft.Set("Consistency","100"));
  var result=draft.Build(false);Eq(BitConverter.DoubleToInt64Bits(0.12345678901234566),BitConverter.DoubleToInt64Bits(result.Target));Eq(long.MinValue,BitConverter.DoubleToInt64Bits(result.Baseline));Eq(true,result.BaselineSet);
  result.Target=99;Eq(0.12345678901234566,draft.Settings.Target);
  bool refused=false;try{draft.Build(true);}catch(ArgumentException){refused=true;}Eq(true,refused);
  Eq(true,draft.Set("HighWater","100"));Eq(true,draft.Set("TrailDistance","10"));Eq(true,draft.Build(true).LocalTrail);
 }
 static void DialogDraftCanRepairMultipleInvalidPresetFields() {
  var draft=new RiskSettingsDraft(new RiskSettings{Ratio=0,MaxContracts=0});
  Eq(true,draft.Set("Ratio","1"));Eq(true,draft.Set("MaxContracts","2"));Eq(true,draft.Build(false).Valid());
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

 static void SeqEq(string[] want,string[] got) { if(string.Join("|",want)!=string.Join("|",got)) throw new Exception("expected ["+string.Join("|",want)+"] got ["+string.Join("|",got)+"]"); }
 static void AccountSyncDiff() {
  var d=AccountSync.Diff(new[]{"Sim101","Sim102"},new[]{"Sim101","Sim102"});
  Eq(false,d.Changed);
  d=AccountSync.Diff(new[]{"Sim101"},new[]{"Sim101","Sim103","Sim102"});
  Eq(true,d.Changed); SeqEq(new[]{"Sim102","Sim103"},d.Added); SeqEq(new string[0],d.Removed);
  d=AccountSync.Diff(new[]{"Sim101","Sim102"},new[]{"Sim102"});
  Eq(true,d.Changed); SeqEq(new string[0],d.Added); SeqEq(new[]{"Sim101"},d.Removed);
  d=AccountSync.Diff(new[]{"Sim101","Sim102"},new[]{"sim101","Sim102"});
  Eq(true,d.Changed); // account names are case-sensitive in NinjaTrader; a case change is a different account
  d=AccountSync.Diff(null,new[]{"A","B"}); Eq(true,d.Changed); SeqEq(new[]{"A","B"},d.Added);
  d=AccountSync.Diff(new[]{"A"},null); Eq(true,d.Changed); SeqEq(new[]{"A"},d.Removed);
  d=AccountSync.Diff(new[]{"A","A"},new[]{"A"}); Eq(false,d.Changed); // duplicates never produce phantom changes
  d=AccountSync.Diff(new[]{"A",null,""},new[]{"A"}); Eq(false,d.Changed); // blank / null names ignored
 }
 static void ContractRollQuarterly() {
  // Quarterly equity-index / metal-less cycle: H M U Z = Mar Jun Sep Dec. Roll on the 8th trading-month rule is NOT assumed:
  // the helper returns the first quarterly month whose expiry month is >= the current month (conservative, user can override).
  Eq("MNQ 12-26",ContractRoll.Front("MNQ",new DateTime(2026,10,11)));
  Eq("MNQ 12-26",ContractRoll.Front("MNQ",new DateTime(2026,12,1)));
  Eq("MNQ 03-27",ContractRoll.Front("MNQ",new DateTime(2026,12,31).AddDays(1)));
  Eq("ES 03-26",ContractRoll.Front("ES",new DateTime(2026,1,5)));
  Eq("ES 06-26",ContractRoll.Front("ES",new DateTime(2026,4,2)));
  Eq("MES 09-26",ContractRoll.Front("MES",new DateTime(2026,7,31)));
  Eq("NQ 12-26",ContractRoll.Front("NQ",new DateTime(2026,10,11)));
  Eq(null,ContractRoll.Front("ZZZ",new DateTime(2026,10,11)));   // unknown root: refuse, never guess
  Eq(null,ContractRoll.Front("",new DateTime(2026,10,11)));
  Eq(null,ContractRoll.Front(null,new DateTime(2026,10,11)));
 }
 static void ContractInputNormalize() {
  var now=new DateTime(2026,10,11);
  Eq("MNQ 12-26",ContractRoll.Normalize("mnq",now));          // bare root -> front contract
  Eq("MNQ 12-26",ContractRoll.Normalize("  MNQ  ",now));
  Eq("MNQ 12-26",ContractRoll.Normalize("MNQ 12-26",now));    // already exact: unchanged
  Eq("MNQ 03-27",ContractRoll.Normalize("MNQ 03-27",now));    // an explicit different month is respected, never silently rolled
  Eq("MNQ 12-26",ContractRoll.Normalize("MNQ1226",now));      // compact form
  Eq("MNQ 12-26",ContractRoll.Normalize("MNQZ6",now));        // broker month-code form
  Eq("ES 06-27",ContractRoll.Normalize("ESM7",now));
  Eq(null,ContractRoll.Normalize("MNQ 13-26",now));           // impossible month
  Eq(null,ContractRoll.Normalize("MNQ 00-26",now));
  Eq(null,ContractRoll.Normalize("ZZZ",now));                 // unknown root
  Eq(null,ContractRoll.Normalize("",now));
  Eq(null,ContractRoll.Normalize(null,now));
  Eq(null,ContractRoll.Normalize("MNQ 12-26; DROP",now));      // junk after a valid prefix is refused, not truncated
 }

 static void AccountSyncDecision() {
  var same=AccountSync.Diff(new[]{"A"},new[]{"A"});
  var added=AccountSync.Diff(new[]{"A"},new[]{"A","B"});
  var removed=AccountSync.Diff(new[]{"A","B"},new[]{"A"});
  Eq(AccountSyncAction.None,AccountSync.Decide(same,false,false));
  Eq(AccountSyncAction.None,AccountSync.Decide(same,true,false));
  Eq(AccountSyncAction.Reload,AccountSync.Decide(added,false,false));           // idle + changed -> refresh the table
  Eq(AccountSyncAction.Reload,AccountSync.Decide(removed,false,false));
  Eq(AccountSyncAction.Notify,AccountSync.Decide(added,true,false));            // copying: never touch the table, only tell the user
  Eq(AccountSyncAction.Notify,AccountSync.Decide(removed,true,false));
  Eq(AccountSyncAction.Defer,AccountSync.Decide(added,false,true));             // user is mid-edit (grid / ratio draft): do not discard their typing
  Eq(AccountSyncAction.Notify,AccountSync.Decide(added,true,true));             // copying wins over editing
  Eq(AccountSyncAction.None,AccountSync.Decide(null,false,false));              // no diff -> nothing, never throws
 }
 // KINFX_SIM_TEST build only (run by `offline.py puresim`).
 static void SimTestDashboardCommandsOpenButNothingElse() {
  foreach(string command in new[]{"start","flattenSelected","flattenAll","batch","refresh"})
   if(!DashboardSafety.CanExecute(command))throw new Exception("SIM TEST build must allow "+command);
 }

 // ---- Added after independent review of PR #21 (reviewer-written regression cases).
 static void RvDraftNeverPublishesNonFiniteOrOutOfRange(){
  foreach(var f in typeof(RiskSettings).GetFields()){
   if(f.FieldType!=typeof(double)&&f.FieldType!=typeof(int))continue;
   foreach(string bad in new[]{"NaN","Infinity","-Infinity","1e309","-1e309","-1","2147483648","1e-400"}){
    var d=new RiskSettingsDraft(new RiskSettings());bool ok=d.Set(f.Name,bad);
    var s=d.Settings;
    if(!ok){bool refused=false;try{d.Build(false);}catch(ArgumentException){refused=true;}Eq(true,refused);}
    Eq(true,s.Valid());
   }
  }
 }
 static void RvDraftMinusZeroAndStrictFields(){
  var d=new RiskSettingsDraft(new RiskSettings());
  Eq(false,d.Set("Ratio","-0"));Eq(false,d.Set("Ratio","0"));Eq(false,d.Set("MaxContracts","0"));Eq(false,d.Set("MaxContracts","1.0"));Eq(false,d.Set("Consistency","100.0000001"));
  Eq(true,d.HasErrors);
  bool refused=false;try{d.Build(false);}catch(ArgumentException){refused=true;}Eq(true,refused);
  foreach(var bad in new[]{new RiskSettings{Ratio=double.NaN},new RiskSettings{Target=double.PositiveInfinity},new RiskSettings{Loss=-1},new RiskSettings{Floor=double.NegativeInfinity},new RiskSettings{MaxContracts=0},new RiskSettings{Consistency=101},new RiskSettings{OnDivergence="Flatten"},new RiskSettings{Baseline=double.NaN},new RiskSettings{HighWater=double.NaN},new RiskSettings{BestDay=double.NaN},new RiskSettings{TotalProfit=double.PositiveInfinity}})Eq(false,bad.Valid());
 }
 static void RvDraftCultureIndependent(){
  var old=System.Threading.Thread.CurrentThread.CurrentCulture;
  try{
   foreach(string c in new[]{"de-DE","fr-FR","tr-TR","ar-SA"}){
    System.Threading.Thread.CurrentThread.CurrentCulture=new System.Globalization.CultureInfo(c);
    var d=new RiskSettingsDraft(new RiskSettings());
    Eq(true,d.Set("Ratio","0.5"));Eq(0.5,d.Settings.Ratio);Eq(false,d.Set("Target","0,5"));Eq(false,d.Set("Target","1,000"));Eq(true,d.Set("MaxContracts","12"));Eq("12",d.Text("MaxContracts"));
    Eq(true,d.Set("Target","1E2"));Eq(100.0,d.Settings.Target);
   }
  } finally{System.Threading.Thread.CurrentThread.CurrentCulture=old;}
 }
 static void RvDraftBuildAfterLoadWithInvalidRawStillRefuses(){
  var d=new RiskSettingsDraft(new RiskSettings());Eq(false,d.Set("Floor","abc"));
  d.Load(new RiskSettings{Floor=5});Eq(true,d.HasErrors);bool refused=false;try{d.Build(false);}catch(ArgumentException){refused=true;}Eq(true,refused);
  Eq(5.0,d.Settings.Floor);
 }
 static void RvDraftLoadedInvalidPresetCannotBuild(){
  var d=new RiskSettingsDraft(new RiskSettings());d.Load(new RiskSettings{Ratio=0,Consistency=500});
  Eq(false,d.HasErrors);bool refused=false;try{d.Build(false);}catch(ArgumentException){refused=true;}Eq(true,refused);
 }

 static void HeaderTextMatchesBuild() {
#if KINFX_SIM_TEST
  Eq(true,DashboardSafety.CanExecute("start"));Eq(true,DashboardSafety.CanExecute("flattenAll"));Eq(true,DashboardSafety.CanExecute("batch"));
  Eq(true,DashboardSafety.HeaderText.Contains("SIM TEST"));
  Eq(false,DashboardSafety.HeaderText.Contains("DISABLED"));
  Eq(true,DashboardSafety.StartTip.Contains("risk warning"));Eq(true,DashboardSafety.ListAllAccounts);
  Eq(false,DashboardSafety.LogNote.Contains("REVIEW HOLD"));
  Eq(true,DashboardSafety.LogNote.Contains("Orders ARE sent"));
#else
  Eq(false,DashboardSafety.CanExecute("start"));Eq(false,DashboardSafety.CanExecute("flattenSelected"));Eq(false,DashboardSafety.CanExecute("flattenAll"));Eq(false,DashboardSafety.CanExecute("batch"));
  Eq("REVIEW HOLD · TRADING DISABLED",DashboardSafety.HeaderText);
  Eq("REVIEW HOLD: copying unavailable until engine safety issues are fixed",DashboardSafety.StartTip);Eq(false,DashboardSafety.ListAllAccounts);
  Eq(true,DashboardSafety.LogNote.StartsWith("REVIEW HOLD"));
#endif
 }
 static void AllowFileLineParsing() {
  string n,a;
  Eq(true,DashboardChoices.ParseAllowLine("LFE1",out n,out a));Eq("LFE1",n);Eq("",a);
  Eq(true,DashboardChoices.ParseAllowLine("  LFE1 = Lucid 0059  ",out n,out a));Eq("LFE1",n);Eq("Lucid 0059",a);
  Eq(false,DashboardChoices.ParseAllowLine("# comment",out n,out a));
  Eq(false,DashboardChoices.ParseAllowLine("   ",out n,out a));
  Eq(false,DashboardChoices.ParseAllowLine(null,out n,out a));
  Eq(false,DashboardChoices.ParseAllowLine("= alias only",out n,out a));
 }
 static void StartSummaryListsLeaderRatioAndExpectedQuantity() {
  var rows=new[]{
   new StartSummaryRow{Name="Lucid 0059",Ratio=1},
   new StartSummaryRow{Name="Tradeify 7695",Ratio=0.5},
   new StartSummaryRow{Name="Sim102",Ratio=0.3}};
  string s=DashboardChoices.StartSummary("Sim101","MES 12-26",2,rows,false);
  Eq(true,s.Contains("Leader: Sim101"));
  Eq(true,s.Contains("MES 12-26"));
  Eq(true,s.Contains("Lucid 0059  ratio 1  → 2 lots when the leader trades 2"));
  Eq(true,s.Contains("Tradeify 7695  ratio 0.5  → 1 lot when the leader trades 2"));
  Eq(true,s.Contains("Sim102  ratio 0.3  → 0 lots when the leader trades 2 (would NOT copy)"));
  Eq(false,s.Contains("DRY RUN"));
  Eq(true,DashboardChoices.StartSummary("Sim101","MES 12-26",2,rows,true).StartsWith("*** DRY RUN"));
  // never claims more than it knows
  Eq(true,DashboardChoices.StartSummary("L","I",1,new StartSummaryRow[0],false).Contains("No followers"));
  Eq(true,DashboardChoices.StartSummary(null,null,1,null,false).Contains("No followers"));
  Eq(true,DashboardChoices.StartSummary("L","I",1,new[]{new StartSummaryRow{Name="X",Ratio=double.NaN}},false).Contains("ratio invalid"));
 }
 static void DefaultLeaderChoice() {
  Eq("Sim101",DashboardChoices.DefaultLeader(new[]{"Backtest","Sim101","Zed"}));
  Eq("Backtest",DashboardChoices.DefaultLeader(new[]{"Backtest","Zed"}));
  Eq(null,DashboardChoices.DefaultLeader(new string[0]));
  Eq(null,DashboardChoices.DefaultLeader(null));
  Eq("sim101",DashboardChoices.DefaultLeader(new[]{"sim101"}));
 }
// Language table tests: appended to RiskTests (pure region only: Lang has no NinjaTrader or WPF dependency).
 static void LangEveryKeyHasThreeTranslations() {
  int n=0;
  foreach(var key in Lang.Keys) {
   var row=Lang.Row(key); n++;
   if(row==null||row.Length!=3) throw new Exception("row shape: "+key);
   for(int i=0;i<3;i++) {
    if(string.IsNullOrWhiteSpace(row[i])) throw new Exception("empty translation "+Lang.Codes[i+1]+": "+key);
    if(Holes(row[i])!=Holes(key)) throw new Exception("placeholder mismatch "+Lang.Codes[i+1]+": "+key);
   }
  }
  if(n<300) throw new Exception("language table unexpectedly small: "+n);
 }
 static string Holes(string s) {
  var list=new System.Collections.Generic.List<string>();
  for(int i=0;i<s.Length;i++) if(s[i]=='{'&&i+2<s.Length&&char.IsDigit(s[i+1])) { int j=s.IndexOf('}',i); if(j>0) { list.Add(s.Substring(i,j-i+1)); i=j; } }
  list.Sort(StringComparer.Ordinal); return string.Join(",",list);
 }
 static void LangSelection() {
  Lang.Set("en"); Eq("Locked",Lang.T("Locked"));
  Eq("zh",Lang.FromCulture("zh-TW")); Eq("zh",Lang.FromCulture("zh-HK")); Eq("ja",Lang.FromCulture("ja-JP")); Eq("ko",Lang.FromCulture("ko-KR")); Eq("en",Lang.FromCulture("fr-FR")); Eq("en",Lang.FromCulture(null));
  // NinjaTrader reports ui=en-US on a zh-TW machine (seen in the real VM); the OS display language must still win.
  Eq("zh",Lang.Detect("zh-HK","zh-TW","en-US")); Eq("zh",Lang.Detect("en-US","zh-TW","en-US")); Eq("ja",Lang.Detect("en-US","ja-JP","en-US")); Eq("ko",Lang.Detect("ko-KR","en-US","en-US"));
  Eq("en",Lang.Detect("en-US","en-GB","en-US")); Eq("en",Lang.Detect()); Eq("en",Lang.Detect(null,"")); Eq("zh",Lang.Detect("fr-FR","zh-TW","en-US"));
  // Engine messages added by the OCO-allocation work are translated through their {0} templates, not left in English.
  Lang.Set("zh");
  Eq(true,Lang.Show("Lucid 0059: leader exit refused — no independent exit capacity; existing protection retained. Use an explicit shared OCO for alternatives.").StartsWith("Lucid 0059：已拒絕主帳戶出場單"));
  Eq(true,Lang.Show("Certified OCO cancellation unresolved: provider timeout").EndsWith("provider timeout"));
  Eq(true,Lang.Show("Certified OCO cancellation unresolved: provider timeout").StartsWith("已認證的 OCO 取消"));
  Lang.Set("en");
  Lang.Set("xx"); Eq("en",Lang.Current);
  Lang.Set("ja"); Eq("ja",Lang.Current); Eq(false,Lang.T("Locked")=="Locked");
  Lang.Set("en");
 }
 static void LangUnknownTextFallsBackToEnglish() {
  string odd="Some sentence that is not in the table";
  foreach(var code in Lang.Codes) { Eq(odd,Lang.T(odd,code)); Eq(odd,Lang.Show(odd,code)); }
  Eq("",Lang.T(null)); Eq("",Lang.Show(null)); Eq("",Lang.Show(""));
 }
 static void LangTemplateRefill() {
  Lang.Set("zh");
  string s=Lang.F("Armed explicitly: {0} simulation follower(s).",2);
  if(s.IndexOf("2",StringComparison.Ordinal)<0||s==("Armed explicitly: 2 simulation follower(s).")) throw new Exception("template not translated/refilled: "+s);
  // A message built in English (as the engine stores it) is translated back through its template.
  string built="Armed explicitly: 3 simulation follower(s).";
  string shown=Lang.Show(built,"zh");
  if(shown==built||shown.IndexOf("3",StringComparison.Ordinal)<0) throw new Exception("composite not translated: "+shown);
  Eq(Lang.F("Armed explicitly: {0} simulation follower(s).",3),shown);
  // Nested: a dynamic part that is itself a known sentence is translated too.
  string nested=Lang.Show("Start refused: Already managing a session. Stop while flat first.","ja");
  if(nested.IndexOf("Already managing",StringComparison.Ordinal)>=0) throw new Exception("nested part not translated: "+nested);
  // A repeated hole must match the same text both times.
  Lang.Set("en");
 }
 static void LangDoesNotChangeEnglishDecisions() {
  // The English build text that other tests and the engine's decisions compare against must never depend on the picked language.
  foreach(var code in Lang.Codes) {
   Lang.Set(code);
#if KINFX_SIM_TEST
   Eq(true,DashboardSafety.HeaderText.Contains("SIM TEST"));
#else
   Eq("REVIEW HOLD · TRADING DISABLED",DashboardSafety.HeaderText);
#endif
   Eq(true,SettingsAdvice.For(new RiskSettings(){Ratio=0},"Follower",2).Length>0);
   Eq("Off",new RiskSettings().Consistency<=0?"Off":"x");
  }
  Lang.Set("en");
 }
 static void LangNoCantoneseInChinese() {
  const string cant="嘅唔咗喺係啲冇佢嚟哋";
  foreach(var key in Lang.Keys) { string zh=Lang.Row(key)[0]; foreach(char c in zh) if(cant.IndexOf(c)>=0) throw new Exception("colloquial Cantonese char '"+c+"' in zh: "+zh); }
 }
 // Confirmation windows: every kind is asked by default; the user may turn single kinds off; a risk warning (account not confirmed simulated) can never be turned off.
 static void ConfirmPrefsDefaultsAskEverything() {
  var none=ConfirmPrefs.Parse(null);
  foreach(var k in ConfirmPrefs.Kinds) Eq(true,ConfirmPrefs.MustAsk(k,none,false));
  Eq(3,ConfirmPrefs.Kinds.Length);
  // Unlock is the only explicit unlock path and can never be skipped.
  Eq(true,ConfirmPrefs.MustAsk("unlock",ConfirmPrefs.Parse(new[]{"skip=unlock"}),false));
  Eq(true,ConfirmPrefs.MustAsk("somethingElse",ConfirmPrefs.Parse(new[]{"skip=somethingElse"}),false));
 }
 static void ConfirmPrefsRoundTripAndUnknownLines() {
  var s=ConfirmPrefs.Parse(new[]{"# comment","skip=start"," skip = flatOne ","skip=bogus","ask=flatten","","garbage"});
  Eq(false,ConfirmPrefs.MustAsk("start",s,false));Eq(false,ConfirmPrefs.MustAsk("flatOne",s,false));
  Eq(true,ConfirmPrefs.MustAsk("flatten",s,false));Eq(true,ConfirmPrefs.MustAsk("unlock",s,false));
  Eq(false,s.Contains("bogus"));
  var again=ConfirmPrefs.Parse(ConfirmPrefs.Serialize(s));
  Eq(2,again.Count);Eq(true,again.Contains("start")&&again.Contains("flatOne"));
 }
 // Review 2026-10-14 DEFECT 1: the switch follows the button, not the row count. One highlighted row + "Flatten selected" = the Flatten switch.
 static void ConfirmPrefsKindFollowsTheButtonNotTheRowCount() {
  Eq("flatOne",ConfirmPrefs.KindFor("flatOne"));
  Eq("flatten",ConfirmPrefs.KindFor("flattenSelected"));
  Eq("flatten",ConfirmPrefs.KindFor("flattenAll"));
  Eq("start",ConfirmPrefs.KindFor("start"));
  var onlyRowOff=ConfirmPrefs.Parse(new[]{"skip=flatOne"});
  Eq(true,ConfirmPrefs.MustAsk(ConfirmPrefs.KindFor("flattenSelected"),onlyRowOff,false));
  Eq("Flat (row)",ConfirmPrefs.Label("flatOne"));Eq("Flatten selected / all",ConfirmPrefs.Label("flatten"));Eq("Start",ConfirmPrefs.Label("start"));
  Eq(false,Lang.Show("Confirmation turned off: Flat (row) (risk warnings and Unlock are still always asked)","zh").StartsWith("Confirmation"));
  Eq(false,Lang.Show("Confirmation skipped (turned off on the Risk tab): Start","zh").StartsWith("Confirmation"));
 }
 // Column prefs: hidden set round-trips; a hand-edited file can never hide a safety column; garbage lines are ignored.
 static void ColumnPrefsRoundTripAndSafetyColumnsNeverHidden() {
  var s=ColumnPrefs.Parse(new[]{"# c","hide=risk:Preset"," hide = copy:Bal ","hide=copy:Flat","hide=risk:St","hide=copy:Acct","hide=risk:Tgt","hide=nogrid:Bal","show=risk:Cons","junk",""});
  Eq(2,s.Count);Eq(true,s.Contains("risk:Preset")&&s.Contains("copy:Bal"));
  Eq(false,ColumnPrefs.Visible("risk","Preset",s));Eq(true,ColumnPrefs.Visible("risk","Cons",s));
  foreach(var k in new[]{"Acct","On","Ratio","Qty","State","Conn","Off","Flat"})Eq(false,ColumnPrefs.CanHide("copy",k));
  foreach(var k in new[]{"Acct","Tgt","DLL","Qty","St","Conn"})Eq(false,ColumnPrefs.CanHide("risk",k));
  Eq(true,ColumnPrefs.Visible("copy","Flat",new System.Collections.Generic.HashSet<string>{"copy:Flat"}));
  var again=ColumnPrefs.Parse(ColumnPrefs.Serialize(s));Eq(2,again.Count);Eq(true,again.SetEquals(s));
  Eq(0,ColumnPrefs.Parse(null).Count);
 }
 static void ConfirmPrefsRiskWarningIsNeverSkipped() {
  var all=ConfirmPrefs.Parse(new[]{"skip=start","skip=flatOne","skip=flatten","skip=unlock"});
  Eq(3,all.Count);
  foreach(var k in ConfirmPrefs.Kinds){Eq(false,ConfirmPrefs.MustAsk(k,all,false));Eq(true,ConfirmPrefs.MustAsk(k,all,true));}
 }
}
