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
  public double Pnl,Equity=50000;public int Submits,Changes,Cancels,Flattens;public bool ThrowSubmit,ThrowCancel,ThrowFlatten;public ManualResetEvent CreateEntered,CreateRelease,SubmitEntered,SubmitRelease;public bool AsyncCancel,NoopFlatten,FlattenWorkingBeforeThrow;
  public double Get(AccountItem i,Currency c){return i==AccountItem.RealizedProfitLoss?Pnl:i==AccountItem.UnrealizedProfitLoss?0:Equity;}
  public Order CreateOrder(Instrument i,OrderAction a,OrderType t,OrderEntry e,TimeInForce tif,int q,double l,double s,string oco,string n,DateTime g,object ignored){if(CreateEntered!=null){CreateEntered.Set();CreateRelease.WaitOne();}return new Order {Account=this,Instrument=i,OrderAction=a,OrderType=t,OrderEntry=e,TimeInForce=tif,Quantity=q,LimitPrice=l,StopPrice=s,Oco=oco,Name=n,Gtd=g};}
  public void Submit(Order[] orders){if(SubmitEntered!=null){SubmitEntered.Set();SubmitRelease.WaitOne();}if(ThrowSubmit)throw new InvalidOperationException("Synthetic submit error");foreach(var o in orders){lock(Orders)Orders.Add(o);o.OrderState=OrderState.Working;Interlocked.Increment(ref Submits);Emit(o);}}
  public System.Collections.Generic.List<int> ChangeQuantities=new System.Collections.Generic.List<int>();public void Change(Order[] orders){foreach(var o in orders){lock(ChangeQuantities)ChangeQuantities.Add(o.QuantityChanged);o.Quantity=o.QuantityChanged;o.LimitPrice=o.LimitPriceChanged;o.StopPrice=o.StopPriceChanged;Interlocked.Increment(ref Changes);Emit(o);}}
  public void Cancel(Order[] orders){if(AsyncCancel){Interlocked.Add(ref Cancels,orders.Length);return;}if(ThrowCancel)throw new InvalidOperationException("Synthetic cancel error");foreach(var o in orders){o.OrderState=OrderState.Cancelled;Interlocked.Increment(ref Cancels);Emit(o);}}
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
 static CopierEngine Arm(NinjaTrader.Cbi.Account l,NinjaTrader.Cbi.Account a,Action<AccountTelemetry> tele=null,Action<string,string> persist=null){var e=new CopierEngine(s=>{},tele??(t=>{}),(attached,s)=>{},persist??((n,r)=>{}));e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10,Target=100},new RiskLatch())});Wait(()=>e.EntriesArmed,"arm");return e;}
 static void Drain(CopierEngine e){var f=typeof(CopierEngine).GetField("worker",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);var w=(SerialWorker)f.GetValue(e);var done=new ManualResetEvent(false);w.Post(()=>done.Set());Check(done.WaitOne(3000),"drain");}
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
 static void AmbiguousIdKeepsFillEvidence(){var l=Acc("amb id lead");var a=Acc("amb id follow");using(var e=Arm(l,a)){l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Drain(e);var order=a.Orders[0];order.OrderId="known-id";a.Emit(order);Drain(e);order.OrderId="changed-id";order.Filled=2;order.OrderState=NinjaTrader.Cbi.OrderState.Filled;a.Emit(order);Drain(e);var dict=(Dictionary<string,List<CopyLink>>)typeof(CopierEngine).GetField("links",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(e);var link=dict.Values.Single().Single();Check(link.ObservedFilled==2&&link.ExecutionEvidence.Any(x=>x.Filled==2),"identity divergence must not discard captured fill evidence");Check(!e.CanClose()&&link.Follower.Latch.Locked,"ambiguous identity denies detach and locks");}}
 static void FlattenThrowMayHaveAccepted(){var l=Acc("throw accepted lead");var a=Acc("throw accepted follow");using(var e=Arm(l,a)){l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Drain(e);Fill(a,2);Drain(e);a.ThrowFlatten=true;a.FlattenWorkingBeforeThrow=true;e.Flatten(new[]{a},inst);Drain(e);l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"no duplicate exit"));Drain(e);Check(a.Submits==1,"Flatten exception may follow provider acceptance; no extra exit before explicit recovery");e.AcknowledgeFlattenAbandoned(a.Name);Drain(e);l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"still blocked"));Drain(e);Check(a.Submits==1,"unknown working flatten forbids abandonment");}}
 static void CancelledLateFillManualRecovery(){var l=Acc("late cancel lead");var a=Acc("late cancel follow");AccountTelemetry latest=null;using(var e=Arm(l,a,t=>latest=t)){l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",4));Wait(()=>a.Submits==1,"entry");Fill(a,2);Drain(e);l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Sell,"stop",4));Wait(()=>a.Submits==2,"exit");a.Orders[1].OrderState=NinjaTrader.Cbi.OrderState.Cancelled;a.Emit(a.Orders[1]);Drain(e);a.Orders[0].Filled=4;a.Orders[0].OrderState=NinjaTrader.Cbi.OrderState.Filled;a.Positions[0].Quantity=4;a.Emit(a.Orders[0]);Drain(e);e.Poll();Drain(e);Check(a.Submits==2&&a.Orders[1].OrderState==NinjaTrader.Cbi.OrderState.Cancelled,"no unsafe autonomous stop recreation");Check(latest.Locked&&!latest.Following&&(latest.Reason.Contains("uncovered")||latest.Reason.Contains("manual")),"uncovered exposure reported honestly for manual recovery");}}
 static void ReplacementReservationsCountOnce(){var l=Acc("reservation replace lead");var a=Acc("reservation replace follow");using(var e=new CopierEngine(s=>{},t=>{},(b,s)=>{},(n,r)=>{})){e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=4},new RiskLatch())});Wait(()=>e.EntriesArmed,"arm");l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",3));Drain(e);var original=a.Orders[0];original.OrderId="partial-id";original.Filled=2;original.OrderState=NinjaTrader.Cbi.OrderState.PartFilled;a.Positions.Add(new NinjaTrader.Cbi.Position{Instrument=inst,Quantity=2,MarketPosition=NinjaTrader.Cbi.MarketPosition.Long});a.Emit(original);Drain(e);var replacement=a.CreateOrder(inst,original.OrderAction,original.OrderType,original.OrderEntry,original.TimeInForce,3,100,0,original.Oco,original.Name,original.Gtd,null);replacement.OrderId=original.OrderId;replacement.Filled=2;replacement.OrderState=NinjaTrader.Cbi.OrderState.PartFilled;a.Orders.Add(replacement);a.Emit(replacement);Drain(e);l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy,"",1));Drain(e);Check(a.Submits==2,"replacement wrappers reserve same provider order only once");}}
 static void InFlightCannotDetach(){var l=Acc("close flight lead");var a=Acc("close flight follow");a.SubmitEntered=new ManualResetEvent(false);a.SubmitRelease=new ManualResetEvent(false);using(var e=Arm(l,a)){l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Check(a.SubmitEntered.WaitOne(3000),"submit in flight");try{Check(!e.CanClose(),"authorized broker work blocks detach even absent Orders and Positions");}finally{a.SubmitRelease.Set();}Drain(e);}}
 static void StopLedgerSafety(){var l=Acc("stop ledger lead");var a=Acc("stop ledger follow");using(var e=Arm(l,a)){l.Emit(LeaderOrder(l,NinjaTrader.Cbi.OrderAction.Buy));Wait(()=>a.Submits==1,"entry");Fill(a,2,false);Drain(e);Check(!e.CanClose(),"owned execution blocks close before Position catches up");e.StopFlat();Drain(e);Check(e.Attached&&!e.EntriesArmed,"Stop retains attached ledger");e.Start(l,inst,new[]{Tuple.Create(a,new RiskSettings{MaxContracts=10},new RiskLatch())});Drain(e);Check(!e.EntriesArmed,"restart denied");}}
}
