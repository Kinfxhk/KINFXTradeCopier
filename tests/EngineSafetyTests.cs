using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NinjaTrader.NinjaScript.AddOns.Kinfx;
namespace NinjaTrader.Core {public static class Globals {public static DateTime MaxDate=DateTime.MaxValue;}}
namespace NinjaTrader.Cbi {
 public enum Provider {Simulator,Playback,Live} public enum ConnectionStatus {Connected,Disconnected} public class Connection {public ConnectionStatus Status=ConnectionStatus.Connected;}
 public enum Currency {Usd} public enum MarketPosition {Long,Short,Flat} public enum OrderAction {Buy,Sell,SellShort,BuyToCover} public enum OrderType {Market,Limit,StopMarket,StopLimit,MIT,Unsupported} public enum OrderEntry {Manual} public enum TimeInForce {Day,Gtd}
 public enum OrderState {Submitted,Accepted,Working,TriggerPending,Cancelled,Rejected,Filled,PartFilled,Initialized} public enum AccountItem {RealizedProfitLoss,UnrealizedProfitLoss,NetLiquidation,SodLiquidatingValue}
 public class Instrument {public string FullName="MES 12-26";} public class Position {public Instrument Instrument;public int Quantity;public MarketPosition MarketPosition;}
 public class Order {public Account Account;public Instrument Instrument;public string Name,Oco,OrderId;public OrderAction OrderAction;public OrderType OrderType;public OrderEntry OrderEntry;public TimeInForce TimeInForce;public DateTime Gtd;public OrderState OrderState;public int Quantity,Filled,QuantityChanged;public double LimitPrice,StopPrice,LimitPriceChanged,StopPriceChanged;public static bool IsTerminalState(OrderState s){return s==OrderState.Filled||s==OrderState.Rejected||s==OrderState.Cancelled;}}
 public class OrderEventArgs:EventArgs {public Order Order;public OrderState OrderState;public int Quantity,Filled;public string OrderId;public double LimitPrice,StopPrice;}
 // Deliberately fake sink: only counters and in-memory synthetic collections. No NT assembly or brokerage access.
 public class Account {
  public string Name;public Provider Provider=Provider.Simulator;public Connection Connection=new Connection();public Currency Denomination=Currency.Usd;
  public List<Position> Positions=new List<Position>();
  readonly List<Order> orders=new List<Order>();public Action OrdersReadHook;
  public List<Order> Orders {get{var hook=OrdersReadHook;if(hook!=null)hook();return orders;}}
  public event EventHandler<OrderEventArgs> OrderUpdate;
  public double Pnl,Equity=50000;public int Submits,Changes,Cancels,Flattens;public bool ThrowSubmit,ThrowCancel,ThrowFlatten;public ManualResetEvent CreateEntered,CreateRelease,SubmitEntered,SubmitRelease,CancelEntered,CancelRelease;public bool AsyncCancel,NoopFlatten,FlattenWorkingBeforeThrow;
  public double Get(AccountItem i,Currency c){return i==AccountItem.RealizedProfitLoss?Pnl:i==AccountItem.UnrealizedProfitLoss?0:Equity;}
  public Order CreateOrder(Instrument i,OrderAction a,OrderType t,OrderEntry e,TimeInForce tif,int q,double l,double s,string oco,string n,DateTime g,object ignored){if(CreateEntered!=null){CreateEntered.Set();CreateRelease.WaitOne();}return new Order {Account=this,Instrument=i,OrderAction=a,OrderType=t,OrderEntry=e,TimeInForce=tif,Quantity=q,LimitPrice=l,StopPrice=s,Oco=oco,Name=n,Gtd=g};}
  public int SubmitAttempts;public ManualResetEvent ChangeEntered,ChangeRelease;public void Submit(Order[] orders){Interlocked.Increment(ref SubmitAttempts);if(SubmitEntered!=null){SubmitEntered.Set();SubmitRelease.WaitOne();}if(ThrowSubmit)throw new InvalidOperationException("Synthetic submit error");foreach(var o in orders){lock(Orders)Orders.Add(o);o.OrderState=OrderState.Working;Interlocked.Increment(ref Submits);Emit(o);}}
  // Synthetic unknown acceptance: mutate the provider wrapper, but withhold its callback or throw AFTER applying.
  public bool SuppressChangeAck,ThrowChangeAfterApply;public int IgnoreChangeQuantity=-1;public Action<Order> ChangeHook;
  public System.Collections.Generic.List<int> ChangeQuantities=new System.Collections.Generic.List<int>();public System.Collections.Generic.List<int> ChangePrices=new System.Collections.Generic.List<int>();
  public void Change(Order[] orders){if(ChangeEntered!=null){ChangeEntered.Set();ChangeRelease.WaitOne();}foreach(var o in orders){var hook=ChangeHook;if(hook!=null)hook(o);Thread.Sleep(1);lock(ChangePrices)ChangePrices.Add((int)o.LimitPriceChanged);lock(ChangeQuantities)ChangeQuantities.Add(o.QuantityChanged);if(o.QuantityChanged!=IgnoreChangeQuantity){o.Quantity=o.QuantityChanged;o.LimitPrice=o.LimitPriceChanged;o.StopPrice=o.StopPriceChanged;}Interlocked.Increment(ref Changes);if(ThrowChangeAfterApply)throw new InvalidOperationException("Synthetic change accepted then threw");if(!SuppressChangeAck)Emit(o);}}
  // Deliver immutable quantities independently of the mutable wrapper, like a delayed provider callback.
  public void EmitCaptured(Order o,int quantity,int filled,OrderState state){var h=OrderUpdate;if(h!=null)h(this,new OrderEventArgs{Order=o,OrderId=o.OrderId,Quantity=quantity,Filled=filled,OrderState=state,LimitPrice=o.LimitPrice,StopPrice=o.StopPrice});}

