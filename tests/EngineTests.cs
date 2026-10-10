using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NinjaTrader.NinjaScript.AddOns.Kinfx;
namespace NinjaTrader.Core {public static class Globals {public static DateTime MaxDate=DateTime.MaxValue;}}
namespace NinjaTrader.Cbi {
 public enum Provider {Simulator,Playback,Live} public enum ConnectionStatus {Connected,Disconnected} public class Connection {public ConnectionStatus Status=ConnectionStatus.Connected;}
 public enum Currency {Usd} public enum MarketPosition {Long,Short,Flat} public enum OrderAction {Buy,Sell,SellShort,BuyToCover} public enum OrderType {Market,Limit,StopMarket,StopLimit,MIT,Unsupported} public enum OrderEntry {Manual} public enum TimeInForce {Day,Gtd}
 public enum OrderState {Submitted,Accepted,Working,TriggerPending,Cancelled,Rejected,Filled,PartFilled} public enum AccountItem {RealizedProfitLoss,UnrealizedProfitLoss,NetLiquidation,SodLiquidatingValue}
 public class Instrument {public string FullName="MES 12-26";} public class Position {public Instrument Instrument;public int Quantity;public MarketPosition MarketPosition;}
 public class Order {public Account Account;public Instrument Instrument;public string Name,Oco,OrderId;public OrderAction OrderAction;public OrderType OrderType;public OrderEntry OrderEntry;public TimeInForce TimeInForce;public DateTime Gtd;public OrderState OrderState;public int Quantity,Filled,QuantityChanged;public double LimitPrice,StopPrice,LimitPriceChanged,StopPriceChanged;public static bool IsTerminalState(OrderState s){return s==OrderState.Filled||s==OrderState.Rejected||s==OrderState.Cancelled;}}
 public class OrderEventArgs:EventArgs {public Order Order;public OrderState OrderState;public int Quantity,Filled;public string OrderId;public double LimitPrice,StopPrice;}
 // Deliberately fake sink: only counters and in-memory synthetic collections. No NT assembly or brokerage access.
 public class Account {
  public string Name;public Provider Provider=Provider.Simulator;public Connection Connection=new Connection();public Currency Denomination=Currency.Usd;
  public List<Position> Positions=new List<Position>();public List<Order> Orders=new List<Order>();public event EventHandler<OrderEventArgs> OrderUpdate;
  public double Pnl,Equity=50000;public int Submits,Changes,Cancels,Flattens;public bool ThrowSubmit,ThrowCancel,ThrowFlatten,DelayCancel;public ManualResetEvent CreateEntered,CreateRelease;public Action<AccountItem> GetHook;
  public double Get(AccountItem i,Currency c){var h=GetHook;if(h!=null)h(i);return i==AccountItem.RealizedProfitLoss?Pnl:i==AccountItem.UnrealizedProfitLoss?0:Equity;}
  public Order CreateOrder(Instrument i,OrderAction a,OrderType t,OrderEntry e,TimeInForce tif,int q,double l,double s,string oco,string n,DateTime g,object ignored){if(CreateEntered!=null){CreateEntered.Set();CreateRelease.WaitOne();}return new Order {Account=this,Instrument=i,OrderAction=a,OrderType=t,OrderEntry=e,TimeInForce=tif,Quantity=q,LimitPrice=l,StopPrice=s,Oco=oco,Name=n,Gtd=g};}
  public void Submit(Order[] orders){if(ThrowSubmit)throw new InvalidOperationException("Synthetic submit error");foreach(var o in orders){lock(Orders)Orders.Add(o);o.OrderState=OrderState.Working;Interlocked.Increment(ref Submits);Emit(o);}}
  public void Change(Order[] orders){foreach(var o in orders){o.Quantity=o.QuantityChanged;o.LimitPrice=o.LimitPriceChanged;o.StopPrice=o.StopPriceChanged;Interlocked.Increment(ref Changes);Emit(o);}}
  public void Cancel(Order[] orders){if(ThrowCancel)throw new InvalidOperationException("Synthetic cancel error");foreach(var o in orders){if(!DelayCancel)o.OrderState=OrderState.Cancelled;Interlocked.Increment(ref Cancels);if(!DelayCancel)Emit(o);}}
  public void Flatten(Instrument[] inst){if(ThrowFlatten)throw new InvalidOperationException("Synthetic flatten error");Interlocked.Increment(ref Flattens);foreach(var o in Orders.ToArray())Cancel(new[]{o});lock(Positions)Positions.Clear();var fake=CreateOrder(inst[0],OrderAction.Sell,OrderType.Market,OrderEntry.Manual,TimeInForce.Day,1,0,0,"","SyntheticFlatten",DateTime.MaxValue,null);fake.OrderState=OrderState.Submitted;Emit(fake);}
  public void Emit(Order o){var h=OrderUpdate;if(h!=null)h(this,new OrderEventArgs {Order=o,OrderId=o.OrderId,OrderState=o.OrderState,Quantity=o.Quantity,Filled=o.Filled,LimitPrice=o.LimitPrice,StopPrice=o.StopPrice});}
 }
}
class EngineTests {
 static int Main(string[] args){try {var cases=new Action[]{ProcessedOffAck,ProcessedOnAck,PublishControlSnapshot,DelayedCancelProcessedAck,ManualWorkingOrderWithoutCallbackLocks,CancelledExitCancelsEntry,DetachedDelegateCannotEnterRestart,DuplicateFollowersRejected,CompletedExitRetiresDelayedBracket,SettingsFrozenAtRequest,ExitCannotAdoptManualExposure,ReFollowRequiresExecutionFlat,RiskDuringChange,OffDuringCreate,ReservationSurvivesMissingOrderCollection,OffRetainsExits,OppositePendingEntries,ManualActivityLocks,FlatWorkingExits,InvalidLeaderLocks,CancelFailureTelemetry,ExitCompletionCancelsEntry,FailedFlattenKeepsProtection,PerFollowerAtm,OldSessionCallbacks,ZeroQuantityUpdate,RiskCancelsOwned,ExecutionCap,FollowingSafety,QueuedStartGuard,AtmMode,Lifecycle,Isolation,DelayedProtection,SubsetFlatten,StartupAndDisconnect};if(args.Length>0&&args[0]!="all"){cases=cases.Where(test=>test.Method.Name==args[0]).ToArray();Check(cases.Length==1,"Unknown test case "+args[0]);}foreach(var test in cases){test();Console.WriteLine("PASS "+test.Method.Name);}Console.WriteLine("PASS actual serial CopierEngine: "+cases.Length+" fake-sink cases; per-account OFF/risk/ATM/session-generation/execution-reservations/protection/rejection. ZERO real or simulator orders.");return 0;}catch(Exception ex){Console.WriteLine("FAIL "+ex);return 1;}}
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Wait(Func<bool> condition,string message){for(int i=0;i<300;i++){if(condition())return;Thread.Sleep(10);}throw new Exception("timeout: "+message);}
 static NinjaTrader.Cbi.Instrument inst=new NinjaTrader.Cbi.Instrument();
 static NinjaTrader.Cbi.Order LeaderOrder(NinjaTrader.Cbi.Account a,NinjaTrader.Cbi.OrderAction action,string oco="",int qty=2){var o=a.CreateOrder(inst,action,NinjaTrader.Cbi.OrderType.Limit,NinjaTrader.Cbi.OrderEntry.Manual,NinjaTrader.Cbi.TimeInForce.Day,qty,100,0,oco,"SyntheticLeader",DateTime.MaxValue,null);o.OrderState=NinjaTrader.Cbi.OrderState.Submitted;return o;}
 static int Processed(AccountTelemetry t){var field=typeof(AccountTelemetry).GetField("ProcessedFollowGeneration");return field==null?t.FollowGeneration:(int)field.GetValue(t);}
 static void ProcessedOffAck(){
  var l=new NinjaTrader.Cbi.Account{Name="Ack OFF lead"};var a=new NinjaTrader.Cbi.Account{Name="Ack OFF follow"};var captured=new List<AccountTelemetry>();var entered=new ManualResetEvent(false);var release=new ManualResetEvent(false);int block=0;
  using(var e=new CopierEngine(s=>{},t=>{lock(captured)captured.Add(t);},(attached,s)=>{},(n,r)=>{})){
   try{e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch())});Wait(()=>e.EntriesArmed,"ack OFF arm");l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Wait(()=>a.Submits==1,"ack OFF entry");
    a.GetHook=i=>{if(i==NinjaTrader.Cbi.AccountItem.NetLiquidation&&Interlocked.Exchange(ref block,0)==1){entered.Set();release.WaitOne();}};Interlocked.Exchange(ref block,1);e.Poll();Check(entered.WaitOne(3000),"pre-cancel CheckRisk barrier");int request=e.SetFollowing(a.Name,false);release.Set();Wait(()=>a.Cancels==1,"queued OFF cancellation handled");Wait(()=>{lock(captured)return captured.Any(t=>t.FollowGeneration==request&&!t.Following&&!t.DisablePending);},"post OFF telemetry");
    AccountTelemetry[] seen;lock(captured)seen=captured.Where(t=>t.FollowGeneration==request).ToArray();Check(seen.Any(t=>Processed(t)<request),"pre-cancel telemetry must not acknowledge unprocessed OFF request");Wait(()=>{lock(captured)return captured.Any(t=>Processed(t)==request&&!t.Following&&!t.DisablePending);},"processed OFF ack after cancel terminal");
   }finally{release.Set();a.GetHook=null;}
  }
 }
 static void ProcessedOnAck(){
  var l=new NinjaTrader.Cbi.Account{Name="Ack ON lead"};var a=new NinjaTrader.Cbi.Account{Name="Ack ON follow"};var captured=new List<AccountTelemetry>();
  using(var e=new CopierEngine(s=>{},t=>{lock(captured)captured.Add(t);},(attached,s)=>{},(n,r)=>{})){
   e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch())});Wait(()=>e.EntriesArmed,"ack ON arm");int off=e.SetFollowing(a.Name,false);Wait(()=>{lock(captured)return captured.Any(t=>t.FollowGeneration==off&&!t.Following);},"ack ON initial OFF");int request=e.SetFollowing(a.Name,true);Wait(()=>{lock(captured)return captured.Any(t=>t.FollowGeneration==request&&t.Following);},"ack ON handled");
   AccountTelemetry[] seen;lock(captured)seen=captured.Where(t=>t.FollowGeneration==request).ToArray();Check(seen.Any(t=>!t.Following&&Processed(t)<request),"CheckRisk during ON must not acknowledge before follow validation");Check(seen.Any(t=>t.Following&&Processed(t)==request),"processed ON ack only after validation");
  }
 }
 static void PublishControlSnapshot(){
  var l=new NinjaTrader.Cbi.Account{Name="Snapshot lead"};var a=new NinjaTrader.Cbi.Account{Name="Snapshot follow"};var captured=new List<AccountTelemetry>();var entered=new ManualResetEvent(false);var release=new ManualResetEvent(false);int reads=0;
  using(var e=new CopierEngine(s=>{},t=>{lock(captured)captured.Add(t);},(attached,s)=>{},(n,r)=>{})){
   try{e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch())});Wait(()=>e.EntriesArmed,"snapshot arm");a.GetHook=i=>{if(i==NinjaTrader.Cbi.AccountItem.RealizedProfitLoss&&Interlocked.Increment(ref reads)==2){entered.Set();release.WaitOne();}};e.Poll();Check(entered.WaitOne(3000),"Publish account Get barrier after control snapshot");int request=e.SetFollowing(a.Name,false);e.GuardedPause();release.Set();Wait(()=>{lock(captured)return captured.Any(t=>t.FollowGeneration==request);},"snapshot drain");AccountTelemetry before;lock(captured)before=captured.Last(t=>t.FollowGeneration==0);Check(before.Following&&before.State=="Active","Publish must use immutable collected control values, not later entries state");
   }finally{release.Set();a.GetHook=null;}
  }
 }
 static void DelayedCancelProcessedAck(){var l=new NinjaTrader.Cbi.Account{Name="Delayed ack lead"};var a=new NinjaTrader.Cbi.Account{Name="Delayed ack follow",DelayCancel=true};var captured=new List<AccountTelemetry>();using(var e=new CopierEngine(s=>{},t=>{lock(captured)captured.Add(t);},(attached,s)=>{},(n,r)=>{})){e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch())});Wait(()=>e.EntriesArmed,"delayed ack arm");l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Wait(()=>a.Submits==1,"delayed ack entry");int request=e.SetFollowing(a.Name,false);Wait(()=>{lock(captured)return captured.Any(t=>Processed(t)==request);},"queued delayed cancel processed");lock(captured)Check(captured.Where(t=>Processed(t)==request).All(t=>!t.Following&&t.DisablePending),"processed request cannot claim terminal confirmation while Cancel awaits acknowledgement");a.Orders[0].OrderState=NinjaTrader.Cbi.OrderState.Cancelled;a.Emit(a.Orders[0]);Wait(()=>{lock(captured)return captured.Any(t=>Processed(t)==request&&!t.Following&&!t.DisablePending);},"terminal cancellation clears pending with current processed ack");}}
 static void ManualWorkingOrderWithoutCallbackLocks(){
  var l=new NinjaTrader.Cbi.Account{Name="Manual poll lead"};var a=new NinjaTrader.Cbi.Account{Name="Manual poll follow"};int locks=0;
  using(var e=new CopierEngine(s=>{},t=>{},(attached,s)=>{},(n,r)=>Interlocked.Increment(ref locks))){
   e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch())});Wait(()=>e.EntriesArmed,"manual poll arm");lock(a.Orders)a.Orders.Add(LeaderOrder(a,NinjaTrader.Cbi.OrderAction.Buy));l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Wait(()=>locks==1,"unknown manual working collection activity locks without callback");Check(a.Submits==0&&a.Cancels==0,"unknown working activity never adopted or cancelled");
  }
 }
 static void CancelledExitCancelsEntry(){
  var l=new NinjaTrader.Cbi.Account{Name="Cancelled exit lead"};var a=new NinjaTrader.Cbi.Account{Name="Cancelled exit follow"};int locks=0;
  using(var e=new CopierEngine(s=>{},t=>{},(attached,s)=>{},(n,r)=>Interlocked.Increment(ref locks))){
   e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch())});Wait(()=>e.EntriesArmed,"cancelled exit arm");l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",4));Wait(()=>a.Submits==1,"cancelled exit entry");a.Orders[0].Filled=2;a.Orders[0].OrderState=NinjaTrader.Cbi.OrderState.PartFilled;a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=2,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});a.Emit(a.Orders[0]);l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"cancelled protection",4));Wait(()=>a.Submits==2,"cancelled protection");a.Orders[1].OrderState=NinjaTrader.Cbi.OrderState.Cancelled;a.Emit(a.Orders[1]);Wait(()=>a.Cancels==1,"terminal cancelled protection cancels entry remainder");Wait(()=>locks==1,"lost protection surfaces durable lock");
  }
 }
 static void DetachedDelegateCannotEnterRestart(){
  var l=new NinjaTrader.Cbi.Account{Name="Detached delegate lead"};var a=new NinjaTrader.Cbi.Account{Name="Detached delegate follow"};
  using(var e=new CopierEngine(s=>{},t=>{},(attached,s)=>{},(n,r)=>{})){
   var fs=new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch())};e.Start(l,inst,fs);Wait(()=>e.EntriesArmed,"delegate initial arm");var stale=(EventHandler<NinjaTrader.Cbi.OrderEventArgs>)typeof(NinjaTrader.Cbi.Account).GetField("OrderUpdate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(l);
   e.StopFlat();Wait(()=>!e.Attached,"delegate detach");e.Start(l,inst,fs);Wait(()=>e.EntriesArmed,"delegate restart arm");var order=LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy);stale(l,new NinjaTrader.Cbi.OrderEventArgs{Order=order,OrderId=order.OrderId,Filled=order.Filled,OrderState=order.OrderState,Quantity=order.Quantity,LimitPrice=order.LimitPrice});Thread.Sleep(100);Check(a.Submits==0,"detached session delegate cannot enter restarted same leader session");l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Wait(()=>a.Submits==1,"current delegate copies");
  }
 }
 static void DuplicateFollowersRejected(){
  var l=new NinjaTrader.Cbi.Account{Name="Duplicate lead"};var a=new NinjaTrader.Cbi.Account{Name="Duplicate follow"};
  using(var e=new CopierEngine(s=>{},t=>{},(attached,s)=>{},(n,r)=>{})){
   var f=Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch());e.Start(l,inst,new[]{f,f});Thread.Sleep(100);Check(!e.Attached&&!e.EntriesArmed,"duplicate follower selections cannot arm / double copy");
  }
 }
 static void CompletedExitRetiresDelayedBracket(){
  var l=new NinjaTrader.Cbi.Account{Name="Retired bracket lead"};var a=new NinjaTrader.Cbi.Account{Name="Retired bracket follow"};
  using(var e=new CopierEngine(s=>{},t=>{},(attached,s)=>{},(n,r)=>{})){
   e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch())});Wait(()=>e.EntriesArmed,"retired bracket arm");l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",4));Wait(()=>a.Submits==1,"retired entry");a.Orders[0].Filled=2;a.Orders[0].OrderState=NinjaTrader.Cbi.OrderState.PartFilled;a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=2,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});a.Emit(a.Orders[0]);
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"first close",2));Wait(()=>a.Submits==2,"first close");l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"independent waiting bracket",4));Thread.Sleep(100);Check(a.Submits==2,"independent close waits reserved budget");a.Orders[1].Filled=2;a.Orders[1].OrderState=NinjaTrader.Cbi.OrderState.Filled;a.Positions.Clear();a.Emit(a.Orders[1]);Wait(()=>a.Cancels==1,"retire remainder");
   a.Orders[0].Filled=4;a.Orders[0].OrderState=NinjaTrader.Cbi.OrderState.Filled;a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=2,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});a.Emit(a.Orders[0]);e.Poll();Thread.Sleep(100);Check(a.Submits==2,"completed cycle never releases stale independent waiting bracket on late fill");
  }
 }
 static void SettingsFrozenAtRequest(){
  var l=new NinjaTrader.Cbi.Account{Name="Frozen request lead"};var a=new NinjaTrader.Cbi.Account{Name="Frozen request follow"};var entered=new ManualResetEvent(false);var release=new ManualResetEvent(false);int calls=0;
  using(var e=new CopierEngine(s=>{},t=>{},(attached,s)=>{if(Interlocked.Increment(ref calls)==1){entered.Set();release.WaitOne();}},(n,r)=>{})){
   e.GuardedPause();Check(entered.WaitOne(3000),"frozen settings worker barrier");var settings=new RiskSettings{MaxContracts=10,Ratio=1};e.Start(l,inst,new[]{Tuple.Create(a,settings,new RiskLatch())});settings.Ratio=4;release.Set();Wait(()=>e.EntriesArmed,"frozen settings arm");l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Wait(()=>a.Submits==1,"frozen settings copy");Check(a.Orders[0].Quantity==2,"settings captured before Start enters worker queue");
  }
 }
 static void ExitCannotAdoptManualExposure(){
  var l=new NinjaTrader.Cbi.Account{Name="Adoption lead"};var a=new NinjaTrader.Cbi.Account{Name="Adoption follow"};int locks=0;
  using(var e=new CopierEngine(s=>{},t=>{},(attached,s)=>{},(n,r)=>Interlocked.Increment(ref locks))){
   e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch())});Wait(()=>e.EntriesArmed,"adoption arm");l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Wait(()=>a.Submits==1,"adoption owned entry");a.Orders[0].Filled=2;a.Orders[0].OrderState=NinjaTrader.Cbi.OrderState.Filled;a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=8,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});a.Emit(a.Orders[0]);l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"adoption bracket",8));Wait(()=>a.Submits==2,"owned subset exit");Check(a.Orders[1].Quantity==2,"exit only covers owned subset, never adopts unrelated exposure");Check(locks==1,"unknown excess position locks");
  }
 }
 static void ReFollowRequiresExecutionFlat(){
  var l=new NinjaTrader.Cbi.Account{Name="ReFollow ledger lead"};var a=new NinjaTrader.Cbi.Account{Name="ReFollow ledger follow"};AccountTelemetry latest=null;
  using(var e=new CopierEngine(s=>{},t=>{latest=t;},(attached,s)=>{},(n,r)=>{})){
   e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch())});Wait(()=>e.EntriesArmed,"refollow ledger arm");l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Wait(()=>a.Submits==1,"refollow ledger entry");a.Orders[0].Filled=2;a.Orders[0].OrderState=NinjaTrader.Cbi.OrderState.Filled;a.Emit(a.Orders[0]);Follow(e,a.Name,false);Wait(()=>latest!=null&&!latest.Following,"refollow off");Follow(e,a.Name,true);Thread.Sleep(100);Check(!latest.Following,"refollow blocked by owned fill even before Position collection catches up");
  }
 }
 static void RiskDuringChange(){
  var l=new NinjaTrader.Cbi.Account{Name="Change risk lead"};var a=new NinjaTrader.Cbi.Account{Name="Change risk follow"};
  using(var e=new CopierEngine(s=>{},t=>{},(attached,s)=>{},(n,r)=>{})){
   e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10,Target=100},new RiskLatch())});Wait(()=>e.EntriesArmed,"change risk arm");var order=LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy);l.Emit(order);Wait(()=>a.Submits==1,"change risk entry");a.Pnl=100;order.Quantity=1;order.LimitPrice=99;order.OrderState=NinjaTrader.Cbi.OrderState.Working;l.Emit(order);Wait(()=>a.Cancels==1,"change risk cancel");Thread.Sleep(100);Check(a.Changes==0,"risk cancellation must not be followed by Change on terminal entry");
  }
 }
 static void OffDuringCreate(){
  var l=new NinjaTrader.Cbi.Account{Name="Create barrier lead"};var a=new NinjaTrader.Cbi.Account{Name="Create barrier follow",CreateEntered=new ManualResetEvent(false),CreateRelease=new ManualResetEvent(false)};AccountTelemetry latest=null;
  using(var e=new CopierEngine(s=>{},t=>{latest=t;},(attached,s)=>{},(n,r)=>{})){
   e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch())});Wait(()=>e.EntriesArmed,"create barrier arm");l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Check(a.CreateEntered.WaitOne(3000),"create broker barrier");Follow(e,a.Name,false);a.CreateRelease.Set();Wait(()=>latest!=null&&!latest.Following,"create off drained");Check(a.Submits==0,"off while CreateOrder blocked prevents subsequent Submit");
  }
 }
 static void ReservationSurvivesMissingOrderCollection(){
  var l=new NinjaTrader.Cbi.Account{Name="Reservation lead"};var a=new NinjaTrader.Cbi.Account{Name="Reservation follow"};
  using(var e=new CopierEngine(s=>{},t=>{},(attached,s)=>{},(n,r)=>{})){
   e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=3},new RiskLatch())});Wait(()=>e.EntriesArmed,"reservation arm");l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Wait(()=>a.Submits==1,"reservation entry");lock(a.Orders)a.Orders.Clear();l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Thread.Sleep(100);Check(a.Submits==1,"owned reservation retained when adapter Orders omits submitted order");
  }
 }
 static void OffRetainsExits(){
  var l=new NinjaTrader.Cbi.Account{Name="Off exits lead"};var a=new NinjaTrader.Cbi.Account{Name="Off exits follow"};AccountTelemetry latest=null;
  using(var e=new CopierEngine(s=>{},t=>{latest=t;},(attached,s)=>{},(n,r)=>{})){
   e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch())});Wait(()=>e.EntriesArmed,"off exits arm");l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",4));Wait(()=>a.Submits==1,"off exits entry");a.Orders[0].Filled=2;a.Orders[0].OrderState=NinjaTrader.Cbi.OrderState.PartFilled;a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=2,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});a.Emit(a.Orders[0]);var stop=LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"off bracket",4);l.Emit(stop);Wait(()=>a.Submits==2,"off exits stop");Follow(e,a.Name,false);Wait(()=>a.Cancels==1&&latest!=null&&!latest.Following,"off exits telemetry");Check(a.Orders[1].OrderState==NinjaTrader.Cbi.OrderState.Working,"off preserves protective exit");Check(latest.State!="Active","disabled telemetry must never claim active");Check(latest.Position==2&&latest.Direction=="Long","off telemetry current direction / position");
   Follow(e,a.Name,true);Thread.Sleep(100);Check(!latest.Following,"follow blocked while exposure or exits remain");stop.LimitPrice=99;l.Emit(stop);Wait(()=>a.Changes==1,"disabled follower still manages exit changes");
  }
 }
 static void OppositePendingEntries(){
  var l=new NinjaTrader.Cbi.Account{Name="Opposite lead"};var a=new NinjaTrader.Cbi.Account{Name="Opposite follow"};int locks=0;
  using(var e=new CopierEngine(s=>{},t=>{},(attached,s)=>{},(n,r)=>Interlocked.Increment(ref locks))){
   e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch())});Wait(()=>e.EntriesArmed,"opposite arm");l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Wait(()=>a.Submits==1,"opposite buy");l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.SellShort));Wait(()=>locks==1,"opposite pending side rejected");Check(a.Submits==1,"never submit pending opposite-direction entry");
  }
 }
 static void ManualActivityLocks(){
  var l=new NinjaTrader.Cbi.Account{Name="Manual lead"};var a=new NinjaTrader.Cbi.Account{Name="Manual follow"};int locks=0;
  using(var e=new CopierEngine(s=>{},t=>{},(attached,s)=>{},(n,r)=>Interlocked.Increment(ref locks))){
   e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch())});Wait(()=>e.EntriesArmed,"manual arm");var manual=LeaderOrder(a,NinjaTrader.Cbi.OrderAction.Buy);a.Orders.Add(manual);a.Emit(manual);Wait(()=>locks==1,"unknown follower activity must fail closed");l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Thread.Sleep(100);Check(a.Submits==0&&a.Cancels==0,"manual activity neither adopted nor cancelled");
  }
 }
 static void FlatWorkingExits(){
  var l=new NinjaTrader.Cbi.Account{Name="Flat exits lead"};var a=new NinjaTrader.Cbi.Account{Name="Flat exits follow"};int locks=0;
  using(var e=new CopierEngine(s=>{},t=>{},(attached,s)=>{},(n,r)=>Interlocked.Increment(ref locks))){
   e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch())});Wait(()=>e.EntriesArmed,"flat exit arm");l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Wait(()=>a.Submits==1,"flat exit entry");a.Orders[0].Filled=2;a.Orders[0].OrderState=NinjaTrader.Cbi.OrderState.Filled;a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=2,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});a.Emit(a.Orders[0]);l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"flat exits bracket"));Wait(()=>a.Submits==2,"flat exit protection");a.Positions.Clear();e.Poll();
   // POLICY (Issue #9, decided): a flat Position reading while the engine still owns exposure is ambiguous telemetry.
   // It must NOT cancel the only protective exit. It locks the account and tells the user to reconcile by hand.
   // Superseded oracle (kept for history): Wait(()=>a.Cancels==1,"flat working exits must not reverse exposure");
   Wait(()=>locks==1,"flat position / execution mismatch locks");Thread.Sleep(150);
   Check(a.Cancels==0,"ambiguous flat telemetry must not cancel the sole protective exit; cancels="+a.Cancels);
   Check(a.Orders[1].OrderState==NinjaTrader.Cbi.OrderState.Working,"protective exit still working; state="+a.Orders[1].OrderState);
  }
 }
 static void InvalidLeaderLocks(){
  foreach(string bad in new[]{"action","type","nan","oversize"}){
   var l=new NinjaTrader.Cbi.Account{Name="Invalid lead "+bad};var a=new NinjaTrader.Cbi.Account{Name="Invalid follow "+bad};int locks=0;
   using(var e=new CopierEngine(s=>{},t=>{},(attached,s)=>{},(n,r)=>Interlocked.Increment(ref locks))){
    e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch())});Wait(()=>e.EntriesArmed,"invalid arm");var order=LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy);if(bad=="action")order.OrderAction=(NinjaTrader.Cbi.OrderAction)999;if(bad=="type")order.OrderType=NinjaTrader.Cbi.OrderType.Unsupported;if(bad=="nan")order.LimitPrice=double.NaN;if(bad=="oversize")order.Quantity=int.MaxValue;l.Emit(order);Wait(()=>locks==1,"invalid leader durable lock "+bad);Check(a.Submits==0,"invalid leader no submit "+bad);
   }
  }
 }
 static void CancelFailureTelemetry(){
  var l=new NinjaTrader.Cbi.Account{Name="Cancel failure lead"};var a=new NinjaTrader.Cbi.Account{Name="Cancel failure follow",ThrowCancel=true};AccountTelemetry latest=null;int locks=0;
  using(var e=new CopierEngine(s=>{},t=>{latest=t;},(attached,s)=>{},(n,r)=>Interlocked.Increment(ref locks))){
   e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch())});Wait(()=>e.EntriesArmed,"cancel failure arm");l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Wait(()=>a.Submits==1,"cancel failure entry");
   var m=typeof(CopierEngine).GetMethod("SetFollowing");Check(m.ReturnType==typeof(int),"SetFollowing must return request generation");int request=(int)m.Invoke(e,new object[]{a.Name,false});
   Wait(()=>latest!=null&&latest.DisablePending&&!latest.Following,"cancel failure off telemetry");var field=typeof(AccountTelemetry).GetField("FollowGeneration");Check(field!=null,"telemetry FollowGeneration required");Check((int)field.GetValue(latest)==request,"telemetry snapshots request generation");Check(locks==1,"cancel failure durable lock once");
   Follow(e,a.Name,true);Thread.Sleep(100);Check(!latest.Following,"unresolved cancellation cannot rearm");
  }
 }
 static void ExitCompletionCancelsEntry(){
  var l=new NinjaTrader.Cbi.Account{Name="Exit completion lead"};var a=new NinjaTrader.Cbi.Account{Name="Exit completion follow"};int locked=0;
  using(var e=new CopierEngine(s=>{},t=>{},(attached,s)=>{},(n,r)=>Interlocked.Increment(ref locked))){
   e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch())});Wait(()=>e.EntriesArmed,"completion arm");l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",4));Wait(()=>a.Submits==1,"completion entry");var entry=a.Orders[0];entry.Filled=2;entry.OrderState=NinjaTrader.Cbi.OrderState.PartFilled;a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=2,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});a.Emit(entry);
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"completed bracket",4));Wait(()=>a.Submits==2,"completion stop");var stop=a.Orders[1];stop.Filled=2;stop.OrderState=NinjaTrader.Cbi.OrderState.Filled;a.Positions.Clear();a.Emit(stop);Wait(()=>a.Cancels==1,"exit completion cancels owned entry remainder");
   // Cancellation raced an in-flight fill: cumulative fills still increase monotonically.
   entry.Filled=4;entry.OrderState=NinjaTrader.Cbi.OrderState.Filled;a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=2,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});a.Emit(entry);Wait(()=>locked==1,"late uncovered fill must lock");e.Poll();Thread.Sleep(100);Check(a.Submits==2,"never resurrect stale completed bracket or replace entry");
  }
 }
 static void FailedFlattenKeepsProtection(){
  var l=new NinjaTrader.Cbi.Account{Name="Failed flatten lead"};var a=new NinjaTrader.Cbi.Account{Name="Failed flatten follow",ThrowFlatten=true};int locked=0;
  using(var e=new CopierEngine(s=>{},t=>{},(attached,s)=>{},(n,r)=>Interlocked.Increment(ref locked))){
   e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch())});Wait(()=>e.EntriesArmed,"failed flatten arm");l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Wait(()=>a.Submits==1,"failed flatten entry");a.Orders[0].Filled=2;a.Orders[0].OrderState=NinjaTrader.Cbi.OrderState.Filled;a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=2,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});a.Emit(a.Orders[0]);
   e.Flatten(new[]{a},inst);Wait(()=>locked==1,"failed flatten lock");l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"post failure protection"));Thread.Sleep(200);
   // POLICY (Issue #9, decided): a thrown Flatten may still have been accepted by the provider. Do NOT copy a new leader exit
   // onto that account (it could double-reduce and reverse it), do NOT retry the Flatten, lock and notify.
   // Superseded oracle (kept for history): Wait(()=>a.Submits==2,"failed flatten retains new protective exits");
   Check(a.Submits==1,"a thrown Flatten must not authorise copying a new exit; submits="+a.Submits);
   Check(a.Flattens==0,"a thrown Flatten is never retried by the engine; flattens="+a.Flattens);
   Check(!e.EntriesArmed,"failed flatten stays guarded");
  }
 }
 static void PerFollowerAtm(){
  var l=new NinjaTrader.Cbi.Account{Name="Per ATM lead"};var a=new NinjaTrader.Cbi.Account{Name="Per ATM off"};var b=new NinjaTrader.Cbi.Account{Name="Per ATM on"};var settings=new RiskSettings{MaxContracts=10,CopyAtm=false};
  using(var e=new CopierEngine(s=>{},t=>{},(attached,s)=>{},(n,r)=>{})){
   e.Start(l,inst,new[]{Tuple.Create(a,settings,new RiskLatch()),Tuple.Create(b,new RiskSettings{MaxContracts=10,CopyAtm=true},new RiskLatch())});Wait(()=>e.EntriesArmed,"per ATM arm");settings.CopyAtm=true;l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Wait(()=>a.Submits==1&&b.Submits==1,"per ATM entry");
   foreach(var f in new[]{a,b}){f.Orders[0].Filled=2;f.Orders[0].OrderState=NinjaTrader.Cbi.OrderState.Filled;f.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=2,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});f.Emit(f.Orders[0]);}
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"bracket"));Wait(()=>b.Submits==2,"ATM on bracket");Thread.Sleep(100);Check(a.Submits==1,"ATM off account skips bracket with frozen settings");var close=LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell);close.OrderType=NinjaTrader.Cbi.OrderType.Market;l.Emit(close);Wait(()=>a.Submits==2,"ATM off market close");
  }
 }
 static void OldSessionCallbacks(){
  var old=new NinjaTrader.Cbi.Account{Name="Old lead"};var current=new NinjaTrader.Cbi.Account{Name="New lead"};var a=new NinjaTrader.Cbi.Account{Name="Session follower"};var entered=new ManualResetEvent(false);var release=new ManualResetEvent(false);int block=0;
  using(var e=new CopierEngine(s=>{},t=>{if(Interlocked.Exchange(ref block,0)==1){entered.Set();release.WaitOne();}},(attached,s)=>{},(n,r)=>{})){
   var fs=new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch())};e.Start(old,inst,fs);Wait(()=>e.EntriesArmed,"old arm");Interlocked.Exchange(ref block,1);e.Poll();Check(entered.WaitOne(3000),"old callback worker barrier");
   e.StopFlat();e.Start(current,inst,fs);old.Emit(LeaderOrder(old,NinjaTrader.Cbi.OrderAction.Buy));release.Set();Wait(()=>e.EntriesArmed,"new arm");Thread.Sleep(100);Check(a.Submits==0,"old leader queued callback cannot leak into new session");current.Emit(LeaderOrder(current,NinjaTrader.Cbi.OrderAction.Buy));Wait(()=>a.Submits==1,"new leader copies");
  }
 }
 static void ZeroQuantityUpdate(){
  var l=new NinjaTrader.Cbi.Account{Name="Zero leader"};var a=new NinjaTrader.Cbi.Account{Name="Zero follower"};
  using(var e=new CopierEngine(s=>{},t=>{},(attached,s)=>{},(n,r)=>{})){
   e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10,Ratio=.25},new RiskLatch())});Wait(()=>e.EntriesArmed,"zero arm");var order=LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",4);l.Emit(order);Wait(()=>a.Submits==1,"zero initial");order.Quantity=1;order.OrderState=NinjaTrader.Cbi.OrderState.PartFilled;order.Filled=1;l.Emit(order);Wait(()=>a.Cancels==1,"PartFilled zero desired cancels existing remainder");
  }
 }
 static void RiskCancelsOwned(){
  foreach(bool loss in new[]{false,true}){
   var l=new NinjaTrader.Cbi.Account{Name="Risk lead "+loss};var a=new NinjaTrader.Cbi.Account{Name="Risk follow "+loss};int locked=0;
   using(var e=new CopierEngine(s=>{},t=>{},(attached,s)=>{},(n,r)=>Interlocked.Increment(ref locked))){
    e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10,Target=loss?0:100,Loss=loss?100:0},new RiskLatch())});Wait(()=>e.EntriesArmed,"risk cancel arm");l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Wait(()=>a.Submits==1,"risk owned entry");
    var manual=LeaderOrder(a,NinjaTrader.Cbi.OrderAction.Buy);a.Orders.Add(manual);a.Pnl=loss?-100:100;e.Poll();Wait(()=>locked==1,"risk lock");Wait(()=>a.Cancels==1,"risk cancels owned entry remainder");Check(manual.OrderState==NinjaTrader.Cbi.OrderState.Submitted,"risk leaves manual order untouched");
    l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Thread.Sleep(100);Check(a.Submits==1,"risk blocks new entries");
   }
  }
 }
 static void ExecutionCap(){
  var l=new NinjaTrader.Cbi.Account{Name="Cap leader"};var a=new NinjaTrader.Cbi.Account{Name="Cap follower"};
  using(var e=new CopierEngine(s=>{},t=>{},(attached,s)=>{},(n,r)=>{})){
   e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=3},new RiskLatch())});Wait(()=>e.EntriesArmed,"cap arm");l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Wait(()=>a.Submits==1,"cap entry");a.Orders[0].Filled=2;a.Orders[0].OrderState=NinjaTrader.Cbi.OrderState.Filled;a.Emit(a.Orders[0]);
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Thread.Sleep(150);Check(a.Submits==1,"filled exposure missing from Position must still consume cap");
  }
 }
 static void Follow(CopierEngine e,string name,bool on){var m=typeof(CopierEngine).GetMethod("SetFollowing");Check(m!=null,"SetFollowing API missing");m.Invoke(e,new object[]{name,on});}
 static void FollowingSafety(){
  var l=new NinjaTrader.Cbi.Account{Name="Follow leader"};var a=new NinjaTrader.Cbi.Account{Name="Follow A"};var b=new NinjaTrader.Cbi.Account{Name="Follow B"};var entered=new ManualResetEvent(false);var release=new ManualResetEvent(false);int block=0;
  using(var e=new CopierEngine(s=>{},t=>{if(t.Name==a.Name&&Interlocked.Exchange(ref block,0)==1){entered.Set();release.WaitOne();}},(attached,s)=>{},(n,r)=>{})){
   e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch()),Tuple.Create(b,new RiskSettings{MaxContracts=10},new RiskLatch())});Wait(()=>e.EntriesArmed,"follow arm");
   var first=LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy);l.Emit(first);Wait(()=>a.Submits==1&&b.Submits==1,"follow first");
   Interlocked.Exchange(ref block,1);e.Poll();Check(entered.WaitOne(3000),"follow worker barrier");var old=LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy);l.Emit(old);Follow(e,a.Name,false);release.Set();Wait(()=>b.Submits==2&&a.Cancels==1,"off cancels only own entries");Check(a.Submits==1,"off immediate prevents queued submit");
   Follow(e,a.Name,true);Thread.Sleep(100);l.Emit(old);Thread.Sleep(100);Check(a.Submits==1,"reenable must not catch up observed leader order");l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Wait(()=>a.Submits==2,"explicit clean follow copies fresh order");
  }
 }
 static void QueuedStartGuard(){
  foreach(bool stop in new[]{false,true}){
   var entered=new ManualResetEvent(false);var release=new ManualResetEvent(false);var drained=new ManualResetEvent(false);
   var l=new NinjaTrader.Cbi.Account{Name="Queued lead "+stop};var a=new NinjaTrader.Cbi.Account{Name="Queued follow "+stop};int calls=0;
   using(var e=new CopierEngine(s=>{},t=>{},(attached,s)=>{if(Interlocked.Increment(ref calls)==1){entered.Set();release.WaitOne();}else drained.Set();},(n,r)=>{})){
    e.GuardedPause();Check(entered.WaitOne(3000),"worker blocked");e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch())});if(stop){e.StopFlat();e.GuardedPause();}else e.GuardedPause();release.Set();Check(drained.WaitOne(3000),"guard drained");Thread.Sleep(100);Check(!e.EntriesArmed,"queued Start must not overwrite immediate guard "+stop);
   }
  }
 }
 static void AtmMode(){
  foreach(bool mirror in new[]{false,true}){
   var l=new NinjaTrader.Cbi.Account{Name="Fake ATM lead "+mirror};var a=new NinjaTrader.Cbi.Account{Name="Fake ATM follower "+mirror};
   using(var e=new CopierEngine(s=>{},t=>{},(attached,s)=>{},(n,r)=>{})){
    e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch())},mirror);Wait(()=>e.EntriesArmed,"ATM mode arm");l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Wait(()=>a.Submits==1,"ATM entry copy");
    a.Orders[0].Filled=2;a.Orders[0].OrderState=NinjaTrader.Cbi.OrderState.Filled;lock(a.Positions)a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=2,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});a.Emit(a.Orders[0]);
    var exit=LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"atm bracket");l.Emit(exit);Thread.Sleep(100);Check(a.Submits==(mirror?2:1),"ATM toggle controls nonmarket exit mirroring");
    if(!mirror){exit=LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell);exit.OrderType=NinjaTrader.Cbi.OrderType.Market;l.Emit(exit);Wait(()=>a.Submits==2,"ATM off still copies market close");}
   }
  }
 }
 static void Lifecycle(){
  var l=new NinjaTrader.Cbi.Account{Name="Fake leader"};var a=new NinjaTrader.Cbi.Account{Name="Fake A"};var b=new NinjaTrader.Cbi.Account{Name="Fake B"};
  var logs=new List<string>();int locks=0;using(var engine=new CopierEngine(s=>{lock(logs)logs.Add(s);},t=>{},(attached,s)=>{},(name,reason)=>Interlocked.Increment(ref locks))){
   engine.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10,Target=100},new RiskLatch()),Tuple.Create(b,new RiskSettings{MaxContracts=10},new RiskLatch())});Wait(()=>engine.EntriesArmed,"arm");
   var entry=LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy);l.Emit(entry);l.Emit(entry);Wait(()=>a.Submits==1&&b.Submits==1,"single submits");Thread.Sleep(100);Check(a.Submits==1,"dedupe");
   lock(a.Positions)a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=2,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});lock(b.Positions)b.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=2,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});
   a.Orders[0].OrderState=NinjaTrader.Cbi.OrderState.Filled;a.Orders[0].Filled=2;b.Orders[0].OrderState=NinjaTrader.Cbi.OrderState.Filled;b.Orders[0].Filled=2;a.Emit(a.Orders[0]);b.Emit(b.Orders[0]);
   var stop=LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"OCO_LONG_"+new string('x',100));l.Emit(stop);Wait(()=>a.Submits==2&&b.Submits==2,"exit copy");
   var target=LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,stop.Oco);target.LimitPrice=110;l.Emit(target);Wait(()=>a.Submits==3&&b.Submits==3,"OCO target");
   Check(a.Orders[1].Oco==a.Orders[2].Oco,"same follower stable OCO");Check(a.Orders[1].Oco!=b.Orders[1].Oco,"account isolated OCO");
   engine.GuardedPause();var blocked=LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy);l.Emit(blocked);Thread.Sleep(100);Check(a.Submits==3,"pause blocks entries");
   a.Pnl=100;engine.Poll();Wait(()=>locks>0,"target durable lock");stop.LimitPrice=102;stop.OrderState=NinjaTrader.Cbi.OrderState.Working;l.Emit(stop);Wait(()=>a.Changes>0&&b.Changes>0,"locked protection price update");
   target.OrderState=NinjaTrader.Cbi.OrderState.Cancelled;l.Emit(target);Wait(()=>a.Cancels==1&&b.Cancels==1,"cancel mirrors");
   engine.Flatten(new[]{l,a,b},inst);Wait(()=>a.Flattens==1&&b.Flattens==1,"flatten");Thread.Sleep(100);Check(a.Submits==3&&b.Submits==3,"flatten leader cannot create copies");
  }
 }
 static void DelayedProtection(){
  var l=new NinjaTrader.Cbi.Account{Name="Fake delayed leader"};var a=new NinjaTrader.Cbi.Account{Name="Fake delayed follower"};
  using(var e=new CopierEngine(s=>{},t=>{},(attached,s)=>{},(n,r)=>{})){
   e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch())});Wait(()=>e.EntriesArmed,"delayed arm");
   var entry=LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",4);l.Emit(entry);Wait(()=>a.Submits==1,"delayed entry");var stop=LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"bracket",4);l.Emit(stop);Thread.Sleep(100);Check(a.Submits==1,"exit waiting flat");
   // Order fill callback comes before Position collection update, which some adapters can do.
   a.Orders[0].Filled=2;a.Orders[0].OrderState=NinjaTrader.Cbi.OrderState.PartFilled;a.Emit(a.Orders[0]);Thread.Sleep(100);
   lock(a.Positions)a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=2,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});e.Poll();Wait(()=>a.Submits==2,"late position must release waiting protection");Check(a.Orders[1].Quantity==2,"partial exposure caps exit");
   lock(a.Positions){a.Positions[0].Quantity=2;}a.Orders[0].Filled=2;a.Orders[0].OrderState=NinjaTrader.Cbi.OrderState.PartFilled;a.Emit(a.Orders[0]);
   stop.Quantity=4;stop.OrderState=NinjaTrader.Cbi.OrderState.Working;l.Emit(stop);Thread.Sleep(100);
   lock(a.Positions){a.Positions[0].Quantity=4;}a.Orders[0].Filled=4;a.Orders[0].OrderState=NinjaTrader.Cbi.OrderState.Filled;a.Emit(a.Orders[0]);
   Wait(()=>a.Orders[1].Quantity==4,"protection expands as remaining owned entry fills");
  }
 }
 static void SubsetFlatten(){
  var l=new NinjaTrader.Cbi.Account{Name="Fake subset leader"};var a=new NinjaTrader.Cbi.Account{Name="Fake subset A"};var b=new NinjaTrader.Cbi.Account{Name="Fake subset B"};
  using(var e=new CopierEngine(s=>{},t=>{},(attached,s)=>{},(n,r)=>{})){
   e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch()),Tuple.Create(b,new RiskSettings{MaxContracts=10},new RiskLatch())});Wait(()=>e.EntriesArmed,"subset arm");var entry=LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy);l.Emit(entry);Wait(()=>a.Submits==1&&b.Submits==1,"subset entry");
   foreach(var f in new[]{a,b}){lock(f.Positions)f.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=2,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});f.Orders[0].OrderState=NinjaTrader.Cbi.OrderState.Filled;f.Orders[0].Filled=2;f.Emit(f.Orders[0]);}
   e.Flatten(new[]{a},inst);Wait(()=>a.Flattens==1,"subset flatten A");var exit=LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"new protection");l.Emit(exit);Wait(()=>b.Submits==2,"unflattened follower retains NEW protective exits");Check(a.Submits==1,"flattened account excludes new exit");
  }
 }
 static void StartupAndDisconnect(){
  var l=new NinjaTrader.Cbi.Account{Name="Fake startup leader"};var a=new NinjaTrader.Cbi.Account{Name="Fake startup A"};int locked=0;
  using(var e=new CopierEngine(s=>{},t=>{},(attached,s)=>{},(n,r)=>Interlocked.Increment(ref locked))){
   lock(a.Positions)a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=1,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings(),new RiskLatch())});Thread.Sleep(100);Check(!e.Attached,"pre-existing position refused");lock(a.Positions)a.Positions.Clear();
   e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=3},new RiskLatch())});Wait(()=>e.EntriesArmed,"startup clean arm");
   using(var other=new CopierEngine(s=>{},t=>{},(attached,s)=>{},(n,r)=>{})){other.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings(),new RiskLatch())});Thread.Sleep(100);Check(!other.Attached,"duplicate ownership refused");}
   var entry=LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy);l.Emit(entry);Wait(()=>a.Submits==1,"startup entry");e.StopFlat();Thread.Sleep(100);Check(e.Attached&&!e.EntriesArmed,"stop protective management retained");
   a.Connection.Status=NinjaTrader.Cbi.ConnectionStatus.Disconnected;e.Poll();Wait(()=>locked==1,"disconnect latched");a.Connection.Status=NinjaTrader.Cbi.ConnectionStatus.Connected;l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Thread.Sleep(100);Check(a.Submits==1,"no reconnect catch-up");
  }
 }
 static void Isolation(){var l=new NinjaTrader.Cbi.Account{Name="Fake leader 2"};var a=new NinjaTrader.Cbi.Account{Name="Fake error",ThrowSubmit=true};var b=new NinjaTrader.Cbi.Account{Name="Fake good"};int locks=0;
  using(var e=new CopierEngine(s=>{},t=>{},(attached,s)=>{},(n,r)=>Interlocked.Increment(ref locks))){e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch()),Tuple.Create(b,new RiskSettings{MaxContracts=10},new RiskLatch())});Wait(()=>e.EntriesArmed,"isolation arm");l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Wait(()=>b.Submits==1&&locks==1,"isolated error");e.GuardedPause();}
  var live=new NinjaTrader.Cbi.Account{Name="Fake live",Provider=NinjaTrader.Cbi.Provider.Live};using(var e=new CopierEngine(s=>{},t=>{},(attached,s)=>{},(n,r)=>{})){e.Start(live,inst,new[]{Tuple.Create(b,new RiskSettings(),new RiskLatch())});Thread.Sleep(100);Check(!e.Attached&&!e.EntriesArmed,"live forbidden");}
 }
}
