using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NinjaTrader.NinjaScript.AddOns.Kinfx;
// Synthetic DEMO only: no accounts, no engine, no orders. Exercises the bulk-edit bar on the real DashboardView.
public static class BulkUiTests {
 static void Layout(FrameworkElement e){e.Measure(new Size(1500,950));e.Arrange(new Rect(0,0,1500,950));e.UpdateLayout();System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ContextIdle,new Action(()=>{}));e.UpdateLayout();}
 static void Check(bool ok,string m){if(!ok)throw new Exception(m);}
 static DashboardView Fresh(){var v=new DashboardView(true);v.Editable(true);Layout(v);return v;}
 static void Select(DashboardView v,params int[] idx){v.AccountGrid.UnselectAll();foreach(int i in idx)v.AccountGrid.SelectedItems.Add(v.Rows[i]);}
 static void TargetAndLossApplyToSelectedOnly(){
  var v=Fresh();Select(v,1,2,3);
  string r=v.BulkApply("Tgt","750");Check(r==null,"valid Tgt must apply: "+r);
  Check(v.Rows[1].Target==750&&v.Rows[2].Target==750&&v.Rows[3].Target==750,"selected rows must get Tgt");
  Check(v.Rows[4].Target==1000,"unselected row must keep its own Tgt");
  r=v.BulkApply("DLL","300");Check(r==null,"valid DLL must apply: "+r);
  Check(v.Rows[1].Loss==300&&v.Rows[3].Loss==300&&v.Rows[4].Loss==500,"DLL scope");
 }
 static void ZeroTurnsLimitOff(){var v=Fresh();Select(v,1);Check(v.BulkApply("Tgt","0")==null&&v.Rows[1].Target==0,"0 must be accepted as OFF");}
 static void RatioStaysIndependent(){
  var v=Fresh();v.Rows[1].Ratio=2;v.Rows[2].Ratio=0.5;Select(v,1,2);
  Check(v.BulkApply("Tgt","900")==null,"apply");
  Check(v.Rows[1].Ratio==2&&v.Rows[2].Ratio==0.5,"bulk Tgt/DLL must never touch each row's own Ratio");
 }
 static void InvalidInputChangesNothing(){
  var v=Fresh();Select(v,1,2);
  foreach(string bad in new[]{"","abc","-5","NaN","1e400","1,000"}){
   string r=v.BulkApply("Tgt",bad);Check(r!=null,"invalid '"+bad+"' must be refused");
   Check(v.Rows[1].Target==1000&&v.Rows[2].Target==1000,"refused input must not change any row: '"+bad+"'");
  }
 }
 static void NoSelectionIsRefused(){var v=Fresh();v.AccountGrid.UnselectAll();Check(v.BulkApply("Tgt","5")!=null,"empty selection must be refused");Check(v.Rows.All(r=>r.Target==1000),"nothing may change");}
 static void LeaderIsNotEdited(){var v=Fresh();Select(v,0,1);Check(v.BulkApply("Tgt","10")==null,"apply");Check(v.Rows[0].Target==1000&&v.Rows[1].Target==10,"leader row is not a follower and must be skipped");}
 static void AtmToggleBulk(){
  var v=Fresh();Select(v,1,2);Check(v.BulkApply("ATM","on")==null,"ATM on");Check(v.Rows[1].CopyAtm&&v.Rows[2].CopyAtm,"ATM on selected");
  Check(v.BulkApply("ATM","off")==null,"ATM off");Check(!v.Rows[1].CopyAtm&&!v.Rows[2].CopyAtm,"ATM off selected");
  Check(v.BulkApply("ATM","maybe")!=null,"bad ATM value refused");
 }
 static void LockedWhenNotEditable(){var v=Fresh();v.Editable(false);Select(v,1);Check(v.BulkApply("Tgt","5")!=null,"attached/locked state must refuse bulk edits");Check(v.Rows[1].Target==1000,"unchanged");}
 static void NeverTouchesFollowOrTrading(){
  var v=Fresh();int follow=0;v.FollowRow=(r,on)=>follow++;bool enabledBefore=v.Rows[1].Enabled;Select(v,1,2);
  v.BulkApply("Tgt","5");v.BulkApply("DLL","5");v.BulkApply("ATM","on");
  Check(follow==0,"bulk settings must not call the follow/engine path");Check(v.Rows[1].Enabled==enabledBefore,"bulk settings must not change the tick");
  Check(!v.StartButton.IsEnabled,"Start must stay held");
 }
 static void BarIsOnCopyTabAndKeepsHolds(){
  var v=Fresh();Layout(v);
  Check(v.AccountGrid.Columns.Count==13,"13 Copy columns retained");
  Check(v.BulkBarPresent,"bulk bar must be on the Copy tab");
 }
 [STAThread] public static int Main(string[] args){
  try{
   AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{string n=e.Name.Split(',')[0];if(n.EndsWith(".resources"))return null;var loaded=AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a=>a.GetName().Name==n);if(loaded!=null)return loaded;string p=System.IO.Path.Combine("C:/Program Files/NinjaTrader 8/bin",n+".dll");return System.IO.File.Exists(p)?System.Reflection.Assembly.LoadFrom(p):null;};
   var all=new Action[]{TargetAndLossApplyToSelectedOnly,ZeroTurnsLimitOff,RatioStaysIndependent,InvalidInputChangesNothing,NoSelectionIsRefused,LeaderIsNotEdited,AtmToggleBulk,LockedWhenNotEditable,NeverTouchesFollowOrTrading,BarIsOnCopyTabAndKeepsHolds};
   int failed=0;foreach(var a in all){try{a();Console.WriteLine("PASS "+a.Method.Name);}catch(Exception ex){failed++;Console.WriteLine("FAIL "+a.Method.Name+" :: "+ex.Message);}}
   return failed==0?0:1;
  }catch(Exception ex){Console.WriteLine("HARNESS "+ex);return 2;}
 }
}