  public void Cancel(Order[] orders){if(CancelEntered!=null){CancelEntered.Set();CancelRelease.WaitOne();}if(AsyncCancel){Interlocked.Add(ref Cancels,orders.Length);return;}if(ThrowCancel)throw new InvalidOperationException("Synthetic cancel error");foreach(var o in orders){o.OrderState=OrderState.Cancelled;Interlocked.Increment(ref Cancels);Emit(o);}}
  public void Flatten(Instrument[] inst){if(NoopFlatten){Interlocked.Increment(ref Flattens);return;}if(ThrowFlatten){if(FlattenWorkingBeforeThrow){var pending=CreateOrder(inst[0],OrderAction.Sell,OrderType.Market,OrderEntry.Manual,TimeInForce.Day,2,0,0,"","SyntheticPendingFlatten",DateTime.MaxValue,null);pending.OrderState=OrderState.Working;Orders.Add(pending);Emit(pending);}throw new InvalidOperationException("Synthetic flatten error");}Interlocked.Increment(ref Flattens);foreach(var o in Orders.ToArray())Cancel(new[]{o});lock(Positions)Positions.Clear();var fake=CreateOrder(inst[0],OrderAction.Sell,OrderType.Market,OrderEntry.Manual,TimeInForce.Day,1,0,0,"","SyntheticFlatten",DateTime.MaxValue,null);fake.OrderState=OrderState.Submitted;Emit(fake);}
  public void Emit(Order o){var h=OrderUpdate;if(h!=null)h(this,new OrderEventArgs {Order=o,OrderId=o.OrderId,OrderState=o.OrderState,Quantity=o.Quantity,Filled=o.Filled,LimitPrice=o.LimitPrice,StopPrice=o.StopPrice});}
 }
}
class EngineSafetyTests {
 static int Main(string[] args){var names=args.Length==0?new[]{"StopLedgerSafety"}:args[0].Split(',');int pass=0,fail=0;foreach(var name in names)try{typeof(EngineSafetyTests).GetMethod(name,System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,null);Console.WriteLine("PASS "+name);pass++;}catch(Exception ex){Console.WriteLine("FAIL "+name+" "+(ex.InnerException??ex));fail++;}Console.WriteLine("TOTAL pass="+pass+" fail="+fail);return fail==0?0:1;}
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Wait(Func<bool> condition,string message){for(int i=0;i<300;i++){if(condition())return;Thread.Sleep(10);}throw new Exception("timeout: "+message);}
 static NinjaTrader.Cbi.Instrument inst=new NinjaTrader.Cbi.Instrument();
 static NinjaTrader.Cbi.Order LeaderOrder(NinjaTrader.Cbi.Account a,NinjaTrader.Cbi.OrderAction action,string oco="",int qty=2){var o=a.CreateOrder(inst,action,NinjaTrader.Cbi.OrderType.Limit,NinjaTrader.Cbi.OrderEntry.Manual,NinjaTrader.Cbi.TimeInForce.Day,qty,100,0,oco,"SyntheticLeader",DateTime.MaxValue,null);o.OrderState=NinjaTrader.Cbi.OrderState.Submitted;return o;}
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
   e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch())});Wait(()=>e.EntriesArmed,"flat exit arm");l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Wait(()=>a.Submits==1,"flat exit entry");a.Orders[0].Filled=2;a.Orders[0].OrderState=NinjaTrader.Cbi.OrderState.Filled;a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=2,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});a.Emit(a.Orders[0]);l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"flat exits bracket"));Wait(()=>a.Submits==2,"flat exit protection");a.Positions.Clear();e.Poll();Drain(e);Check(a.Orders[1].OrderState==NinjaTrader.Cbi.OrderState.Working,"ambiguous flat must not cancel only protection");Wait(()=>locks==1,"flat position / execution mismatch locks");
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
 // ---- Competitor-issue replay (Austin's Trade Copier #3): the follower's copy of a protective STOP is REJECTED by the exchange
 // (price-limit error) after the leader's own stop already filled. Required: no silent open exposure, no resubmission / market fallback
 // (competitors resubmit as Market; this copier must not), the account locks, and the warning counts the unprotected lots.
 static void RejectedProtectiveStopLocksAndNeverResubmits(){
  var l=new NinjaTrader.Cbi.Account{Name="Rejected stop lead"};var a=new NinjaTrader.Cbi.Account{Name="Rejected stop follow"};int locked=0;string reason="";
  using(var e=new CopierEngine(s=>{},t=>{},(attached,s)=>{},(n,r)=>{Interlocked.Increment(ref locked);reason=r;})){
   e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch())});Wait(()=>e.EntriesArmed,"rejected-stop arm");
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Wait(()=>a.Submits==1,"entry copied");
   a.Orders[0].Filled=2;a.Orders[0].OrderState=NinjaTrader.Cbi.OrderState.Filled;a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=2,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});a.Emit(a.Orders[0]);Drain(e);
   // leader places its protective stop; the follower copy is submitted ...
   var stop=LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"bracket",2);stop.OrderType=NinjaTrader.Cbi.OrderType.StopMarket;stop.StopPrice=95;l.Emit(stop);
   Wait(()=>a.Submits==2,"follower protective stop submitted");
   // ... and the exchange rejects it (position stays open, nothing is protecting it)
   var rejected=a.Orders[1];rejected.OrderState=NinjaTrader.Cbi.OrderState.Rejected;a.Emit(rejected);Drain(e);
   Wait(()=>locked>=1,"rejected protective stop must lock the account");
   Thread.Sleep(150);Drain(e);
   Check(a.Submits==2,"no automatic resubmission and no market-order fallback after a rejected protective stop");
   Check(a.Flattens==0,"the copier never flattens on its own after a rejection");
   Check(reason.Length>0,"a lock reason is recorded");
  }
 }
 static void FailedFlattenKeepsProtection(){
  var l=new NinjaTrader.Cbi.Account{Name="Failed flatten lead"};var a=new NinjaTrader.Cbi.Account{Name="Failed flatten follow",ThrowFlatten=true};int locked=0;
  using(var e=new CopierEngine(s=>{},t=>{},(attached,s)=>{},(n,r)=>Interlocked.Increment(ref locked))){
   e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch())});Wait(()=>e.EntriesArmed,"failed flatten arm");l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Wait(()=>a.Submits==1,"failed flatten entry");a.Orders[0].Filled=2;a.Orders[0].OrderState=NinjaTrader.Cbi.OrderState.Filled;a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=2,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});a.Emit(a.Orders[0]);
   // A throw cannot prove provider rejection. Preserve existing protection, block another close until explicit reconciliation.
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"existing protection"));Wait(()=>a.Submits==2,"pre-existing protection");Drain(e);var protection=a.Orders[1];
   e.Flatten(new[]{a},inst);Wait(()=>locked==1,"failed flatten lock");Drain(e);l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"post failure duplicate close"));Drain(e);Check(a.Submits==2,"ambiguous thrown Flatten cannot authorize duplicate reductions");Check(protection.OrderState==NinjaTrader.Cbi.OrderState.Working&&a.Cancels==0,"ambiguous Flatten preserves existing protection");Check(((Dictionary<string,List<CopyLink>>)typeof(CopierEngine).GetField("links",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(e)).Values.SelectMany(x=>x).First().Follower.Flattening,"throw retains uncertain close acceptance");Check(!e.EntriesArmed,"failed flatten stays guarded");
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
    a.Orders[0].Filled=2;a.Orders[0].OrderState=NinjaTrader.Cbi.OrderState.Filled;lock(a.Positions)a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=2,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});a.Emit(a.Orders[0]);Drain(e);
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
   a.Orders[0].OrderState=NinjaTrader.Cbi.OrderState.Filled;a.Orders[0].Filled=2;b.Orders[0].OrderState=NinjaTrader.Cbi.OrderState.Filled;b.Orders[0].Filled=2;a.Emit(a.Orders[0]);b.Emit(b.Orders[0]);Drain(engine);
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
   foreach(var f in new[]{a,b}){lock(f.Positions)f.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=2,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});f.Orders[0].OrderState=NinjaTrader.Cbi.OrderState.Filled;f.Orders[0].Filled=2;f.Emit(f.Orders[0]);}Drain(e);
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
 static NinjaTrader.Cbi.Account Acc(string name){return new NinjaTrader.Cbi.Account{Name=name};}
 static void OverlappingChangesOldEqualAckCannotReleaseReservation() { OverlappingEqualAck(0); }
 static void OverlappingSentChangesOldEqualAckCannotReleaseReservation() { OverlappingEqualAck(1); }
 static void OverlappingReturnedChangesOldEqualAckCannotReleaseReservation() { OverlappingEqualAck(2); }
 static void OverlappingAckCaptureIgnoresLaterWrapperMutation() { OverlappingEqualAck(3); }
 static void PositiveFillDuringOverlappingChangesPreservesReservation() { OverlappingEqualAck(4); }
 static void OverlappingEqualAck(int phase) {
  var l=Acc("overlap leader");var a=Acc("overlap follower");var e=Arm(l,a);
  var secondEntered=new ManualResetEvent(false);var secondRelease=new ManualResetEvent(false);
  var callbackEntered=new ManualResetEvent(false);var callbackRelease=new ManualResetEvent(false);
  try {
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",4));Drain(e);Fill(a,4);Drain(e);
   var stop=LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"overlap first",2);l.Emit(stop);Drain(e);
   var exit=a.Orders[1];a.SuppressChangeAck=true;a.IgnoreChangeQuantity=2;
   a.ChangeEntered=new ManualResetEvent(false);a.ChangeRelease=new ManualResetEvent(false);
   a.ChangeHook=o=>{if(o.QuantityChanged==2){secondEntered.Set();Check(secondRelease.WaitOne(5000),"second reduction released");}};
   stop.Quantity=4;l.Emit(stop);Check(a.ChangeEntered.WaitOne(3000),"first increase4 blocked in provider");
   stop.Quantity=2;l.Emit(stop);
   // Pump only the engine: the provider is intentionally blocked.
   var worker=(SerialWorker)typeof(CopierEngine).GetField("worker",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(e);
   var queued=new ManualResetEvent(false);Check(worker.Post(()=>queued.Set()),"amend marker");Check(queued.WaitOne(3000),"second request queued");
   var links=(Dictionary<string,List<CopyLink>>)typeof(CopierEngine).GetField("links",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(e);
   var link=links.Values.SelectMany(v=>v).Single(x=>x.Order==exit);
   Check(link.RequestedQuantity==2&&link.ReservedQuantity==4&&link.ChangesInFlight==2,"2-to-4-to-2 requests overlap");
   if(phase>0) {
    a.ChangeRelease.Set();Check(secondEntered.WaitOne(3000),"increase4 applied; reduction2 now pending");
    Check(exit.Quantity==4&&link.ChangesInFlight==1,"one larger applied but unacknowledged change");
    if(phase==2){secondRelease.Set();Drain(e);Check(link.ChangesInFlight==0,"both calls returned without ACK");}
   }
   if(phase==4) {
    exit.Filled=1;exit.OrderState=NinjaTrader.Cbi.OrderState.PartFilled;SetPos(a,3);
    a.EmitCaptured(exit,4,1,NinjaTrader.Cbi.OrderState.PartFilled);
    var filled=new ManualResetEvent(false);Check(worker.Post(()=>filled.Set()),"fill marker");Check(filled.WaitOne(3000),"positive fill applied while reduction pending");
    Check(link.ObservedFilled==1,"captured fill retained");
   }
   if(phase==3) {
    Check(worker.Post(()=>{callbackEntered.Set();callbackRelease.WaitOne(5000);}),"callback processing barrier");Check(callbackEntered.WaitOne(3000),"callback worker blocked");
   }
   // Replay the OLD initial Working2 acknowledgement; it coincidentally equals the newest request.
   a.EmitCaptured(exit,2,0,NinjaTrader.Cbi.OrderState.Working);
   if(phase==3){exit.Quantity=2;exit.QuantityChanged=2;callbackRelease.Set();}
   var acked=new ManualResetEvent(false);Check(worker.Post(()=>acked.Set()),"old ack marker");Check(acked.WaitOne(3000),"old ack processed");
   bool retained=link.ReservedQuantity==4;
   if(phase==3)exit.Quantity=4; // a later wrapper mutation must not erase the earlier captured contradiction
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"overlap independent",2));
   var submitted=new ManualResetEvent(false);Check(worker.Post(()=>submitted.Set()),"independent exit marker");Check(submitted.WaitOne(3000),"independent exit considered");
   a.ChangeRelease.Set();secondRelease.Set();Drain(e);
   Console.WriteLine("phase="+phase+" retained="+retained+" working="+a.Orders.Where(o=>o.OrderAction==NinjaTrader.Cbi.OrderAction.Sell&&!NinjaTrader.Cbi.Order.IsTerminalState(o.OrderState)).Sum(o=>o.Quantity-o.Filled));
   Check(link.ObservedFilled==(phase==4?1:0),"older zero-fill ACK never revokes captured positive fill");
   Check(retained&&exit.Quantity==4&&a.Submits==2,"old equal ACK / no-op reduction cannot release reservation and allow working6 on owned4");
   // Positive control: a matching ACK with a coherently reduced wrapper releases the reserved capacity.
   a.IgnoreChangeQuantity=-1;a.SuppressChangeAck=false;exit.Quantity=2;a.EmitCaptured(exit,2,phase==4?1:0,phase==4?NinjaTrader.Cbi.OrderState.PartFilled:NinjaTrader.Cbi.OrderState.Working);Drain(e);
   Check(link.ReservedQuantity==2&&a.Submits==3&&a.Orders[2].Quantity==2,"fresh coherent ACK permits waiting protection to converge to2+2");
  } finally {a.ChangeRelease?.Set();secondRelease.Set();callbackRelease.Set();e.Dispose();}
 }
 // Issue #4: callbacks admitted during a blocking cleanliness read invalidate its earlier frontier.
 static void DisposeRechecksCallbacksAfterCleanRead() { CloseReadMustRetainCallbacks(true); }
 static void StopFlatRechecksCallbacksAfterCleanRead() { CloseReadMustRetainCallbacks(false); }
 static void DisposeRechecksAppliedFillAfterCleanRead() { CloseReadMustRetainCallbacks(true,false); }
 static void CleanDisposeStillStopsMailbox() {
  var l=Acc("clean dispose leader");var a=Acc("clean dispose follower");var e=Arm(l,a);
  e.Dispose();Check(!e.Attached,"unchanged clean session still disposes");
  var worker=(SerialWorker)typeof(CopierEngine).GetField("worker",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(e);
  Check(!worker.Post(()=>{}),"successful clean dispose stops admission");e.Dispose();
 }
 static void CloseReadMustRetainCallbacks(bool dispose,bool holdWorker=true) {
  var l=Acc("close frontier leader");var a=Acc("close frontier follower");var e=Arm(l,a);
  var cleanEntered=new ManualResetEvent(false);var cleanRelease=new ManualResetEvent(false);
  var workerEntered=new ManualResetEvent(false);var workerRelease=new ManualResetEvent(false);
  Thread closer=null;Exception closeError=null;
  try {
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Drain(e);
   var entry=a.Orders[0];entry.OrderState=NinjaTrader.Cbi.OrderState.Cancelled;a.Emit(entry);Drain(e);
   Check(e.CanClose(),"zero-fill terminal entry is initially clean");
   var worker=(SerialWorker)typeof(CopierEngine).GetField("worker",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(e);
   int readingThread=0,entered=0;
   l.OrdersReadHook=()=>{if(Thread.CurrentThread.ManagedThreadId==Volatile.Read(ref readingThread)&&Interlocked.Exchange(ref entered,1)==0){cleanEntered.Set();Check(cleanRelease.WaitOne(5000),"release cleanliness read");}};
   if(dispose) {
    if(holdWorker){Check(worker.Post(()=>{workerEntered.Set();workerRelease.WaitOne(5000);}),"worker barrier admitted");Check(workerEntered.WaitOne(3000),"worker held");}
    closer=new Thread(()=>{try{readingThread=Thread.CurrentThread.ManagedThreadId;e.Dispose();}catch(Exception ex){closeError=ex;}});closer.Start();
   } else {
    // StopFlat performs its cleanliness read on the worker itself.
    Check(worker.Post(()=>readingThread=Thread.CurrentThread.ManagedThreadId),"reader identity admitted");e.StopFlat();
   }
   Check(cleanEntered.WaitOne(3000),"close reads clean accounts after its initial frontier check");
   for(int i=0;i<130;i++) {
    entry.Filled=2;entry.OrderState=NinjaTrader.Cbi.OrderState.Filled;a.Emit(entry);
   }
   var pending=typeof(CopierEngine).GetField("pendingFollowerCallbacks",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
   if(holdWorker)Check((int)pending.GetValue(e)==130,"all 130 positive callback envelopes accepted while clean read is blocked");
   else {
    var beforeReturn=new ManualResetEvent(false);Check(worker.Post(()=>beforeReturn.Set()),"applied-fill marker admitted");Check(beforeReturn.WaitOne(3000),"all callbacks applied before cleanliness read returns");
    Check((int)pending.GetValue(e)==0,"applied evidence must block close even with no pending callbacks");
   }
   cleanRelease.Set();workerRelease.Set();if(closer!=null)Check(closer.Join(6000),"Dispose returns without abandoning pending callbacks");
   if(closeError!=null)throw closeError;
   // A marker must still be admitted after a refused close; it runs behind all accepted envelopes.
   var applied=new ManualResetEvent(false);Check(worker.Post(()=>applied.Set()),"refused close keeps mailbox accepting evidence");Check(applied.WaitOne(3000),"all queued callbacks applied");
   var links=(Dictionary<string,List<CopyLink>>)typeof(CopierEngine).GetField("links",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(e);
   var link=links.Values.SelectMany(v=>v).Single();
   Check(e.Attached&&!e.EntriesArmed&&!e.CanClose(),"late positive evidence retains disarmed ownership/session");
   Check(link.ObservedFilled==2&&link.ExecutionEvidence.Count(x=>x.Filled==2)==130,"every accepted immutable positive envelope retained");
   Check((int)pending.GetValue(e)==0,"accepted callbacks finish their pending counters");
  } finally {cleanRelease.Set();workerRelease.Set();l.OrdersReadHook=null;if(closer!=null)closer.Join(6000);e.Dispose();}
 }
 // Issue #4: supported shutdown paths cannot detach while provider completion is unresolved.
 static void UnresolvedSubmitRefusesStopAndDispose() {
  var l=Acc("completion lead");var a=Acc("completion follower");
  a.SubmitEntered=new ManualResetEvent(false);a.SubmitRelease=new ManualResetEvent(false);
  var e=Arm(l,a);int session=e.SessionGeneration;
  try {
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));
   Check(a.SubmitEntered.WaitOne(3000),"provider submit entered");
   e.StopFlat();e.Dispose();
   var worker=(SerialWorker)typeof(CopierEngine).GetField("worker",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(e);
   var stopped=new ManualResetEvent(false);
   Check(worker.Post(()=>stopped.Set()),"refused Dispose retains mailbox");Check(stopped.WaitOne(3000),"Stop processed");
   Check(e.Attached&&e.SessionGeneration==session&&!e.EntriesArmed,"unresolved call retains the disarmed session");
   Check(e.ProviderCallsInFlight==1&&!e.CanClose(),"provider completion still owns its in-flight count");
   a.SubmitRelease.Set();Drain(e);
   Check(e.ProviderCallsInFlight==0&&a.Submits==1,"accepted submit completes exactly once");
   Check(e.Attached&&!e.CanClose(),"working order evidence still prevents detach after completion");
   a.Orders[0].OrderState=NinjaTrader.Cbi.OrderState.Cancelled;a.Emit(a.Orders[0]);Drain(e);
   e.StopFlat();Drain(e);Check(!e.Attached,"terminal acknowledgement permits stop");
  } finally {a.SubmitRelease.Set();e.Dispose();}
 }
 // Retain an actual subscribed follower delegate, then invoke it after a supported clean restart.
 // This is a late callback test, not a claim that forced detach during a provider call is supported.
 static void DetachedFollowerCallbackCannotMutateRestart() {
  var l=Acc("late callback lead");var a=Acc("late callback follower");int locks=0;
  var e=Arm(l,a,null,(n,r)=>Interlocked.Increment(ref locks));
  try {
   var stale=(EventHandler<NinjaTrader.Cbi.OrderEventArgs>)typeof(NinjaTrader.Cbi.Account).GetField("OrderUpdate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(a);
   int oldSession=e.SessionGeneration;e.StopFlat();Drain(e);Check(!e.Attached,"clean stop");
   e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch())});Drain(e);
   Check(e.EntriesArmed&&e.SessionGeneration!=oldSession,"new session armed");
   var late=LeaderOrder(a,NinjaTrader.Cbi.OrderAction.Buy);late.Filled=2;late.OrderState=NinjaTrader.Cbi.OrderState.Filled;
   stale(a,new NinjaTrader.Cbi.OrderEventArgs{Order=late,OrderState=late.OrderState,Quantity=2,Filled=2});Drain(e);
   Check(locks==0&&e.CanClose()&&e.ProviderCallsInFlight==0,"old callback cannot lock or dirty the new session");
   Check(a.Submits==0&&a.Cancels==0&&a.Flattens==0,"old callback cannot issue commands");
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Drain(e);Check(a.Submits==1,"current leader callback still copies");
   // Positive control: the current follower delegate must still reject unknown activity.
   a.Emit(late);Drain(e);Check(locks==1&&!e.CanClose(),"current unknown follower callback still locks");
  } finally {e.Dispose();}
 }
 static CopyLink ExitLink(CopierEngine e,NinjaTrader.Cbi.Order order) {
  var links=(Dictionary<string,List<CopyLink>>)typeof(CopierEngine).GetField("links",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(e);
  return links.Values.SelectMany(v=>v).Single(x=>x.Order==order);
 }
 // Issue #2: Position ahead of captured fills never authorizes adopting the excess lots.
 static void PositionAheadProtectsOnlyCapturedOwnedLots() {
  var l=Acc("frontier ahead lead");var a=Acc("frontier ahead follower");int locks=0;
  using(var e=Arm(l,a,null,(n,r)=>Interlocked.Increment(ref locks))) {
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",4));Drain(e);
   Fill(a,2);Drain(e);SetPos(a,4);
   var stop=LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"ahead protection",4);l.Emit(stop);Drain(e);
   Check(a.Submits==2&&a.Orders[1].Quantity==2,"Position4 cannot authorize protection beyond captured owned2");
   Check(locks==1,"unexplained excess Position locks the follower");
   stop.Quantity=6;stop.LimitPrice=99;l.Emit(stop);Drain(e);
   int[] sent;lock(a.ChangeQuantities)sent=a.ChangeQuantities.ToArray();
   Check(sent.Length==1&&sent.All(q=>q==2),"every outbound Change stays within owned2; "+Sent(a));
   Check(a.Submits==2&&a.Flattens==0,"no catch-up entry or flatten for unknown excess");
  }
 }
 // Returning or throwing after Change is not a callback acknowledgement of released capacity.
 static void NoAckChangeRetainsReservationUntilCallback() { UnknownChangeRetainsReservation(false); }
 static void AcceptedThenThrowChangeRetainsReservation() { UnknownChangeRetainsReservation(true); }
 static void UnknownChangeRetainsReservation(bool throws) {
  var l=Acc("unknown change lead");var a=Acc("unknown change follower");int locks=0;
  using(var e=Arm(l,a,null,(n,r)=>Interlocked.Increment(ref locks))) {
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",4));Drain(e);Fill(a,4);Drain(e);
   var stop=LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"first reserve",4);l.Emit(stop);Drain(e);
   var link=ExitLink(e,a.Orders[1]);Check(link.ReservedQuantity==4,"initial acknowledged reserve4");
   a.SuppressChangeAck=true;a.ThrowChangeAfterApply=throws;
   stop.Quantity=2;stop.LimitPrice=99;l.Emit(stop);Drain(e);
   Check(a.Changes==1&&a.Orders[1].Quantity==2,"fake accepted the 2-lot change without callback");
   Check(link.RequestedQuantity==2&&link.ReservedQuantity==4,"mutable wrapper/return/throw must not release acknowledged reserve4");
   var other=LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"independent reserve",2);l.Emit(other);Drain(e);
   for(int i=0;i<3;i++){e.Poll();Drain(e);}
   Check(a.Submits==2,"independent exit cannot spend unacknowledged capacity");
   Check(a.Changes==1&&a.Cancels==0&&a.Flattens==0,"poll never retries an uncertain reduction or cancels protection");
   Check(locks==(throws?1:0),"accepted-then-throw locks; missing callback alone is not a new policy");
   int[] sent;lock(a.ChangeQuantities)sent=a.ChangeQuantities.ToArray();Check(sent.Length==1&&sent[0]==2,"exact outbound quantity trace");
   if(!throws) {
    a.SuppressChangeAck=false;a.Emit(a.Orders[1]);Drain(e);
    Check(link.ReservedQuantity==2&&a.Submits==3&&a.Orders[2].Quantity==2,"captured callback releases reserve and waiting protection converges");
   } else Check(link.ReservedQuantity==4,"unknown accepted-then-throw keeps conservative reservation");
  }

 }
 static void QueuedLargerChangeOldEqualAckRetainsCapacity() {
  var l=Acc("queued leader");var a=Acc("queued follower");var e=Arm(l,a);
  try {
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",4));Drain(e);Fill(a,4);Drain(e);
   var stop=LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"queued stop",2);l.Emit(stop);Drain(e);var exit=a.Orders[1];
   a.SuppressChangeAck=true;a.IgnoreChangeQuantity=2;a.ChangeEntered=new ManualResetEvent(false);a.ChangeRelease=new ManualResetEvent(false);
   stop.LimitPrice+=1;l.Emit(stop);Check(a.ChangeEntered.WaitOne(3000),"price-only Change holds dispatcher at quantity2");
   stop.Quantity=4;l.Emit(stop);stop.Quantity=2;l.Emit(stop);
   var worker=(SerialWorker)typeof(CopierEngine).GetField("worker",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(e);
   Action pump=()=>{var done=new ManualResetEvent(false);Check(worker.Post(()=>done.Set()),"marker admitted");Check(done.WaitOne(3000),"marker processed");};pump();
   var links=(Dictionary<string,List<CopyLink>>)typeof(CopierEngine).GetField("links",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(e);
   var link=links.Values.SelectMany(v=>v).Single(x=>x.Order==exit);
   Check(link.ChangesInFlight==3&&link.ReservedQuantity==4&&link.RequestedQuantity==2,"increase and reduction queued behind price Change");
   Check(exit.Quantity==2&&exit.QuantityChanged==2,"neither mutable quantity reveals queued increase4");
   a.Emit(exit);pump();bool retained=link.ReservedQuantity==4;
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"queued independent",2));pump();
   a.ChangeRelease.Set();Drain(e);
   Console.WriteLine("retained="+retained+" working="+a.Orders.Where(o=>o.OrderAction==NinjaTrader.Cbi.OrderAction.Sell).Sum(o=>o.Quantity-o.Filled));
   Check(retained&&a.Submits==2,"old equal ACK cannot spend capacity reserved for queued larger Change");
   a.IgnoreChangeQuantity=-1;a.SuppressChangeAck=false;exit.Quantity=2;a.Emit(exit);Drain(e);
   Check(link.ReservedQuantity==2&&a.Submits==3,"consistent post-completion ACK releases waiting protection");
  } finally {a.ChangeRelease?.Set();e.Dispose();}
 }
 static void DisposeRechecksEvidenceQueuedAfterCallerCheck() {
  var l=Acc("dispose commit leader");var a=Acc("dispose commit follower");var e=Arm(l,a);
  var entered=new ManualResetEvent(false);var release=new ManualResetEvent(false);var finished=new ManualResetEvent(false);
  try {
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Drain(e);var entry=a.Orders[0];entry.OrderState=NinjaTrader.Cbi.OrderState.Cancelled;a.Emit(entry);Drain(e);
   Check(e.CanClose(),"clean before caller Dispose check");
   var worker=(SerialWorker)typeof(CopierEngine).GetField("worker",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(e);
   Check(worker.Post(()=>{entered.Set();release.WaitOne(5000);}),"worker barrier");Check(entered.WaitOne(3000),"worker blocked");
   var closer=new Thread(()=>{e.Dispose();finished.Set();});closer.Start();
   var queue=(Queue<Action>)typeof(SerialWorker).GetField("queue",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(worker);
   var gate=typeof(SerialWorker).GetField("gate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(worker);
   Wait(()=>{lock(gate)return queue.Count>0;},"Dispose continuation queued after caller check");
   // Even a callback refused by an already-stopped mailbox must invalidate closure, not erase ownership.
   entry.Filled=2;a.Emit(entry);release.Set();Check(finished.WaitOne(4000),"Dispose returns");Thread.Sleep(50);
   Check(e.Attached&&!e.EntriesArmed&&!e.CanClose(),"late positive evidence retains session and ownership");
   Check(worker.Post(()=>{}),"refused close keeps mailbox admission alive");
   Drain(e);var links=(Dictionary<string,List<CopyLink>>)typeof(CopierEngine).GetField("links",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(e);
   Check(links.Values.SelectMany(v=>v).Single(x=>x.Order==entry).ObservedFilled==2,"accepted late fill applied");
  } finally {release.Set();e.Dispose();}
 }
 static CopierEngine Arm(NinjaTrader.Cbi.Account l,NinjaTrader.Cbi.Account a,Action<AccountTelemetry> tele=null,Action<string,string> persist=null){var e=new CopierEngine(s=>{},tele??(t=>{}),(attached,s)=>{},persist??((n,r)=>{}));e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10,Target=100},new RiskLatch())});Wait(()=>e.EntriesArmed,"arm");return e;}
 static void Drain(CopierEngine e){var f=typeof(CopierEngine).GetField("worker",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);var w=(SerialWorker)f.GetValue(e);Action pump=()=>{var done=new ManualResetEvent(false);w.Post(()=>done.Set());Check(done.WaitOne(3000),"drain");};
  // Provider calls run on per-account threads and complete back on the engine worker: settle both.
  for(int i=0;i<300;i++){pump();if(e.ProviderCallsInFlight==0){pump();if(e.ProviderCallsInFlight==0)return;}else Thread.Sleep(3);}}
 static void Fill(NinjaTrader.Cbi.Account a,int qty,bool position=true){var o=a.Orders[0];o.Filled=qty;o.OrderState=o.Quantity==qty?NinjaTrader.Cbi.OrderState.Filled:NinjaTrader.Cbi.OrderState.PartFilled;if(position)a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=qty,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});a.Emit(o);}
 // User misuse: the leader account must never also be a follower, by identity or by name.
 static void LeaderAlsoFollowerIsRefused(){
  var l=Acc("misuse lead");var other=Acc("misuse other");var sameName=Acc("misuse lead");
  foreach(var follower in new[]{l,sameName}){
   using(var e=new CopierEngine(s=>{},t=>{},(attached,s)=>{},(n,r)=>{})){
    e.Start(l,inst,new[]{Tuple.Create(follower,new RiskSettings{MaxContracts=10},new RiskLatch()),Tuple.Create(other,new RiskSettings{MaxContracts=10},new RiskLatch())});
    Drain(e);Thread.Sleep(150);
    Check(!e.Attached&&!e.EntriesArmed,"Start must be refused when the leader is also a follower (identity or same name)");
    Check(l.Submits==0&&other.Submits==0,"nothing may be submitted");
   }
  }
  using(var e=new CopierEngine(s=>{},t=>{},(attached,s)=>{},(n,r)=>{})){
   e.Start(l,inst,new[]{Tuple.Create(other,new RiskSettings{MaxContracts=10},new RiskLatch())});Wait(()=>e.EntriesArmed,"valid control must still start");
  }
 }
 // User misuse: the same follower listed twice must be refused as a whole.
 static void DuplicateFollowerListIsRefused(){
  var l=Acc("dup lead");var a=Acc("dup follow");
  using(var e=new CopierEngine(s=>{},t=>{},(attached,s)=>{},(n,r)=>{})){
   e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch()),Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch())});
   Drain(e);Thread.Sleep(150);Check(!e.Attached&&!e.EntriesArmed,"duplicate follower must refuse Start");
  }
 }
 static void SetPos(NinjaTrader.Cbi.Account a,int q){lock(a.Positions){a.Positions[0].Quantity=q;}}
 static string Sent(NinjaTrader.Cbi.Account a){int[] s;lock(a.ChangeQuantities)s=a.ChangeQuantities.ToArray();return "["+string.Join(",",s)+"]";}
 // F01 reviewer counterexample R7: two independent (non-OCO) stops 2+2, owned 4, Position lags to 3. Neither stop may shrink.
 static void F01TwoIndependentExitsNeverShrinkOnPositionLag(){
  var l=Acc("f01c lead");var a=Acc("f01c follow");
  using(var e=Arm(l,a)){
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",4));Wait(()=>a.Submits==1,"entry");Fill(a,4);Drain(e);
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"f01c A",2));Wait(()=>a.Submits==2,"A");l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"f01c B",2));Wait(()=>a.Submits==3,"B");Drain(e);
   Check(a.Orders[1].Quantity==2&&a.Orders[2].Quantity==2,"A,B acknowledged at 2+2");
   SetPos(a,3);e.Poll();Drain(e);e.Poll();Drain(e);
   int[] s;lock(a.ChangeQuantities)s=a.ChangeQuantities.ToArray();
   Check(s.All(q=>q>=2)&&a.Orders[1].Quantity>=2&&a.Orders[2].Quantity>=2,"no stop below its acknowledged 2 while owned=4; sent="+Sent(a)+" A="+a.Orders[1].Quantity+" B="+a.Orders[2].Quantity);
  }
 }
 // F01 reviewer finding R2: during a Position lag a leader PRICE-only move must still reach the follower, without shrinking quantity.
 static void F01PriceMoveStillForwardedDuringPositionLag(){
  var l=Acc("f01d lead");var a=Acc("f01d follow");
  using(var e=Arm(l,a)){
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",4));Wait(()=>a.Submits==1,"entry");Fill(a,4);Drain(e);
   var ls=LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"f01d br",4);l.Emit(ls);Wait(()=>a.Submits==2,"stop");Drain(e);var stop=a.Orders[1];
   SetPos(a,1);ls.LimitPrice=99;ls.OrderState=NinjaTrader.Cbi.OrderState.Working;l.Emit(ls);Drain(e);e.Poll();Drain(e);
   Check(stop.LimitPrice==99&&stop.Quantity>=4,"price move forwarded while quantity stays >=4; price="+stop.LimitPrice+" qty="+stop.Quantity+" sent="+Sent(a));
  }
 }
 // Reviewer R1/R11: a lagging Position must NOT latch the account (availability) and must NOT cancel protection.
 static void F01PositionLagDoesNotLockOrCancel(){
  var l=Acc("f01e lead");var a=Acc("f01e follow");AccountTelemetry t=null;int locks=0;
  using(var e=Arm(l,a,x=>t=x,(n,r)=>locks++)){
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",4));Wait(()=>a.Submits==1,"entry");Fill(a,4);Drain(e);
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"f01e br",4));Wait(()=>a.Submits==2,"stop");Drain(e);var stop=a.Orders[1];
   SetPos(a,2);e.Poll();Drain(e);e.Poll();Drain(e);
   Check(locks==0,"a transient Position lag must not write a durable latch; locks="+locks);
   Check(stop.OrderState==NinjaTrader.Cbi.OrderState.Working&&stop.Quantity==4&&a.Cancels==0,"protection untouched: state="+stop.OrderState+" qty="+stop.Quantity+" cancels="+a.Cancels);
  }
 }
 // Once Position catches up with owned evidence, a legitimate leader reduction is applied again (no permanent freeze).
 static void F01ReductionAppliedAfterPositionCatchesUp(){
  var l=Acc("f01f lead");var a=Acc("f01f follow");
  using(var e=Arm(l,a)){
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",4));Wait(()=>a.Submits==1,"entry");Fill(a,4);Drain(e);
   var ls=LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"f01f br",4);l.Emit(ls);Wait(()=>a.Submits==2,"stop");Drain(e);var stop=a.Orders[1];
   SetPos(a,2);ls.Quantity=2;ls.OrderState=NinjaTrader.Cbi.OrderState.Working;l.Emit(ls);Drain(e);e.Poll();Drain(e);
   Check(stop.Quantity==4,"while Position lags the exit is not shrunk; qty="+stop.Quantity);
   SetPos(a,4);e.Poll();Drain(e);
   Check(stop.Quantity==2,"after Position agrees with owned the leader's reduction to 2 is applied; qty="+stop.Quantity+" sent="+Sent(a));
  }
 }
 // Mailbox: a follower callback refused by a stopped worker must not leave the pending-callback counter raised forever.
 static void RefusedFollowerCallbackDoesNotStrandPendingCounter(){
  var l=Acc("mb lead");var a=Acc("mb follow");
  var e=Arm(l,a);
  l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",2));Wait(()=>a.Submits==1,"entry");
  var wf=typeof(CopierEngine).GetField("worker",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
  var worker=(SerialWorker)wf.GetValue(e);
  var pf=typeof(CopierEngine).GetField("pendingFollowerCallbacks",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
  worker.Dispose(); // mailbox stops: later Post calls are refused
  int before=(int)pf.GetValue(e);
  var o=a.Orders[0];o.Filled=1;o.OrderState=NinjaTrader.Cbi.OrderState.PartFilled;a.Emit(o);
  Thread.Sleep(150);
  int after=(int)pf.GetValue(e);
  Check(after==before,"a callback the stopped mailbox refused must not stay counted as pending; before="+before+" after="+after);
 }
 // Mailbox: a Poll refused by a stopped worker must not leave the poll-pending flag stuck, or every later Poll is silently skipped.
 static void RefusedPollDoesNotStickPollPending(){
  var l=Acc("mbp lead");var a=Acc("mbp follow");
  var e=Arm(l,a);
  var worker=(SerialWorker)typeof(CopierEngine).GetField("worker",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(e);
  var pp=typeof(CopierEngine).GetField("pollPending",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
  worker.Dispose();
  e.Poll();Thread.Sleep(100);
  Check((int)pp.GetValue(e)==0,"a Poll the stopped mailbox refused must clear the pending flag; flag="+pp.GetValue(e));
 }
 // Mailbox: SetFollowing raises FollowGeneration before Post. If the stopped mailbox refuses it, the processed generation
 // must still catch up, or the UI row stays pending forever. A refused request must never be treated as "following".
 static void RefusedSetFollowingDoesNotStrandProcessedGeneration(){
  var l=Acc("mbs lead");var a=Acc("mbs follow");
  var e=Arm(l,a);
  var worker=(SerialWorker)typeof(CopierEngine).GetField("worker",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(e);
  worker.Dispose();
  foreach(bool on in new[]{false,true}){
   int request=e.SetFollowing(a.Name,on);Thread.Sleep(50);
   var view=(System.Collections.IEnumerable)typeof(CopierEngine).GetField("followerView",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(e);
   object f=null;foreach(var x in view){f=x;break;}
   Check(f!=null,"follower view present");
   var t=f.GetType();
   int processed=(int)t.GetField("ProcessedFollowGeneration").GetValue(f);bool following=(bool)t.GetField("Following").GetValue(f);
   Check(request>0,"request id returned: "+request);
   Check(processed>=request,"refused request "+request+" (on="+on+") must still be marked processed; processed="+processed);
   Check(!following,"a refused request must never leave the account following (on="+on+")");
  }
 }
 // Mailbox: Flatten raises suppressNew before Post. A refused Flatten must restore it.
 static void RefusedFlattenDoesNotStickSuppressNew(){
  var l=Acc("mbf lead");var a=Acc("mbf follow");
  var e=Arm(l,a);
  var worker=(SerialWorker)typeof(CopierEngine).GetField("worker",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(e);
  worker.Dispose();
  e.Flatten(new[]{a},inst);Thread.Sleep(100);
  bool sup=(bool)typeof(CopierEngine).GetField("suppressNew",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(e);
  Check(!sup,"a Flatten the stopped mailbox refused must not leave suppressNew stuck at true");
  Check(a.Flattens==0,"a refused Flatten must not have reached the account; flattens="+a.Flattens);
 }
 static NinjaTrader.Cbi.Order LeaderLimit(NinjaTrader.Cbi.Account l,NinjaTrader.Cbi.OrderAction act,string oco,int qty,double price){var o=LeaderOrder(l,act,oco,qty);o.LimitPrice=price;return o;}
 static NinjaTrader.Cbi.Order[] Snap(NinjaTrader.Cbi.Account a){lock(a.Orders)return a.Orders.ToArray();}
 static string Probe(NinjaTrader.Cbi.Account a){return "submits="+a.Submits+" cancels="+a.Cancels+" orders=["+string.Join(";",Snap(a).Select(o=>o.Name+" q="+o.Quantity+" f="+o.Filled+" "+o.OrderState))+"]";}
 // Issue #3 scenario 1: entry 4 split into groups A (2) and B (2), both groups already protected. Fill A's target fully.
 // A's stop must go; B's stop and target must stay working.
 static void OcoCompletedGroupDoesNotCancelOtherProtectedGroup(){
  var l=Acc("oco1 lead");var a=Acc("oco1 follow");AccountTelemetry t=null;
  using(var e=Arm(l,a,x=>t=x)){
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",4));Wait(()=>a.Submits==1,"entry");Fill(a,4);Drain(e);
   l.Emit(LeaderLimit(l,NinjaTrader.Cbi.OrderAction.Sell,"oco1 A",2,99));l.Emit(LeaderLimit(l,NinjaTrader.Cbi.OrderAction.Sell,"oco1 A",2,110));
   l.Emit(LeaderLimit(l,NinjaTrader.Cbi.OrderAction.Sell,"oco1 B",2,98));l.Emit(LeaderLimit(l,NinjaTrader.Cbi.OrderAction.Sell,"oco1 B",2,111));
   Wait(()=>a.Submits==5,"four protective orders submitted; "+Probe(a));Drain(e);
   var aTarget=a.Orders[2];aTarget.Filled=2;aTarget.OrderState=NinjaTrader.Cbi.OrderState.Filled;a.Positions.Clear();a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=2,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});a.Emit(aTarget);Drain(e);e.Poll();Drain(e);
   var bStop=a.Orders[3];var bTarget=a.Orders[4];var aStop=a.Orders[1];
   Check(aStop.OrderState==NinjaTrader.Cbi.OrderState.Cancelled,"A's sibling stop is retired; "+Probe(a));
   // Window 1: A's stop cancel is requested but NOT yet acknowledged. Captured evidence still shows it Working, so it still
   // reserves 2 and B appears to have no closable exposure. B's sole protection must be frozen, never cancelled.
   Check(bStop.OrderState==NinjaTrader.Cbi.OrderState.Working&&bTarget.OrderState==NinjaTrader.Cbi.OrderState.Working,"B group protection must stay working before A's cancel ack; "+Probe(a));
   Check(bStop.Quantity==2&&bTarget.Quantity==2,"B quantities frozen before the ack; "+Probe(a));
   // Window 2: the ack arrives. B keeps full protection of the remaining 2.
   a.Emit(aStop);Drain(e);e.Poll();Drain(e);
   Check(bStop.OrderState==NinjaTrader.Cbi.OrderState.Working&&bTarget.OrderState==NinjaTrader.Cbi.OrderState.Working&&bStop.Quantity==2&&bTarget.Quantity==2,"B group protection intact after A's cancel ack; "+Probe(a));
  }
 }
 // Issue #3 scenario 2: B's exits are still WAITING (no closable exposure yet) and A completes. The rest of the entry can then only
 // fill AFTER an exit group completed. The engine must fail closed (lock, manual recovery) - it must not guess how the late lots
 // split across groups, must not cancel anything extra, and must not submit unprotected-by-design orders.
 // KNOWN LIMITATION (documented): B's waiting exit is therefore never sent; the user reconciles by hand.
 static void OcoCompletionWithUnfilledEntryFailsClosed(){
  var l=Acc("oco2 lead");var a=Acc("oco2 follow");AccountTelemetry latest=null;
  using(var e=Arm(l,a,x=>latest=x)){
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",4));Wait(()=>a.Submits==1,"entry");Fill(a,2);Drain(e);
   l.Emit(LeaderLimit(l,NinjaTrader.Cbi.OrderAction.Sell,"oco2 A",2,99));l.Emit(LeaderLimit(l,NinjaTrader.Cbi.OrderAction.Sell,"oco2 A",2,110));
   Wait(()=>a.Submits==3,"A protected; "+Probe(a));
   l.Emit(LeaderLimit(l,NinjaTrader.Cbi.OrderAction.Sell,"oco2 B",2,98));Drain(e);
   var aTarget=a.Orders[2];aTarget.Filled=2;aTarget.OrderState=NinjaTrader.Cbi.OrderState.Filled;a.Positions.Clear();a.Emit(aTarget);Drain(e);
   var entry=a.Orders[0];entry.Filled=4;entry.OrderState=NinjaTrader.Cbi.OrderState.Filled;a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=2,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});a.Emit(entry);Drain(e);
   a.Emit(a.Orders[1]);Drain(e);e.Poll();Drain(e);e.Poll();Drain(e);
   Check(latest.Locked&&!latest.Following,"late entry fill after an exit group completed must lock; "+Probe(a));
   Check(latest.Reason.Contains("manual"),"lock reason must say manual recovery is required: "+latest.Reason);
   // Issue #3 decided policy (conservative): lock, never auto-create orders, and SAY how many lots are unprotected and what to do.
   Check(latest.Reason.Contains("UNPROTECTED"),"the lock reason must say plainly that lots are unprotected: "+latest.Reason);
   Check(latest.Reason.Contains("2 lots")||latest.Reason.Contains("2 lot"),"the lock reason must state how many lots hold no protective exit (2): "+latest.Reason);
   Check(latest.Reason.Contains("NinjaTrader"),"the lock reason must tell the user to act in NinjaTrader: "+latest.Reason);
   Check(a.Submits==3,"no order is guessed or submitted after the lock; "+Probe(a));
   Check(a.Cancels==2,"no extra cancel: only A's stop and the entry remainder; "+Probe(a));
  }
 }
 // Issue #3 guard (review-driven): a leader-cancelled exit whose follower cancel is NOT acknowledged is still a working order and must keep
 // reserving capacity, whatever OCO id the NEW exit carries. Only a sibling the ENGINE retired because its OCO group completed is exempt.
 static int WorkingSell(NinjaTrader.Cbi.Account a){return Snap(a).Where(o=>o.OrderAction==NinjaTrader.Cbi.OrderAction.Sell&&!NinjaTrader.Cbi.Order.IsTerminalState(o.OrderState)).Sum(o=>o.Quantity-o.Filled);}
 static void LeaderCancelledUnackedExit(string xOco,string yOco,int held,bool partialFirst,int polls,string why){
  var l=Acc("ocg lead "+why);var a=Acc("ocg follow "+why);a.AsyncCancel=true;
  using(var e=Arm(l,a)){
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",held));Wait(()=>a.Submits==1,"entry "+why);Fill(a,held);Drain(e);
   var x=LeaderLimit(l,NinjaTrader.Cbi.OrderAction.Sell,xOco,held,99);l.Emit(x);Wait(()=>a.Submits==2,"exit X "+why+"; "+Probe(a));Drain(e);
   int open=held;
   if(partialFirst){var fx=a.Orders[1];fx.Filled=1;fx.OrderState=NinjaTrader.Cbi.OrderState.PartFilled;a.Positions.Clear();a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=held-1,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});a.Emit(fx);Drain(e);open=held-1;}
   x.OrderState=NinjaTrader.Cbi.OrderState.Cancelled;l.Emit(x);Drain(e);   // leader cancels X; follower cancel is async and never acknowledged
   Check(WorkingSell(a)==open,"X must still be working and unacknowledged ("+why+"); open="+open+" "+Probe(a));
   l.Emit(LeaderLimit(l,NinjaTrader.Cbi.OrderAction.Sell,yOco,open,98));Drain(e);
   for(int k=0;k<polls;k++){e.Poll();Drain(e);}
   Check(WorkingSell(a)<=open,"working exit quantity must never exceed the "+open+" lots still held ("+why+"); working="+WorkingSell(a)+" "+Probe(a));
  }
 }
 static void UnackedCancelledExitDifferentOcoStillReserves(){LeaderCancelledUnackedExit("","ocg B",2,false,1,"noOco-vs-B");LeaderCancelledUnackedExit("ocg A","ocg B",2,false,1,"A-vs-B");LeaderCancelledUnackedExit("ocg A2","",2,false,1,"A-vs-none");}
 static void UnackedCancelledExitNeverAckedStillReserves(){LeaderCancelledUnackedExit("ocg A3","ocg B3",2,false,5,"never-acked");}
 static void UnackedCancelledPartiallyFilledExitStillReserves(){LeaderCancelledUnackedExit("ocg A4","ocg B4",2,true,1,"partial");}
 // Issue #2 property sweep. Entry 4 at the leader. The follower entry has captured fill c when the leader bracket arrives and the follower
 // stop is acknowledged. Then the provider fills d MORE lots: that callback is either applied or still QUEUED, Position reads p, and the
 // leader amends the exit to s2. TRUE filled = c+d. INVARIANTS on every outbound Change and on the final state:
 //  (I1) no Change below the quantity already acknowledged while the Position view lags (stale evidence must not shrink protection);
 //  (I2) NON-terminal exit quantity never exceeds TRUE filled lots (reversal risk), at every observation point;
 //  (I3) once the callback is applied and Position agrees with truth, protection converges to min(s2, true filled).
 static string FrontierCase(int c,int d,int p,int s2,bool queued){
  string id=""+c+d+p+s2+(queued?"q":"n");
  var l=Acc("fr lead "+id);var a=Acc("fr follow "+id);
  using(var e=Arm(l,a)){
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",4));Wait(()=>a.Submits==1,"entry "+id);
   var entry=a.Orders[0];
   if(c>0){entry.Filled=c;entry.OrderState=NinjaTrader.Cbi.OrderState.PartFilled;a.Positions.Clear();a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=c,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});a.Emit(entry);Drain(e);}
   var stopL=LeaderLimit(l,NinjaTrader.Cbi.OrderAction.Sell,"fr bracket",4,99);l.Emit(stopL);
   if(c>0){Wait(()=>a.Submits==2,"stop "+id);Drain(e);}else{Drain(e);}
   int acked=a.Orders.Count>1?a.Orders[1].Quantity:0;
   var worker=(SerialWorker)typeof(CopierEngine).GetField("worker",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(e);
   var entered=new ManualResetEvent(false);var release=new ManualResetEvent(false);
   if(queued){worker.Post(()=>{entered.Set();release.WaitOne();});entered.WaitOne(3000);}
   int trueFilled=c+d;
   if(d>0){entry.Filled=trueFilled;entry.OrderState=trueFilled>=4?NinjaTrader.Cbi.OrderState.Filled:NinjaTrader.Cbi.OrderState.PartFilled;a.Emit(entry);}
   a.Positions.Clear();if(p>0)a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=p,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});
   stopL.Quantity=s2;stopL.LimitPrice=98;l.Emit(stopL);
   var bad=new List<string>();
   Func<string,int> working=where=>{int w=Snap(a).Where(o=>o.OrderAction==NinjaTrader.Cbi.OrderAction.Sell&&!NinjaTrader.Cbi.Order.IsTerminalState(o.OrderState)).Sum(o=>o.Quantity-o.Filled);if(w>trueFilled)bad.Add("I2("+where+") working "+w+" > true filled "+trueFilled);return w;};
   working("before release");
   if(queued)release.Set();
   Drain(e);working("after drain");e.Poll();Drain(e);working("after poll");
   int[] sent;lock(a.ChangeQuantities)sent=a.ChangeQuantities.ToArray();
   if(acked>0&&sent.Any(q=>q<acked&&q<c&&p<trueFilled))bad.Add("I1 change "+string.Join(",",sent)+" below acknowledged "+acked+"/captured "+c+" while Position lags (p="+p+")");
   a.Positions.Clear();a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=trueFilled,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});
   if(trueFilled==0)a.Positions.Clear();
   e.Poll();Drain(e);e.Poll();Drain(e);
   int fin=working("final");
   int want=a.Orders.Count>1||trueFilled>0?Math.Min(s2,trueFilled):0;
   if(trueFilled>0&&a.Orders.Count>1&&fin!=want)bad.Add("I3 after catch-up working "+fin+" want "+want);
   Console.WriteLine("COMBO "+id+" acked="+acked+" changes=["+string.Join(",",sent)+"] final="+fin);
   return bad.Count==0?null:"c="+c+" d="+d+" p="+p+" s2="+s2+" queued="+queued+" => "+string.Join("; ",bad)+" "+Probe(a);
  }
 }
 static void FrontierPropertySweep(){
  var fails=new List<string>();int ran=0;
  foreach(int c in new[]{0,2})foreach(int d in new[]{0,1,2})foreach(int p in new[]{0,1,2,3,4})foreach(int s2 in new[]{1,2,4})foreach(bool q in new[]{false,true}){
   if(c+d>4||(d==0&&q))continue;
   ran++;string r=null;try{r=FrontierCase(c,d,p,s2,q);}catch(Exception ex){r="c="+c+" d="+d+" p="+p+" s2="+s2+" queued="+q+" EXC "+ex.Message;}
   if(r!=null)fails.Add(r);
  }
  Console.WriteLine("SWEEP combos="+ran+" failures="+fails.Count);foreach(var f in fails.Take(40))Console.WriteLine("SWEEPFAIL "+f);
  Check(ran>=100,"sweep must actually enumerate many combinations: "+ran);
  Check(fails.Count==0,"frontier property sweep: "+fails.Count+" violating combinations; first: "+(fails.Count>0?fails[0]:""));
 }
 // Issue #2: TWO independent (non-OCO) exits 2+2 on 4 held lots. The leader then AMENDS one of them upward (to 4) and the other too.
 // Total working exit quantity must never exceed the 4 lots held, for any Position reading, or the account can reverse.
 static void TwoExitsAmendedUpNeverOverCover(){
  foreach(int p in new[]{4,3,2,0}){
   var l=Acc("two up lead "+p);var a=Acc("two up follow "+p);
   using(var e=Arm(l,a)){
    l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",4));Wait(()=>a.Submits==1,"entry");Fill(a,4);Drain(e);
    var x=LeaderLimit(l,NinjaTrader.Cbi.OrderAction.Sell,"",2,99);var y=LeaderLimit(l,NinjaTrader.Cbi.OrderAction.Sell,"",2,110);
    l.Emit(x);l.Emit(y);Wait(()=>a.Submits==3,"two exits; "+Probe(a));Drain(e);
    a.Positions.Clear();if(p>0)a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=p,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});
    x.Quantity=4;x.LimitPrice=98;l.Emit(x);Drain(e);y.Quantity=4;y.LimitPrice=111;l.Emit(y);Drain(e);e.Poll();Drain(e);e.Poll();Drain(e);
    Check(WorkingSell(a)<=4,"total working exit quantity must never exceed the 4 lots held (position reads "+p+"); working="+WorkingSell(a)+" "+Probe(a));
   }
  }
 }
 // Issue #4: one stalled follower must not delay healthy ones. N followers; follower #1 blocks inside Submit. Healthy followers must be
 // submitted without waiting for the stalled one, at 10 / 20 / 30 accounts, and the stalled one must still receive its order afterwards.
 static void StalledFollowerDoesNotBlockHealthyOnes(){
  foreach(int n in new[]{10,20,30}){
   var l=Acc("hol lead "+n);var fs=new List<NinjaTrader.Cbi.Account>();
   for(int i=0;i<n;i++)fs.Add(Acc("hol f"+n+"_"+i));
   var stalled=fs[0];stalled.SubmitEntered=new ManualResetEvent(false);stalled.SubmitRelease=new ManualResetEvent(false);
   var rows=fs.Select(x=>Tuple.Create(x,new RiskSettings{MaxContracts=10,Target=100},new RiskLatch())).ToArray();
   var e=new CopierEngine(s=>{},t=>{},(att,s)=>{},(nm,r)=>{});
   try{
    e.Start(l,inst,rows);Wait(()=>e.EntriesArmed,"arm "+n);
    l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",1));
    Check(stalled.SubmitEntered.WaitOne(3000),"stalled follower reached Submit ("+n+")");
    var sw=System.Diagnostics.Stopwatch.StartNew();
    bool healthyDone=false;
    while(sw.ElapsedMilliseconds<1500){if(fs.Skip(1).All(x=>x.Submits==1)){healthyDone=true;break;}Thread.Sleep(10);}
    int done=fs.Skip(1).Count(x=>x.Submits==1);
    Check(healthyDone,"while ONE follower is stalled inside Submit, the other "+(n-1)+" must still be served within 1.5s; served "+done+"/"+(n-1)+" ("+sw.ElapsedMilliseconds+"ms)");
    stalled.SubmitRelease.Set();
    Wait(()=>stalled.Submits==1,"the stalled follower still receives its order once released ("+n+")");
   }finally{stalled.SubmitRelease.Set();e.Dispose();}
  }
 }
 // Issue #4 hazards of per-account dispatch. Every one of these must hold with a stalled provider call in flight.
 static CopierEngine ArmMany(NinjaTrader.Cbi.Account l,List<NinjaTrader.Cbi.Account> fs,Action<string,string> persist=null){
  var rows=fs.Select(x=>Tuple.Create(x,new RiskSettings{MaxContracts=10,Target=100},new RiskLatch())).ToArray();
  var e=new CopierEngine(s=>{},t=>{},(att,s)=>{},persist??((nm,r)=>{}));e.Start(l,inst,rows);Wait(()=>e.EntriesArmed,"arm many");return e;
 }
 // (a) the engine must refuse to close while a provider call is still unresolved, then close cleanly once it returns.
 static void StalledProviderCallBlocksCloseUntilResolved(){
  var l=Acc("hol close lead");var a=Acc("hol close a");var b=Acc("hol close b");
  a.SubmitEntered=new ManualResetEvent(false);a.SubmitRelease=new ManualResetEvent(false);
  var e=ArmMany(l,new List<NinjaTrader.Cbi.Account>{a,b});
  try{
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",1));Check(a.SubmitEntered.WaitOne(3000),"a stalled");Wait(()=>b.Submits==1,"b served");
   Check(!e.CanClose(),"CanClose must be false while a provider call is in flight");
   e.Dispose();Check(e.Attached,"Dispose must refuse while a provider call is unresolved (still attached)");
   a.SubmitRelease.Set();Wait(()=>a.Submits==1,"a released");Drain(e);
   Check(e.ProviderCallsInFlight==0,"no provider call left in flight; "+e.ProviderCallsInFlight);
  }finally{a.SubmitRelease.Set();}
 }
 // (b) a Submit that THROWS on one follower locks only that follower; healthy followers are untouched and still served.
 static void ThrowingFollowerLocksOnlyItself(){
  var l=Acc("hol throw lead");var fs=Enumerable.Range(0,10).Select(i=>Acc("hol throw f"+i)).ToList();fs[3].ThrowSubmit=true;
  var locked=new List<string>();var e=ArmMany(l,fs,(n,r)=>{lock(locked)locked.Add(n);});
  try{
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",1));Wait(()=>fs.Where((x,i)=>i!=3).All(x=>x.Submits==1),"healthy served");Drain(e);
   Check(fs[3].Submits==0,"throwing follower sent nothing");
   string[] lk;lock(locked)lk=locked.ToArray();
   Check(lk.Length==1&&lk[0]==fs[3].Name,"exactly the throwing follower is locked; locked=["+string.Join(",",lk)+"]");
  }finally{e.Dispose();}
 }
 // (c) per-account ORDER is preserved: Submit then Change then Cancel for ONE follower reach the provider in that order.
 static void PerAccountCommandOrderIsPreserved(){
  var l=Acc("hol order lead");var a=Acc("hol order a");var log=new List<string>();
  var e=ArmMany(l,new List<NinjaTrader.Cbi.Account>{a});
  try{
   var x=LeaderLimit(l,NinjaTrader.Cbi.OrderAction.Buy,"",4,95);l.Emit(x);Wait(()=>a.Submits==1,"submit");Drain(e);
   x.LimitPrice=94;l.Emit(x);Drain(e);x.LimitPrice=93;l.Emit(x);Drain(e);
   x.OrderState=NinjaTrader.Cbi.OrderState.Cancelled;l.Emit(x);Drain(e);
   Check(a.Changes>=1&&a.Cancels==1,"changes then cancel arrived; changes="+a.Changes+" cancels="+a.Cancels);
   Check(a.Orders[0].LimitPrice==93,"the LAST price (93) won, not a reordered earlier one; price="+a.Orders[0].LimitPrice);
   Check(a.Orders[0].OrderState==NinjaTrader.Cbi.OrderState.Cancelled,"cancel applied last; "+a.Orders[0].OrderState);
  }finally{e.Dispose();}
 }
 // (d) a cancel for a follower whose provider is stalled does not block cancelling the others (OFF on many accounts).
 static void StalledFollowerDoesNotBlockCancelOfOthers(){
  var l=Acc("hol cancel lead");var fs=Enumerable.Range(0,10).Select(i=>Acc("hol cancel f"+i)).ToList();
  var e=ArmMany(l,fs);
  try{
   var x=LeaderLimit(l,NinjaTrader.Cbi.OrderAction.Buy,"",2,95);l.Emit(x);Wait(()=>fs.All(y=>y.Submits==1),"all submitted");Drain(e);
   var lead=fs[0];lead.CancelEntered=new ManualResetEvent(false);lead.CancelRelease=new ManualResetEvent(false);
   x.OrderState=NinjaTrader.Cbi.OrderState.Cancelled;l.Emit(x);
   Check(lead.CancelEntered.WaitOne(3000),"first follower's cancel is stalled inside the provider");
   var sw=System.Diagnostics.Stopwatch.StartNew();bool ok=false;while(sw.ElapsedMilliseconds<1500){if(fs.Skip(1).All(y=>y.Cancels==1)){ok=true;break;}Thread.Sleep(10);}
   Check(ok,"the other 9 cancels must go through while one is stalled; done "+fs.Skip(1).Count(y=>y.Cancels==1)+"/9");
   lead.CancelRelease.Set();Wait(()=>lead.Cancels==1,"stalled cancel completes");
  }finally{fs[0].CancelRelease?.Set();e.Dispose();}
 }
 // (a2) A stalled CANCEL has no ledger flag of its own: only the in-flight provider-call count keeps a close from detaching under it.
 static void StalledCancelBlocksCloseUntilResolved(){
  var l=Acc("hol cclose lead");var a=Acc("hol cclose a");
  var e=ArmMany(l,new List<NinjaTrader.Cbi.Account>{a});
  try{
   var x=LeaderLimit(l,NinjaTrader.Cbi.OrderAction.Buy,"",2,95);l.Emit(x);Wait(()=>a.Submits==1,"submitted");Drain(e);
   // the entry is cancelled by the provider-visible path, but its terminal ack arrives so nothing else keeps the ledger open:
   a.Orders[0].OrderState=NinjaTrader.Cbi.OrderState.Cancelled;a.Emit(a.Orders[0]);Drain(e);
   // now a second leader order whose cancel stalls
   var y=LeaderLimit(l,NinjaTrader.Cbi.OrderAction.Buy,"",2,94);l.Emit(y);Wait(()=>a.Submits==2,"second submitted");Drain(e);
   a.CancelEntered=new ManualResetEvent(false);a.CancelRelease=new ManualResetEvent(false);
   y.OrderState=NinjaTrader.Cbi.OrderState.Cancelled;l.Emit(y);Check(a.CancelEntered.WaitOne(3000),"cancel stalled inside provider");
   // make every OTHER ledger condition clean so ONLY the in-flight call can block the close
   a.Orders[1].OrderState=NinjaTrader.Cbi.OrderState.Cancelled;
   Check(e.ProviderCallsInFlight==1,"one provider call in flight; "+e.ProviderCallsInFlight);
   // Isolate the in-flight counter: clear the engine's separate ledger flag by reflection, as it would be once evidence settles.
   typeof(CopierEngine).GetField("ledgerUnresolved",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(e,false);
   Check(!e.CanClose(),"with every other condition clean, ONLY the unresolved provider call may keep CanClose false");
   a.CancelRelease.Set();Drain(e);Check(e.ProviderCallsInFlight==0,"in flight returns to 0; "+e.ProviderCallsInFlight);
   Check(e.CanClose(),"CanClose becomes true once the provider call returns and evidence is clean");
  }finally{a.CancelRelease?.Set();}
 }
 // (c2) ordering under pressure: many price changes in a row for ONE follower must reach the provider strictly in order.
 static void PerAccountOrderUnderBurst(){
  var l=Acc("hol burst lead");var a=Acc("hol burst a");
  var e=ArmMany(l,new List<NinjaTrader.Cbi.Account>{a});
  try{
   var x=LeaderLimit(l,NinjaTrader.Cbi.OrderAction.Buy,"",2,100);l.Emit(x);Wait(()=>a.Submits==1,"submit");Drain(e);
   for(int p=99;p>=60;p--){x.LimitPrice=p;l.Emit(x);}
   Drain(e);
   Check(a.Orders[0].LimitPrice==60,"final price must be the LAST one sent (60), a reordered earlier command must never win; got "+a.Orders[0].LimitPrice);
   int[] seen;lock(a.ChangePrices)seen=a.ChangePrices.ToArray();
   for(int i=1;i<seen.Length;i++)Check(seen[i]<seen[i-1],"provider saw prices out of order at "+i+": ["+string.Join(",",seen)+"]");
  }finally{e.Dispose();}
 }
 // Issue #4 review counterexamples (independent reviewer, reproduced here). Commands now reach the provider LATER than the engine decided them,
 // so every decision that used to read "already applied" state must account for queued commands.
 // R1: a Flatten must not overtake a Submit already queued/in flight for the same account.
 static void FlattenDoesNotOvertakeQueuedSubmit(){
  var l=Acc("hol r1 lead");var a=Acc("hol r1 a");
  var e=ArmMany(l,new List<NinjaTrader.Cbi.Account>{a});
  try{
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",2));Wait(()=>a.Submits==1,"entry");Fill(a,2);Drain(e);
   a.SubmitEntered=new ManualResetEvent(false);a.SubmitRelease=new ManualResetEvent(false);
   l.Emit(LeaderLimit(l,NinjaTrader.Cbi.OrderAction.Sell,"",2,90));Check(a.SubmitEntered.WaitOne(3000),"exit submit is in flight inside the provider");
   e.Flatten(new[]{a},inst);Thread.Sleep(150);
   Check(a.Flattens==0,"Flatten must wait behind the in-flight Submit for the SAME account; flattens="+a.Flattens);
   a.SubmitRelease.Set();Wait(()=>a.Flattens==1,"flatten reaches provider after the submit");Drain(e);
   Check(WorkingSell(a)==0,"a flat account must not end with a working Sell; "+Probe(a));
  }finally{a.SubmitRelease.Set();e.Dispose();}
 }
 // R2: after one provider call for an account throws, a later ENTRY for that account must not be sent, even if it was decided before
 // the failure completion reached the engine thread.
 static void SubmitThrowStillSendsNoSecondEntry(){
  var l=Acc("hol r2 lead");var a=Acc("hol r2 a");a.ThrowSubmit=true;
  a.SubmitEntered=new ManualResetEvent(false);a.SubmitRelease=new ManualResetEvent(false);
  var e=ArmMany(l,new List<NinjaTrader.Cbi.Account>{a});
  try{
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",1));Check(a.SubmitEntered.WaitOne(3000),"first submit in flight");
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",1));Thread.Sleep(200);   // engine thread is free: second entry decided while first is unresolved
   a.SubmitRelease.Set();Drain(e);Thread.Sleep(100);Drain(e);
   Check(a.SubmitAttempts==1,"after the first Submit threw, no further entry may be attempted on that account; attempts="+a.SubmitAttempts);
  }finally{a.SubmitRelease.Set();e.Dispose();}
 }
 // R3: a Change still queued (stalled) must not make a later REVERT look like "no change".
 static void RevertAfterQueuedChangeIsStillSent(){
  var l=Acc("hol r3 lead");var a=Acc("hol r3 a");
  var e=ArmMany(l,new List<NinjaTrader.Cbi.Account>{a});
  try{
   var x=LeaderLimit(l,NinjaTrader.Cbi.OrderAction.Buy,"",2,95);l.Emit(x);Wait(()=>a.Submits==1,"submit");Drain(e);
   a.ChangeEntered=new ManualResetEvent(false);a.ChangeRelease=new ManualResetEvent(false);
   x.LimitPrice=99;l.Emit(x);Check(a.ChangeEntered.WaitOne(3000),"change to 99 stalled inside provider");
   x.LimitPrice=95;l.Emit(x);Thread.Sleep(200);
   a.ChangeRelease.Set();Wait(()=>a.Changes>=2||a.Orders[0].LimitPrice==95,"changes applied");Drain(e);
   Check(a.Orders[0].LimitPrice==95,"follower must track the leader's LAST price 95; got "+a.Orders[0].LimitPrice+" changes="+a.Changes);
  }finally{a.ChangeRelease?.Set();e.Dispose();}
 }
 // Issue #4 (self-inflicted regression found while fixing the review findings): once the follower exit has been amended and nothing else
 // changes, repeated polls must NOT keep sending Change commands. The inline engine sent 2; a stale "already sent" comparison made the new
 // dispatcher oscillate between two quantities forever (about 600 Change calls, 47 s). This is the exact sweep combination that exposed it:
 // captured fill 2, a further 2 lots filled, Position reading 3, leader amends the exit to 2.
 static void AmendedExitDoesNotOscillateOnPoll(){
  var l=Acc("hol osc lead");var a=Acc("hol osc a");
  var e=ArmMany(l,new List<NinjaTrader.Cbi.Account>{a});
  try{
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",4));Wait(()=>a.Submits==1,"entry");
   var entry=a.Orders[0];entry.Filled=2;entry.OrderState=NinjaTrader.Cbi.OrderState.PartFilled;a.Positions.Clear();a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=2,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});a.Emit(entry);Drain(e);
   var st=LeaderLimit(l,NinjaTrader.Cbi.OrderAction.Sell,"hol osc bracket",4,99);l.Emit(st);Wait(()=>a.Submits==2,"stop");Drain(e);
   entry.Filled=4;entry.OrderState=NinjaTrader.Cbi.OrderState.Filled;a.Emit(entry);
   a.Positions.Clear();a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=3,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});
   st.Quantity=2;st.LimitPrice=98;l.Emit(st);Drain(e);
   var sw=System.Diagnostics.Stopwatch.StartNew();
   for(int i=0;i<20;i++){e.Poll();Drain(e);}
   int sent;lock(a.ChangeQuantities)sent=a.ChangeQuantities.Count;
   Check(sent<=4,"20 polls with nothing changing must not cause a stream of Change commands; sent="+sent+" in "+sw.ElapsedMilliseconds+"ms");
  }finally{e.Dispose();}
 }
 // Issue #3 policy: the unprotected-lots text is computed from owned exposure minus working exit quantity, and is never optimistic.
 static void UnprotectedLotsTextIsExactAndNeverOptimistic(){
  var l=Acc("unprot lead");var a=Acc("unprot a");AccountTelemetry latest=null;
  var e=new CopierEngine(s=>{},t=>latest=t,(att,s)=>{},(n,r)=>{});
  try{
   e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10,Target=100},new RiskLatch())});Wait(()=>e.EntriesArmed,"arm");
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",4));Wait(()=>a.Submits==1,"entry");Fill(a,2);Drain(e);
   l.Emit(LeaderLimit(l,NinjaTrader.Cbi.OrderAction.Sell,"unprot A",2,99));l.Emit(LeaderLimit(l,NinjaTrader.Cbi.OrderAction.Sell,"unprot A",2,110));
   Wait(()=>a.Submits==3,"A protected");Drain(e);
   var aT=a.Orders[2];aT.Filled=2;aT.OrderState=NinjaTrader.Cbi.OrderState.Filled;a.Positions.Clear();a.Emit(aT);Drain(e);
   var entry=a.Orders[0];entry.Filled=4;entry.OrderState=NinjaTrader.Cbi.OrderState.Filled;a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=2,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});a.Emit(entry);Drain(e);
   a.Emit(a.Orders[1]);Drain(e);e.Poll();Drain(e);
   Check(latest.Locked,"locked");
   var r=latest.Reason;
   Check(System.Text.RegularExpressions.Regex.IsMatch(r,@"2 lots? UNPROTECTED"),"exact count text '2 lots UNPROTECTED' expected; was: "+r);
   Check(r.IndexOf("coverage not established",StringComparison.OrdinalIgnoreCase)>=0,"must still say coverage is not established: "+r);
   Check(r.IndexOf("OK",StringComparison.Ordinal)<0&&r.IndexOf("protected by",StringComparison.OrdinalIgnoreCase)<0,"must never claim protection exists: "+r);
  }finally{e.Dispose();}
 }
 // The unprotected-lots figure itself: exact, never optimistic (a cancelled or terminal exit covers nothing), never a false alarm.
 static int UnprotectedNow(CopierEngine e){
  var fl=(System.Collections.IEnumerable)typeof(CopierEngine).GetField("followerView",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(e);object f=null;foreach(var x in fl){f=x;break;}
  var m=typeof(CopierEngine).GetMethod("UnprotectedLots",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
  var box=new int[1];var done=new ManualResetEvent(false);var w=(SerialWorker)typeof(CopierEngine).GetField("worker",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(e);
  w.Post(()=>{box[0]=(int)m.Invoke(e,new[]{f});done.Set();});Check(done.WaitOne(3000),"engine thread");return box[0];
 }
 static void UnprotectedLotsFigureIsExact(){
  var l=Acc("upf lead");var a=Acc("upf a");
  var e=ArmMany(l,new List<NinjaTrader.Cbi.Account>{a});
  try{
   Check(UnprotectedNow(e)==0,"no exposure: nothing unprotected; got "+UnprotectedNow(e));
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",4));Wait(()=>a.Submits==1,"entry");Fill(a,4);Drain(e);
   Check(UnprotectedNow(e)==4,"4 lots held, no exit at all: 4 unprotected; got "+UnprotectedNow(e));
   var x=LeaderLimit(l,NinjaTrader.Cbi.OrderAction.Sell,"",2,99);l.Emit(x);Wait(()=>a.Submits==2,"exit 2");Drain(e);
   Check(UnprotectedNow(e)==2,"4 held, a 2-lot exit working: 2 unprotected (partial cover); got "+UnprotectedNow(e));
   var y=LeaderLimit(l,NinjaTrader.Cbi.OrderAction.Sell,"",2,110);l.Emit(y);Wait(()=>a.Submits==3,"exit 2b");Drain(e);
   Check(UnprotectedNow(e)==0,"two independent 2-lot exits cover 4: 0 unprotected (no false alarm); got "+UnprotectedNow(e));
   a.AsyncCancel=true;x.OrderState=NinjaTrader.Cbi.OrderState.Cancelled;l.Emit(x);Drain(e);
   Check(UnprotectedNow(e)==2,"a cancelled exit covers NOTHING even while the provider still shows it working: 2 unprotected; got "+UnprotectedNow(e));
  }finally{e.Dispose();}
 }
 // F01 (Issue #2), scenario A: leader stop total 4; follower owned fill 2, wrapper already shows 4, Position reads 1.
 // Safety invariant: NO outbound Change may ever request a total below the 4 the stop was acknowledged at.
 static void F01PositionBehindOwnedNeverShrinksProtection(){
  var l=Acc("f01a lead");var a=Acc("f01a follow");int locks=0;
  using(var e=Arm(l,a,null,(n,r)=>locks++)){
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",4));Wait(()=>a.Submits==1,"entry");
   Fill(a,4);Drain(e);
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"f01a bracket",4));Wait(()=>a.Submits==2,"stop submitted");Drain(e);
   var stop=a.Orders[1];Check(stop.Quantity==4,"stop acknowledged at 4");
   a.Positions.Clear();a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=1,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});
   e.Poll();Drain(e);e.Poll();Drain(e);
   int[] sent;lock(a.ChangeQuantities)sent=a.ChangeQuantities.ToArray();
   Check(sent.All(q=>q>=4),"every outbound Change must keep total>=4 while owned fills are 4; sent: ["+string.Join(",",sent)+"]");
   Check(stop.Quantity>=4,"working stop must never be shrunk below 4; is "+stop.Quantity);
  }
 }
 // scenario B: queued Working0 then PartFilled2 must not make an intermediate request smaller than captured fill 2 (and never re-expand later).
 static void F01QueuedStaleWorkingNeverUndersizes(){
  var l=Acc("f01b lead");var a=Acc("f01b follow");
  using(var e=Arm(l,a)){
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",4));Wait(()=>a.Submits==1,"entry");
   Fill(a,2);Drain(e);
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"f01b bracket",4));Wait(()=>a.Submits==2,"stop");Drain(e);
   var entry=a.Orders[0];var worker=(SerialWorker)typeof(CopierEngine).GetField("worker",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(e);
   var entered=new ManualResetEvent(false);var release=new ManualResetEvent(false);worker.Post(()=>{entered.Set();release.WaitOne();});Check(entered.WaitOne(3000),"queue barrier");
   // older Working(fill 0) is queued first, then the real PartFilled(2); Position already reads 2 while the engine has not applied either yet
   entry.Filled=0;entry.OrderState=NinjaTrader.Cbi.OrderState.Working;a.Emit(entry);
   entry.Filled=2;entry.OrderState=NinjaTrader.Cbi.OrderState.PartFilled;a.Emit(entry);
   release.Set();Drain(e);e.Poll();Drain(e);
   int[] sent;lock(a.ChangeQuantities)sent=a.ChangeQuantities.ToArray();
   Check(sent.All(q=>q>=2),"no outbound Change may request a total below captured fill 2; sent: ["+string.Join(",",sent)+"]");
  }
 }
 static void ImmutableLateFillSafety(){var l=Acc("mutable fill lead");var a=Acc("mutable fill follow");int locks=0;using(var e=Arm(l,a,null,(n,r)=>locks++)){l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",4));Wait(()=>a.Submits==1,"entry");Fill(a,2);Drain(e);l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"completed",2));Wait(()=>a.Submits==2,"exit");Drain(e);var entry=a.Orders[0];var stop=a.Orders[1];var worker=(SerialWorker)typeof(CopierEngine).GetField("worker",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(e);var entered=new ManualResetEvent(false);var release=new ManualResetEvent(false);worker.Post(()=>{entered.Set();release.WaitOne();});Check(entered.WaitOne(3000),"queue barrier");l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",1));stop.Filled=2;stop.OrderState=NinjaTrader.Cbi.OrderState.Filled;a.Positions.Clear();a.Emit(stop);entry.Filled=4;entry.OrderState=NinjaTrader.Cbi.OrderState.Filled;a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=2,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});a.Emit(entry);release.Set();Drain(e);e.Poll();Drain(e);Check(locks==1,"immutable callback ledger detects uncovered late fill");}}
 static void OffDelayedExitSafety(){var l=Acc("off delayed lead");var a=Acc("off delayed follow");using(var e=Arm(l,a)){l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Wait(()=>a.Submits==1,"entry");Fill(a,2,false);Drain(e);e.SetFollowing(a.Name,false);Drain(e);l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"lag protection"));Drain(e);a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=2,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});e.Poll();Drain(e);Check(a.Submits==2,"OFF retains delayed protection for observed owned fills");}}
 static void FlatLagProtectionSafety(){var l=Acc("flat lag lead");var a=Acc("flat lag follow");using(var e=Arm(l,a)){l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Wait(()=>a.Submits==1,"entry");Fill(a,2);Drain(e);l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"protect"));Wait(()=>a.Submits==2,"stop");Drain(e);a.Positions.Clear();e.Poll();Drain(e);Check(a.Orders[1].OrderState==NinjaTrader.Cbi.OrderState.Working,"ambiguous flat preserves sole protection");a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=2,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});e.Poll();Drain(e);Check(a.Submits==2&&a.Orders[1].OrderState==NinjaTrader.Cbi.OrderState.Working,"protection still working when Position catches up");}}
 static void OcoDivergenceSafety(){var l=Acc("oco race lead");var a=Acc("oco race follow");int locks=0;AccountTelemetry latest=null;using(var e=Arm(l,a,t=>latest=t,(n,r)=>locks++)){l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Wait(()=>a.Submits==1,"entry");Fill(a,2);Drain(e);l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"same oco"));l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"same oco"));Drain(e);Check(a.Submits==3,"two OCO exits");a.Orders[1].Filled=2;a.Orders[1].OrderState=NinjaTrader.Cbi.OrderState.Filled;a.Orders[2].Filled=2;a.Orders[2].OrderState=NinjaTrader.Cbi.OrderState.Filled;a.Positions[0].MarketPosition=NinjaTrader.Cbi.MarketPosition.Short;a.Emit(a.Orders[1]);a.Emit(a.Orders[2]);Drain(e);e.Poll();Drain(e);Check(locks==1&&latest.Position==-2&&latest.Locked,"provider OCO double fill durably locked for manual recovery");}}
 static void ReplacementIdentitySafety(){var l=Acc("replacement lead");var a=Acc("replacement follow");using(var e=Arm(l,a)){var o=LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy);o.OrderId="provider-order-17";l.Emit(o);Wait(()=>a.Submits==1,"first entry");var replacement=LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy);replacement.Name=o.Name;replacement.OrderId=o.OrderId;replacement.OrderState=NinjaTrader.Cbi.OrderState.Working;l.Emit(replacement);Drain(e);Check(a.Submits==1,"provider ID replacement cannot duplicate copied entry");}}
 static void PersistenceFailureSafety(){var l=Acc("durable failure lead");var a=Acc("durable failure follow");using(var e=Arm(l,a,null,(n,r)=>{throw new System.IO.IOException("synthetic journal write failed");})){l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Wait(()=>a.Submits==1,"working entry");Drain(e);var manual=LeaderOrder(a,NinjaTrader.Cbi.OrderAction.Buy);a.Emit(manual);Drain(e);Check(a.Cancels==1&&a.Orders[0].OrderState==NinjaTrader.Cbi.OrderState.Cancelled,"persistence failure must not interrupt owned entry cancellation");}}
 static void TypeTransitionSafety(){var l=Acc("type change lead");var a=Acc("type change follow");using(var e=Arm(l,a)){var o=LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy);o.OrderType=NinjaTrader.Cbi.OrderType.StopMarket;o.LimitPrice=0;o.StopPrice=100;l.Emit(o);Wait(()=>a.Submits==1,"stop market entry");o.OrderType=NinjaTrader.Cbi.OrderType.Limit;o.StopPrice=0;o.LimitPrice=95;o.OrderState=NinjaTrader.Cbi.OrderState.Working;l.Emit(o);Drain(e);Check(a.Changes==0&&a.Orders[0].StopPrice==100,"type transition rejected without corrupting submitted stop");}}
 static void AsyncCancelAckSafety(){var l=Acc("async off lead");var a=Acc("async off follow");AccountTelemetry latest=null;using(var e=Arm(l,a,t=>latest=t)){l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Wait(()=>a.Submits==1,"entry");a.AsyncCancel=true;e.SetFollowing(a.Name,false);Drain(e);Check(latest.DisablePending,"cancel pending");a.Orders[0].OrderState=NinjaTrader.Cbi.OrderState.Cancelled;a.Emit(a.Orders[0]);Drain(e);e.Poll();Drain(e);Check(!latest.DisablePending,"pending clears after terminal cancel acknowledgement");e.SetFollowing(a.Name,true);Drain(e);Check(latest.Following,"clean terminal ack permits explicit refollow");}}
 static void CancelledBracketSafety(){var l=Acc("cancel waiting lead");var a=Acc("cancel waiting follow");using(var e=Arm(l,a)){l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",4));Wait(()=>a.Submits==1,"entry");Fill(a,2);Drain(e);l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"first",2));Wait(()=>a.Submits==2,"first exit");l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"independent stale",4));Drain(e);Check(a.Submits==2,"second waits reserved budget");a.Orders[1].OrderState=NinjaTrader.Cbi.OrderState.Cancelled;a.Emit(a.Orders[1]);Drain(e);Check(a.Submits==2,"cancelled protection retires stale waiting bracket");}}
 static void ManualTerminalPollSafety(){var l=Acc("manual filled lead");var a=Acc("manual filled follow");int locks=0;AccountTelemetry latest=null;using(var e=Arm(l,a,t=>latest=t,(n,r)=>locks++)){var manual=LeaderOrder(a,NinjaTrader.Cbi.OrderAction.Buy);manual.Filled=2;manual.OrderState=NinjaTrader.Cbi.OrderState.Filled;a.Orders.Add(manual);a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=2,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});e.Poll();Drain(e);Check(locks==1&&!latest.Following&&latest.Locked,"Poll detects terminal manual fills and unknown position");}}
 static void AsyncFlattenSafety(){var l=Acc("async flatten lead");var a=Acc("async flatten follow");using(var e=Arm(l,a)){l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Wait(()=>a.Submits==1,"entry");Fill(a,2);Drain(e);a.NoopFlatten=true;e.Flatten(new[]{a},inst);Drain(e);e.Poll();Drain(e);Check(a.Submits==1,"unconfirmed flatten blocks concurrent new exits");var recover=typeof(CopierEngine).GetMethod("AcknowledgeFlattenAbandoned");Check(recover!=null,"explicit manual flatten recovery API required");recover.Invoke(e,new object[]{a.Name});Drain(e);l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"new stop"));Drain(e);Check(a.Flattens==1&&a.Submits==2&&a.Positions[0].Quantity==2,"unconfirmed flatten with no working close permits explicit mirrored recovery exit");}}
 static void InFlightOffSafety(){var l=Acc("linear lead");var a=Acc("linear follow");AccountTelemetry latest=null;a.SubmitEntered=new ManualResetEvent(false);a.SubmitRelease=new ManualResetEvent(false);using(var e=Arm(l,a,t=>latest=t)){l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Check(a.SubmitEntered.WaitOne(3000),"broker submit barrier");try{var dict=(Dictionary<string,List<CopyLink>>)typeof(CopierEngine).GetField("links",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(e);Check(dict.Values.Single().Single().SubmissionInFlight,"authorization marked in-flight before broker call");int generation=e.SetFollowing(a.Name,false);Check(latest.ProcessedFollowGeneration<generation,"OFF not processed while prior submit in flight");}finally{a.SubmitRelease.Set();}Drain(e);Drain(e);Check(a.Submits==1&&a.Cancels==1&&!latest.DisablePending,"prior authorized submit canceled then terminal ack observed");l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Drain(e);Check(a.Submits==1,"zero newly authorized entry after OFF gate");}}
 static void FollowerReplacementSafety(){var l=Acc("f replace lead");var a=Acc("f replace follow");int locks=0;using(var e=Arm(l,a,null,(n,r)=>locks++)){l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Wait(()=>a.Submits==1,"entry");var entry=a.Orders[0];entry.OrderId="entry-9";a.Emit(entry);Drain(e);var replacement=a.CreateOrder(inst,entry.OrderAction,entry.OrderType,entry.OrderEntry,entry.TimeInForce,entry.Quantity,entry.LimitPrice,entry.StopPrice,entry.Oco,entry.Name,entry.Gtd,null);replacement.OrderId=entry.OrderId;replacement.Filled=2;replacement.OrderState=NinjaTrader.Cbi.OrderState.Filled;lock(a.Orders){a.Orders.Clear();a.Orders.Add(replacement);}a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=2,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});a.Emit(replacement);Drain(e);Check(locks==0,"stable follower provider ID recognizes replacement");l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"replace exit"));Drain(e);Check(a.Submits==2&&a.Orders[1].Quantity==2,"replacement fill counted once and protects owned exposure");}}
 static void FillEvidenceNeverReadsFutureMutation(){var l=Acc("read ledger lead");var a=Acc("read ledger follow");using(var e=Arm(l,a)){l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",4));Wait(()=>a.Submits==1,"entry");Fill(a,2);Drain(e);var dict=(Dictionary<string,List<CopyLink>>)typeof(CopierEngine).GetField("links",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(e);var link=dict.Values.Single().Single();a.Orders[0].Filled=4;var owned=typeof(CopierEngine).GetMethod("OwnedPosition",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);Check((int)owned.Invoke(e,new object[]{link.Follower})==2,"future mutable Filled cannot advance execution ledger");Check(link.ObservedFilled==2,"late fill evidence not consumed early");}}
 static void BlankProviderTransitionSafety(){var l=Acc("id transition lead");var a=Acc("id transition follow");int locks=0;using(var e=Arm(l,a,null,(n,r)=>locks++)){var o=LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy);l.Emit(o);Drain(e);o.OrderId="late-provider-id";l.Emit(o);Drain(e);var replacement=LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy);replacement.OrderId=o.OrderId;replacement.OrderState=NinjaTrader.Cbi.OrderState.Working;l.Emit(replacement);Drain(e);Check(a.Submits==1&&locks==0,"blank to stable ID preserves same canonical identity");replacement.OrderId="";l.Emit(replacement);Drain(e);Check(a.Submits==1&&locks==1,"stable ID disappearance fails closed");}}
 static void NoCatchupReplacementSafety(){var l=Acc("no catchup lead");var a=Acc("no catchup follow");using(var e=Arm(l,a)){e.SetFollowing(a.Name,false);Drain(e);var old=LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy);old.OrderId="ignored-id";l.Emit(old);Drain(e);e.SetFollowing(a.Name,true);Drain(e);var replacement=LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy);replacement.OrderId=old.OrderId;replacement.OrderState=NinjaTrader.Cbi.OrderState.Working;l.Emit(replacement);Drain(e);Check(a.Submits==0,"replacement wrapper cannot bypass ignored canonical ID");l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Drain(e);Check(a.Submits==1,"fresh explicit entry only");}}
 static void WorkingFlattenBlocksRecovery(){var l=Acc("flatten working lead");var a=Acc("flatten working follow");using(var e=Arm(l,a)){l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Drain(e);Fill(a,2);Drain(e);a.NoopFlatten=true;e.Flatten(new[]{a},inst);Drain(e);var brokerClose=LeaderOrder(a,NinjaTrader.Cbi.OrderAction.Sell,"",2);a.Orders.Add(brokerClose);a.Emit(brokerClose);Drain(e);e.AcknowledgeFlattenAbandoned(a.Name);Drain(e);l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"no extra exit"));Drain(e);Check(a.Submits==1,"working external flatten prohibits recovery exit alongside it");}}
 static void ExecutionEvidenceRetained(){var l=Acc("evidence lead");var a=Acc("evidence follow");using(var e=Arm(l,a)){l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Drain(e);Fill(a,2,false);Drain(e);var dict=(Dictionary<string,List<CopyLink>>)typeof(CopierEngine).GetField("links",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(e);var link=dict.Values.Single().Single();var field=typeof(CopyLink).GetField("ExecutionEvidence");Check(field!=null,"retain immutable callback execution evidence");var evidence=(List<FollowerSnapshot>)field.GetValue(link);Check(evidence.Last().Filled==2,"fill evidence recorded");a.Orders[0].Filled=7;Check(evidence.Last().Filled==2,"stored evidence immutable after adapter mutation");}}
 static void ConfirmedCycleCanClose(){var l=Acc("clean cycle lead");var a=Acc("clean cycle follow");using(var e=Arm(l,a)){l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Drain(e);Fill(a,2);Drain(e);l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"clean close"));Drain(e);var close=a.Orders[1];close.Filled=2;close.OrderState=NinjaTrader.Cbi.OrderState.Filled;a.Positions.Clear();a.Emit(close);Drain(e);Drain(e);Check(e.CanClose(),"matched immutable owned entry and exit executions plus no orders confirm cycle flat");e.StopFlat();Drain(e);Check(!e.Attached,"confirmed flat ledger permits detach");}}
 static void ProductionQuarantine(){var l=Acc("quarantine lead");var a=Acc("quarantine follower");using(var e=new CopierEngine(s=>{},t=>{},(b,s)=>{},(n,r)=>{})){e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch())});Drain(e);Check(!e.Attached&&!e.EntriesArmed,"ALL provider Start quarantine");e.Flatten(new[]{a},inst);Drain(e);Check(a.Flattens==0,"ALL provider Flatten quarantine");}}
 // Real NinjaTrader (Tradovate-backed accounts, trace 2026-10-12): the SAME Order object first reports Initialized with a client-side GUID,
 // then Submitted with the exchange id, which stays stable through Accepted / Working / Filled. That one replacement must not lock the follower.
 static void ClientIdReplacedByExchangeIdDoesNotLock(){var l=Acc("client id lead");var a=Acc("client id follow");using(var e=Arm(l,a)){l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Drain(e);var order=a.Orders[0];
  order.OrderId="2a31a2d89db7447089d8ad11829022a0";a.EmitCaptured(order,2,0,NinjaTrader.Cbi.OrderState.Initialized);Drain(e);
  order.OrderId="695407360007";a.EmitCaptured(order,2,0,NinjaTrader.Cbi.OrderState.Submitted);Drain(e);a.EmitCaptured(order,2,0,NinjaTrader.Cbi.OrderState.Accepted);Drain(e);a.EmitCaptured(order,2,0,NinjaTrader.Cbi.OrderState.Working);Drain(e);
  order.Filled=2;order.OrderState=NinjaTrader.Cbi.OrderState.Filled;a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=2,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});a.EmitCaptured(order,2,2,NinjaTrader.Cbi.OrderState.Filled);Drain(e);
  var dict=(Dictionary<string,List<CopyLink>>)typeof(CopierEngine).GetField("links",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(e);var link=dict.Values.Single().Single();
  Check(!link.Follower.Latch.Locked,"client GUID replaced once by exchange id on the same wrapper must not lock");Check(link.ObservedFilled==2,"fill evidence kept");Check(link.ProviderId=="695407360007","provider identity is the exchange id, not the pre-submit GUID");}}
 // The conservative lock stays: once an exchange id is bound, a DIFFERENT id is still ambiguous.
 static void ExchangeIdThenDifferentIdStillLocks(){var l=Acc("exch id lead");var a=Acc("exch id follow");using(var e=Arm(l,a)){l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Drain(e);var order=a.Orders[0];
  order.OrderId="695407360007";a.EmitCaptured(order,2,0,NinjaTrader.Cbi.OrderState.Submitted);Drain(e);order.OrderId="695407369999";a.EmitCaptured(order,2,0,NinjaTrader.Cbi.OrderState.Working);Drain(e);
  var dict=(Dictionary<string,List<CopyLink>>)typeof(CopierEngine).GetField("links",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(e);var link=dict.Values.Single().Single();
  Check(link.Follower.Latch.Locked,"a second, different exchange id remains ambiguous and locks");}}
 // Real NinjaTrader reloads today's FILLED orders from the broker after a restart (and after any earlier session). Orders that were already terminal
 // BEFORE Start are history, not unknown live activity: they must not lock the follower at Start, and copying must still work afterwards.
 static void PreSessionTerminalHistoryDoesNotLockStart(){var l=Acc("history lead");var a=Acc("history follow");AccountTelemetry latest=null;
  var old=a.CreateOrder(inst,NinjaTrader.Cbi.OrderAction.Buy,NinjaTrader.Cbi.OrderType.Market,NinjaTrader.Cbi.OrderEntry.Manual,NinjaTrader.Cbi.TimeInForce.Day,1,0,0,"","manual-history",DateTime.MaxValue,null);
  old.OrderId="695407360007";old.Filled=1;old.OrderState=NinjaTrader.Cbi.OrderState.Filled;lock(a.Orders)a.Orders.Add(old);
  using(var e=Arm(l,a,t=>latest=t)){l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Wait(()=>a.Submits==1,"copy still works after history was ignored");Drain(e);Check(latest==null||!latest.Locked,"pre-session filled history must not lock the follower");}}
 // The guard itself stays: a NEW filled order with no link that appears AFTER Start is unknown activity and still locks.
 static void NewUnlinkedFilledOrderAfterStartStillLocks(){var l=Acc("newfill lead");var a=Acc("newfill follow");AccountTelemetry latest=null;
  using(var e=Arm(l,a,t=>latest=t)){var manual=a.CreateOrder(inst,NinjaTrader.Cbi.OrderAction.Buy,NinjaTrader.Cbi.OrderType.Market,NinjaTrader.Cbi.OrderEntry.Manual,NinjaTrader.Cbi.TimeInForce.Day,1,0,0,"","manual-new",DateTime.MaxValue,null);
   manual.OrderId="695407369001";manual.Filled=1;manual.OrderState=NinjaTrader.Cbi.OrderState.Filled;lock(a.Orders)a.Orders.Add(manual);e.Poll();Drain(e);Check(latest!=null&&latest.Locked,"a filled order created after Start that this copier did not place must lock");}}
 // History means TERMINAL at Start only. An order that was still working when the session began is never excused, even if it later fills.
 static void WorkingOrderBeforeStartIsNeverHistory(){var l=Acc("workhist lead");var a=Acc("workhist follow");AccountTelemetry latest=null;
  // Start reads the follower's Orders in this order: 1-2 first Clean(), 3-4 pre-session history snapshot, 5-6 post-subscribe Clean() recheck.
  // A WORKING order that is present at the snapshot is live activity and must never be recorded as pre-session history. Make it fill between the
  // snapshot and the recheck while the position callback lags (position still 0): the recheck passes, so only the history rule can still catch it.
  var w=a.CreateOrder(inst,NinjaTrader.Cbi.OrderAction.Buy,NinjaTrader.Cbi.OrderType.Limit,NinjaTrader.Cbi.OrderEntry.Manual,NinjaTrader.Cbi.TimeInForce.Day,1,50,0,"","manual-working",DateTime.MaxValue,null);
  w.OrderId="695407369500";w.OrderState=NinjaTrader.Cbi.OrderState.Working;
  var backing=(List<NinjaTrader.Cbi.Order>)typeof(NinjaTrader.Cbi.Account).GetField("orders",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(a);
  int reads=0;a.OrdersReadHook=()=>{ int n=System.Threading.Interlocked.Increment(ref reads);
    if(n==3){ lock(backing)backing.Add(w); }                                   // present when the history snapshot runs
    if(n==5){ w.Filled=1;w.OrderState=NinjaTrader.Cbi.OrderState.Filled; } };    // filled before the recheck; Position() still 0
  string refusal="";var e=new CopierEngine(x=>{},t=>latest=t,(att,st)=>{},(n,r)=>{});
  using(e){try{e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10,Target=100},new RiskLatch())});}catch(InvalidOperationException ex){refusal=ex.Message;}
   Drain(e);a.OrdersReadHook=null;e.Poll();Drain(e);
   Check(reads>=6,"the fill landed between snapshot and recheck (reads="+reads+")");
   Check(refusal.IndexOf("latched",StringComparison.OrdinalIgnoreCase)>=0||(latest!=null&&latest.Locked),
     "a working order that filled during Start is unknown live exposure: Start must be refused by the risk latch or the follower must lock (refusal='"+refusal+"')");}}
 static void LeaderSellFromFlatOpensShortAndIsCopied(){var l=Acc("short lead");var a=Acc("short follow");AccountTelemetry latest=null;
  // NinjaTrader's Order Ticket / Chart Trader send OrderAction.Sell even when the leader is FLAT and the order opens a SHORT (the platform has no SellShort for manual orders).
  // A Sell with no leader position to reduce is an ENTRY, not an exit: the follower must copy it as a Sell entry (and later close it), never silently ignore it.
  using(var e=Arm(l,a,t=>latest=t)){
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"",1));
   Wait(()=>a.Submits==1,"a Sell from a flat leader must be copied as a short entry");
   Check(a.Orders[0].OrderAction==NinjaTrader.Cbi.OrderAction.Sell||a.Orders[0].OrderAction==NinjaTrader.Cbi.OrderAction.SellShort,"follower order is a sell");
   Check(latest==null||!latest.Locked,"copying a short entry must not lock the follower");}}
 static void LeaderBuyCoversShortIsCopiedAsExit(){var l=Acc("cover lead");var a=Acc("cover follow");AccountTelemetry latest=null;
  // Short round trip as NinjaTrader's ticket sends it: Sell (opens short) then Buy (covers). The Buy must be an EXIT: sized to the follower's short, never a new long entry.
  using(var e=Arm(l,a,t=>latest=t)){
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"",2));Wait(()=>a.Submits==1,"short entry copied");
   var f=a.Orders[0];f.Filled=2;f.OrderState=NinjaTrader.Cbi.OrderState.Filled;a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=2,MarketPosition=NinjaTrader.Cbi.MarketPosition.Short});a.Emit(f);Drain(e);
   // the leader is now short 2 (fixture position), then covers
   l.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=2,MarketPosition=NinjaTrader.Cbi.MarketPosition.Short});
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",2));Wait(()=>a.Submits==2,"cover copied");
   Check(a.Orders[1].OrderAction==NinjaTrader.Cbi.OrderAction.Buy,"follower cover is a Buy");
   Check(a.Orders[1].Quantity==2,"cover is sized to the follower's short (2), got "+a.Orders[1].Quantity);
   Check(latest==null||!latest.Locked,"a normal short round trip must not lock");}}
 static void LeaderSellClosingALongStaysAnExit(){var l=Acc("longexit lead");var a=Acc("longexit follow");AccountTelemetry latest=null;
  // Regression guard for the legacy meaning: leader long, then Sell => exit (sized to the follower's long), not a new short.
  using(var e=Arm(l,a,t=>latest=t)){
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",2));Wait(()=>a.Submits==1,"long entry");Fill(a,2);Drain(e);
   l.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=2,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"",2));Wait(()=>a.Submits==2,"exit copied");
   Check(a.Orders[1].Quantity==2,"exit sized to the follower's long (2), got "+a.Orders[1].Quantity);
   Check(latest==null||!latest.Locked,"normal long round trip must not lock");}}
 static void LaterEventsOfAShortOpeningOrderCreateNoSecondFollowerOrder(){var l=Acc("stick lead");var a=Acc("stick follow");
  // A Sell from a flat leader is an ENTRY (short). Later events of the SAME leader order (Working, Filled) arrive while the leader's Position changes; none may create another follower order.
  using(var e=Arm(l,a)){
   var o=LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"",1);o.OrderId="stick-1";l.Emit(o);Wait(()=>a.Submits==1,"entry copied");
   l.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=1,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});
   o.OrderState=NinjaTrader.Cbi.OrderState.Working;l.Emit(o);Drain(e);
   o.Filled=1;o.OrderState=NinjaTrader.Cbi.OrderState.Filled;l.Emit(o);Drain(e);
   Check(a.Submits==1,"later events of the same leader order must not create another follower order (submits="+a.Submits+")");}}
 static void NeverSubmittedCopierOrderDoesNotBlockStart(){var l=Acc("ghost lead");var a=Acc("ghost follow");
  // A copier order left in Initialized (never sent: dry run / failed submit) is not on the market and must not make Start refuse forever.
  var g=a.CreateOrder(inst,NinjaTrader.Cbi.OrderAction.Buy,NinjaTrader.Cbi.OrderType.Market,NinjaTrader.Cbi.OrderEntry.Manual,NinjaTrader.Cbi.TimeInForce.Day,1,0,0,"","KTC>ghost",DateTime.MaxValue,null);
  g.OrderState=NinjaTrader.Cbi.OrderState.Initialized;((List<NinjaTrader.Cbi.Order>)typeof(NinjaTrader.Cbi.Account).GetField("orders",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(a)).Add(g);
  Check(CopierEngine.Clean(a,inst),"a never-submitted KTC> order must not count as live activity");
  var logs=new List<string>();var eng=new CopierEngine(m=>{lock(logs)logs.Add(m);},t=>{},(att,m)=>{},(n,r)=>{});
  try{eng.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10,Target=100},new RiskLatch())});try{Wait(()=>eng.EntriesArmed,"arm");}catch(Exception){string[] snap;lock(logs)snap=logs.ToArray();throw new Exception("did not arm with a leftover never-submitted KTC> order; engine log: "+string.Join(" | ",snap));}}finally{eng.Dispose();}}
 static void NeverSubmittedForeignOrderStillBlocksStart(){var l=Acc("foreign lead");var a=Acc("foreign follow");
  var g=a.CreateOrder(inst,NinjaTrader.Cbi.OrderAction.Buy,NinjaTrader.Cbi.OrderType.Market,NinjaTrader.Cbi.OrderEntry.Manual,NinjaTrader.Cbi.TimeInForce.Day,1,0,0,"","manual-ticket",DateTime.MaxValue,null);
  g.OrderState=NinjaTrader.Cbi.OrderState.Initialized;((List<NinjaTrader.Cbi.Order>)typeof(NinjaTrader.Cbi.Account).GetField("orders",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(a)).Add(g);
  Check(!CopierEngine.Clean(a,inst),"an Initialized order that is not ours may be about to submit: still blocks Start");}
 static void DirtyReportNamesTheBlockingAccount(){var a=Acc("dirty follow");var b=Acc("clean follow");a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=2,MarketPosition=NinjaTrader.Cbi.MarketPosition.Short});
  var r=CopierEngine.DirtyReport(new[]{a,b},inst);
  Check(r.Contains("dirty follow")&&r.Contains("-2")&&!r.Contains("clean follow"),"report must name only the blocking account and its position: "+r);}
 static void GhostThatLaterFillsStillLocks(){var l=Acc("ghostfill lead");var a=Acc("ghostfill follow");AccountTelemetry latest=null;
  var g=a.CreateOrder(inst,NinjaTrader.Cbi.OrderAction.Buy,NinjaTrader.Cbi.OrderType.Market,NinjaTrader.Cbi.OrderEntry.Manual,NinjaTrader.Cbi.TimeInForce.Day,1,0,0,"","KTC>ghost2",DateTime.MaxValue,null);
  g.OrderState=NinjaTrader.Cbi.OrderState.Initialized;
  ((List<NinjaTrader.Cbi.Order>)typeof(NinjaTrader.Cbi.Account).GetField("orders",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(a)).Add(g);
  using(var e=Arm(l,a,t=>latest=t)){
   // the leftover order now actually fills: that is real, unowned exposure and must lock, never be waved through as history
   g.Filled=1;g.OrderState=NinjaTrader.Cbi.OrderState.Filled;a.Emit(g);Drain(e);
   Wait(()=>latest!=null&&latest.Locked,"a ghost that later fills must lock the follower");}}
 static void ExitFillBeforePositionUpdateDoesNotLock(){var l=Acc("exitlag lead");var a=Acc("exitlag follow");AccountTelemetry latest=null;
  using(var e=Arm(l,a,t=>latest=t)){l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Wait(()=>a.Submits==1,"entry");Fill(a,2);Drain(e);
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"exitlag close"));Wait(()=>a.Submits==2,"exit");Drain(e);
   var close=a.Orders[1];close.Filled=2;close.OrderState=NinjaTrader.Cbi.OrderState.Filled;a.Emit(close);Drain(e);e.Poll();Drain(e);
   Check(latest!=null&&!latest.Locked,"exit fill seen before the Position update must not lock the follower (lag, not unknown exposure)");
   var fv=(System.Collections.IEnumerable)typeof(CopierEngine).GetField("followerView",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(e);var f=fv.Cast<object>().Single();
   var fld=f.GetType().GetField("LastFillUtc");Check(fld!=null,"LastFillUtc exists");fld.SetValue(f,DateTime.UtcNow.AddSeconds(-30));e.Poll();Drain(e);
   Check(latest.Locked,"the same divergence persisting beyond the grace period still locks");}}
 static void AmbiguousIdKeepsFillEvidence(){var l=Acc("amb id lead");var a=Acc("amb id follow");using(var e=Arm(l,a)){l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Drain(e);var order=a.Orders[0];order.OrderId="known-id";a.Emit(order);Drain(e);order.OrderId="changed-id";order.Filled=2;order.OrderState=NinjaTrader.Cbi.OrderState.Filled;a.Emit(order);Drain(e);var dict=(Dictionary<string,List<CopyLink>>)typeof(CopierEngine).GetField("links",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(e);var link=dict.Values.Single().Single();Check(link.ObservedFilled==2&&link.ExecutionEvidence.Any(x=>x.Filled==2),"identity divergence must not discard captured fill evidence");Check(!e.CanClose()&&link.Follower.Latch.Locked,"ambiguous identity denies detach and locks");}}
 static void FlattenThrowMayHaveAccepted(){var l=Acc("throw accepted lead");var a=Acc("throw accepted follow");using(var e=Arm(l,a)){l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Drain(e);Fill(a,2);Drain(e);a.ThrowFlatten=true;a.FlattenWorkingBeforeThrow=true;e.Flatten(new[]{a},inst);Drain(e);l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"no duplicate exit"));Drain(e);Check(a.Submits==1,"Flatten exception may follow provider acceptance; no extra exit before explicit recovery");e.AcknowledgeFlattenAbandoned(a.Name);Drain(e);l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"still blocked"));Drain(e);Check(a.Submits==1,"unknown working flatten forbids abandonment");}}
 static void CancelledLateFillManualRecovery(){var l=Acc("late cancel lead");var a=Acc("late cancel follow");AccountTelemetry latest=null;using(var e=Arm(l,a,t=>latest=t)){l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",4));Wait(()=>a.Submits==1,"entry");Fill(a,2);Drain(e);l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"stop",4));Wait(()=>a.Submits==2,"exit");a.Orders[1].OrderState=NinjaTrader.Cbi.OrderState.Cancelled;a.Emit(a.Orders[1]);Drain(e);a.Orders[0].Filled=4;a.Orders[0].OrderState=NinjaTrader.Cbi.OrderState.Filled;a.Positions[0].Quantity=4;a.Emit(a.Orders[0]);Drain(e);e.Poll();Drain(e);Check(a.Submits==2&&a.Orders[1].OrderState==NinjaTrader.Cbi.OrderState.Cancelled,"no unsafe autonomous stop recreation");Check(latest.Locked&&!latest.Following&&(latest.Reason.Contains("uncovered")||latest.Reason.Contains("manual")),"uncovered exposure reported honestly for manual recovery");}}
 static void ReplacementReservationsCountOnce(){var l=Acc("reservation replace lead");var a=Acc("reservation replace follow");using(var e=new CopierEngine(s=>{},t=>{},(b,s)=>{},(n,r)=>{})){e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=4},new RiskLatch())});Wait(()=>e.EntriesArmed,"arm");l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",3));Drain(e);var original=a.Orders[0];original.OrderId="partial-id";original.Filled=2;original.OrderState=NinjaTrader.Cbi.OrderState.PartFilled;a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=2,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});a.Emit(original);Drain(e);var replacement=a.CreateOrder(inst,original.OrderAction,original.OrderType,original.OrderEntry,original.TimeInForce,3,100,0,original.Oco,original.Name,original.Gtd,null);replacement.OrderId=original.OrderId;replacement.Filled=2;replacement.OrderState=NinjaTrader.Cbi.OrderState.PartFilled;a.Orders.Add(replacement);a.Emit(replacement);Drain(e);l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",1));Drain(e);Check(a.Submits==2,"replacement wrappers reserve same provider order only once");}}
 static void InFlightCannotDetach(){var l=Acc("close flight lead");var a=Acc("close flight follow");a.SubmitEntered=new ManualResetEvent(false);a.SubmitRelease=new ManualResetEvent(false);using(var e=Arm(l,a)){l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Check(a.SubmitEntered.WaitOne(3000),"submit in flight");try{Check(!e.CanClose(),"authorized broker work blocks detach even absent Orders and Positions");}finally{a.SubmitRelease.Set();}Drain(e);}}
 static void StopLedgerSafety(){var l=Acc("stop ledger lead");var a=Acc("stop ledger follow");using(var e=Arm(l,a)){l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Wait(()=>a.Submits==1,"entry");Fill(a,2,false);Drain(e);Check(!e.CanClose(),"owned execution blocks close before Position catches up");e.StopFlat();Drain(e);Check(e.Attached&&!e.EntriesArmed,"Stop retains attached ledger");e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch())});Drain(e);Check(!e.EntriesArmed,"restart denied");}}

 // ---- KINFX_SIM_TEST build only (run by `offline.py enginesim`): Simulator / Playback may trade, anything else never.
 static NinjaTrader.Cbi.Account ProvAcc(string name,NinjaTrader.Cbi.Provider p){var x=Acc(name);x.Provider=p;return x;}
#if KINFX_SIM_TEST
 static void SimTestAllowListLoadsEveryLineAndAlias(){
  string path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"kinfx-allow-"+Guid.NewGuid().ToString("N")+".txt");
  System.IO.File.WriteAllLines(path,new[]{"# c","ACCT A = Alpha","ACCT B","ACCT C = Gamma"});
  try{
   var names=CopierEngine.LoadAllowFile(path);
   Check(names.Count==3&&names.ContainsKey("ACCT A")&&names.ContainsKey("ACCT B")&&names.ContainsKey("ACCT C"),"every listed account loads, not just the first");
   Check(names["ACCT A"]=="Alpha"&&names["ACCT B"]==""&&names["ACCT C"]=="Gamma","aliases kept per account");
  } finally { System.IO.File.Delete(path); }
  Check(CopierEngine.LoadAllowFile(System.IO.Path.Combine(System.IO.Path.GetTempPath(),"does-not-exist-"+Guid.NewGuid().ToString("N")+".txt")).Count==0,"missing file = empty list");
 }
 static void SimTestAllowFileParsingAndAlias(){
  Check(CopierEngine.AliasFor("never listed")=="","unlisted account has no alias");
 }
 static void SimTestOperatorAllowList(){
  var eval=ProvAcc("allow eval",NinjaTrader.Cbi.Provider.Live);var other=ProvAcc("allow other",NinjaTrader.Cbi.Provider.Live);
  CopierEngine.SetSimAllow(null);
  Check(!CopierEngine.Sim(eval)&&!CopierEngine.SimScope(new[]{eval}),"non-Simulator provider refused without an allow entry");
  CopierEngine.SetSimAllow(new[]{"allow eval"});
  Check(CopierEngine.Sim(eval)&&CopierEngine.SimScope(new[]{eval}),"operator-attested account permitted");
  Check(!CopierEngine.Sim(other)&&!CopierEngine.SimScope(new[]{eval,other}),"an account that is not on the list still refuses the whole scope");
  Check(!CopierEngine.Sim(ProvAcc("ALLOW EVAL",NinjaTrader.Cbi.Provider.Live)),"match is exact and case-sensitive");
  CopierEngine.SetSimAllow(null);
  Check(!CopierEngine.Sim(eval),"clearing the list refuses again");
 }
 static void SimTestScopePolicy(){
  var sim=ProvAcc("scope sim",NinjaTrader.Cbi.Provider.Simulator);var pb=ProvAcc("scope pb",NinjaTrader.Cbi.Provider.Playback);var live=ProvAcc("scope live",NinjaTrader.Cbi.Provider.Live);
  Check(CopierEngine.SimScope(new[]{sim}),"Simulator alone is permitted");
  Check(CopierEngine.SimScope(new[]{sim,pb}),"Simulator + Playback permitted");
  Check(!CopierEngine.SimScope(new[]{live}),"Live alone refused");
  Check(!CopierEngine.SimScope(new[]{sim,live}),"one Live account in scope refuses the whole scope");
  Check(!CopierEngine.SimScope(new[]{live,sim,pb}),"Live anywhere in the list refuses");
  Check(!CopierEngine.SimScope(null),"null scope refused");
  Check(!CopierEngine.SimScope(new NinjaTrader.Cbi.Account[]{sim,null}),"null member refused");
  Check(!CopierEngine.SimScope(new NinjaTrader.Cbi.Account[0]),"empty scope refused");
 }
#endif
#if KINFX_SIM_TEST
 // ---- Dry run (SIM build only): the engine decides and logs every provider call, but the call is never sent.
 static CopierEngine ArmDry(NinjaTrader.Cbi.Account l,NinjaTrader.Cbi.Account a,List<string> log){var e=new CopierEngine(s=>{lock(log)log.Add(s);},t=>{},(attached,s)=>{},(n,r)=>{});e.DryRun=true;e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10,Target=100},new RiskLatch())});Wait(()=>e.EntriesArmed,"arm");return e;}
 static void SimTestDryRunSendsNothingButLogsTheDecision(){
  var l=ProvAcc("dry lead",NinjaTrader.Cbi.Provider.Simulator);var a=ProvAcc("dry follow",NinjaTrader.Cbi.Provider.Simulator);var log=new List<string>();
  using(var e=ArmDry(l,a,log)){
   e.SetFollowing(a.Name,true);Drain(e);
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));
   Wait(()=>{lock(log)return log.Any(x=>x.StartsWith("DRY RUN"));},"dry run decision is logged");Drain(e);
   Check(a.Submits==0&&a.SubmitAttempts==0,"dry run must not call Account.Submit");
   lock(log)Check(log.Any(x=>x.Contains("dry follow")&&x.Contains("Buy")),"log names the follower and the action it WOULD have sent");
   Check(!log.Any(x=>x.StartsWith("COPY ")),"dry run never logs a real COPY");
   e.Flatten(new[]{a},inst);Drain(e);
   Check(a.Flattens==0,"dry run must not call Account.Flatten");
   lock(log)Check(log.Any(x=>x.StartsWith("DRY RUN")&&x.Contains("Flatten")),"flatten is logged as a dry run");
  }
 }
 static void SimTestDryRunOffStillSends(){
  var l=ProvAcc("wet lead",NinjaTrader.Cbi.Provider.Simulator);var a=ProvAcc("wet follow",NinjaTrader.Cbi.Provider.Simulator);var log=new List<string>();
  var e=new CopierEngine(s=>{lock(log)log.Add(s);},t=>{},(attached,s)=>{},(n,r)=>{});
  using(e){
   Check(!e.DryRun,"dry run is off by default");
   e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10,Target=100},new RiskLatch())});Wait(()=>e.EntriesArmed,"arm");
   e.SetFollowing(a.Name,true);Drain(e);
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Wait(()=>a.Submits==1,"normal copy still sent");
   Check(!log.Any(x=>x.StartsWith("DRY RUN")),"no dry-run text when the mode is off");
  }
 }
 static void SimTestDryRunCannotBeSwitchedOnMidSession(){
  var l=ProvAcc("mid lead",NinjaTrader.Cbi.Provider.Simulator);var a=ProvAcc("mid follow",NinjaTrader.Cbi.Provider.Simulator);
  using(var e=Arm(l,a)){
   e.SetFollowing(a.Name,true);Drain(e);
   bool threw=false;try{e.DryRun=true;}catch(InvalidOperationException){threw=true;}
   Check(threw&&!e.DryRun,"the mode is fixed for the whole session: changing it while attached is refused");
  }
 }
#endif
 static void SimTestSimAndPlaybackStartCopyFlatten(){
  foreach(var p in new[]{NinjaTrader.Cbi.Provider.Simulator,NinjaTrader.Cbi.Provider.Playback}){
   var l=ProvAcc("simtest lead "+p,p);var a=ProvAcc("simtest follow "+p,p);
   using(var e=Arm(l,a)){
    Check(e.Attached&&e.EntriesArmed,"Start attaches and arms for "+p);
    e.SetFollowing(a.Name,true);Drain(e);
    l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Wait(()=>a.Submits==1,"follower copy submitted for "+p);
    Fill(a,2);Drain(e);
    e.Flatten(new[]{a},inst);Drain(e);
    Check(a.Flattens==1,"Flatten reaches a "+p+" account");
   }}
 }
 static void SimTestLiveNeverStarts(){
  var live=NinjaTrader.Cbi.Provider.Live;var sim=NinjaTrader.Cbi.Provider.Simulator;
  var cases=new[]{Tuple.Create(live,live),Tuple.Create(live,sim),Tuple.Create(sim,live)};
  foreach(var c in cases){
   var l=ProvAcc("livetest lead "+c.Item1,c.Item1);var a=ProvAcc("livetest follow "+c.Item2,c.Item2);
   using(var e=new CopierEngine(s=>{},t=>{},(b,s)=>{},(n,r)=>{})){
    e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch())});Drain(e);
    e.SetFollowing(a.Name,true);Drain(e);
    l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Drain(e);
    Check(!e.Attached&&!e.EntriesArmed&&a.Submits==0&&l.Submits==0,"leader "+c.Item1+" follower "+c.Item2+": no attach, no entries, no submits");
   }}
 }
 static void SimTestFlattenRefusesAnyLiveTarget(){
  var sim=ProvAcc("flat sim",NinjaTrader.Cbi.Provider.Simulator);var live=ProvAcc("flat live",NinjaTrader.Cbi.Provider.Live);
  using(var e=new CopierEngine(s=>{},t=>{},(b,s)=>{},(n,r)=>{})){
   e.Flatten(new[]{sim,live},inst);Drain(e);
   Check(sim.Flattens==0&&live.Flattens==0,"a Live account in the flatten scope refuses the WHOLE request, including the Simulator account");
   e.Flatten(new[]{live},inst);Drain(e);Check(live.Flattens==0,"Live alone never flattened");
  }
 }
 static void SimTestNullArgumentsRefusedWithoutThrowing(){
  var sim=ProvAcc("null sim",NinjaTrader.Cbi.Provider.Simulator);
  using(var e=new CopierEngine(s=>{},t=>{},(b,s)=>{},(n,r)=>{})){
   e.Start(null,inst,new[]{Tuple.Create(sim,new RiskSettings{MaxContracts=10},new RiskLatch())});Drain(e);Check(!e.Attached,"null leader refused");
   e.Start(sim,inst,null);Drain(e);Check(!e.Attached,"null follower list refused");
   e.Start(sim,inst,new Tuple<NinjaTrader.Cbi.Account,RiskSettings,RiskLatch>[]{Tuple.Create((NinjaTrader.Cbi.Account)null,new RiskSettings{MaxContracts=10},new RiskLatch())});Drain(e);Check(!e.Attached,"null follower account refused");
   e.Flatten(null,inst);e.Flatten(new NinjaTrader.Cbi.Account[]{null},inst);Drain(e);Check(sim.Flattens==0,"null flatten scope refused");
  }
 }

 static void SimTestProviderFlipsAfterStartFollowerNotSubmitted(){
  var l=ProvAcc("flip f lead",NinjaTrader.Cbi.Provider.Simulator);var a=ProvAcc("flip f follow",NinjaTrader.Cbi.Provider.Simulator);
  using(var e=Arm(l,a)){
   e.SetFollowing(a.Name,true);Drain(e);
   a.Provider=NinjaTrader.Cbi.Provider.Live;
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Drain(e);
   Check(a.Submits==0,"engine must not submit to an account that is Live at submit time (Submits="+a.Submits+")");
  }
 }
 static void SimTestProviderFlipsAfterStartLeaderNotCopied(){
  var l=ProvAcc("flip l lead",NinjaTrader.Cbi.Provider.Simulator);var a=ProvAcc("flip l follow",NinjaTrader.Cbi.Provider.Simulator);
  using(var e=Arm(l,a)){
   e.SetFollowing(a.Name,true);Drain(e);
   l.Provider=NinjaTrader.Cbi.Provider.Live;
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Drain(e);
   Check(a.Submits==0,"leader turned Live after Start: follower copy should stop (Submits="+a.Submits+")");
  }
 }
 static void SimTestFlattenWithoutSessionAndNullInstrument(){
  var sim=ProvAcc("nullinst sim",NinjaTrader.Cbi.Provider.Simulator);
  using(var e=new CopierEngine(s=>{},t=>{},(b,s)=>{},(n,r)=>{})){
   e.Flatten(new[]{sim},null);Drain(e);
   Check(sim.Flattens==0,"null instrument must not reach account.Flatten");
   e.Flatten(new[]{sim},inst);Drain(e);
   Check(sim.Flattens==1,"a Simulator account can still be flattened with a real instrument and no session");
  }
 }
 // The scope that was VERIFIED must be the scope that is USED: a second enumeration of the follower list may not smuggle in a Live account.
 static IEnumerable<Tuple<NinjaTrader.Cbi.Account,RiskSettings,RiskLatch>> Lazy(NinjaTrader.Cbi.Account first,NinjaTrader.Cbi.Account second,int[] calls){
  calls[0]++;
  yield return Tuple.Create(calls[0]==1?first:second,new RiskSettings{MaxContracts=10},new RiskLatch());
 }
 static void SimTestStartEnumeratesFollowersOnce(){
  var l=ProvAcc("lazy lead",NinjaTrader.Cbi.Provider.Simulator);var simF=ProvAcc("lazy sim f",NinjaTrader.Cbi.Provider.Simulator);var liveF=ProvAcc("lazy live f",NinjaTrader.Cbi.Provider.Live);
  var calls=new[]{0};
  using(var e=new CopierEngine(s=>{},t=>{},(b,s)=>{},(n,r)=>{})){
   e.Start(l,inst,Lazy(simF,liveF,calls));Drain(e);
   Check(calls[0]==1,"followers enumerated "+calls[0]+" times; the verified list must be the used list");
   Check(e.Attached,"first (Sim) enumeration is a legal scope and attaches");
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Drain(e);
   Check(liveF.Submits==0,"a Live account smuggled in by a second enumeration is never submitted to");
  }
 }

 // ---- Added after independent review of PR #17/#18/#19 (reviewer-written regression cases).
 static T Fld<T>(object o,string n){return (T)o.GetType().GetField(n,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public).GetValue(o);}
 static void Pump(CopierEngine e){var w=Fld<SerialWorker>(e,"worker");var d=new ManualResetEvent(false);Check(w.Post(()=>d.Set()),"pump post");Check(d.WaitOne(3000),"pump");}
 static void RvSetup(out NinjaTrader.Cbi.Account l,out NinjaTrader.Cbi.Account a,out CopierEngine e,out NinjaTrader.Cbi.Order stop,out NinjaTrader.Cbi.Order exit,out CopyLink link,string tag){
  l=Acc("rv leader "+tag);a=Acc("rv follower "+tag);e=Arm(l,a);
  l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",4));Drain(e);Fill(a,4);Drain(e);
  stop=LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"rv stop "+tag,2);l.Emit(stop);Drain(e);exit=a.Orders[1];
  var links=Fld<Dictionary<string,List<CopyLink>>>(e,"links");var ex=exit;link=links.Values.SelectMany(v=>v).Single(x=>x.Order==ex);
 }
 static void RvPendingNoLeakChangeThrowsBeforeApply(){
  NinjaTrader.Cbi.Account l,a;CopierEngine e;NinjaTrader.Cbi.Order stop,exit;CopyLink link;RvSetup(out l,out a,out e,out stop,out exit,out link,"tb");
  try{a.ChangeHook=o=>{throw new InvalidOperationException("before apply");};stop.Quantity=4;l.Emit(stop);Drain(e);
   Check(link.PendingChangeQuantities.Count==0&&link.ChangesInFlight==0,"pending entry removed after provider throw before apply; count="+link.PendingChangeQuantities.Count);}
  finally{e.Dispose();}
 }
 static void RvPendingNoLeakChangeThrowsAfterApply(){
  NinjaTrader.Cbi.Account l,a;CopierEngine e;NinjaTrader.Cbi.Order stop,exit;CopyLink link;RvSetup(out l,out a,out e,out stop,out exit,out link,"ta");
  try{a.SuppressChangeAck=true;a.ThrowChangeAfterApply=true;stop.Quantity=4;l.Emit(stop);Drain(e);
   Check(link.PendingChangeQuantities.Count==0,"pending entry removed after throw after apply; count="+link.PendingChangeQuantities.Count);
   Check(link.ReservedQuantity==4,"unknown acceptance keeps reservation 4");}
  finally{e.Dispose();}
 }
 static void RvPendingNoLeakStoppedDispatcher(){
  NinjaTrader.Cbi.Account l,a;CopierEngine e;NinjaTrader.Cbi.Order stop,exit;CopyLink link;RvSetup(out l,out a,out e,out stop,out exit,out link,"sd");
  try{var fs=Fld<List<EngineAccount>>(e,"followers");fs[0].Dispatcher.Dispose();stop.Quantity=4;l.Emit(stop);Drain(e);
   Check(a.Changes==0,"stopped dispatcher: no provider Change sent");
   Check(link.PendingChangeQuantities.Count==0,"stopped dispatcher: pending entry removed; count="+link.PendingChangeQuantities.Count);
   Check(e.ProviderCallsInFlight==0,"in-flight counter not leaked");}
  finally{e.Dispose();}
 }
 static void RvPendingDuplicateValuesRemovedOnePerCompletion(){
  NinjaTrader.Cbi.Account l,a;CopierEngine e;NinjaTrader.Cbi.Order stop,exit;CopyLink link;RvSetup(out l,out a,out e,out stop,out exit,out link,"dup");
  try{a.ChangeEntered=new ManualResetEvent(false);a.ChangeRelease=new ManualResetEvent(false);
   stop.Quantity=4;l.Emit(stop);Check(a.ChangeEntered.WaitOne(3000),"first blocked");
   stop.Quantity=2;l.Emit(stop);Pump(e);stop.Quantity=4;l.Emit(stop);Pump(e);
   Check(link.PendingChangeQuantities.Count==3&&link.PendingChangeQuantities.Count(q=>q==4)==2,"4,2,4 pending; got "+string.Join(",",link.PendingChangeQuantities));
   a.ChangeRelease.Set();Drain(e);
   Check(link.PendingChangeQuantities.Count==0,"all completions remove exactly one each; left="+string.Join(",",link.PendingChangeQuantities));
   Check(exit.Quantity==4&&link.ReservedQuantity==4,"final 4/4; qty="+exit.Quantity+" res="+link.ReservedQuantity);}
  finally{if(a.ChangeRelease!=null)a.ChangeRelease.Set();e.Dispose();}
 }
 static void RvDisposeFromEngineWorkerDoesNotDeadlock(){
  var l=Acc("rv dw l");var a=Acc("rv dw a");var e=Arm(l,a);var w=Fld<SerialWorker>(e,"worker");var done=new ManualResetEvent(false);
  long ms=-1;Check(w.Post(()=>{var sw=System.Diagnostics.Stopwatch.StartNew();e.Dispose();ms=sw.ElapsedMilliseconds;done.Set();}),"post");Check(done.WaitOne(6000),"Dispose on worker returned");Check(ms>=0&&ms<1000,"Dispose on engine worker must not self-wait; ms="+ms);
  Wait(()=>!e.Attached,"detached");Check(!w.Post(()=>{}),"mailbox stopped");
 }
 static void RvConcurrentDisposeDetachesOnce(){
  var l=Acc("rv cd l");var a=Acc("rv cd a");int offs=0;
  var e=new CopierEngine(s=>{},t=>{},(att,s)=>{if(!att)Interlocked.Increment(ref offs);},(n,r)=>{});
  e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10,Target=100},new RiskLatch())});Wait(()=>e.EntriesArmed,"arm");
  var bar=new Barrier(3);var ts=Enumerable.Range(0,2).Select(i=>new Thread(()=>{bar.SignalAndWait();e.Dispose();})).ToList();ts.ForEach(t=>t.Start());bar.SignalAndWait();ts.ForEach(t=>Check(t.Join(6000),"dispose joined"));
  Thread.Sleep(100);Check(!e.Attached,"detached");Check(offs==1,"stateChanged(false) exactly once; got "+offs);
 }
 static void RvStopFlatThenDisposeDetachesOnce(){
  var l=Acc("rv sd l");var a=Acc("rv sd a");int offs=0;
  var e=new CopierEngine(s=>{},t=>{},(att,s)=>{if(!att)Interlocked.Increment(ref offs);},(n,r)=>{});
  e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10,Target=100},new RiskLatch())});Wait(()=>e.EntriesArmed,"arm");
  e.StopFlat();Wait(()=>!e.Attached,"stopflat detached");Drain(e);e.Dispose();Thread.Sleep(100);
  Check(offs==1,"Detach/FinishDetach once after StopFlat+Dispose; got "+offs);
 }
 static void RvStartImmediatelyAfterStopFlatSingleHandler(){
  var l=Acc("rv sf l");var a=Acc("rv sf a");var e=Arm(l,a);
  try{e.StopFlat();e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10,Target=100},new RiskLatch())});
   Wait(()=>e.Attached&&e.EntriesArmed,"restart armed");Drain(e);
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",2));Drain(e);Thread.Sleep(100);Drain(e);
   Check(a.Submits==1,"exactly one follower submit after restart; got "+a.Submits);}
  finally{e.Dispose();}
 }
 static void RvDisposeTimeoutStillClosesAfterWorkerFrees(){
  var l=Acc("rv dt l");var a=Acc("rv dt a");var e=Arm(l,a);var w=Fld<SerialWorker>(e,"worker");
  var entered=new ManualResetEvent(false);var rel=new ManualResetEvent(false);
  try{Check(w.Post(()=>{entered.Set();rel.WaitOne(8000);}),"barrier");Check(entered.WaitOne(3000),"blocked");
   var sw=System.Diagnostics.Stopwatch.StartNew();e.Dispose();sw.Stop();Check(sw.ElapsedMilliseconds<4500,"Dispose bounded; ms="+sw.ElapsedMilliseconds);
   Check(e.Attached&&!e.EntriesArmed,"half state during timeout: attached but entries blocked");
   rel.Set();Wait(()=>!e.Attached,"queued close ran");Check(!w.Post(()=>{}),"mailbox stopped after late close");}
  finally{rel.Set();}
 }

 // ---- KINFX_SIM_TEST build only: added after the second independent review (provider flips between call and worker, unknown enum values, queued Cancel).
 static void HoldWorker(CopierEngine e,out ManualResetEvent release){
  var f=typeof(CopierEngine).GetField("worker",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);var w=(SerialWorker)f.GetValue(e);
  var entered=new ManualResetEvent(false);var rel=new ManualResetEvent(false);release=rel;
  w.Post(()=>{entered.Set();rel.WaitOne(5000);});Check(entered.WaitOne(3000),"worker held");
 }
 static void SimTestUnknownProviderEnumNeverAttachedOrFlattened(){
  foreach(var v in new[]{99,-1,3,int.MaxValue}){
   var p=(NinjaTrader.Cbi.Provider)v;
   var l=ProvAcc("unk lead "+v,p);var a=ProvAcc("unk follow "+v,p);var s=ProvAcc("unk sim "+v,NinjaTrader.Cbi.Provider.Simulator);
   using(var e=new CopierEngine(x=>{},t=>{},(b,x)=>{},(n,r)=>{})){
    e.Start(s,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch())});Drain(e);Check(!e.Attached,"unknown follower provider "+v);
    e.Start(l,inst,new[]{Tuple.Create(s,new RiskSettings{MaxContracts=10},new RiskLatch())});Drain(e);Check(!e.Attached,"unknown leader provider "+v);
    e.Flatten(new[]{a},inst);e.Flatten(new[]{s,a},inst);Drain(e);Check(a.Flattens==0&&s.Flattens==0,"unknown provider flatten "+v);
   }}
 }
 static void SimTestFlattenArrayMutatedAfterCallStillSafe(){
  var sim=ProvAcc("mut sim",NinjaTrader.Cbi.Provider.Simulator);var live=ProvAcc("mut live",NinjaTrader.Cbi.Provider.Live);
  using(var e=new CopierEngine(x=>{},t=>{},(b,x)=>{},(n,r)=>{})){
   ManualResetEvent rel;HoldWorker(e,out rel);
   var arr=new[]{sim};e.Flatten(arr,inst);arr[0]=live;rel.Set();Drain(e);
   Check(live.Flattens==0,"caller mutating the array after Flatten returned must not redirect the call to a Live account (live.Flattens="+live.Flattens+")");
  }
 }
 static void SimTestFlattenProviderFlipBetweenQueueAndWorkerNoSession(){
  var sim=ProvAcc("flipq sim",NinjaTrader.Cbi.Provider.Simulator);
  using(var e=new CopierEngine(x=>{},t=>{},(b,x)=>{},(n,r)=>{})){
   ManualResetEvent rel;HoldWorker(e,out rel);
   e.Flatten(new[]{sim},inst);sim.Provider=NinjaTrader.Cbi.Provider.Live;rel.Set();Drain(e);
   Check(sim.Flattens==0,"account became Live after the request was queued: direct (non-follower) Flatten must not be sent");
  }
 }
 static void SimTestFlattenFollowerFlipBetweenQueueAndWorker(){
  var l=ProvAcc("flipqf lead",NinjaTrader.Cbi.Provider.Simulator);var a=ProvAcc("flipqf follow",NinjaTrader.Cbi.Provider.Simulator);
  using(var e=Arm(l,a)){
   ManualResetEvent rel;HoldWorker(e,out rel);
   e.Flatten(new[]{a},inst);a.Provider=NinjaTrader.Cbi.Provider.Live;rel.Set();Drain(e);
   Check(a.Flattens==0,"follower became Live after flatten queued: not sent");
  }
 }
 static void SimTestStartProviderFlipBetweenCallAndWorker(){
  var l=ProvAcc("flips lead",NinjaTrader.Cbi.Provider.Simulator);var a=ProvAcc("flips follow",NinjaTrader.Cbi.Provider.Simulator);
  using(var e=new CopierEngine(x=>{},t=>{},(b,x)=>{},(n,r)=>{})){
   ManualResetEvent rel;HoldWorker(e,out rel);
   e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch())});a.Provider=NinjaTrader.Cbi.Provider.Live;rel.Set();Drain(e);
   Check(!e.Attached,"follower flipped to Live between Start() and the worker running: must not attach");
  }
 }
 static void SimTestQueuedCancelBehindBlockedSubmitNotSentAfterFlip(){
  var l=ProvAcc("qc lead",NinjaTrader.Cbi.Provider.Simulator);var a=ProvAcc("qc follow",NinjaTrader.Cbi.Provider.Simulator);
  using(var e=Arm(l,a)){
   e.SetFollowing(a.Name,true);Drain(e);
   a.SubmitEntered=new ManualResetEvent(false);a.SubmitRelease=new ManualResetEvent(false);
   var o=LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy);l.Emit(o);
   Check(a.SubmitEntered.WaitOne(3000),"first submit is in flight");
   o.OrderState=NinjaTrader.Cbi.OrderState.Cancelled;l.Emit(o);
   Thread.Sleep(300);
   a.Provider=NinjaTrader.Cbi.Provider.Live;
   a.SubmitRelease.Set();Drain(e);
   Check(a.Cancels==0,"a Cancel queued behind an in-flight Submit must not be sent after the account turned Live (Cancels="+a.Cancels+")");
   Check(a.SubmitAttempts==1,"only the already-in-flight submit happened");
  }
 }
 static void SimTestLiveFollowerNeverGetsCreateOrder(){
  var l=ProvAcc("co lead",NinjaTrader.Cbi.Provider.Simulator);var a=ProvAcc("co follow",NinjaTrader.Cbi.Provider.Simulator);
  using(var e=Arm(l,a)){
   e.SetFollowing(a.Name,true);Drain(e);a.Provider=NinjaTrader.Cbi.Provider.Live;
   l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Drain(e);
   Check(a.Submits==0&&a.SubmitAttempts==0,"no Submit attempt at all (attempts="+a.SubmitAttempts+")");
   Check(a.Orders.Count==0,"no order wrapper in the account's order list");
  }
 }
}
