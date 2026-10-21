// KINFX Trade Copier. Copyright (c) 2026 Kinfxhk. MIT License.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
namespace NinjaTrader.NinjaScript.AddOns.Kinfx
{
// BEGIN KINFX PURE
    public static class DashboardSafety
    {
        // Quarantine after independent review: UI inspection only until fixes pass.
        // The three texts below are English constants (tests and the hold audit read them); the Dashboard shows the translated variants.
        public static string HeaderShown { get { return Lang.T(HeaderText); } }
        public static string StartTipShown { get { return Lang.T(StartTip); } }
        public static string LogNoteShown { get { return Lang.T(LogNote); } }
#if KINFX_SIM_TEST
        // SIM TEST BUILD ONLY: the dashboard commands are reachable, but the engine still refuses any non-Simulator/Playback account.
        public static bool CanExecute(string command) { return true; }
        public const string HeaderText="SIM TEST BUILD · demo / prop-firm evaluation accounts";
        public const string StartTip="SIM TEST BUILD: copies between simulated accounts. An account not yet confirmed as simulated shows a risk warning first.";
        public const string LogNote="SIM TEST BUILD: every NinjaTrader account is listed. Simulator / Playback accounts and accounts you confirmed as simulated (sim-allow.txt, filled in for you when you answer the risk warning) can Start / Flatten. ATM means order mirroring, not a follower native ATM. Orders ARE sent to those accounts.";
        // Every account is listed and can be ticked; one not confirmed as simulated asks a non-skippable risk warning before Start / Flatten.
        public const bool ListAllAccounts=true;
#else
        public const string HeaderText="REVIEW HOLD · TRADING DISABLED";
        public const bool ListAllAccounts=false;
        public const string StartTip="REVIEW HOLD: copying unavailable until engine safety issues are fixed";
        public const string LogNote="REVIEW HOLD: Start / Flatten / bulk risk disabled. ATM means order mirroring, not a follower native ATM. No real or simulation orders were submitted by this build verification.";
        public static bool CanExecute(string command)
        { return command!="start" && command!="flattenSelected" && command!="flattenAll" && command!="batch" && command!="resume"; }
#endif
    }
    // Advisory text for settings that are legal but easy to misread. Pure: no account, engine or dispatch access.
    // Returns "" when there is nothing to say. Never blocks anything; it only informs.
    public static class SettingsAdvice
    {
        public static string For(RiskSettings s,string role,int referenceLots)
        {
            if(s==null||role=="Leader")return "";
            if(!RiskPolicy.Finite(s.Ratio)||s.Ratio<=0)return Lang.T("Ratio is not a positive number");
            var notes=new System.Collections.Generic.List<string>();
            if(s.OnDivergence=="FlattenOnce")notes.Add(Lang.T("On divergence = FlattenOnce is saved but NOT active in this alpha: the engine only locks and notifies, it never flattens by itself"));
            if(s.Target<=0&&s.Loss<=0)notes.Add(Lang.T("no daily target and no daily loss limit (0 = off)"));
            else if(s.Loss<=0)notes.Add(Lang.T("no daily loss limit (0 = off)"));
            else if(s.Target<=0)notes.Add(Lang.T("no daily target (0 = off)"));
            if(referenceLots>0)
            {
                if(RiskPolicy.Quantity(referenceLots,s.Ratio)==0)notes.Add(Lang.F("Ratio gives 0 lots for a {0}-lot leader order",referenceLots));
                else if(s.MaxContracts>0&&RiskPolicy.Quantity(referenceLots,s.Ratio)>s.MaxContracts)notes.Add(Lang.F("over Max {0}: that order is skipped, not reduced",s.MaxContracts));
            }
            return string.Join("; ",notes);
        }
    }
    // Bulk-edit helpers: pure input grammar only. No account, engine or dispatch access.
    public static class BulkEdit
    {
        // Accepts a plain non-negative finite decimal ("1000", "250.5", "0"). Rejects grouping commas,
        // signs other than the zero case, exponent overflow, NaN/Infinity and surrounding junk.
        public static bool TryParseAmount(string text,out double value)
        {
            value=0;
            if(text==null)return false;
            string t=text.Trim();
            if(t.Length==0)return false;
            if(!double.TryParse(t,NumberStyles.AllowDecimalPoint|NumberStyles.AllowLeadingSign|NumberStyles.AllowExponent,CultureInfo.InvariantCulture,out value))return false;
            if(double.IsNaN(value)||double.IsInfinity(value)||value<0)return false;
            if(value==0)value=0.0;
            return true;
        }
    }
    public static class RiskPolicy
    {
        public static bool Finite(double n) { return !double.IsNaN(n) && !double.IsInfinity(n); }
        public static bool CanUnlock(RiskSettings s,double pnl,double equity,bool connected)
        {
            var effective=s.Clone();if(s.LocalTrail&&s.TrailDistance>0)effective.Floor=Math.Max(s.Floor,s.HighWater-s.TrailDistance);
            return Evaluate(effective,new RiskLatch(),pnl,equity,connected)=="";
        }
        // Unlock all (one click for every locked follower): "" = this account may be unlocked; otherwise why it stays locked.
        // The same conditions as the single-account unlock: connected, the session instrument flat with no working orders,
        // and no risk limit (daily target / daily loss / drawdown floor) still reached. Never arms copying and never sends anything.
        public static string UnlockBlock(RiskSettings s,bool connected,bool clean,double pnl,double equity)
        {
            if(s==null||!s.Valid())return "Settings invalid";
            if(!connected)return "Disconnected";
            if(!clean)return "Position or working order on the instrument";
            var effective=s.Clone();if(s.LocalTrail&&s.TrailDistance>0)effective.Floor=Math.Max(s.Floor,s.HighWater-s.TrailDistance);
            string why=Evaluate(effective,new RiskLatch(),pnl,equity,true);
            return why==""?"":why=="Target hit"?"Daily target still reached":why=="Daily loss"?"Daily loss still reached":why=="Drawdown floor"?"Drawdown floor still reached":why;
        }
        public static string ConsistencyAdvice(RiskSettings s)
        {
            if(s.Consistency<=0)return Lang.T("Off");if(s.TotalProfit<=0||s.BestDay<=0)return Lang.T("History required");
            double pct=s.BestDay/s.TotalProfit*100;string pc=pct.ToString("F2",CultureInfo.InvariantCulture);return pct>s.Consistency?Lang.F("Over advisory limit: {0}%",pc):Lang.F("Within advisory limit: {0}%",pc);
        }
        public static int ChangeTotal(int current, int filled, int desired, bool exit, int closablePosition, int otherReserved, int maxContracts, bool entriesAllowed)
        {
            if(exit) return filled+CopyLedger.ExitBudget(Math.Max(0,desired-filled),Math.Max(0,closablePosition),Math.Max(0,otherReserved));
            // Any reduction is allowed even if a risk latch / pause is set. An increase needs capacity AND permission.
            if(desired<=current)return Math.Max(filled,desired);
            return entriesAllowed && (long)closablePosition+otherReserved+desired-filled<=maxContracts ? desired : current;
        }
        public static int ExitRemaining(int desiredTotal, int filled, int signedPosition)
        { return Math.Max(0, Math.Min(Math.Max(0, desiredTotal - filled), Math.Max(0, signedPosition))); }
        public static string Evaluate(RiskSettings s, RiskLatch latch, double pnl, double equity, bool connected)
        {
            string why = !s.Valid() ? "Settings invalid" : !connected ? "Disconnected" : !Finite(pnl) || !Finite(equity) ? "Telemetry invalid" :
                s.Target > 0 && pnl >= s.Target ? "Target hit" : s.Loss > 0 && pnl <= -s.Loss ? "Daily loss" :
                s.Floor > 0 && equity <= s.Floor ? "Drawdown floor" : "";
            if (why.Length > 0 && !latch.Locked) { latch.Locked = true; latch.Reason = why; }
            return latch.Locked ? latch.Reason : "";
        }
        public static bool CanEnter(RiskSettings s, RiskLatch latch, int position, int pending, int quantity)
        { return s.Valid() && !latch.Locked && quantity > 0 && (long)Math.Abs(position) + pending + quantity <= s.MaxContracts; }
        public static string TradingDay(DateTime utc, string zone, int resetHour)
        {
            DateTime local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById(zone));
            return local.AddHours(-resetHour).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        public static int Quantity(int quantity, double ratio)
        {
            if (quantity <= 0 || !Finite(ratio) || ratio <= 0) return 0;
            double n = Math.Floor(quantity * ratio);
            return Finite(n) && n > 0 && n <= int.MaxValue ? (int)n : 0;
        }
    }
    public sealed class RiskSettings
    {
        public double Ratio = 1, Target, Loss, Floor, Baseline, TrailDistance, HighWater, BestDay, TotalProfit, Consistency;
        public int MaxContracts = 1;
        public string Alias = "", Group = "", Preset = "Custom", DrawdownType = "User defined";
        // Reaction when this follower diverges (opposite / unknown exposure). "Notify" (default) = lock, red state, log; the engine
        // never flattens by itself. "FlattenOnce" is an explicit per-account opt-in for a future guarded Flatten: one request, never retried.
        // Stored only; the alpha engine has no automatic flatten and Flatten is disabled for every provider.
        public string OnDivergence = "Notify";
        public bool BaselineSet, LocalTrail, CopyAtm=true;
        // Opt-in per account (default OFF): when THIS account's Daily target or Daily loss latches, send ONE Flatten for it on the session instrument.
        // Never retried, never for any other lock reason (disconnect, unknown order, rejected order ...), other accounts untouched.
        public bool FlatOnLimit;
        // Opt-in per account (default OFF): after THIS follower's copied exit completes and the leader is flat too, tick it again
        // automatically (same checks as a manual re-tick: clean, connected, unlocked, entries on). Copies only LATER entries; no catch-up.
        public bool AutoRefollow;
        public RiskSettings Clone() { return (RiskSettings)MemberwiseClone(); }
        public bool Valid()
        {
            return new[] {Ratio,Target,Loss,Floor,Baseline,TrailDistance,HighWater,BestDay,TotalProfit,Consistency}.All(RiskPolicy.Finite)
                && Ratio > 0 && Target >= 0 && Loss >= 0 && Floor >= 0 && MaxContracts > 0 && TrailDistance >= 0 && Consistency >= 0 && Consistency <= 100
                && DivergenceChoice.IsValid(OnDivergence);
        }
    }
    // The dialog owns this draft; no accepted row settings change until the whole form builds.
    internal sealed class RiskSettingsDraft
    {
        RiskSettings current;
        readonly Dictionary<string,string> invalid=new Dictionary<string,string>();
        public RiskSettingsDraft(RiskSettings initial){current=initial.Clone();}
        public RiskSettings Settings {get{return current.Clone();}}
        public bool HasErrors {get{return invalid.Count!=0;}}
        public bool IsInvalid(string name){return invalid.ContainsKey(name);}
        public string Text(string name){string raw;return invalid.TryGetValue(name,out raw)?raw:SettingsStore.FieldText(typeof(RiskSettings).GetField(name).GetValue(current));}
        public bool Set(string name,string text)
        {
            var field=typeof(RiskSettings).GetField(name);if(field==null)throw new ArgumentException(Lang.T("Unknown risk field"));
            try
            {
                var value=SettingsStore.FieldValue(text,field.FieldType);
                var probe=new RiskSettings();field.SetValue(probe,value);
                if(!probe.Valid())throw new ArgumentException(Lang.T("Invalid risk value"));
                var next=current.Clone();field.SetValue(next,value);
                current=next;invalid.Remove(name);return true;
            }
            catch(FormatException){}catch(OverflowException){}catch(ArgumentException){}
            invalid[name]=text??"";return false;
        }
        public void Load(RiskSettings preset){current=preset.Clone();} // Explicit template load keeps unresolved typed errors visible.
        public RiskSettings Build(bool localTrail)
        {
            if(HasErrors)throw new ArgumentException(Lang.T("Correct the highlighted risk values before Apply."));
            var next=current.Clone();next.LocalTrail=localTrail;next.BaselineSet=true;
            if(!next.Valid())throw new ArgumentException(Lang.T("Every number must be finite; ratio / max quantity positive; monetary guards nonnegative."));
            if(next.LocalTrail&&(next.HighWater<=0||next.TrailDistance<=0))throw new ArgumentException(Lang.T("Supply positive confirmed high-water and trail distance first."));
            return next;
        }
    }
    public static class DivergenceChoice
    {
        public static bool IsValid(string value){return value=="Notify"||value=="FlattenOnce";}
    }
    public sealed class RiskLatch
    {
        public bool Locked;
        public string Reason = "";
    }
    public sealed class SavedAccount
    {
        public string Name = "";
        public RiskSettings Settings = new RiskSettings();
        public RiskLatch Latch = new RiskLatch();
    }
    public sealed class OcoMap
    {
        readonly Dictionary<string, string> ids = new Dictionary<string, string>();
        public string Get(string account, string leaderOco)
        {
            if (string.IsNullOrEmpty(leaderOco)) return "";
            string key = account.Length + ":" + account + leaderOco, id;
            if (!ids.TryGetValue(key, out id)) ids[key] = id = "KTC" + Guid.NewGuid().ToString("N");
            return id;
        }
    }
    public static class SettingsStore
    {
        static readonly object IoGate = new object();
        internal static string FieldText(object value)
        {
            if(!(value is double))return Convert.ToString(value,CultureInfo.InvariantCulture);
            double number=(double)value;
            // Framework's general format loses precision and its zero format loses the sign.
            return number==0&&BitConverter.DoubleToInt64Bits(number)<0?"-0":number.ToString("R",CultureInfo.InvariantCulture);
        }
        internal static object FieldValue(string text,Type type)
        {
            if(type!=typeof(double))return Convert.ChangeType(text,type,CultureInfo.InvariantCulture);
            // Float permits invariant decimals/exponents, but never grouping commas (0,125 != 125).
            double number=double.Parse(text,NumberStyles.Float,CultureInfo.InvariantCulture);
            return number==0&&text.TrimStart().StartsWith("-",StringComparison.Ordinal)?BitConverter.Int64BitsToDouble(long.MinValue):number;
        }
        public static XElement ToXml(IEnumerable<SavedAccount> rows, string instrument, string zone, int resetHour)
        {
            return new XElement("KINFX", new XAttribute("version",2), new XAttribute("instrument",instrument), new XAttribute("zone",zone), new XAttribute("resetHour",resetHour),
                rows.Select(r => new XElement("Account", new XAttribute("name",r.Name), new XAttribute("locked",r.Latch.Locked),new XAttribute("reason",r.Latch.Reason ?? ""),
                    typeof(RiskSettings).GetFields().Select(f => new XAttribute(f.Name, FieldText(f.GetValue(r.Settings)))))));
        }
        public static List<SavedAccount> FromXml(XElement xml)
        {
            if (xml == null || xml.Name != "KINFX" || (int?)xml.Attribute("version") != 2) throw new InvalidDataException(Lang.T("Unsupported settings version"));
            var result = new List<SavedAccount>();
            foreach (XElement e in xml.Elements("Account"))
            {
                var r = new SavedAccount { Name=(string)e.Attribute("name") ?? "" };
                foreach (var f in typeof(RiskSettings).GetFields())
                {
                    XAttribute a=e.Attribute(f.Name); if (a != null) f.SetValue(r.Settings, FieldValue(a.Value,f.FieldType));
                }
                r.Latch.Locked=(bool?)e.Attribute("locked") ?? false; r.Latch.Reason=(string)e.Attribute("reason") ?? Lang.T("Persisted lock");
                if (!r.Settings.Valid()) throw new InvalidDataException(Lang.T("Invalid finite settings: ") + r.Name);
                result.Add(r);
            }
            if (result.Select(r=>r.Name).Distinct().Count()!=result.Count) throw new InvalidDataException(Lang.T("Duplicate account settings"));
            return result;
        }
        public static Dictionary<string,string> ReadLatches(string path)
        {
            lock(IoGate)
            {
                if(!File.Exists(path))return new Dictionary<string,string>();
                XElement root=XElement.Load(path);if(root.Name!="Latches")throw new InvalidDataException(Lang.T("Invalid latch journal"));
                return root.Elements("Lock").ToDictionary(e=>(string)e.Attribute("account"),e=>(string)e.Attribute("reason"));
            }
        }
        public static void SaveLatch(string path,string account,string reason)
        {
            lock(IoGate) {var all=ReadLatches(path);all[account]=reason;AtomicSave(path,new XElement("Latches",all.Select(kv=>new XElement("Lock",new XAttribute("account",kv.Key),new XAttribute("reason",kv.Value)))));}
        }
        public static void RemoveLatch(string path,string account)
        {
            lock(IoGate) {var all=ReadLatches(path);all.Remove(account);AtomicSave(path,new XElement("Latches",all.Select(kv=>new XElement("Lock",new XAttribute("account",kv.Key),new XAttribute("reason",kv.Value)))));}
        }
        // Same temp-file-then-replace pattern as AtomicSave, for a small text file (one line per entry).
        public static void AtomicWriteLines(string path, string[] lines)
        {
            lock (IoGate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                try
                {
                    using (var f=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)) using (var w=new StreamWriter(f,new System.Text.UTF8Encoding(false))) { foreach(var l in lines) w.WriteLine(l); w.Flush(); f.Flush(true); }
                    if (File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
        }
        public static void AtomicSave(string path, XElement xml)
        {
            lock (IoGate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                try
                {
                    using (var f=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { xml.Save(f); f.Flush(true); }
                    if (File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
        }
    }
    public sealed class SerialWorker : IDisposable
    {
        readonly Queue<Action> queue = new Queue<Action>();
        readonly object gate = new object();
        readonly Action<Exception> error;
        readonly Thread thread;
        bool stopping;
        public SerialWorker(Action<Exception> errorHandler)
        { error=errorHandler; thread=new Thread(Run) { IsBackground=true, Name="KINFX serial copier" }; thread.Start(); }
        // Returns true only when the action was accepted into the mailbox. A refusal after Dispose is reported, never silent.
        // Everything accepted before Dispose still runs: Run() drains the queue before the thread exits.
        public bool Post(Action action) { lock(gate) { if(stopping)return false; queue.Enqueue(action); Monitor.Pulse(gate); return true; } }
        void Run()
        {
            while(true)
            {
                Action action;
                lock(gate) { while(queue.Count==0 && !stopping) Monitor.Wait(gate); if(queue.Count==0 && stopping)return; action=queue.Dequeue(); }
                try { action(); } catch(Exception ex) { error(ex); }
            }
        }
        public bool IsCurrentThread {get{return Thread.CurrentThread==thread;}}
        public void Dispose() { lock(gate) { stopping=true; Monitor.PulseAll(gate); } if(Thread.CurrentThread!=thread) thread.Join(3000); }
    }
    public static class SessionOwnership
    {
        static readonly Dictionary<string,object> owners=new Dictionary<string,object>();
        public static bool Acquire(IEnumerable<string> keys, object owner)
        {
            lock(owners) { string[] all=keys.Distinct().ToArray(); if(all.Any(k=>owners.ContainsKey(k) && owners[k]!=owner)) return false; foreach(string k in all)owners[k]=owner; return true; }
        }
        public static void Release(object owner) { lock(owners) { foreach(string key in owners.Where(x=>x.Value==owner).Select(x=>x.Key).ToArray()) owners.Remove(key); } }
    }
    public sealed class CopyLedger
    {
        readonly HashSet<string> seen = new HashSet<string>();
        public bool First(string id) { return seen.Add(id); }
        public static bool AllowNew(bool entriesArmed, bool exit) { return entriesArmed || exit; }
        // otherReserved includes all independent exits; each OCO group consumes its largest member only.
        public static int ExitBudget(int desired, int position, int otherReserved) { return Math.Max(0,Math.Min(desired,position-otherReserved)); }
    }
    public sealed class Preset
    {
        public readonly string Firm, Plan, Size, RawTarget, RawDrawdown, RawLoss, DrawdownType, RawConsistency, RawContracts;
        public Preset(string firm,string plan,string size,string target,string dd,string dll,string type,string consistency,string contracts)
        { Firm=firm; Plan=plan; Size=size; RawTarget=target; RawDrawdown=dd; RawLoss=dll; DrawdownType=type; RawConsistency=consistency; RawContracts=contracts; }
        public string Key { get { return Firm+" / "+Plan+" / "+Size; } }
        public string Notes { get { return Key+"\nSource PT "+RawTarget+"; DD "+RawDrawdown+" ("+DrawdownType+"); DLL "+RawLoss+"; mini cap "+RawContracts+"; consistency "+RawConsistency+
            "\nSource: propfirmsdrama.com/full-comparison/ — 2026-10-02; TPT 2026-08-31. Verify current firm rules. Funded/eval stages differ; scaling may lower cap.\nDD amount is NOT an absolute equity floor. Confirm your floor manually. Optional/soft DLL stays off unless chosen. Consistency is advisory, needs supplied history."; } }
        static double Amount(string raw) { double value; return raw.StartsWith("$") && double.TryParse(raw.Substring(1),NumberStyles.Number,CultureInfo.InvariantCulture,out value) ? value : 0; }
        public RiskSettings Apply(RiskSettings current, string instrumentRoot, bool optionalLoss)
        {
            RiskSettings s=current.Clone(); s.Preset=Key; s.DrawdownType=DrawdownType; s.TrailDistance=Amount(RawDrawdown); s.LocalTrail=false;
            // Evaluation PT is account-lifetime metadata, never the user's DAILY target.
            // Preserve the explicit local target when loading a firm template.
            s.Target=current.Target;
            s.Loss=Amount(RawLoss);
            if (Firm=="Topstep" || !RawLoss.StartsWith("$")) s.Loss=0;
            if (optionalLoss) { int i=RawLoss.IndexOf('$'); if(i>=0) { string n=new string(RawLoss.Substring(i+1).TakeWhile(c=>char.IsDigit(c)||c==','||c=='.').ToArray()); double v; if(double.TryParse(n,NumberStyles.Number,CultureInfo.InvariantCulture,out v)) s.Loss=v; } }
            int cap; int multiple= new[]{"MES","MNQ","M2K","MYM"}.Contains(instrumentRoot) ? 10 : new[]{"ES","NQ","RTY","YM"}.Contains(instrumentRoot) ? 1 : 0;
            if (multiple>0 && int.TryParse(RawContracts,out cap)) s.MaxContracts=cap*multiple;
            int pct=RawConsistency.IndexOf('%'); s.Consistency=0;
            if(pct>0) { string n=new string(RawConsistency.Substring(0,pct).Reverse().TakeWhile(char.IsDigit).Reverse().ToArray()); double v; if(double.TryParse(n,out v))s.Consistency=v; }
            return s;
        }
    }
    public static class PresetCatalog
    {
        public static readonly List<Preset> All = new List<Preset>
        {
            new Preset("Tradeify","Growth","25K","$1,500","$1,000","$600","EOD Trail","無","1"),
            new Preset("Tradeify","Growth","50K","$3,000","$2,000","$1,250","EOD Trail","無","4"),
            new Preset("Tradeify","Growth","100K","$6,000","$3,500","$2,500","EOD Trail","無","8"),
            new Preset("Tradeify","Growth","150K","$9,000","$5,000","$3,750","EOD Trail","無","12"),
            new Preset("Tradeify","Select Eval","25K","$1,500","$1,000","無","EOD Trail","40%","1"),
            new Preset("Tradeify","Select Eval","50K","$3,000","$2,000","無","EOD Trail","40%","4"),
            new Preset("Tradeify","Select Eval","100K","$6,000","$3,000","無","EOD Trail","40%","8"),
            new Preset("Tradeify","Select Eval","150K","$9,000","$4,500","無","EOD Trail","40%","12"),
            new Preset("Tradeify","Select Eval","300K","$14,000","$6,000","無","EOD Trail","40%","—"),
            new Preset("Tradeify","→ Select Flex","50K","$3,000","$2,000","無","EOD Trail","Funded無","4"),
            new Preset("Tradeify","→ Select Daily","50K","$3,000","$2,000","$1,000 Soft","EOD Trail","Funded無","4"),
            new Preset("Tradeify","→ Select Flex","300K","$14,000","$6,000","無","EOD Trail","Funded無","—"),
            new Preset("Tradeify","→ Select Daily","300K","$14,000","$5,000","$2,500 Soft","EOD Trail","Funded無","—"),
            new Preset("Tradeify","Lightning","25K","$1,500","$1,000","無","EOD Trail","—","1"),
            new Preset("Tradeify","Lightning","50K","$3,000","$2,000","$1,250","EOD Trail","—","4"),
            new Preset("Tradeify","Lightning","100K","$6,000","$4,000","$2,500","EOD Trail","—","8"),
            new Preset("Tradeify","Lightning","150K","$7,500","$5,250","$3,000","EOD Trail","—","12"),
            new Preset("Lucid","Pro","25K","$1,250","$1,000","可選","EOD Trail","40%","2"),
            new Preset("Lucid","Pro","50K","$3,000","$2,000","可選 $1,200","EOD Trail","40%","4"),
            new Preset("Lucid","Pro","100K","$6,000","$3,000","可選 $1,800","EOD Trail","40%","6"),
            new Preset("Lucid","Pro","150K","$9,000","$4,500","可選 $2,700","EOD Trail","40%","10"),
            new Preset("Lucid","Flex","25K","$1,250","$1,000","可選","EOD Trail","50%","2"),
            new Preset("Lucid","Flex","50K","$3,000","$2,000","可選","EOD Trail","50%","4"),
            new Preset("Lucid","Flex","100K","$6,000","$3,000","可選","EOD Trail","50%","6"),
            new Preset("Lucid","Flex","150K","$9,000","$4,500","可選","EOD Trail","50%","10"),
            new Preset("Lucid","Daily","25K","$1,250","$1,000","可選 $600","EOD／Intraday 自選","50%","2"),
            new Preset("Lucid","Daily","50K","$3,000","$2,000","可選 $1,200","EOD／Intraday 自選","50%","4"),
            new Preset("Lucid","Daily","100K","$6,000","$3,000","可選 $1,800","EOD／Intraday 自選","50%","6"),
            new Preset("Lucid","Daily","150K","$9,000","$4,500","可選 $2,700","EOD／Intraday 自選","50%","10"),
            new Preset("Lucid","Direct","25K","$1,250","$1,000","無","EOD Trail","20%","2"),
            new Preset("Lucid","Direct","50K","$3,000","$2,000","$1,200","EOD Trail","20%","4"),
            new Preset("Lucid","Direct","100K","$6,000","$3,500","$2,100","EOD Trail","20%","6"),
            new Preset("Lucid","Direct","150K","$9,000","$5,000","$3,000","EOD Trail","20%","10"),
            new Preset("Lucid","Maxx","25K","$1,250","$1,000","無","EOD Trail","40%","—"),
            new Preset("Lucid","Maxx","50K","$3,000","$2,000","無","EOD Trail","40%","—"),
            new Preset("Lucid","Maxx","100K","$6,000","$3,000","無","EOD Trail","40%","—"),
            new Preset("Lucid","Maxx","150K","$9,000","$4,500","無","EOD Trail","40%","—"),
            new Preset("Apex","EOD NoFee","25K","$1,500","$1,000","$500","EOD Trail","無","4"),
            new Preset("Apex","EOD NoFee","50K","$3,000","$2,000","$1,000","EOD Trail","無","6"),
            new Preset("Apex","EOD NoFee","100K","$6,000","$3,000","$1,500","EOD Trail","無","8"),
            new Preset("Apex","EOD NoFee","150K","$9,000","$4,000","$2,000","EOD Trail","無","12"),
            new Preset("Apex","Intraday NoFee","25K","$1,500","$1,000","無","Intraday Trail","無","4"),
            new Preset("Apex","Intraday NoFee","50K","$3,000","$2,000","無","Intraday Trail","無","6"),
            new Preset("Apex","Intraday NoFee","100K","$6,000","$3,000","無","Intraday Trail","無","8"),
            new Preset("Apex","Intraday NoFee","150K","$9,000","$4,000","無","Intraday Trail","無","12"),
            new Preset("Phidias","E2L OTP","25K","$1,500","$500","無","Static","無","2"),
            new Preset("Phidias","E2L OTP","50K","$2,500","$650","無","Static","無","5"),
            new Preset("Phidias","E2L OTP","100K","$3,500","$800","無","Static","無","7"),
            new Preset("Phidias","E2L OTP","150K","$4,500","$1,000","無","Static","無","9"),
            new Preset("Phidias","Fund OTP","50K","$4,000","$2,500","無","EOD Trail","CASH 30%","10"),
            new Preset("Phidias","Fund OTP","100K","$6,000","$3,000","無","EOD Trail","CASH 30%","14"),
            new Preset("Phidias","Fund OTP","150K","$9,000","$4,500","無","EOD Trail","CASH 30%","17"),
            new Preset("FTMO","Growth","50K","$3,000","$2,000","無","EOD Trail","40%","5"),
            new Preset("FTMO","Growth","100K","$6,000","$3,500","無","EOD Trail","40%","10"),
            new Preset("FTMO","Growth","150K","$9,000","$5,000","無","EOD Trail","40%","15"),
            new Preset("FTMO","Pro","50K","$3,000","$3,000","$1,000","EOD Trail","50%","5"),
            new Preset("FTMO","Pro","100K","$6,000","$4,500","$1,500","EOD Trail","50%","10"),
            new Preset("FTMO","Pro","150K","$9,000","$6,000","$2,000","EOD Trail","50%","15"),
            new Preset("Earn2Trade","Gauntlet Mini","50K","$3,000","$2,000","$1,100","EOD Trail","30%","6"),
            new Preset("Earn2Trade","Gauntlet Mini","100K","$6,000","$3,500","$2,200","EOD Trail","30%","12"),
            new Preset("Earn2Trade","Gauntlet Mini","150K","$9,000","$4,500","$3,300","EOD Trail","30%","15"),
            new Preset("Earn2Trade","Gauntlet Mini","200K","$11,000","$6,000","$4,400","EOD Trail","30%","16"),
            new Preset("E8","Zero Starter","50K","$3,000","$1,500","無","EOD-Dynamic","40%","—"),
            new Preset("E8","Zero Starter","100K","$6,500","$3,000","無","EOD-Dynamic","40%","—"),
            new Preset("E8","Zero Starter","200K","$13,500","$6,000","無","EOD-Dynamic","40%","—"),
            new Preset("E8","Zero MAX","50K","$3,000","$1,500","無","EOD-Dynamic","40%","—"),
            new Preset("E8","Zero MAX","100K","$6,500","$3,000","無","EOD-Dynamic","40%","—"),
            new Preset("E8","Zero MAX","200K","$13,500","$6,000","無","EOD-Dynamic","40%","—"),
            new Preset("E8","Signature","25K","$1,500","$1,000","Pause $500","EOD Dynamic","35% payout","—"),
            new Preset("E8","Signature","50K","$3,000","$2,000","Pause $1,000","EOD Dynamic","35% payout","—"),
            new Preset("E8","Signature","100K","$6,000","$3,000","Pause $2,000","EOD Dynamic","35% payout","—"),
            new Preset("E8","Signature","150K","$9,000","$4,000","Pause $3,000","EOD Dynamic","35% payout","—"),
            new Preset("Topstep","Combine 標準","50K","$3,000","$2,000","$1,000","MLL","50% Eval","5"),
            new Preset("Topstep","Combine 標準","100K","$6,000","$3,000","$2,000","MLL","50% Eval","10"),
            new Preset("Topstep","Combine 標準","150K","$9,000","$4,500","$3,000","MLL","50% Eval","15"),
            new Preset("Topstep","XFA Standard","50K","$0","$2,000","可選","MLL","Funded無","5"),
            new Preset("Topstep","XFA Consistency","50K","$0","$2,000","可選","MLL","40% Funded","5"),
            new Preset("MFFU","Rapid","25K","$1,500","$1,000","無","EOD→Intraday**","50%","—"),
            new Preset("MFFU","Rapid","50K","$3,000","$2,000","無","EOD→Intraday**","50%","—"),
            new Preset("MFFU","Rapid","100K","$6,000","$3,000","無","EOD→Intraday**","50%","—"),
            new Preset("MFFU","Rapid","150K","$9,000","$4,500","無","EOD→Intraday**","50%","—"),
            new Preset("MFFU","Builder","50K","$3,000","$2,000","無","EOD Trail","—","—"),
            new Preset("Take Profit Trader","Test","50K","$3,000","$2,000","—","EOD Trail","—","6"),
        };
    }
    // Detects a changed account list. Pure: names only, no account / engine / dispatch access.
    public sealed class AccountSyncResult { public bool Changed; public string[] Added=new string[0]; public string[] Removed=new string[0]; }
    public enum AccountSyncAction { None, Reload, Notify, Defer }
    public static class AccountSync
    {
        // Decision only (pure). Copying always wins: the table of a live session is never rebuilt, the user is only told.
        // An in-progress edit is never discarded: the reload waits.
        public static AccountSyncAction Decide(AccountSyncResult diff,bool attached,bool editing)
        {
            if(diff==null||!diff.Changed)return AccountSyncAction.None;
            if(attached)return AccountSyncAction.Notify;
            return editing?AccountSyncAction.Defer:AccountSyncAction.Reload;
        }
        static System.Collections.Generic.HashSet<string> Names(System.Collections.Generic.IEnumerable<string> x)
        {
            var set=new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            if(x!=null)foreach(var n in x)if(!string.IsNullOrEmpty(n))set.Add(n);   // NinjaTrader account names are case-sensitive
            return set;
        }
        public static AccountSyncResult Diff(System.Collections.Generic.IEnumerable<string> current,System.Collections.Generic.IEnumerable<string> latest)
        {
            var a=Names(current);var b=Names(latest);
            var added=b.Where(n=>!a.Contains(n)).OrderBy(n=>n,StringComparer.Ordinal).ToArray();
            var removed=a.Where(n=>!b.Contains(n)).OrderBy(n=>n,StringComparer.Ordinal).ToArray();
            return new AccountSyncResult{Changed=added.Length+removed.Length>0,Added=added,Removed=removed};
        }
    }
    // Contract-name helper. A convenience only: Instrument.GetInstrument stays the final authority, and an explicit month is never silently rolled.
    // Front() answers only for roots whose expiry cycle is quarterly (a safe default); metals / energy have no safe default, so the user types the month.
    public static class ContractRoll
    {
        sealed class Spec { public int[] Months; public bool SafeFront; }
        static readonly int[] Quarterly=new[]{3,6,9,12};
        static readonly System.Collections.Generic.Dictionary<string,Spec> Roots=BuildRoots();
        static System.Collections.Generic.Dictionary<string,Spec> BuildRoots()
        {
            var d=new System.Collections.Generic.Dictionary<string,Spec>(StringComparer.Ordinal);
            foreach(var r in new[]{"ES","MES","NQ","MNQ","YM","MYM","RTY","M2K"})d[r]=new Spec{Months=Quarterly,SafeFront=true};
            foreach(var r in new[]{"GC","MGC"})d[r]=new Spec{Months=new[]{2,4,6,8,10,12},SafeFront=false};
            foreach(var r in new[]{"SI","SIL"})d[r]=new Spec{Months=new[]{3,5,7,9,12},SafeFront=false};
            foreach(var r in new[]{"CL","MCL"})d[r]=new Spec{Months=new[]{1,2,3,4,5,6,7,8,9,10,11,12},SafeFront=false};
            return d;
        }
        const string Codes="FGHJKMNQUVXZ";
        static string Format(string root,int month,int year){return root+" "+month.ToString("00",CultureInfo.InvariantCulture)+"-"+(year%100).ToString("00",CultureInfo.InvariantCulture);}
        static string Clean(string s){return string.IsNullOrWhiteSpace(s)?"":s.Trim().ToUpperInvariant();}
        public static string Front(string root,DateTime now)
        {
            Spec spec;var r=Clean(root);
            if(r.Length==0||!Roots.TryGetValue(r,out spec)||!spec.SafeFront)return null;
            bool estimated;return QuarterlyFront(r,now.Date,out estimated);
        }
        // The front quarterly contract = the newest contract whose roll date <= today. Known dates come from
        // InstrumentCatalog.QuarterlyRolls (read from NinjaTrader 8.1.8.3); outside that table the roll date is
        // estimated as the Monday (3rd Friday minus 4 days) of the PREVIOUS quarter's expiry week, and EstimatedFront
        // reports true so the UI can log that the date was estimated.
        static string QuarterlyFront(string root,DateTime day,out bool estimated)
        {
            estimated=false;
            var rows=InstrumentCatalog.QuarterlyRolls;
            string best=null;DateTime bestDate=DateTime.MinValue;
            for(int y=day.Year-1;y<=day.Year+2;y++)
                foreach(int m in Quarterly)
                {
                    DateTime roll;bool est;
                    var known=KnownRoll(rows,y,m);
                    if(known.HasValue){roll=known.Value;est=false;}
                    else{roll=EstimatedRollDate(y,m);est=true;}
                    if(roll<=day&&roll>bestDate){bestDate=roll;best=Format(root,m,y);estimated=est;}
                }
            return best;
        }
        static DateTime? KnownRoll(InstrumentCatalog.RollRow[] rows,int year,int month)
        {
            foreach(var row in rows)if(row.Year==year&&row.Month==month)return row.Date;
            return null;
        }
        static DateTime EstimatedRollDate(int year,int month)
        {
            int prevMonth=month-3,prevYear=year;
            if(prevMonth<=0){prevMonth+=12;prevYear--;}
            return ThirdFriday(prevYear,prevMonth).AddDays(-4);
        }
        static DateTime ThirdFriday(int year,int month)
        {
            var first=new DateTime(year,month,1);
            int offset=((int)DayOfWeek.Friday-(int)first.DayOfWeek+7)%7;
            return first.AddDays(offset+14);
        }
        // True when the front contract for a quarterly root was determined by the estimate formula, not the known table.
        public static bool EstimatedFront(string root,DateTime now)
        {
            Spec spec;var r=Clean(root);
            if(r.Length==0||!Roots.TryGetValue(r,out spec)||!spec.SafeFront)return false;
            bool estimated;QuarterlyFront(r,now.Date,out estimated);return estimated;
        }
        // Next `count` listed months for a root starting after the current month (picker for metals / energy; never the front).
        public static string[] NextMonths(string root,int count,DateTime now)
        {
            Spec spec;var r=Clean(root);
            if(r.Length==0||!Roots.TryGetValue(r,out spec))return new string[0];
            var list=new System.Collections.Generic.List<string>();
            var start=new DateTime(now.Year,now.Month,1);
            for(int add=1;add<=24&&list.Count<count;add++){var d=start.AddMonths(add);if(spec.Months.Contains(d.Month))list.Add(Format(r,d.Month,d.Year));}
            return list.ToArray();
        }
        static string Explicit(string root,int month,int year)
        {
            Spec spec;
            if(!Roots.TryGetValue(root,out spec)||month<1||month>12||!spec.Months.Contains(month)||year<2000||year>2099)return null;
            return Format(root,month,year);
        }
        public static string Normalize(string text,DateTime now)
        {
            var t=Clean(text);
            if(t.Length==0)return null;
            if(Roots.ContainsKey(t))return Front(t,now);
            var rx=System.Text.RegularExpressions.Regex.Match(t,@"^([A-Z0-9]+) (\d{2})-(\d{2})$");
            if(!rx.Success)rx=System.Text.RegularExpressions.Regex.Match(t,@"^([A-Z0-9]+?)(\d{2})(\d{2})$");
            if(rx.Success)return Explicit(rx.Groups[1].Value,int.Parse(rx.Groups[2].Value,CultureInfo.InvariantCulture),2000+int.Parse(rx.Groups[3].Value,CultureInfo.InvariantCulture));
            var code=System.Text.RegularExpressions.Regex.Match(t,@"^([A-Z0-9]+?)([FGHJKMNQUVXZ])(\d)$");
            if(code.Success)
            {
                int year=(now.Year/10)*10+int.Parse(code.Groups[3].Value,CultureInfo.InvariantCulture);
                if(year<now.Year)year+=10;
                return Explicit(code.Groups[1].Value,Codes.IndexOf(code.Groups[2].Value[0])+1,year);
            }
            return null;
        }
    }
    // Pure helpers shared by the dashboard and the offline tests. No Account, engine or UI access.
    public sealed class StartSummaryRow { public string Name=""; public double Ratio=1; }
    // Which confirmation windows the user chose not to see again. Stored one "skip=<kind>" per line in confirm-prefs.txt; unknown kinds and lines are ignored.
    // A window whose text carries a risk warning (an account in scope is not confirmed simulated) is ALWAYS shown, whatever was saved. Pure: no file access.
    // Which grid columns the user hid. One "hide=<grid>:<column key>" per line in columns.txt; unknown grids / keys / lines are ignored.
    // Safety columns (follow tick, state, Off, Flat, Qty, connection, Tgt, DLL ...) can never be hidden, whatever the file says. The account name can be
    // hidden (the Alias column identifies the row and its tooltip shows the full name; every confirmation window names the full account).
    // Defaults, Parse, Serialize and Visible do not touch a file or the UI. Load reads columns.txt: a missing or unreadable path is Defaults().
    public static class ColumnPrefs
    {
        public const string Copy="copy", Risk="risk";
        static readonly Dictionary<string,string[]> hideable=new Dictionary<string,string[]>(StringComparer.Ordinal)
        {
            {Copy,new[]{"Acct","Alias","Tgt","DLL","P/L","Bal","Drift"}},
            {Risk,new[]{"Acct","Alias","Grp","FlatLim","Max","P/L","CCY","To Tgt","Loss left","Preset","Cons"}},
        };
        // Hidden until the user shows them (user request 2026-10-15: the account name takes too much room; the Alias identifies the account,
        // every account gets an alias automatically and the Alias cell's tooltip shows the full name). "show=" in columns.txt reveals one.
        // Settings also hides Preset, Cons, CCY and P/L by default (P/L is on Monitor): with every column shown the Settings table needed
        // a horizontal scrollbar at the default 1160 px window (real NT 2026-10-17). The Columns button shows any of them again.
        static readonly string[] hiddenByDefault={Copy+":Acct",Risk+":Acct",Risk+":Preset",Risk+":Cons",Risk+":CCY",Risk+":P/L"};
        public static HashSet<string> Defaults(){return new HashSet<string>(hiddenByDefault,StringComparer.Ordinal);}
        // Missing, blank or unreadable columns.txt keeps the default hidden columns. A readable file is parsed, so show= can reveal one.
        public static HashSet<string> Load(string path){try{if(!string.IsNullOrEmpty(path)&&File.Exists(path))return Parse(File.ReadAllLines(path));}catch{}return Defaults();}
        // An alias for an account that has none, so the row stays identifiable with the account-name column hidden. Short names stay as they are (Sim101);
        // long ones keep their leading letters and the last 4 characters (FIRMAB..0001). Display only: the user can change it; it never selects an account.
        public static string DefaultAlias(string name)
        {
            if(string.IsNullOrEmpty(name))return "";
            if(name.Length<=12)return name;
            int n=0;while(n<name.Length&&n<6&&char.IsLetter(name[n]))n++;
            return name.Substring(0,n)+".."+name.Substring(name.Length-4);
        }
        public static string[] Hideable(string grid){string[] k;return grid!=null&&hideable.TryGetValue(grid,out k)?(string[])k.Clone():new string[0];}
        public static bool CanHide(string grid,string key){string[] k;return grid!=null&&key!=null&&hideable.TryGetValue(grid,out k)&&Array.IndexOf(k,key)>=0;}
        public static HashSet<string> Parse(IEnumerable<string> lines)
        {
            var h=Defaults();
            if(lines==null)return h;
            foreach(var raw in lines)
            {
                if(raw==null)continue; string t=raw.Trim(); if(t.Length==0||t.StartsWith("#"))continue;
                int eq=t.IndexOf('='); if(eq<0)continue; string verb=t.Substring(0,eq).Trim(); if(verb!="hide"&&verb!="show")continue;
                string v=t.Substring(eq+1).Trim(); int c=v.IndexOf(':'); if(c<0)continue;
                string grid=v.Substring(0,c).Trim(), key=v.Substring(c+1).Trim();
                if(!CanHide(grid,key))continue;
                if(verb=="hide")h.Add(grid+":"+key);else h.Remove(grid+":"+key);
            }
            return h;
        }
        public static string[] Serialize(HashSet<string> hidden)
        {
            var list=new List<string>{"# KINFX Trade Copier: columns hidden by the user (right-click a column header, or the Columns button). Delete a line (or this file) to show it again."};
            foreach(var grid in new[]{Copy,Risk})foreach(var k in hideable[grid])
            {
                string id=grid+":"+k;bool hide=hidden!=null&&hidden.Contains(id);
                if(hide)list.Add("hide="+id);else if(Array.IndexOf(hiddenByDefault,id)>=0)list.Add("show="+id);
            }
            return list.ToArray();
        }
        public static bool Visible(string grid,string key,HashSet<string> hidden){return !CanHide(grid,key)||hidden==null||!hidden.Contains(grid+":"+key);}
    }
    public static class ConfirmPrefs
    {
        // Unlock is deliberately not here: it is the only explicit unlock path, so its question is always asked.
        public const string Start="start", FlatOne="flatOne", Flatten="flatten";
        public static readonly string[] Kinds={Start,FlatOne,Flatten};
        public static bool Known(string kind){return kind!=null&&Array.IndexOf(Kinds,kind)>=0;}
        // The switch is chosen by the BUTTON pressed, never by how many rows are in scope: "Flatten selected" on one highlighted row is still "Flatten selected / all".
        public static string KindFor(string command){return command=="flatOne"?FlatOne:command=="start"?Start:Flatten;}
        // English label of a switch (as shown on the Settings tab), so log lines translate instead of printing internal codes.
        public static string Label(string kind){return kind==Start?"Start":kind==FlatOne?"Flat (row)":kind==Flatten?"Flatten selected / all":kind;}
        public static HashSet<string> Parse(IEnumerable<string> lines)
        {
            var h=new HashSet<string>(StringComparer.Ordinal);
            if(lines==null)return h;
            foreach(var raw in lines)
            {
                if(raw==null)continue; string t=raw.Trim(); if(t.Length==0||t.StartsWith("#"))continue;
                int eq=t.IndexOf('='); if(eq<0)continue;
                string key=t.Substring(0,eq).Trim(), kind=t.Substring(eq+1).Trim();
                if(key=="skip"&&Known(kind))h.Add(kind);
            }
            return h;
        }
        public static string[] Serialize(HashSet<string> skipped)
        {
            var list=new List<string>{"# KINFX Trade Copier: confirmation windows turned off by the user. Delete a line (or this file) to be asked again."};
            foreach(var k in Kinds)if(skipped!=null&&skipped.Contains(k))list.Add("skip="+k);
            return list.ToArray();
        }
        public static bool MustAsk(string kind,HashSet<string> skipped,bool riskWarning)
        {
            if(riskWarning||!Known(kind)||skipped==null)return true;
            return !skipped.Contains(kind);
        }
    }
    public static class DashboardChoices
    {
        // Text shown before Start: who leads, on what, and for each follower what the engine WILL do for a sample leader order.
        // Uses RiskPolicy.Quantity, the same function the engine uses, so the preview cannot disagree with the behaviour.
        public static string StartSummary(string leader,string instrument,int sampleLeaderQty,StartSummaryRow[] followers,bool dryRun)
        {
            var sb=new System.Text.StringBuilder();
            if(dryRun)sb.Append(Lang.T("*** DRY RUN: nothing will be sent; decisions are only logged. ***")).Append("\n\n");
            sb.Append(Lang.F("Leader: {0}   Instrument: {1}",leader??Lang.T("(none)"),instrument??Lang.T("(none)"))).Append("\n");
            if(followers==null||followers.Length==0){sb.Append(Lang.T("No followers selected."));return sb.ToString();}
            sb.Append(Lang.F(followers.Length==1?"{0} follower:":"{0} followers:",followers.Length)).Append("\n");
            foreach(var f in followers)
            {
                string name=f==null?"?":f.Name;
                if(f==null||!RiskPolicy.Finite(f.Ratio)||f.Ratio<=0){sb.Append(Lang.F("  {0}  ratio invalid — will NOT copy",name)).Append("\n");continue;}
                int q=RiskPolicy.Quantity(sampleLeaderQty,f.Ratio);
                string ratioText=f.Ratio.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
                sb.Append(Lang.F(q==1?"  {0}  ratio {1}  → {2} lot when the leader trades {3}":"  {0}  ratio {1}  → {2} lots when the leader trades {3}",name,ratioText,q,sampleLeaderQty));
                if(q==0)sb.Append(" ").Append(Lang.T("(would NOT copy)"));
                sb.Append("\n");
            }
            return sb.ToString().TrimEnd('\n');
        }
        // Default Leader when nothing was chosen before: the stock Sim101 account if present, else the first listed. Never returns null for a non-empty list.
        public static string DefaultLeader(IEnumerable<string> names)
        {
            string first=null;
            if(names!=null)foreach(var n in names){if(first==null)first=n;if(string.Equals(n,"Sim101",StringComparison.Ordinal))return n;}
            return first;
        }
        // One line of sim-allow.txt = exact account name, optionally "= alias". Blank lines and # comments are ignored.
        public static bool ParseAllowLine(string line,out string name,out string alias)
        {
            name="";alias="";if(line==null)return false;
            string t=line.Trim();if(t.Length==0||t.StartsWith("#"))return false;
            int eq=t.IndexOf('=');
            if(eq<0){name=t;return true;}
            name=t.Substring(0,eq).Trim();alias=t.Substring(eq+1).Trim();
            return name.Length>0;
        }
    }
    // PURE: instrument picker catalog + the known CME quarterly rollover table. No UI, no accounts, no file access.
    public static class InstrumentCatalog
    {
        // UI / test clock. Tests pin it to a fixed date; the picker's front month and the header hint follow it.
        internal static Func<DateTime> Now=()=>DateTime.Now;

        // One row of the CME equity-index rollover table read from NinjaTrader 8.1.8.3 (MasterInstruments.UserData).
        // "Contract X becomes the front contract on Date". Same for every quarterly index root.
        public sealed class RollRow { public DateTime Date; public int Month; public int Year; }
        public static readonly RollRow[] QuarterlyRolls=new RollRow[]
        {
            new RollRow{Date=new DateTime(2026,6,12),Month=9,Year=2026},
            new RollRow{Date=new DateTime(2026,9,14),Month=12,Year=2026},
            new RollRow{Date=new DateTime(2026,12,14),Month=3,Year=2027},
            new RollRow{Date=new DateTime(2027,3,9),Month=6,Year=2027},
            new RollRow{Date=new DateTime(2027,6,7),Month=9,Year=2027},
        };

        // Picker entries. Header rows have Root==null; contract rows carry the full text to write (Front) and a description.
        public sealed class Entry { public string Root; public string Front; public string Description; public bool Header; public bool ChooseMonth; }

        public sealed class RootInfo { public string Root; public string Group; public string Description; }
        public static readonly RootInfo[] Roots=new RootInfo[]
        {
            new RootInfo{Root="MNQ",Group="Equity index",Description="Micro Nasdaq-100"},
            new RootInfo{Root="NQ",Group="Equity index",Description="Nasdaq-100"},
            new RootInfo{Root="MES",Group="Equity index",Description="Micro S&P 500"},
            new RootInfo{Root="ES",Group="Equity index",Description="S&P 500"},
            new RootInfo{Root="MYM",Group="Equity index",Description="Micro Dow"},
            new RootInfo{Root="YM",Group="Equity index",Description="Dow"},
            new RootInfo{Root="M2K",Group="Equity index",Description="Micro Russell 2000"},
            new RootInfo{Root="RTY",Group="Equity index",Description="Russell 2000"},
            new RootInfo{Root="MGC",Group="Metals",Description="Micro Gold"},
            new RootInfo{Root="GC",Group="Metals",Description="Gold"},
            new RootInfo{Root="SIL",Group="Metals",Description="Micro Silver"},
            new RootInfo{Root="SI",Group="Metals",Description="Silver"},
            new RootInfo{Root="MCL",Group="Energy",Description="Micro Crude Oil"},
            new RootInfo{Root="CL",Group="Energy",Description="Crude Oil"},
        };

        public static bool IsKnown(string root)
        {
            var r=(root??"").Trim().ToUpperInvariant();
            foreach(var info in Roots)if(info.Root==r)return true;
            return false;
        }

        static RootInfo Find(string root)
        {
            var r=(root??"").Trim().ToUpperInvariant();
            foreach(var info in Roots)if(info.Root==r)return info;
            return null;
        }

        // The standard grouped list (headers + every root), no recent section.
        public static Entry[] Items { get { return Build(new string[0]); } }

        // Grouped list with the most-recently-picked roots first (validated, deduplicated, max 5), under a "Recent" header.
        public static Entry[] Build(System.Collections.Generic.IEnumerable<string> recentRoots)
        {
            var now=Now();
            var list=new System.Collections.Generic.List<Entry>();
            var recent=new System.Collections.Generic.List<string>();
            if(recentRoots!=null)
                foreach(var raw in recentRoots)
                {
                    var root=(raw??"").Trim().ToUpperInvariant();
                    if(root.Length==0||!IsKnown(root)||recent.Contains(root))continue;
                    recent.Add(root);
                    if(recent.Count>=5)break;
                }
            if(recent.Count>0)
            {
                list.Add(new Entry{Header=true,Description="Recent"});
                foreach(var root in recent)AddRoot(list,root,now);
            }
            string group=null;
            foreach(var info in Roots)
            {
                if(info.Group!=group){list.Add(new Entry{Header=true,Description=info.Group});group=info.Group;}
                AddRoot(list,info.Root,now);
            }
            return list.ToArray();
        }

        static void AddRoot(System.Collections.Generic.List<Entry> list,string root,DateTime now)
        {
            var info=Find(root);if(info==null)return;
            var front=ContractRoll.Front(root,now);
            if(front!=null){list.Add(new Entry{Root=root,Front=front,Description=info.Description});return;}
            foreach(var month in ContractRoll.NextMonths(root,2,now))
                list.Add(new Entry{Root=root,Front=month,Description=info.Description,ChooseMonth=true});
        }

        // True when the front contract for a quarterly root came from the estimate fallback, not the known table.
        public static bool IsEstimated(string root,DateTime now){return ContractRoll.EstimatedFront(root,now);}
    }
    // PURE: tested NinjaTrader version vs the running one. A mismatch is a warning, never a block.
    public static class NtVersion
    {
        public const string Tested="8.1.8.3";
        // "" when the running build matches the tested one; otherwise a translated warning naming both.
        public static string Notice(string actual)
        {
            var a=(actual??"").Trim();
            if(a.Length==0||a==Tested)return "";
            return Lang.F("NinjaTrader {0} is not the tested version ({1}). It may still work; report problems.",a,Tested);
        }
    }
    // Monitor-page summary card math. Pure: no account, engine or UI access.
    public static class MonitorSummary
    {
        // Lots the engine itself could not protect, parsed from a lock reason like "… 2 lots UNPROTECTED …". 0 when absent.
        public static int UnprotectedLots(string reason)
        {
            if(string.IsNullOrEmpty(reason))return 0;
            int total=0;
            foreach(System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(reason,@"(\d+)\s+lot(s)?\s+UNPROTECTED",System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {int n;if(int.TryParse(m.Groups[1].Value,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out n))total+=n;}
            return total;
        }
        // Sum of the finite values; NaN / Infinity contribute nothing.
        public static double SumFinite(System.Collections.Generic.IEnumerable<double> values)
        {
            double sum=0;if(values==null)return 0;
            foreach(double v in values)if(RiskPolicy.Finite(v))sum+=v;
            return sum;
        }
        // Money for display: currency symbol in front ("-$1,234.50"), never the NinjaTrader enum name ("UsDollar").
        // Accepts the Currency enum name or an ISO code; unknown currencies fall back to "CODE 1,234.50" so nothing is mislabelled as dollars.
        public static string CurrencySymbol(string currency)
        {
            string c=(currency??"").Trim();
            switch(c)
            {
                case "UsDollar":case "USD":return "$";
                case "Euro":case "EUR":return "\u20AC";
                case "BritishPound":case "GBP":return "\u00A3";
                case "JapaneseYen":case "JPY":return "\u00A5";
                case "AustralianDollar":case "AUD":return "A$";
                case "CanadianDollar":case "CAD":return "C$";
                case "HongKongDollar":case "HKD":return "HK$";
                case "SingaporeDollar":case "SGD":return "S$";
                case "NewZealandDollar":case "NZD":return "NZ$";
                case "TaiwanNewDollar":case "TWD":return "NT$";
                default:return null;
            }
        }
        // NinjaTrader Currency enum name -> ISO code for currencies without a symbol above (unknown names are shown as they are).
        static readonly string[] currencyNames={"AustralianDollar","BritishPound","CanadianDollar","Euro","HongKongDollar","JapaneseYen","SwissFranc","UsDollar","KoreanWon","IndianRupee","SwedishKrona","MexicanPeso","BrazilianReal","SingaporeDollar","ChinaYuan","MalaysiaRinggit","ThailandBaht","TaiwanNewDollar","NewZealandDollar","SouthAfricanRand","CzechRepublicKoruna","NorwayKrone","DenmarkKrone","HungaryForint","PolandZloty","TurkeyLira","IsraeliShekel","RussiaRuble","SaudiRiyal"};
        static readonly string[] currencyCodes={"AUD","GBP","CAD","EUR","HKD","JPY","CHF","USD","KRW","INR","SEK","MXN","BRL","SGD","CNY","MYR","THB","TWD","NZD","ZAR","CZK","NOK","DKK","HUF","PLN","TRY","ILS","RUB","SAR"};
        public static string IsoCode(string currency){string c=(currency??"").Trim();int i=Array.IndexOf(currencyNames,c);return i>=0?currencyCodes[i]:c;}
        public static string Money(double value,string currency)
        {
            if(double.IsNaN(value)||double.IsInfinity(value))return "\u2014";
            string amount=Math.Abs(value).ToString("N2",System.Globalization.CultureInfo.InvariantCulture);
            if(amount=="0.00")value=0;
            string sign=value<0?"-":"";
            string sym=CurrencySymbol(currency);
            if(sym!=null)return sign+sym+amount;
            string code=IsoCode(currency);
            return code.Length==0?sign+amount:code+" "+sign+amount;
        }
        // The one currency when every non-empty one agrees; null when mixed (the card then shows "Mixed CCY").
        public static string SingleCurrency(System.Collections.Generic.IEnumerable<string> currencies)
        {
            string found=null;
            if(currencies!=null)foreach(string c in currencies){string t=(c??"").Trim();if(t.Length==0)continue;if(found==null)found=t;else if(!string.Equals(found,t,StringComparison.Ordinal))return null;}
            return found;
        }
    }
    // ---- WP3: log model, classifier and export. Pure: no account, engine or UI access. ----
    // One displayed log line. Level is one of Info / Order / Warn / Lock / Error. Text keeps the raw English
    // message (translated only when drawn, so history follows the picked language). LevelText / MessageText /
    // CategoryText are the translated display strings (Lang lives in the PURE region, so these stay pure).
    public sealed class LogLine
    {
        public DateTime Time { get; set; }
        public string Level { get; set; }
        public string Account { get; set; }
        public string Category { get; set; }
        public string Text { get; set; }
        public string LevelText { get { return Lang.T(Level); } }
        public string CategoryText { get { return Lang.T(Category); } }
        public string MessageText { get { return Lang.Show(Text); } }
    }
    // Classifies a free English engine/UI log message into a LogLine. Rules are case-sensitive substrings,
    // first match wins in this order: Lock, Error, Order, Warn, else Info. Account is the longest account name
    // that occurs in the message (ordinal), else "". Category: Lock for lock lines, else Order, Session, Settings,
    // Accounts, General.
    public static class LogClassifier
    {
        // Daily log text grouped by each line's own date: file name "kinfx-yyyyMMdd.log" -> text, in time order of first appearance. Pure.
        public static List<KeyValuePair<string,string>> DailyFiles(IEnumerable<LogLine> lines)
        {
            var order=new List<string>();var text=new Dictionary<string,System.Text.StringBuilder>();
            if(lines!=null)foreach(var l in lines)
            {
                if(l==null)continue;
                string name="kinfx-"+l.Time.ToString("yyyyMMdd",CultureInfo.InvariantCulture)+".log";
                System.Text.StringBuilder sb;if(!text.TryGetValue(name,out sb)){sb=new System.Text.StringBuilder();text[name]=sb;order.Add(name);}
                sb.Append(l.Time.ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture)).Append("  ").Append(l.Level).Append("  ").Append(l.Text).Append(Environment.NewLine);
            }
            return order.Select(n=>new KeyValuePair<string,string>(n,text[n].ToString())).ToList();
        }
        public static readonly string[] Levels = { "Info", "Order", "Warn", "Lock", "Error" };
        public static LogLine Classify(string message, IEnumerable<string> accountNames)
        {
            string msg = message ?? "";
            string level = "Info";
            if (IsLock(msg)) level = "Lock";
            else if (IsError(msg)) level = "Error";
            else if (IsOrder(msg)) level = "Order";
            else if (IsWarn(msg)) level = "Warn";
            string account = Account(msg, accountNames);
            string category = level == "Lock" ? "Lock" : Category(msg, level);
            return new LogLine { Level = level, Account = account, Category = category, Text = msg };
        }
        static bool StartsWithAny(string s, params string[] prefixes)
        {
            foreach (var p in prefixes) if (p != null && s.StartsWith(p, StringComparison.Ordinal)) return true;
            return false;
        }
        static bool IsLock(string s) { return s.IndexOf(" LOCK: ", StringComparison.Ordinal) >= 0 || (s.IndexOf("LOCK", StringComparison.Ordinal) >= 0 && s.IndexOf("Coverage not established", StringComparison.Ordinal) >= 0); }
        static bool IsError(string s) { return StartsWithAny(s, "ENGINE ERROR", "PROVIDER DISPATCH ERROR", "PROVIDER COMPLETION ERROR", "PERSISTENCE ERROR", "DURABLE LATCH WRITE FAILED", "FLATTEN FAILED") || s.IndexOf("UI action failed", StringComparison.Ordinal) >= 0; }
        static bool IsOrder(string s) { return StartsWithAny(s, "COPY ", "DRY RUN would send", "Flatten request", "Flat requested"); }
        static bool IsWarn(string s) { return s.IndexOf("refused", StringComparison.Ordinal) >= 0 || s.IndexOf("blocked", StringComparison.Ordinal) >= 0 || s.IndexOf("BLOCKED", StringComparison.Ordinal) >= 0 || s.IndexOf("Refused", StringComparison.Ordinal) >= 0 || s.IndexOf("not recognised", StringComparison.Ordinal) >= 0 || s.IndexOf("not available", StringComparison.Ordinal) >= 0 || s.IndexOf("Trading-day boundary", StringComparison.Ordinal) >= 0 || s.IndexOf("Confirmation skipped", StringComparison.Ordinal) >= 0 || s.IndexOf("NOT saved", StringComparison.Ordinal) >= 0; }
        static string Account(string s, IEnumerable<string> names)
        {
            if (names == null) return "";
            string best = "";
            foreach (var n in names) if (!string.IsNullOrEmpty(n) && s.IndexOf(n, StringComparison.Ordinal) >= 0 && n.Length > best.Length) best = n;
            return best;
        }
        static string Category(string s, string level)
        {
            if (level == "Order") return "Order";
            if (StartsWithAny(s, "Armed", "Start", "Stop", "Paused")) return "Session";
            if (s.IndexOf("Settings", StringComparison.Ordinal) >= 0 || s.IndexOf("saved", StringComparison.Ordinal) >= 0 || s.IndexOf("Unlock", StringComparison.Ordinal) >= 0 || s.IndexOf("unlock", StringComparison.Ordinal) >= 0) return "Settings";
            if (s.IndexOf("Account list", StringComparison.Ordinal) >= 0) return "Accounts";
            return "General";
        }
    }
    // Export-time masking: replace each account name by ACCOUNT-<n> (1-based, numbered by first appearance).
    // Longest names first so a name that is a prefix of another can never leave a half-replaced fragment.
    // Email alerts (pure). Settings live in notify.txt: "to=<address>" and one "on=<kind>" per alert kind; unknown lines are ignored.
    // Mail is sent through NinjaTrader's own default email share service (Tools > Options > General > Share services), so KINFX never stores
    // a password or SMTP server. Decide() picks which log lines alert and rate-limits them; it never touches the engine or orders.
    public static class NotifyPrefs
    {
        public const string Lock="lock", Error="error", Limit="limit", Disconnect="disconnect", Boundary="boundary";
        public static readonly string[] Kinds={Lock,Error,Limit,Disconnect,Boundary};
        public static string Label(string kind){return kind==Lock?"Account locked":kind==Error?"Errors":kind==Limit?"Target / daily loss hit":kind==Disconnect?"Leader disconnected":kind==Boundary?"Trading-day boundary":kind;}
        public sealed class Settings
        {
            public string To="";
            public readonly HashSet<string> On=new HashSet<string>(StringComparer.Ordinal);
            public bool Enabled{get{return IsAddress(To)&&On.Count>0;}}
        }
        // Default: nothing is sent until the user enters an address. Once an address is set, lock / error / limit / disconnect are on.
        public static Settings Defaults(){var s=new Settings();foreach(var k in new[]{Lock,Error,Limit,Disconnect})s.On.Add(k);return s;}
        public static bool IsAddress(string a)
        {
            if(string.IsNullOrWhiteSpace(a))return false;a=a.Trim();
            if(a.Length>254||a.IndexOfAny(new[]{' ',',',';','\r','\n','\t','<','>','"'})>=0)return false;
            int at=a.IndexOf('@');return at>0&&at==a.LastIndexOf('@')&&a.IndexOf('.',at)>at+1&&!a.EndsWith(".");
        }
        public static Settings Parse(IEnumerable<string> lines)
        {
            var s=Defaults();if(lines==null)return s;bool anyOn=false;var on=new HashSet<string>(StringComparer.Ordinal);
            foreach(var raw in lines)
            {
                if(raw==null)continue;string t=raw.Trim();if(t.Length==0||t.StartsWith("#"))continue;
                int eq=t.IndexOf('=');if(eq<0)continue;string key=t.Substring(0,eq).Trim(),val=t.Substring(eq+1).Trim();
                if(key=="to"){s.To=IsAddress(val)?val:"";}
                else if(key=="on"){anyOn=true;if(Array.IndexOf(Kinds,val)>=0)on.Add(val);}
                else if(key=="none"){anyOn=true;}
            }
            if(anyOn){s.On.Clear();foreach(var k in on)s.On.Add(k);}
            return s;
        }
        public static string[] Serialize(Settings s)
        {
            var list=new List<string>{"# KINFX Trade Copier: email alerts. Mail goes through NinjaTrader's default email share service; no password is stored here."};
            list.Add("to="+(s==null?"":s.To));
            if(s==null||s.On.Count==0)list.Add("none=");
            else foreach(var k in Kinds)if(s.On.Contains(k))list.Add("on="+k);
            return list.ToArray();
        }
        // Which alert kind a classified log line raises, or null. Order lines, plain info and warnings never mail.
        public static string KindOf(LogLine line)
        {
            if(line==null||line.Text==null)return null;string m=line.Text;
            if(line.Level=="Lock")
            {
                if(m.IndexOf("Leader disconnected",StringComparison.Ordinal)>=0)return Disconnect;
                if(m.IndexOf(" LOCK: Target hit",StringComparison.Ordinal)>=0||m.IndexOf(" LOCK: Daily loss",StringComparison.Ordinal)>=0||m.IndexOf(" LOCK: Drawdown floor",StringComparison.Ordinal)>=0)return Limit;
                return Lock;
            }
            if(line.Level=="Error")return Error;
            if(m.StartsWith("Trading-day boundary",StringComparison.Ordinal))return Boundary;
            return null;
        }
        // Rate limit: at most one mail per kind+account every 10 minutes and at most 20 mails per hour overall; the rest are counted
        // and summarised in the next mail. State is the caller's (one per Dashboard tab).
        public sealed class Limiter
        {
            readonly Dictionary<string,DateTime> last=new Dictionary<string,DateTime>(StringComparer.Ordinal);
            readonly Queue<DateTime> sent=new Queue<DateTime>();
            public int Suppressed;
            public static readonly TimeSpan PerKey=TimeSpan.FromMinutes(10);public const int PerHour=20;
            public bool Allow(string kind,string account,DateTime now)
            {
                while(sent.Count>0&&now-sent.Peek()>=TimeSpan.FromHours(1))sent.Dequeue();
                string key=kind+"|"+(account??"");DateTime prev;
                if((last.TryGetValue(key,out prev)&&now-prev<PerKey)||sent.Count>=PerHour){Suppressed++;return false;}
                last[key]=now;sent.Enqueue(now);return true;
            }
        }
        public sealed class Mail{public string Subject="",Body="";}
        // The mail for one line, or null when it must not be sent. Account names are masked like a log export.
        public static Mail Decide(Settings s,Limiter limiter,LogLine line,IEnumerable<string> accountNames,DateTime now)
        {
            if(s==null||!s.Enabled||limiter==null)return null;
            string kind=KindOf(line);if(kind==null||!s.On.Contains(kind))return null;
            if(!limiter.Allow(kind,line.Account,now))return null;
            int skipped=limiter.Suppressed;limiter.Suppressed=0;
            string text=LogExport.Mask(line.Text,accountNames);
            string subject="KINFX Trade Copier: "+Label(kind);
            string body=now.ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture)+"  "+line.Level+"\n"+text+"\n\n"
                +(skipped>0?skipped+" more alert(s) were rate-limited since the last mail; see the Logs tab.\n":"")
                +"Sent by KINFX Trade Copier through NinjaTrader. Account names are masked. This mail never means an order was sent or cancelled.";
            return new Mail{Subject=subject,Body=body};
        }
    }
    // S1 one-click re-follow (pure): a follower is offered only when the engine closed it after a completed exit, it is flat, connected,
    // unlocked and not paused. The engine re-checks everything again (SetFollowing ON) and refuses anything not clean.
    public static class Refollow
    {
        public static bool Eligible(bool exitClosed,bool following,bool locked,bool paused,bool connected,int position)
        { return exitClosed&&!following&&!locked&&!paused&&connected&&position==0; }
        // Auto re-follow additionally needs the opt-in and a flat leader, and never re-ticks an account the user flattened by hand
        // (Flat / Flatten selected / Flatten all): the engine marks that close the same way as a copied exit (review 5, 2026-10-17).
        public static bool Auto(bool optIn,bool userFlattened,bool exitClosed,bool following,bool locked,bool paused,bool connected,int position,int leaderPosition)
        { return optIn&&!userFlattened&&leaderPosition==0&&Eligible(exitClosed,following,locked,paused,connected,position); }
    }
    // S1 auto re-follow: one attempt per close. The attempt is not spent while entries are off (the engine would refuse it), and
    // entries coming back on, or a new session, give every still-closed follower a fresh attempt. No account or engine access.
    public sealed class AutoRefollowGate
    {
        readonly HashSet<string> tried=new HashSet<string>(StringComparer.Ordinal);int session=int.MinValue;bool armed;
        // Returns the names that may be re-followed now, and marks them tried.
        public string[] Take(int sessionGeneration,bool entriesArmed,IEnumerable<string> closedEligible,IEnumerable<string> stillClosed)
        {
            if(sessionGeneration!=session){session=sessionGeneration;tried.Clear();}
            var closed=new HashSet<string>(stillClosed??new string[0],StringComparer.Ordinal);tried.RemoveWhere(n=>!closed.Contains(n));
            if(entriesArmed&&!armed)tried.Clear();armed=entriesArmed;
            if(!entriesArmed)return new string[0];
            var take=(closedEligible??new string[0]).Where(n=>!tried.Contains(n)).Distinct().ToArray();
            foreach(var n in take)tried.Add(n);return take;
        }
    }
    // S1 trading-day boundary reaction. "pause" (default) = guarded pause of new entries, as before. "notify" = log only.
    public static class BoundaryMode
    {
        public const string Pause="pause", Notify="notify";
        public static string Parse(string v){return v==Notify?Notify:Pause;}
    }
    public static class LogExport
    {
        public static string Mask(string text, IEnumerable<string> names)
        {
            if (text == null) return "";
            if (names == null) return text;
            var order = new List<string>();
            var number = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var n in names)
            {
                if (string.IsNullOrEmpty(n) || number.ContainsKey(n)) continue;
                number[n] = number.Count + 1;
                order.Add(n);
            }
            string result = text;
            foreach (var n in order.OrderByDescending(x => x.Length)) result = result.Replace(n, "ACCOUNT-" + number[n]);
            return result;
        }
    }
    // Read-only avg/qty drift. Display and notification only: this type never submits, changes, cancels or flattens.
    public enum PositionConsistencyStatus
    {
        WaitingReports,
        QtyMatch,
        QtyDiverge,
        PriceDiverge,
        Unavailable
    }
    public sealed class PositionSnap
    {
        public string Instrument="";
        public int SignedQuantity;
        public bool HasAveragePrice;
        public double AveragePrice;
        public bool Connected=true;
        public bool ContractsVerified=true;
        public long EvidenceVersion;
        public DateTime EvidenceUtc;
        public DateTime ReadUtc;
        public double Ratio=1;
        public int MaxContracts=int.MaxValue;
    }
    public struct EvidenceMark
    {
        public long Version;
        public DateTime EvidenceUtc;
        public bool Tracked;
        public int Quantity;
        public bool HasAverage;
        public double Average;
    }
    public sealed class PositionConsistencyResult
    {
        public PositionConsistencyStatus Status;
        public int? QtyDelta;
        public int? ExpectedQuantity;
        public int? PriceDeltaTicks;
        public int? AdverseTicks;
        public string Badge="\u2014";
        public string Reason=PositionConsistency.ReasonNotComparable;
    }
    public static class PositionConsistency
    {
        public static readonly TimeSpan ReportGrace=TimeSpan.FromSeconds(3);
        public const string Dash="\u2014";
        public const string NotAvailable="n/a";
        public const string Disclaimer="Same side/qty does not mean same fill price. Badge only — no auto trade.";
        public const string ReasonWaiting="Waiting for position reports";
        public const string ReasonQtyMatch="Quantity matches";
        public const string ReasonQtyDiverge="Quantity diverges";
        public const string ReasonPrice="Average fill diverges";
        public const string ReasonNotComparable="Unavailable: not comparable";
        public const string ReasonContract="Unavailable: platform contract missing";
        public const string ReasonDisconnected="Unavailable: disconnected";
        public const string ReasonInstrument="Unavailable: instrument mismatch";
        public const string ReasonStale="Unavailable: stale report";
        public const string ReasonLeaderFlat="Unavailable: leader flat";
        public const string ReasonAvgMissing="Unavailable: average price missing";
        public const string ReasonTick="Unavailable: tick size invalid";
        public const string ReasonScale="Unavailable: scale settings invalid";
        public const string ReasonLeaderBaseline="Leader row is the comparison baseline";
        public const string TipExpected="Expected quantity {0} · actual delta {1}";
        public const string TipPrice="Average-price delta {0} ticks · adverse {1} ticks";
        public const string Notice="Avg drift {0}: {1} (display only — no order)";
        public const string WarnLabel="Warn if avg differs by more than";
        public const string WarnSuffix="ticks (display only)";
        public const string WarnTip="Warn if avg differs by more than N ticks (display only)";
        public const string PrefIgnored="Display preference ignored; avg warn ticks reset to the default. Badge only — no order.";
        public const string WarnInvalid="Avg warn ticks must be a whole number from 0 to 100 (display only)";
        public const string DisplayFailed="Position consistency display failed: {0}";
        public const string NotSaved="Avg warn ticks NOT saved (used for this session only): {0}";
        public const string ColumnHeader="Drift";
        public static bool ContractsReady(bool averagePriceIsPublicDouble,bool tickSizeIsPublicDouble)
        {
            return averagePriceIsPublicDouble&&tickSizeIsPublicDouble;
        }
        public static Exception Unexpected(PositionConsistencyStatus status)
        {
            switch(status)
            {
                case PositionConsistencyStatus.WaitingReports:
                case PositionConsistencyStatus.QtyMatch:
                case PositionConsistencyStatus.QtyDiverge:
                case PositionConsistencyStatus.PriceDiverge:
                case PositionConsistencyStatus.Unavailable:
                    return new InvalidOperationException("position consistency status fell through");
                default:
                    return new InvalidOperationException("unhandled position consistency status");
            }
        }
        public static EvidenceMark Note(EvidenceMark previous,int signedQty,bool hasAverage,double average,DateTime nowUtc)
        {
            bool changed=!previous.Tracked||signedQty!=previous.Quantity||hasAverage!=previous.HasAverage||(hasAverage&&average!=previous.Average);
            if(!changed)return previous;
            return new EvidenceMark{Version=previous.Version+1,EvidenceUtc=nowUtc,Tracked=true,Quantity=signedQty,HasAverage=hasAverage,Average=hasAverage?average:0};
        }
        // shortcut: scales the leader's whole position, while the engine floors each order and skips an order over Max, so several small
        // fractional-Ratio entries or one over-Max entry can show a qty gap the engine intended; upgrade by comparing against the engine's own copy ledger.
        public static int ExpectedSignedQuantity(int leaderSigned,double ratio,int maxContracts)
        {
            int sign=Math.Sign(leaderSigned);
            if(sign==0||leaderSigned==int.MinValue||maxContracts<1||!RiskPolicy.Finite(ratio)||ratio<=0)return 0;
            int abs=RiskPolicy.Quantity(Math.Abs(leaderSigned),ratio);
            if(abs>maxContracts)abs=maxContracts;
            return sign*abs;
        }
        public static PositionConsistencyResult Evaluate(PositionSnap leader,PositionSnap follower,double tickSize,int thresholdTicks)
        {
            return Evaluate(leader,follower,tickSize,thresholdTicks,DateTime.UtcNow,ReportGrace);
        }
        public static PositionConsistencyResult Evaluate(PositionSnap leader,PositionSnap follower,double tickSize,int thresholdTicks,DateTime nowUtc,TimeSpan grace)
        {
            if(leader==null||follower==null)return Finish(PositionConsistencyStatus.Unavailable,ReasonNotComparable,Dash);
            if(!leader.ContractsVerified||!follower.ContractsVerified)return Finish(PositionConsistencyStatus.Unavailable,ReasonContract,NotAvailable);
            if(thresholdTicks<0||grace<TimeSpan.Zero)return Finish(PositionConsistencyStatus.Unavailable,ReasonNotComparable,Dash);
            if(!leader.Connected||!follower.Connected)return Finish(PositionConsistencyStatus.Unavailable,ReasonDisconnected,Dash);
            if(string.IsNullOrWhiteSpace(leader.Instrument)||string.IsNullOrWhiteSpace(follower.Instrument)||!string.Equals(leader.Instrument,follower.Instrument,StringComparison.Ordinal))
                return Finish(PositionConsistencyStatus.Unavailable,ReasonInstrument,Dash);
            if(!Fresh(leader,nowUtc,grace)||!Fresh(follower,nowUtc,grace))return Finish(PositionConsistencyStatus.Unavailable,ReasonStale,Dash);
            if(Awaiting(leader,follower,nowUtc,grace))return Finish(PositionConsistencyStatus.WaitingReports,ReasonWaiting,"wait");
            if(leader.SignedQuantity==0||leader.SignedQuantity==int.MinValue)return Finish(PositionConsistencyStatus.Unavailable,ReasonLeaderFlat,Dash);
            if(!ScaleOk(follower))return Finish(PositionConsistencyStatus.Unavailable,ReasonScale,Dash);
            int expected=ExpectedSignedQuantity(leader.SignedQuantity,follower.Ratio,follower.MaxContracts);
            long deltaLong=(long)follower.SignedQuantity-expected;
            if(deltaLong>int.MaxValue||deltaLong<int.MinValue)return Finish(PositionConsistencyStatus.Unavailable,ReasonNotComparable,Dash);
            int qtyDelta=(int)deltaLong;
            bool qtyOk=qtyDelta==0;
            int? ticks=null,adverse=null;
            bool priceOk=TryPrice(leader,follower,tickSize,out ticks,out adverse);
            if(!qtyOk)
            {
                string badge=FormatQty(qtyDelta);
                if(priceOk)badge+=" \u00B7 "+FormatAvg(ticks.Value,adverse.Value);
                return Finish(PositionConsistencyStatus.QtyDiverge,ReasonQtyDiverge,badge,qtyDelta,expected,priceOk?ticks:null,priceOk?adverse:null);
            }
            if(expected==0)return Finish(PositionConsistencyStatus.QtyMatch,ReasonQtyMatch,"qty 0",0,0);
            if(!leader.HasAveragePrice||!follower.HasAveragePrice)return Finish(PositionConsistencyStatus.Unavailable,ReasonAvgMissing,Dash);
            if(!priceOk)return Finish(PositionConsistencyStatus.Unavailable,ReasonTick,Dash);
            int absTicks=Math.Abs(ticks.Value);
            bool warn=absTicks>thresholdTicks;
            return Finish(warn?PositionConsistencyStatus.PriceDiverge:PositionConsistencyStatus.QtyMatch,warn?ReasonPrice:ReasonQtyMatch,FormatAvg(ticks.Value,adverse.Value),0,expected,ticks,adverse);
        }
        public static PositionConsistencyResult LeaderBaseline()
        {
            return Finish(PositionConsistencyStatus.Unavailable,ReasonLeaderBaseline,Dash);
        }
        public static PositionConsistencyResult NotComparable()
        {
            return Finish(PositionConsistencyStatus.Unavailable,ReasonNotComparable,Dash);
        }
        public static string FormatTip(PositionConsistencyResult result)
        {
            if(result==null)result=Finish(PositionConsistencyStatus.Unavailable,ReasonNotComparable,Dash);
            var sb=new System.Text.StringBuilder();
            sb.Append(result.Badge).Append(" · ").Append(Lang.T(result.Reason));
            if(result.ExpectedQuantity.HasValue&&result.QtyDelta.HasValue)
                sb.Append('\n').Append(Lang.F(TipExpected,result.ExpectedQuantity.Value,Signed(result.QtyDelta.Value)));
            if(result.PriceDeltaTicks.HasValue&&result.AdverseTicks.HasValue)
                sb.Append('\n').Append(Lang.F(TipPrice,Signed(result.PriceDeltaTicks.Value),Signed(result.AdverseTicks.Value)));
            sb.Append('\n').Append(Lang.T(Disclaimer));
            return sb.ToString();
        }
        static bool ScaleOk(PositionSnap follower)
        {
            return follower.MaxContracts>0&&RiskPolicy.Finite(follower.Ratio)&&follower.Ratio>0;
        }
        static bool Fresh(PositionSnap snap,DateTime nowUtc,TimeSpan grace)
        {
            if(snap.ReadUtc==DateTime.MinValue)return false;
            if(snap.ReadUtc>nowUtc)return true;
            return nowUtc-snap.ReadUtc<=grace;
        }
        static bool Awaiting(PositionSnap leader,PositionSnap follower,DateTime nowUtc,TimeSpan grace)
        {
            // EvidenceMark.Version counts changes on that account alone. Grace follows the leader evidence time.
            if(!WithinReportGrace(leader,nowUtc,grace))return false;
            if(leader.SignedQuantity!=0&&leader.SignedQuantity!=int.MinValue&&!ScaleOk(follower))return false;
            int expected=leader.SignedQuantity==0||leader.SignedQuantity==int.MinValue?0:ExpectedSignedQuantity(leader.SignedQuantity,follower.Ratio,follower.MaxContracts);
            return follower.SignedQuantity!=expected;
        }
        static bool WithinReportGrace(PositionSnap leader,DateTime nowUtc,TimeSpan grace)
        {
            if(leader.EvidenceUtc==DateTime.MinValue)return false;
            if(leader.EvidenceUtc>nowUtc)return true;
            return nowUtc-leader.EvidenceUtc<=grace;
        }
        static bool TryPrice(PositionSnap leader,PositionSnap follower,double tickSize,out int? ticks,out int? adverse)
        {
            ticks=null;adverse=null;
            if(follower.SignedQuantity==0||!leader.HasAveragePrice||!follower.HasAveragePrice)return false;
            int raw;
            if(!TryTickDelta(leader.AveragePrice,follower.AveragePrice,tickSize,out raw))return false;
            int sign=Math.Sign(leader.SignedQuantity);
            ticks=raw;adverse=sign<0?-raw:raw;return true;
        }
        static bool TryTickDelta(double leaderAvg,double followerAvg,double tickSize,out int ticks)
        {
            ticks=0;
            if(!RiskPolicy.Finite(leaderAvg)||!RiskPolicy.Finite(followerAvg)||!RiskPolicy.Finite(tickSize)||tickSize<=0)return false;
            if(leaderAvg<=0||followerAvg<=0)return false;
            double raw=(followerAvg-leaderAvg)/tickSize;
            if(!RiskPolicy.Finite(raw)||Math.Abs(raw)>1000000d)return false;
            double nearest=Math.Round(raw,0,MidpointRounding.AwayFromZero);
            if(!RiskPolicy.Finite(nearest)||Math.Abs(nearest)>1000000d)return false;
            ticks=(int)nearest;return true;
        }
        static string FormatQty(int delta)
        {
            if(delta>0)return "qty+"+delta.ToString(CultureInfo.InvariantCulture);
            return "qty"+delta.ToString(CultureInfo.InvariantCulture);
        }
        static string FormatAvg(int ticks,int adverse)
        {
            string body=ticks>0?"avg +"+ticks.ToString(CultureInfo.InvariantCulture)+"t":ticks<0?"avg \u2212"+Math.Abs(ticks).ToString(CultureInfo.InvariantCulture)+"t":"avg 0t";
            if(adverse>0)body+=" adverse";
            return body;
        }
        static string Signed(int value)
        {
            return value>0?"+"+value.ToString(CultureInfo.InvariantCulture):value.ToString(CultureInfo.InvariantCulture);
        }
        static PositionConsistencyResult Finish(PositionConsistencyStatus status,string reason,string badge,int? qtyDelta=null,int? expected=null,int? ticks=null,int? adverse=null)
        {
            switch(status)
            {
                case PositionConsistencyStatus.WaitingReports:
                case PositionConsistencyStatus.QtyMatch:
                case PositionConsistencyStatus.QtyDiverge:
                case PositionConsistencyStatus.PriceDiverge:
                case PositionConsistencyStatus.Unavailable:
                    break;
                default:
                    throw Unexpected(status);
            }
            return new PositionConsistencyResult{Status=status,Reason=reason,Badge=badge,QtyDelta=qtyDelta,ExpectedQuantity=expected,PriceDeltaTicks=ticks,AdverseTicks=adverse};
        }
    }
    public static class DisplayPrefs
    {
        public const int DefaultAvgWarnTicks=1;
        public const int MaxAvgWarnTicks=100;
        public static bool TryParseTicks(string text,out int ticks)
        {
            ticks=DefaultAvgWarnTicks;
            string raw=(text??"").Trim();
            if(raw.Length==0||raw.Length>3)return false;
            if(raw[0]=='0'&&raw.Length>1)return false;
            for(int i=0;i<raw.Length;i++)if(raw[i]<'0'||raw[i]>'9')return false;
            int n;
            if(!int.TryParse(raw,NumberStyles.None,CultureInfo.InvariantCulture,out n))return false;
            if(n<0||n>MaxAvgWarnTicks)return false;
            ticks=n;return true;
        }
        public static bool TryReadAvgWarnTicks(string json,out int ticks)
        {
            ticks=DefaultAvgWarnTicks;
            if(string.IsNullOrEmpty(json))return false;
            if(json[0]=='\uFEFF')json=json.Substring(1);
            var sb=new System.Text.StringBuilder();
            for(int i=0;i<json.Length;i++)
            {
                char c=json[i];
                if(c==' '||c=='\t'||c=='\r'||c=='\n')continue;
                sb.Append(c);
            }
            string s=sb.ToString();
            const string prefix="{\"avgWarnTicks\":";
            if(!s.StartsWith(prefix,StringComparison.Ordinal)||!s.EndsWith("}",StringComparison.Ordinal))return false;
            return TryParseTicks(s.Substring(prefix.Length,s.Length-prefix.Length-1),out ticks);
        }
        public static string ToJson(int ticks)
        {
            int n;
            if(!TryParseTicks(ticks.ToString(CultureInfo.InvariantCulture),out n))throw new ArgumentOutOfRangeException("ticks");
            return "{\"avgWarnTicks\":"+n.ToString(CultureInfo.InvariantCulture)+"}";
        }
        public static void SaveAvgWarnTicks(string path,int ticks)
        {
            if(string.IsNullOrEmpty(path))throw new ArgumentException("path");
            string json=ToJson(ticks);
            string dir=Path.GetDirectoryName(path);
            if(!string.IsNullOrEmpty(dir))Directory.CreateDirectory(dir);
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                File.WriteAllText(temp,json);
                if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
            }
            finally { if(File.Exists(temp))File.Delete(temp); }
        }
    }
// BEGIN LANG TABLE (generated by scripts/gen_lang.py from i18n/strings.json; do not edit by hand)
    // Display-only translation. The English sentence IS the key; a missing translation shows English, never a blank.
    // Engine decisions, lock reasons stored in XML, log text kept in memory and everything the tests read stay English;
    // they are translated only when drawn (State tooltip, Logs tab) with Lang.Show / Lang.T.
    public static class Lang
    {
        public const string English = "en", Chinese = "zh", Japanese = "ja", Korean = "ko";
        public static readonly string[] Codes = { English, Chinese, Japanese, Korean };
        // Native names, shown in the language picker; never translated.
        public static readonly string[] Names = { "English", "中文（繁體）", "日本語", "한국어" };
        static volatile string current = English;
        public static string Current { get { return current; } }
        public static bool IsValid(string code) { return Array.IndexOf(Codes, code) >= 0; }
        public static void Set(string code) { current = IsValid(code) ? code : English; }
        // Language from the Windows UI culture (zh-* -> zh, ja -> ja, ko -> ko, anything else -> English).
        public static string FromCulture(string cultureName)
        {
            string c = (cultureName ?? "").ToLowerInvariant();
            return c.StartsWith("zh") ? Chinese : c.StartsWith("ja") ? Japanese : c.StartsWith("ko") ? Korean : English;
        }
        // NinjaTrader pins its UI thread to en-US, so the Windows display language (installed UI culture) is tried first, then the regional
        // format culture, then the thread UI culture. The first one that names zh / ja / ko wins; otherwise English.
        public static string Detect(params string[] cultureNames)
        {
            foreach (var name in cultureNames) { string code = FromCulture(name); if (code != English) return code; }
            return English;
        }
        static int Col(string code) { return code == Chinese ? 0 : code == Japanese ? 1 : 2; }
        public static string T(string en) { return T(en, current); }
        public static string T(string en, string code)
        {
            if (en == null) return "";
            if (code == English || !IsValid(code)) return en;
            string[] row;
            if (Table.TryGetValue(en, out row)) { string s = row[Col(code)]; return s.Length > 0 ? s : en; }
            return en;
        }
        // Fill a translated template. Falls back to the English template if the translated one is malformed.
        public static string F(string en, params object[] args)
        {
            string text = T(en);
            try { return string.Format(CultureInfo.InvariantCulture, text, args); }
            catch (FormatException) { return string.Format(CultureInfo.InvariantCulture, en, args); }
        }
        // Translate an already-built English message (log line, stored lock reason). Exact sentences are looked up directly;
        // otherwise each template is tried by turning its {n} holes into a pattern and refilling the translated template.
        public static string Show(string english) { return Show(english, current); }
        public static string Show(string english, string code)
        {
            if (string.IsNullOrEmpty(english) || code == English || !IsValid(code)) return english ?? "";
            string[] row;
            if (Table.TryGetValue(english, out row)) { string s = row[Col(code)]; return s.Length > 0 ? s : english; }
            // Split a "prefix: reason" style line one segment at a time so a composite message still translates its known parts.
            string composite = TranslateComposite(english, code);
            return composite ?? english;
        }
        static string TranslateComposite(string text, string code)
        {
            foreach (var p in Patterns)
            {
                var m = p.Regex.Match(text);
                if (!m.Success) continue;
                string[] row = Table[p.Key]; string tpl = row[Col(code)];
                if (tpl.Length == 0) return null;
                var args = new object[p.Holes];
                for (int i = 0; i < p.Holes; i++) args[i] = Show(m.Groups["h" + i].Value, code);
                try { return string.Format(CultureInfo.InvariantCulture, tpl, args); } catch (FormatException) { return null; }
            }
            return null;
        }
        static readonly System.Collections.Generic.Dictionary<string, string[]> Table = Build();
        static System.Collections.Generic.Dictionary<string, string[]> Build()
        {
            var t = new System.Collections.Generic.Dictionary<string, string[]>(StringComparer.Ordinal);
            t["REVIEW HOLD · TRADING DISABLED"] = new[] { "審查封鎖 · 交易已停用", "レビュー保留 · 取引無効", "검토 보류 · 거래 비활성화" };
            t["REVIEW HOLD: copying unavailable until engine safety issues are fixed"] = new[] { "審查封鎖：在引擎安全問題修復前，無法跟單", "レビュー保留：エンジンの安全上の問題が修正されるまでコピー不可", "검토 보류: 엔진 안전 문제가 해결될 때까지 복사 불가" };
            t["REVIEW HOLD: Start / Flatten / bulk risk disabled. ATM means order mirroring, not a follower native ATM. No real or simulation orders were submitted by this build verification."] = new[] { "審查封鎖：Start / Flatten / 批次風控已停用。ATM 指委託單鏡像，並非跟單帳戶的原生 ATM。本次建置驗證未提交任何真實或模擬委託單。", "レビュー保留：Start / Flatten / 一括リスクは無効。ATM は注文のミラーリングを意味し、フォロワーのネイティブ ATM ではない。このビルド検証では実際の注文もシミュレーション注文も送信されていない。", "검토 보류: Start / Flatten / 일괄 리스크가 비활성화되었습니다. ATM은 주문 미러링을 의미하며 팔로워의 네이티브 ATM이 아닙니다. 이 빌드 검증에서는 실거래 주문도 시뮬레이션 주문도 전송되지 않았습니다." };
            t["Leader"] = new[] { "主帳戶", "リーダー", "리더" };
            t["Ratio is not a positive number"] = new[] { "比例不是正數", "比率が正の数ではない", "비율이 양수가 아님" };
            t["On divergence = FlattenOnce is saved but NOT active in this alpha: the engine only locks and notifies, it never flattens by itself"] = new[] { "On divergence = FlattenOnce 已儲存，但在此 alpha 版本中「未啟用」：引擎僅會鎖定並通知，絕不會自行平倉", "On divergence = FlattenOnce は保存されるが、このアルファ版では有効ではない：エンジンはロックと通知のみ行い、自動で全決済することはない", "On divergence = FlattenOnce는 저장되지만 이 알파 버전에서는 활성화되지 않습니다: 엔진은 잠금과 알림만 수행하며 스스로 청산하지 않습니다" };
            t["no daily target and no daily loss limit (0 = off)"] = new[] { "無每日目標，亦無每日虧損上限（0 = 關閉）", "日次目標なし、日次損失上限なし（0 = オフ）", "일일 목표 없음, 일일 손실 한도 없음 (0 = 끔)" };
            t["no daily loss limit (0 = off)"] = new[] { "無每日虧損上限（0 = 關閉）", "日次損失上限なし（0 = オフ）", "일일 손실 한도 없음 (0 = 끔)" };
            t["no daily target (0 = off)"] = new[] { "無每日目標（0 = 關閉）", "日次目標なし（0 = オフ）", "일일 목표 없음 (0 = 끔)" };
            t["On divergence must be Notify or FlattenOnce"] = new[] { "On divergence 必須為 Notify 或 FlattenOnce", "On divergence は Notify または FlattenOnce でなければならない", "On divergence는 Notify 또는 FlattenOnce여야 합니다" };
            t["Telemetry error"] = new[] { "遙測錯誤", "テレメトリーエラー", "텔레메트리 오류" };
            t["Target hit"] = new[] { "已達目標", "目標達成", "목표 도달" };
            t["Cancelling"] = new[] { "取消中", "取消中", "취소 중" };
            t["Rechecking"] = new[] { "重新檢查中", "再確認中", "재확인 중" };
            t["Locked"] = new[] { "已鎖定", "ロック中", "잠김" };
            t["Error"] = new[] { "錯誤", "エラー", "오류" };
            t["Paused"] = new[] { "已暫停", "一時停止中", "일시정지됨" };
            t["Copying"] = new[] { "跟單中", "コピー中", "복사 중" };
            t["Offline"] = new[] { "離線", "オフライン", "오프라인" };
            t["Demo only"] = new[] { "僅限示範", "デモのみ", "데모 전용" };
            t["Demo"] = new[] { "示範", "デモ", "데모" };
            t["Selected"] = new[] { "已選", "選択済み", "선택됨" };
            t["Off"] = new[] { "關", "オフ", "끔" };
            t[" · cancellation not confirmed"] = new[] { " · 取消尚未確認", " · キャンセル未確認", " · 취소 확인되지 않음" };
            t[" · NOTE: "] = new[] { " · 注意： ", " · 注意: ", " · 참고: " };
            t["This account Ratio must be finite and > 0"] = new[] { "此帳戶的比例必須為有限數且大於 0", "この口座の比率は有限値かつ 0 より大きい必要がある", "이 계좌의 비율은 유한한 값이며 0보다 커야 합니다" };
            t["Daily target must be a finite amount >= 0 (0 = off)"] = new[] { "每日目標必須為大於或等於 0 的有限金額（0 = 關閉）", "日次目標は 0 以上の有限の金額でなければならない（0 = オフ）", "일일 목표는 0 이상의 유한한 금액이어야 합니다 (0 = 끔)" };
            t["Daily loss must be a finite amount >= 0 (0 = off)"] = new[] { "每日虧損必須為大於或等於 0 的有限金額（0 = 關閉）", "日次損失は 0 以上の有限の金額でなければならない（0 = オフ）", "일일 손실은 0 이상의 유한한 금액이어야 합니다 (0 = 끔)" };
            t["Ratio for THIS account only · finite > 0 · follower qty = floor(leader qty × ratio) · locked while attached"] = new[] { "僅此帳戶的比例 · 有限數且大於 0 · 跟單帳戶數量 = floor(主帳戶數量 × 比例) · 連接期間鎖定", "この口座のみの比率 · 有限値かつ 0 より大きい · フォロワー数量 = floor(リーダー数量 × 比率) · 接続中はロック", "이 계좌에만 적용되는 비율 · 유한값이며 0보다 큼 · 팔로워 수량 = floor(리더 수량 × 비율) · 연결 중 잠금" };
            t["Follow account; untick stops new entries"] = new[] { "跟隨此帳戶；取消勾選將停止新進場", "この口座をフォロー；チェックを外すと新規エントリーを停止", "이 계좌를 따라감; 선택 해제 시 신규 진입 중지" };
            t["Explicit follow / OFF · pending cancels and latch may block re-enable"] = new[] { "明確跟隨 / OFF · 待處理的取消及鎖存可能阻止重新啟用", "明示的にフォロー / OFF · 保留中のキャンセルとラッチにより再有効化がブロックされる場合がある", "명시적 팔로우 / OFF · 대기 중인 취소와 래치로 인해 재활성화가 차단될 수 있음" };
            t["Follower enabled · one click"] = new[] { "跟單帳戶已啟用 · 一鍵切換", "フォロワー有効 · ワンクリック", "팔로워 활성화 · 원클릭" };
            t["Acct"] = new[] { "帳戶", "口座", "계좌" };
            t["Exact broker identity · full name on hover · differing suffix emphasized"] = new[] { "確切的券商身分 · 懸停顯示完整名稱 · 突顯不同的後綴", "正確なブローカー識別子 · ホバーで全名表示 · 異なる接尾辞を強調", "정확한 브로커 식별자 · 마우스 오버 시 전체 이름 표시 · 다른 접미사 강조" };
            t["State"] = new[] { "狀態", "状態", "상태" };
            t["Observed copy state, NOT connection or follower selection"] = new[] { "觀察到的跟單狀態，並非連線或跟單帳戶選取狀態", "観測されたコピー状態であり、接続状態やフォロワーの選択状態ではない", "관찰된 복사 상태이며 연결 또는 팔로워 선택 상태가 아님" };
            t["Per-account ATM exit mirroring"] = new[] { "逐帳戶 ATM 出場單鏡像", "口座ごとの ATM 決済注文ミラーリング", "계좌별 ATM 청산 주문 미러링" };
            t["Per-account ATM exit mirroring, NOT a native ATM clone. OFF ignores non-market SL / TP: follower can be unprotected. Market closes still copy. Frozen while attached."] = new[] { "逐帳戶 ATM 出場單鏡像，並非原生 ATM 複製。OFF 會忽略非市價 SL / TP：跟單帳戶可能無保護。市價平倉仍會跟單。連接期間凍結。", "口座ごとの ATM 決済注文ミラーリングであり、ネイティブ ATM の複製ではない。OFF では成行以外の SL / TP を無視する：フォロワーは保護されない可能性がある。成行の決済は引き続きコピーされる。接続中は固定。", "계좌별 ATM 청산 주문 미러링이며 네이티브 ATM 복제가 아닙니다. OFF는 시장가 외의 SL / TP를 무시합니다: 팔로워가 보호되지 않을 수 있습니다. 시장가 청산은 계속 복사됩니다. 연결 중에는 고정됩니다." };
            t["ATM"] = new[] { "ATM", "ATM", "ATM" };
            t["Independent ATM exit mirror · OFF can leave exposure unprotected"] = new[] { "獨立的 ATM 出場單鏡像 · OFF 可能使持倉風險無保護", "独立した ATM 決済注文ミラー · OFF ではエクスポージャーが保護されない可能性がある", "독립 ATM 청산 주문 미러 · OFF 시 노출 포지션이 보호되지 않을 수 있음" };
            t["OFF: block new entries / request owned entry cancellation; retain exits. Pending fills may still occur. Explicit follow required again; latch remains."] = new[] { "OFF：阻止新進場 / 請求取消自有進場單；保留出場單。待處理的委託仍可能成交。需再次明確跟隨；鎖存仍然保留。", "OFF：新規エントリーをブロック / 自身のエントリー注文のキャンセルを要求；決済注文は維持。保留中の注文は約定する場合がある。再度の明示的フォローが必要；ラッチは維持される。", "OFF: 신규 진입 차단 / 자체 진입 주문 취소 요청; 청산 주문은 유지. 대기 중인 주문은 체결될 수 있음. 명시적 팔로우를 다시 해야 하며 래치는 유지됨." };
            t["Stop following this account"] = new[] { "停止跟隨此帳戶", "この口座のフォローを停止", "이 계좌 따라가기 중지" };
            t["One-click OFF is not Flatten; current positions / SL / TP remain"] = new[] { "一鍵 OFF 並非 Flatten；目前持倉 / SL / TP 仍保留", "ワンクリック OFF は Flatten ではない；現在のポジション / SL / TP は維持される", "원클릭 OFF는 Flatten이 아님; 현재 포지션 / SL / TP는 유지됨" };
            t["Flat"] = new[] { "平倉", "決済", "청산" };
            t["FLAT this account only: cancels its working orders and closes its position in the session instrument after you confirm. Other accounts keep copying. The account goes OFF; re-follow explicitly. Sent once, never retried."] = new[] { "僅對此帳戶平倉：經您確認後，取消其掛單並平掉其在本次工作階段商品上的持倉。其他帳戶繼續跟單。此帳戶將轉為 OFF；需明確重新跟隨。僅發送一次，不會重試。", "この口座のみ全決済：確認後、待機中の注文をキャンセルし、セッション銘柄のポジションを決済する。他の口座はコピーを継続。この口座は OFF になり、再度明示的にフォローする必要がある。送信は 1 回のみで、再試行はしない。", "이 계좌만 청산: 확인 후 대기 주문을 취소하고 세션 종목의 포지션을 청산합니다. 다른 계좌는 계속 복사합니다. 이 계좌는 OFF가 되며 명시적으로 다시 팔로우해야 합니다. 한 번만 전송하며 재시도하지 않습니다." };
            t["Flatten this account only"] = new[] { "僅平倉此帳戶", "この口座のみ全決済", "이 계좌만 청산" };
            t["Flatten ONE account (confirmation names it). Not the same as OFF: OFF keeps the position, Flat closes it."] = new[] { "僅平倉「一個」帳戶（確認視窗會列出帳戶名稱）。與 OFF 不同：OFF 保留持倉，平倉則會平掉持倉。", "1 つの口座のみ全決済（確認画面に口座名が表示される）。OFF とは異なる：OFF はポジションを維持し、全決済はポジションを決済する。", "계좌 하나만 청산 (확인 창에 계좌 이름 표시). OFF와 다름: OFF는 포지션을 유지하고 청산은 포지션을 닫습니다." };
            t[" · green / amber / red / off"] = new[] { " · 綠 / 琥珀 / 紅 / 關閉", " · 緑 / 琥珀 / 赤 / オフ", " · 녹색 / 주황 / 빨강 / 끔" };
            t["Tgt"] = new[] { "Tgt", "Tgt", "Tgt" };
            t["DLL"] = new[] { "DLL", "DLL", "DLL" };
            t["Qty"] = new[] { "數量", "数量", "수량" };
            t["Bal"] = new[] { "餘額", "残高", "잔고" };
            t["DEMO · NO TRADES"] = new[] { "示範 · 無交易", "デモ · 取引なし", "데모 · 거래 없음" };
            t["Simulation leader account · no live opt-in"] = new[] { "模擬主帳戶 · 未選擇實盤", "シミュレーションのリーダー口座 · ライブ未許可", "시뮬레이션 리더 계좌 · 실거래 미승인" };
            t["Lead"] = new[] { "主帳", "リーダー", "리더" };
            t["Exact instrument"] = new[] { "確切商品", "正確な銘柄", "정확한 종목" };
            t["Inst"] = new[] { "商品", "銘柄", "종목" };
            t["→  0 followers  "] = new[] { "→  0 個跟單帳戶  ", "→  フォロワー 0  ", "→  팔로워 0명  " };
            t["▶  Start"] = new[] { "▶  開始", "▶  開始", "▶  시작" };
            t["Guarded pause · new entries only"] = new[] { "保護性暫停 · 僅限新進場", "ガード付き一時停止 · 新規エントリーのみ", "보호 일시정지 · 신규 진입만" };
            t["Stop when flat"] = new[] { "持倉歸零後停止", "フラット後に停止", "청산 후 중지" };
            t["Refresh accounts"] = new[] { "重新整理帳戶", "口座を更新", "계좌 새로고침" };
            t["Search account / alias / group / preset · Ctrl+F · Escape clears"] = new[] { "搜尋帳戶 / 別名 / 群組 / 預設 · Ctrl+F · Escape 清除", "口座 / エイリアス / グループ / プリセットを検索 · Ctrl+F · Escape でクリア", "계좌 / 별칭 / 그룹 / 프리셋 검색 · Ctrl+F · Escape로 지우기" };
            t["Filter displayed account states"] = new[] { "篩選顯示的帳戶狀態", "表示する口座状態を絞り込み", "표시할 계좌 상태 필터" };
            t["All"] = new[] { "全部", "すべて", "전체" };
            t["Tick all visible followers"] = new[] { "勾選所有可見的跟單帳戶", "表示中のフォロワーをすべて選択", "표시된 팔로워 모두 선택" };
            t["Untick all visible followers"] = new[] { "取消勾選所有可見的跟單帳戶", "表示中のフォロワーをすべて選択解除", "표시된 팔로워 모두 선택 해제" };
            t["Ratio: edit each account row"] = new[] { "比例：請於各帳戶列編輯", "比率：各口座行で編集", "비율: 각 계좌 행에서 편집" };
            t["Bulk  "] = new[] { "批次  ", "一括  ", "일괄  " };
            t["Amount for the highlighted accounts · account currency · 0 = off"] = new[] { "選取的帳戶之金額 · 帳戶幣別 · 0 = 關閉", "選択中の口座の金額 · 口座通貨 · 0 = オフ", "선택한 계좌의 금액 · 계좌 통화 · 0 = 끔" };
            t["Bulk amount"] = new[] { "批次金額", "一括金額", "일괄 금액" };
            t["Set Target on highlighted accounts"] = new[] { "為選取的帳戶設定 Tgt", "選択中の口座に Tgt を設定", "선택한 계좌에 Tgt 설정" };
            t["Set Daily loss on highlighted accounts"] = new[] { "為選取的帳戶設定每日虧損上限", "選択中の口座に日次損失上限を設定", "선택한 계좌에 일일 손실 한도 설정" };
            t["ATM on"] = new[] { "ATM 開", "ATM オン", "ATM 켬" };
            t["ATM exit-mirroring ON for highlighted accounts"] = new[] { "為選取的帳戶開啟 ATM 出場鏡像", "選択中の口座の ATM 決済ミラーリングをオン", "선택한 계좌의 ATM 청산 미러링 켬" };
            t["ATM off"] = new[] { "ATM 關", "ATM オフ", "ATM 끔" };
            t["ATM exit-mirroring OFF for highlighted accounts"] = new[] { "為選取的帳戶關閉 ATM 出場鏡像", "選択中の口座の ATM 決済ミラーリングをオフ", "선택한 계좌의 ATM 청산 미러링 끔" };
            t["Div: notify"] = new[] { "偏離：通知", "乖離：通知", "불일치: 알림" };
            t["On divergence: only lock, show red and notify (default) for highlighted accounts"] = new[] { "發生偏離時：僅鎖定、以紅色顯示並通知（預設）（套用於選取的帳戶）", "乖離発生時：ロック、赤表示、通知のみ（デフォルト）（選択中の口座）", "불일치 발생 시: 잠금, 빨간색 표시 및 알림만 수행(기본값) (선택한 계좌)" };
            t["Div: flatten 1x"] = new[] { "偏離：平倉 1 次", "乖離：全決済 1回", "불일치: 청산 1회" };
            t["On divergence: request ONE flatten, never retried (saved only; NOT active in this alpha) for highlighted accounts"] = new[] { "發生偏離時：僅請求一次平倉，不會重試（僅儲存；本 alpha 版本中尚未啟用）（套用於選取的帳戶）", "乖離発生時：全決済を1回だけ要求、再試行なし（保存のみ；このアルファ版では有効ではありません）（選択中の口座）", "불일치 발생 시: 청산을 1회만 요청하며 재시도하지 않음(저장만 됨; 이 알파 버전에서는 활성화되지 않음) (선택한 계좌)" };
            t["From Leader"] = new[] { "取自主帳戶", "リーダーから", "리더에서" };
            t["Copy the leader row's Tgt / DLL / ATM to highlighted followers (all visible if none highlighted). One-time copy; edit any row afterwards. Ratio and ticks are not copied."] = new[] { "將主帳戶列的 Tgt / DLL / ATM 複製到選取的跟單帳戶（若未選取則套用於所有可見者）。僅複製一次；之後可自行編輯任一列。比例與勾選不會被複製。", "リーダー行の Tgt / DLL / ATM を選択中のフォロワーにコピー（未選択の場合は表示中のすべて）。1回限りのコピー；以後は各行を編集可能。比率とチェックはコピーされません。", "리더 행의 Tgt / DLL / ATM을 선택한 팔로워에 복사합니다(선택이 없으면 표시된 모든 팔로워). 1회성 복사이며 이후 각 행을 편집할 수 있습니다. 비율과 선택 표시는 복사되지 않습니다." };
            t["Optional display alias"] = new[] { "選填顯示別名", "表示エイリアス（任意）", "표시 별칭(선택)" };
            t["Independent daily local profit target · account CCY / 0 = off"] = new[] { "獨立的每日本機獲利目標 · 帳戶 CCY / 0 = 關閉", "独立した日次ローカル利益目標 · 口座 CCY / 0 = オフ", "독립 일일 로컬 수익 목표 · 계좌 CCY / 0 = 끔" };
            t["Independent daily loss limit · positive amount / 0 = off"] = new[] { "獨立的每日虧損上限 · 正數金額 / 0 = 關閉", "独立した日次損失上限 · 正の金額 / 0 = オフ", "독립 일일 손실 한도 · 양수 금액 / 0 = 끔" };
            t["Signed position · + long / − short"] = new[] { "帶正負號的持倉 · + 多 / − 空", "符号付きポジション · + ロング / − ショート", "부호 있는 포지션 · + 매수 / − 매도" };
            t["Observed session P/L in account currency · baseline is not historical daily accounting"] = new[] { "以帳戶幣別計的觀察到的本次連線 P/L · 基準值並非歷史每日帳務", "口座通貨での観測セッション P/L · ベースラインは過去の日次会計ではありません", "계좌 통화 기준 관측된 세션 P/L · 기준값은 과거 일일 회계가 아님" };
            t["CashValue in account currency; not NetLiquidation / net equity"] = new[] { "以帳戶幣別計的 CashValue；並非 NetLiquidation / 淨值", "口座通貨の CashValue；NetLiquidation / 純資産ではありません", "계좌 통화 기준 CashValue; NetLiquidation / 순자산이 아님" };
            t["Batch risk / presets"] = new[] { "批次風控 / 預設組合", "一括リスク / プリセット", "일괄 리스크 / 프리셋" };
            t["Batch risk / presets · review hold"] = new[] { "批次風控 / 預設組合 · 審查封鎖", "一括リスク / プリセット · レビュー保留", "일괄 리스크 / 프리셋 · 검토 보류" };
            t["Save settings"] = new[] { "儲存設定", "設定を保存", "설정 저장" };
            t["Trading-day boundary / timezone"] = new[] { "交易日分界 / 時區", "取引日の境界 / タイムゾーン", "거래일 경계 / 시간대" };
            t["Pause highlighted accounts"] = new[] { "暫停選取的帳戶", "選択中の口座を一時停止", "선택한 계좌 일시정지" };
            t["Resume highlighted accounts · latch unchanged / no auto-arm"] = new[] { "恢復選取的帳戶 · 鎖存狀態不變 / 不會自動武裝", "選択中の口座を再開 · ラッチは変更なし / 自動アームなし", "선택한 계좌 재개 · 래치 변경 없음 / 자동 활성화 없음" };
            t["Flatten highlighted accounts (market-closes their positions)"] = new[] { "平倉選取的帳戶（以市價平掉其持倉）", "選択中の口座を全決済（ポジションを成行で決済）", "선택한 계좌 청산(포지션을 시장가로 청산)" };
            t["Flatten highlighted accounts · review hold"] = new[] { "平倉選取的帳戶 · 審查封鎖", "選択中の口座を全決済 · レビュー保留", "선택한 계좌 청산 · 검토 보류" };
            t["Flatten ALL displayed accounts (market-closes every position shown)"] = new[] { "平倉所有顯示的帳戶（以市價平掉所示的每一筆持倉）", "表示中のすべての口座を全決済（表示中のすべてのポジションを成行で決済）", "표시된 모든 계좌 청산(표시된 모든 포지션을 시장가로 청산)" };
            t["Flatten all displayed accounts · review hold"] = new[] { "平倉所有顯示的帳戶 · 審查封鎖", "表示中のすべての口座を全決済 · レビュー保留", "표시된 모든 계좌 청산 · 검토 보류" };
            t["Live (RO)"] = new[] { "即時（唯讀）", "ライブ（読取専用）", "라이브(읽기 전용)" };
            t["Display live accounts read-only · live copying blocked"] = new[] { "以唯讀方式顯示即時帳戶 · 禁止即時帳戶跟單", "ライブ口座を読取専用で表示 · ライブ口座へのコピーはブロック", "라이브 계좌를 읽기 전용으로 표시 · 라이브 복사 차단됨" };
            t["Grp"] = new[] { "組", "グル", "그룹" };
            t["Group account rows"] = new[] { "帳戶列分組", "口座行をグループ化", "계좌 행 그룹화" };
            t["Broker account"] = new[] { "券商帳戶", "ブローカー口座", "브로커 계좌" };
            t["Display alias"] = new[] { "顯示別名", "表示エイリアス", "표시 별칭" };
            t["Account group"] = new[] { "帳戶群組", "口座グループ", "계좌 그룹" };
            t["Daily local profit target · 0 = off"] = new[] { "每日本機獲利目標 · 0 = 關閉", "日次ローカル利益目標 · 0 = オフ", "일일 로컬 수익 목표 · 0 = 끔" };
            t["Daily loss limit · positive value / 0 = off"] = new[] { "每日虧損上限 · 正數值 / 0 = 關閉", "日次損失上限 · 正の値 / 0 = オフ", "일일 손실 한도 · 양수 값 / 0 = 끔" };
            t["Max"] = new[] { "上限", "最大", "최대" };
            t["Maximum exact-instrument contracts"] = new[] { "完全相同商品的最大合約數", "同一銘柄の最大契約数", "동일 종목 최대 계약 수" };
            t["Observed session P/L"] = new[] { "觀察到的本次連線 P/L", "観測セッション P/L", "관측된 세션 P/L" };
            t["CCY"] = new[] { "CCY", "CCY", "CCY" };
            t["Account currency"] = new[] { "帳戶幣別", "口座通貨", "계좌 통화" };
            t["Signed position"] = new[] { "帶正負號的持倉", "符号付きポジション", "부호 있는 포지션" };
            t["Connection status"] = new[] { "連線狀態", "接続ステータス", "연결 상태" };
            t["St"] = new[] { "狀態", "状態", "상태" };
            t["Status"] = new[] { "狀態", "ステータス", "상태" };
            t["Copier / risk state"] = new[] { "跟單 / 風控狀態", "コピー / リスク状態", "복사 / 리스크 상태" };
            t["Preset"] = new[] { "預設組合", "プリセット", "프리셋" };
            t["Editable firm template"] = new[] { "可編輯的券商範本", "編集可能なファーム用テンプレート", "편집 가능한 펌 템플릿" };
            t["Cons"] = new[] { "一致", "一貫", "일관" };
            t["Consistency advisory · not a broker guarantee"] = new[] { "一致性參考提示 · 非券商保證", "一貫性の参考情報 · ブローカーの保証ではありません", "일관성 참고 안내 · 브로커의 보증이 아님" };
            t["Account risk / template / history"] = new[] { "帳戶風控 / 範本 / 歷史", "口座リスク / テンプレート / 履歴", "계좌 리스크 / 템플릿 / 기록" };
            t["OFF cancels owned entries, retains exits · Re-follow explicit / only when clean · ATM OFF may be unprotected · Local guards only"] = new[] { "OFF 會取消自有進場單、保留出場單 · 重新跟單需明確操作 / 僅在乾淨狀態下 · ATM OFF 可能無保護 · 僅限本機防護", "OFF は自己の新規注文を取消し、決済注文は維持 · 再フォローは明示操作 / クリーンな場合のみ · ATM OFF は保護なしとなる可能性あり · ローカルガードのみ", "OFF는 자체 진입 주문을 취소하고 청산 주문은 유지 · 재팔로우는 명시적 / 정상 상태일 때만 · ATM OFF는 보호되지 않을 수 있음 · 로컬 가드만 적용" };
            t["Clear log"] = new[] { "清除日誌", "ログをクリア", "로그 지우기" };
            t["Clear this log view (does not affect settings, orders or saved latches)"] = new[] { "清除此日誌檢視（不影響設定、委託單或已儲存的鎖存）", "このログ表示をクリア（設定・注文・保存済みラッチには影響しません）", "이 로그 보기 지우기(설정, 주문, 저장된 래치에는 영향 없음)" };
            t["Demo / not broker data"] = new[] { "示範 / 非券商資料", "デモ / ブローカーデータではありません", "데모 / 브로커 데이터 아님" };
            t["Locked while attached or on review hold"] = new[] { "連線期間或審查封鎖時已鎖定", "接続中またはレビュー保留中はロック", "연결 중이거나 검토 보류 중에는 잠김" };
            t["Highlight at least one follower account first"] = new[] { "請先選取至少一個跟單帳戶", "先にフォロワー口座を1つ以上選択してください", "먼저 팔로워 계좌를 하나 이상 선택하세요" };
            t["Enter a plain amount like 1000 (0 = off); no commas, signs or text"] = new[] { "請輸入純數字金額，例如 1000（0 = 關閉）；不可含逗號、符號或文字", "1000 のような数字のみの金額を入力（0 = オフ）。カンマ・符号・文字は不可", "1000처럼 숫자만 입력하세요 (0 = 끔). 쉼표, 부호, 문자는 사용할 수 없습니다" };
            t["ATM value must be on or off"] = new[] { "ATM 值必須為開啟或關閉", "ATM の値はオンまたはオフにしてください", "ATM 값은 켜짐 또는 꺼짐이어야 합니다" };
            t["Unknown bulk field"] = new[] { "未知的批次欄位", "不明な一括設定フィールド", "알 수 없는 일괄 설정 필드" };
            t["No leader row to copy from"] = new[] { "沒有可供複製的主帳戶列", "コピー元のリーダー行がありません", "복사할 리더 행이 없습니다" };
            t["No follower accounts to update"] = new[] { "沒有可更新的跟單帳戶", "更新対象のフォロワー口座がありません", "업데이트할 팔로워 계좌가 없습니다" };
            t["Leader Tgt / DLL are not valid amounts"] = new[] { "主帳戶的 Tgt / DLL 不是有效金額", "リーダーの Tgt / DLL が有効な金額ではありません", "리더의 Tgt / DLL이 유효한 금액이 아닙니다" };
            t["Copied leader Tgt / DLL / ATM to followers (one-time); Ratio and ticks unchanged. Edit any row to fine-tune."] = new[] { "已將主帳戶的 Tgt / DLL / ATM 複製到跟單帳戶（僅一次）；比例與跳動點數未變更。可編輯任一列進行微調。", "リーダーの Tgt / DLL / ATM をフォロワーにコピーしました（1回のみ）。比率とティックは変更なし。任意の行を編集して微調整できます。", "리더의 Tgt / DLL / ATM을 팔로워에 복사했습니다 (1회성). 비율과 틱은 변경되지 않았습니다. 행을 편집하여 세부 조정할 수 있습니다." };
            t["Mixed CCY"] = new[] { "混合 CCY", "CCY 混在", "CCY 혼합" };
            t["REVIEW HOLD · unexpected attachment"] = new[] { "審查封鎖 · 非預期的附加項目", "レビュー保留 · 想定外のアタッチメント", "검토 보류 · 예상치 못한 첨부" };
            t["Error — guarded pause; verify exposure manually"] = new[] { "錯誤 — 保護性暫停；請手動確認持倉風險", "エラー — ガード付き一時停止。エクスポージャーを手動で確認してください", "오류 — 보호 일시정지; 노출 포지션을 수동으로 확인하세요" };
            t["PUBLIC ALPHA SAFETY HOLD — Start blocked for ALL providers, including Sim / Playback. Trading DISABLED."] = new[] { "公開 Alpha 安全封鎖 — 所有報價/交易供應端（含 Sim / Playback）均禁止開始。交易已停用。", "パブリックアルファ安全保留 — Sim / Playback を含む全プロバイダーで開始をブロック。取引は無効。", "퍼블릭 알파 안전 보류 — Sim / Playback을 포함한 모든 제공자에서 시작이 차단되었습니다. 거래 비활성화." };
            t["ALL-ACCOUNT SAFETY HOLD — Start blocked, including Simulator / Playback. UI preview only; provider acceptance unverified."] = new[] { "全帳戶安全封鎖 — 禁止開始，包含 Simulator / Playback。僅為介面預覽；供應端接受狀況未經驗證。", "全口座安全保留 — Simulator / Playback を含め開始をブロック。UI プレビューのみ。プロバイダーの受理は未確認。", "전체 계좌 안전 보류 — Simulator / Playback을 포함하여 시작이 차단되었습니다. UI 미리보기 전용; 제공자 수락 여부는 확인되지 않았습니다." };
            t["Already managing a session. Stop while flat first."] = new[] { "已在管理一個工作階段。請先於持倉歸零後停止。", "すでにセッションを管理中。先にフラット後に停止してください。", "이미 세션을 관리 중입니다. 먼저 청산 후 중지하세요." };
            t["LIVE BLOCKED in v0.2. Only Simulator / Playback; no live override is provided."] = new[] { "v0.2 禁止實盤。僅支援 Simulator / Playback；不提供實盤覆寫選項。", "v0.2 ではライブをブロック。Simulator / Playback のみ。ライブへの上書き手段はありません。", "v0.2에서는 실거래가 차단됩니다. Simulator / Playback만 지원하며 실거래 우회 옵션은 제공되지 않습니다." };
            t["Start refused: select unlocked, valid followers."] = new[] { "已拒絕開始：請選取未鎖定且有效的跟單帳戶。", "開始を拒否: ロックされていない有効なフォロワーを選択してください。", "시작 거부: 잠기지 않은 유효한 팔로워를 선택하세요." };
            t["Start refused: all selected accounts must be connected."] = new[] { "已拒絕開始：所有已選帳戶都必須已連線。", "開始を拒否: 選択したすべての口座が接続されている必要があります。", "시작 거부: 선택한 모든 계좌가 연결되어 있어야 합니다." };
            t["Start refused: another copier window owns one of these accounts."] = new[] { "已拒絕開始：另一個跟單視窗正在管理其中一個帳戶。", "開始を拒否: 別のコピーウィンドウがこれらの口座の1つを管理しています。", "시작 거부: 다른 복사 창이 이 계좌 중 하나를 관리하고 있습니다." };
            t["Start superseded by guard"] = new[] { "開始已被防護取代", "開始はガードにより置き換えられました", "시작이 가드에 의해 대체됨" };
            t["Ambiguous provider order identity / transition — no catch-up, manual recovery required"] = new[] { "供應端委託單身分／狀態轉換不明確 — 不補單，需人工處理", "プロバイダーの注文識別／遷移が曖昧 — キャッチアップなし、手動での復旧が必要", "제공자 주문 식별/전환이 모호함 — 캐치업 없음, 수동 복구 필요" };
            t["Invalid / unsupported leader order — entries blocked"] = new[] { "無效／不支援的主帳戶委託單 — 已封鎖進場", "無効／未対応のリーダー注文 — エントリーをブロック", "유효하지 않거나 지원되지 않는 리더 주문 — 진입 차단" };
            t["Leader disconnected — no catch-up"] = new[] { "主帳戶已斷線 — 不補單", "リーダー切断 — キャッチアップなし", "리더 연결 끊김 — 캐치업 없음" };
            t["Session history bound reached — guarded pause; stop flat and restart."] = new[] { "已達工作階段歷史上限 — 保護性暫停；請於持倉歸零後停止並重新啟動。", "セッション履歴の上限に到達 — ガード付き一時停止。フラット後に停止して再起動してください。", "세션 기록 한도 도달 — 보호 일시정지; 청산 후 중지하고 다시 시작하세요." };
            t["Oversize follower quantity"] = new[] { "跟單帳戶數量過大", "フォロワー数量が過大", "팔로워 수량 초과" };
            t["Unsupported leader action / type / OCO / time-in-force transition — submitted order unchanged; manual recovery required"] = new[] { "不支援的主帳戶動作／類型／OCO／有效期限轉換 — 已送出的委託單未變更；需人工處理", "未対応のリーダーのアクション／種別／OCO／有効期限の遷移 — 送信済み注文は変更なし。手動での復旧が必要", "지원되지 않는 리더 동작/유형/OCO/유효 기간 전환 — 제출된 주문은 변경되지 않음; 수동 복구 필요" };
            t["Exposure ledger overflow"] = new[] { "風險部位紀錄溢位", "エクスポージャー台帳のオーバーフロー", "노출 원장 오버플로" };
            t["Position / owned execution mismatch — no adoption"] = new[] { "持倉與自有成交不符 — 不接管", "ポジションと自己の約定が不一致 — 引き継ぎなし", "포지션과 자체 체결 불일치 — 인수 안 함" };
            t["Unknown / opposite exposure — exit adoption blocked"] = new[] { "未知／反向持倉 — 已封鎖出場接管", "不明／反対方向のエクスポージャー — 決済の引き継ぎをブロック", "알 수 없거나 반대 방향의 노출 — 청산 인수 차단" };
            t["Unknown excess position — protect owned subset only"] = new[] { "未知的超額持倉 — 僅保護自有部分", "不明な超過ポジション — 自己保有分のみ保護", "알 수 없는 초과 포지션 — 자체 보유분만 보호" };
            t["Ambiguous reversal blocked"] = new[] { "不明確的反轉已封鎖", "曖昧な反転をブロック", "모호한 반전 차단" };
            t["provider call"] = new[] { "供應端呼叫", "プロバイダー呼び出し", "제공자 호출" };
            t["Disconnected — no catch-up"] = new[] { "已斷線 — 不補單", "切断 — キャッチアップなし", "연결 끊김 — 캐치업 없음" };
            t["Exit could not obtain closable exposure within 10s — review protection"] = new[] { "出場單在 10 秒內無法取得可平倉的持倉 — 請檢查保護", "決済が10秒以内に決済可能なエクスポージャーを取得できませんでした — 保護を確認してください", "청산이 10초 내에 청산 가능한 노출을 확보하지 못함 — 보호 상태를 확인하세요" };
            t["Unsupported order type"] = new[] { "不支援的委託單類型", "未対応の注文種別", "지원되지 않는 주문 유형" };
            t["Disconnected — protection may be stale"] = new[] { "已斷線 — 保護可能已過期", "切断 — 保護が古い可能性あり", "연결 끊김 — 보호가 오래되었을 수 있음" };
            t["Ambiguous flat Position conflicts with owned executions — existing exit left unchanged; manual reconciliation required, coverage unverified"] = new[] { "不明確的持倉為零與自有成交衝突 — 現有出場單維持不變；需人工核對，無法確認已有保護", "曖昧なフラット Position が自己の約定と矛盾 — 既存の決済注文は変更なし。手動での照合が必要、保護は確認できません", "모호한 청산 Position이 자체 체결과 충돌 — 기존 청산 주문은 변경되지 않음; 수동 대조 필요, 보호 여부를 확인할 수 없음" };
            t["Cancel working order"] = new[] { "取消掛單", "待機中の注文をキャンセル", "대기 주문 취소" };
            t["Flat Position conflicts with owned executions — exits cancelled to prevent reversal; review protection"] = new[] { "持倉為零與自有成交衝突 — 為防止反轉已取消出場單；請檢查保護", "フラット Position が自己の約定と矛盾 — 反転防止のため決済注文をキャンセル。保護を確認してください", "청산 Position이 자체 체결과 충돌 — 반전 방지를 위해 청산 주문이 취소됨; 보호 상태를 확인하세요" };
            t["Unknown follower order activity — no adoption / manual review"] = new[] { "未知的跟單帳戶委託單活動 — 不接管／需人工檢查", "不明なフォロワー注文アクティビティ — 引き継ぎなし／手動確認", "알 수 없는 팔로워 주문 활동 — 인수 안 함/수동 검토" };
            t["Follower provider identity transition ambiguous — captured fill evidence retained, manual recovery required"] = new[] { "跟單帳戶供應端身分轉換不明確 — 已保留擷取的成交證據，需人工處理", "フォロワーのプロバイダー識別の遷移が曖昧 — 取得済み約定証跡は保持、手動での復旧が必要", "팔로워 제공자 식별 전환이 모호함 — 수집된 체결 증거 보존, 수동 복구 필요" };
            t["Follower execution metadata divergence — manual recovery required"] = new[] { "跟單帳戶成交中繼資料偏離 — 需人工處理", "フォロワーの約定メタデータが乖離 — 手動での復旧が必要", "팔로워 체결 메타데이터 불일치 — 수동 복구 필요" };
            t["Follower terminal-state evidence divergence — older active state cannot revoke terminal acknowledgement; manual recovery required"] = new[] { "跟單帳戶終態證據偏離 — 較舊的有效狀態無法撤銷終態確認；需人工處理", "フォロワーの最終状態の証跡が乖離 — 古いアクティブ状態では最終状態の確認を取り消せません。手動での復旧が必要", "팔로워 최종 상태 증거 불일치 — 이전 활성 상태로는 최종 상태 확인을 철회할 수 없음; 수동 복구 필요" };
            t["OCO multiple exits filled — durable divergence lock; provider atomicity not guaranteed, manual recovery required"] = new[] { "OCO 多筆出場單已成交 — 持久性偏離鎖定；供應端不保證原子性，需人工處理", "OCO の複数の決済注文が約定 — 永続的な乖離ロック。プロバイダーの原子性は保証されません、手動での復旧が必要", "OCO 복수 청산 주문 체결 — 영구 불일치 잠금; 제공자의 원자성은 보장되지 않음, 수동 복구 필요" };
            t["Cancel OCO sibling"] = new[] { "取消 OCO 關聯單", "OCO の対となる注文をキャンセル", "OCO 연결 주문 취소" };
            t["Protective exit terminal with uncovered exposure — manual review required"] = new[] { "保護性出場單已終結但仍有未受保護的持倉 — 需人工檢查", "保護的な決済注文が終了したがエクスポージャーが未カバー — 手動での確認が必要", "보호 청산 주문이 종료되었으나 보호되지 않는 노출 존재 — 수동 검토 필요" };
            t["Entry fill arrived AFTER the leader exit was ignored as 'nothing to close' — no copied exit covers it; manual recovery required; coverage not established"] = new[] { "進場成交在主帳戶出場單被視為「無可平倉」而忽略「之後」才出現 — 沒有任何已複製的出場單涵蓋它；需人工處理；無法確認已有保護", "エントリー約定がリーダーの決済が「決済対象なし」として無視された「後」に到着 — コピーされた決済注文でカバーされていません。手動での復旧が必要、保護は確認できません", "진입 체결이 리더 청산이 '청산할 것 없음'으로 무시된 '후'에 도착 — 복사된 청산 주문이 이를 보호하지 않음; 수동 복구 필요; 보호 여부를 확인할 수 없음" };
            t["Late uncovered entry fill after exit completion — manual recovery required; coverage not established"] = new[] { "出場完成後出現遲到且未受保護的進場成交 — 需人工處理；無法確認已有保護", "決済完了後に遅れて未カバーのエントリー約定が発生 — 手動での復旧が必要、保護は確認できません", "청산 완료 후 늦게 보호되지 않은 진입 체결 발생 — 수동 복구 필요; 보호 여부를 확인할 수 없음" };
            t["Follower order rejected — review protection"] = new[] { "跟單帳戶委託單被拒 — 請檢查保護", "フォロワー注文が拒否されました — 保護を確認してください", "팔로워 주문 거부됨 — 보호 상태를 확인하세요" };
            t["Awaiting provider flat / order confirmation"] = new[] { "等待供應端的平倉／委託單確認", "プロバイダーのフラット／注文確認を待機中", "제공자의 청산/주문 확인 대기 중" };
            t["Flatten unconfirmed — no concurrent recovery exits authorized; verify provider state manually before explicit abandonment"] = new[] { "平倉未獲確認 — 不授權任何並行的復原出場單；明確放棄前請手動確認供應端狀態", "全決済が未確認 — 並行する復旧用決済注文は許可されません。明示的に放棄する前にプロバイダーの状態を手動で確認してください", "청산 미확인 — 동시 복구 청산 주문은 허용되지 않음; 명시적으로 포기하기 전에 제공자 상태를 수동으로 확인하세요" };
            t["Unknown follower order / terminal fill evidence — no adoption; manual recovery required"] = new[] { "未知的跟單帳戶委託單／終態成交證據 — 不接管；需人工處理", "不明なフォロワー注文／最終約定の証跡 — 引き継ぎなし、手動での復旧が必要", "알 수 없는 팔로워 주문/최종 체결 증거 — 인수 안 함; 수동 복구 필요" };
            t["Provider wrapper diverges from captured order / fill evidence — reservations retained; manual recovery required"] = new[] { "供應端封裝物件與擷取的委託單／成交證據不一致 — 保留預留量；需人工處理", "プロバイダーのラッパーが取得済みの注文／約定証跡と乖離 — 予約を保持、手動での復旧が必要", "제공자 래퍼가 수집된 주문/체결 증거와 불일치 — 예약 유지; 수동 복구 필요" };
            t["Position diverges from owned execution ledger — manual recovery required"] = new[] { "持倉與自有成交紀錄偏離 — 需人工處理", "ポジションが自己の約定台帳と乖離 — 手動での復旧が必要", "포지션이 자체 체결 원장과 불일치 — 수동 복구 필요" };
            t["Cancel entry (OFF)"] = new[] { "取消進場單（OFF）", "エントリーをキャンセル（OFF）", "진입 주문 취소 (OFF)" };
            t["GUARDED PAUSE — no new entries; existing lifecycle + new exits still managed"] = new[] { "保護性暫停 — 不再新增進場；既有生命週期與新的出場單仍會管理", "ガード付き一時停止 — 新規エントリーなし。既存のライフサイクルと新規決済は引き続き管理", "보호 일시정지 — 신규 진입 없음; 기존 수명주기와 신규 청산은 계속 관리됨" };
            t["Paused NEW entries. Working entries may still fill; new exits and changes/cancels remain mirrored. Flatten explicitly to remove exposure."] = new[] { "已暫停新進場。掛單進場仍可能成交；新的出場單與修改／取消仍會同步鏡射。請明確執行平倉以移除持倉風險。", "新規エントリーを一時停止。待機中のエントリーは約定する可能性があります。新規決済と変更／キャンセルは引き続きミラーされます。エクスポージャーを解消するには明示的に全決済してください。", "신규 진입을 일시정지했습니다. 대기 중인 진입은 체결될 수 있으며, 신규 청산과 변경/취소는 계속 미러링됩니다. 노출을 제거하려면 명시적으로 청산하세요." };
            t["GUARDED PAUSE — stop blocked until owned executions reconcile flat / no working orders"] = new[] { "保護性暫停 — 在自有成交對帳為持倉歸零／無掛單前，禁止停止", "ガード付き一時停止 — 自己の約定がフラット／待機注文なしで照合されるまで停止をブロック", "보호 일시정지 — 자체 체결이 청산/대기 주문 없음으로 대조될 때까지 중지가 차단됨" };
            t["Stop refused: unresolved execution / order evidence. Manual reconciliation required; coverage is not guaranteed."] = new[] { "已拒絕停止：存在未解決的成交／委託單證據。需人工核對；不保證已有保護。", "停止を拒否: 未解決の約定／注文の証跡があります。手動での照合が必要。保護は保証されません。", "중지 거부: 해결되지 않은 체결/주문 증거가 있습니다. 수동 대조 필요; 보호는 보장되지 않습니다." };
            t["PUBLIC ALPHA SAFETY HOLD — Flatten blocked for ALL providers, including Sim / Playback. Trading DISABLED."] = new[] { "公開 Alpha 安全封鎖 — 所有報價/交易供應端（含 Sim / Playback）均禁止平倉。交易已停用。", "パブリックアルファ安全保留 — Sim / Playback を含む全プロバイダーで全決済をブロック。取引は無効。", "퍼블릭 알파 안전 보류 — Sim / Playback을 포함한 모든 제공자에서 청산이 차단되었습니다. 거래 비활성화." };
            t["ALL-ACCOUNT SAFETY HOLD — Flatten blocked for every account. No trading actions authorized."] = new[] { "全帳戶安全封鎖 — 所有帳戶均禁止平倉。未授權任何交易動作。", "全口座安全保留 — 全口座で全決済をブロック。取引アクションは一切許可されません。", "전체 계좌 안전 보류 — 모든 계좌에서 청산이 차단되었습니다. 어떤 거래 동작도 허용되지 않습니다." };
            t["Flatten blocked: another copier owns an account in this scope."] = new[] { "已封鎖平倉：另一個跟單程式擁有此範圍內的帳戶。", "全決済をブロック: この範囲内の口座を別のコピーが所有しています。", "청산 차단: 다른 복사 프로그램이 이 범위의 계좌를 소유하고 있습니다." };
            t["Flatten leader blocked: other managed followers still have exposure/working orders. Include all managed followers or flatten followers first."] = new[] { "已封鎖主帳戶平倉：其他受管理的跟單帳戶仍有持倉／掛單。請納入所有受管理的跟單帳戶，或先平倉跟單帳戶。", "リーダーの全決済をブロック: 他の管理対象フォロワーにまだエクスポージャー／待機注文があります。管理対象のフォロワーをすべて含めるか、先にフォロワーを全決済してください。", "리더 청산 차단: 다른 관리 대상 팔로워에 아직 노출/대기 주문이 있습니다. 관리 대상 팔로워를 모두 포함하거나 팔로워를 먼저 청산하세요." };
            t["Request threw — provider acceptance unknown"] = new[] { "請求拋出例外 — 供應端是否已接受未知", "リクエストが例外をスロー — プロバイダーの受理は不明", "요청에서 예외 발생 — 제공자 수락 여부를 알 수 없음" };
            t[": Flatten "] = new[] { "：平倉 ", ": 全決済 ", ": 청산 " };
            t["Flatten refused: copier is stopped; no request was sent."] = new[] { "已拒絕平倉：跟單已停止；未送出任何請求。", "全決済を拒否: コピーは停止中。リクエストは送信されていません。", "청산 거부: 복사가 중지되었습니다. 요청은 전송되지 않았습니다." };
            t["Manual flatten abandonment — entries locked; only explicit leader exits managed, no autonomous protection / provider guarantee"] = new[] { "手動平倉已放棄——進場已鎖定；僅管理主帳戶明確的出場，無自主保護／供應端保證", "手動フラット化を放棄——エントリーはロック中；リーダーの明示的な決済のみ管理、自律的な保護／プロバイダー保証なし", "수동 청산 포기 — 진입 잠김; 리더의 명시적 청산만 관리, 자율 보호/제공자 보증 없음" };
            t["Close refused: owned executions / orders unresolved; callbacks and ownership retained. Resolve manually before closing."] = new[] { "拒絕關閉：自有成交／委託尚未解決；回呼與所有權仍保留。請先手動處理後再關閉。", "クローズを拒否：自己の約定／注文が未解決；コールバックと所有権は保持。クローズ前に手動で解決してください。", "닫기 거부: 소유한 체결/주문이 미해결; 콜백과 소유권 유지. 닫기 전에 수동으로 해결하십시오." };
            t["Refresh blocked while managing a session."] = new[] { "管理工作階段期間無法重新整理。", "セッション管理中は更新できません。", "세션 관리 중에는 새로고침할 수 없습니다." };
            t["Settings NOT saved: correct invalid per-account Ratio / row values first."] = new[] { "設定未儲存：請先修正各帳戶無效的比例／列數值。", "設定は保存されていません：口座ごとの無効な比率／行の値を先に修正してください。", "설정이 저장되지 않았습니다: 계좌별 잘못된 비율/행 값을 먼저 수정하십시오." };
            t["REVIEW HOLD: trading and bulk edits disabled. Inspect Dashboard only; use native NT8 for positions."] = new[] { "審查封鎖：交易與批次編輯已停用。僅可檢視儀表板；持倉請使用 NT8 原生介面。", "レビュー保留：取引と一括編集は無効。ダッシュボードの確認のみ可；ポジションはネイティブの NT8 を使用。", "검토 보류: 거래 및 일괄 편집 비활성화. 대시보드만 확인 가능; 포지션은 NT8 기본 화면을 사용하십시오." };
            t["Settings saved; startup stays disarmed."] = new[] { "設定已儲存；啟動維持未武裝狀態。", "設定を保存しました；起動時は非アーム状態のまま。", "설정이 저장되었습니다; 시작 시 비무장 상태 유지." };
            t["Settings / enabled edits blocked during session. Pause per account or stop when flat first."] = new[] { "工作階段期間無法編輯設定／啟用狀態。請先依帳戶暫停，或於持倉歸零後停止。", "セッション中は設定／有効化の編集不可。先に口座ごとに一時停止するか、フラット後に停止してください。", "세션 중에는 설정/활성화 편집이 차단됩니다. 먼저 계좌별로 일시정지하거나 청산 후 중지하십시오." };
            t["Start refused: unresolved inline validation error."] = new[] { "拒絕開始：尚有未解決的內嵌驗證錯誤。", "開始を拒否：未解決のインライン検証エラーがあります。", "시작 거부: 해결되지 않은 인라인 검증 오류가 있습니다." };
            t["Start blocked: settings persistence error. Review / reset while disarmed."] = new[] { "開始已封鎖：設定持久化錯誤。請於未武裝狀態下檢視／重設。", "開始をブロック：設定の永続化エラー。非アーム状態で確認／リセットしてください。", "시작 차단: 설정 저장 오류. 비무장 상태에서 검토/초기화하십시오." };
            t["Choose a simulation leader."] = new[] { "請選擇模擬主帳戶。", "シミュレーションのリーダーを選択してください。", "시뮬레이션 리더를 선택하십시오." };
            t["Unknown exact instrument."] = new[] { "商品不明確。", "銘柄を特定できません。", "정확한 종목을 알 수 없습니다." };
            t["Enable at least one unpaused follower."] = new[] { "請至少啟用一個未暫停的跟單帳戶。", "一時停止中でないフォロワーを少なくとも1つ有効にしてください。", "일시정지되지 않은 팔로워를 하나 이상 활성화하십시오." };
            t["Invalid finite settings or risk-latched follower. Review Settings / confirmed reset."] = new[] { "有限數值設定無效，或跟單帳戶已觸發風控鎖存。請檢視設定／確認後重設。", "有限値の設定が無効、またはリスクラッチ中のフォロワーがあります。設定を確認／確認付きリセットしてください。", "유한 값 설정이 잘못되었거나 리스크 래치된 팔로워가 있습니다. 설정을 검토/확인 후 초기화하십시오." };
            t["Use global guarded pause for leader."] = new[] { "請對主帳戶使用全域保護性暫停。", "リーダーにはグローバルのガード付き一時停止を使用してください。", "리더에는 전체 보호 일시정지를 사용하십시오." };
            t["Select accounts first."] = new[] { "請先選取帳戶。", "先に口座を選択してください。", "먼저 계좌를 선택하십시오." };
            t["Stop flat before risk edits. Active engine uses immutable settings snapshots."] = new[] { "請先於持倉歸零後停止，再編輯風控。運行中的引擎使用不可變的設定快照。", "リスク編集の前にフラット後に停止してください。稼働中のエンジンは不変の設定スナップショットを使用します。", "리스크 편집 전에 청산 후 중지하십시오. 실행 중인 엔진은 변경 불가능한 설정 스냅샷을 사용합니다." };
            t["Flat: no account."] = new[] { "平倉：無帳戶。", "決済：口座なし。", "청산: 계좌 없음." };
            t["REVIEW HOLD: Flat disabled."] = new[] { "審查封鎖：平倉已停用。", "レビュー保留：決済は無効。", "검토 보류: 청산 비활성화." };
            t["Flat: the leader row is not a follower; use Flatten all or flatten it in NinjaTrader."] = new[] { "平倉：主帳戶列並非跟單帳戶；請使用「全部平倉」，或於 NinjaTrader 中自行平倉。", "決済：リーダー行はフォロワーではありません；「すべて全決済」を使うか、NinjaTrader で決済してください。", "청산: 리더 행은 팔로워가 아닙니다; 전체 청산을 사용하거나 NinjaTrader에서 직접 청산하십시오." };
            t["No accounts in flatten scope."] = new[] { "平倉範圍內沒有帳戶。", "全決済の対象範囲に口座がありません。", "청산 범위에 계좌가 없습니다." };
            t["Flatten: exact instrument unknown."] = new[] { "平倉：商品不明確。", "全決済：銘柄を特定できません。", "청산: 정확한 종목을 알 수 없습니다." };
            t["Flatten refused: live account in scope; v0.2 simulation only."] = new[] { "拒絕平倉：範圍內含真實帳戶；v0.2 僅支援模擬。", "全決済を拒否：対象にライブ口座を含みます；v0.2 はシミュレーションのみ。", "청산 거부: 범위에 실계좌가 포함됨; v0.2는 시뮬레이션 전용." };
            t["Account list changed — will refresh when you finish editing."] = new[] { "帳戶清單已變更——將於編輯完成後重新整理。", "口座リストが変更されました——編集完了後に更新します。", "계좌 목록이 변경되었습니다 — 편집이 끝나면 새로고침합니다." };
            t["History required"] = new[] { "需要歷史資料", "履歴が必要", "기록 필요" };
            t["Settings invalid"] = new[] { "設定無效", "設定が無効", "설정 오류" };
            t["Telemetry invalid"] = new[] { "遙測資料無效", "テレメトリが無効", "텔레메트리 오류" };
            t["Daily loss"] = new[] { "每日虧損", "日次損失", "일일 손실" };
            t["Drawdown floor"] = new[] { "回撤底線", "ドローダウン下限", "드로다운 하한" };
            t["Unknown risk field"] = new[] { "未知的風控欄位", "不明なリスク項目", "알 수 없는 리스크 필드" };
            t["Invalid risk value"] = new[] { "風控值無效", "無効なリスク値", "잘못된 리스크 값" };
            t["Correct the highlighted risk values before Apply."] = new[] { "套用前請先修正標示的風控值。", "適用前にハイライトされたリスク値を修正してください。", "적용 전에 강조 표시된 리스크 값을 수정하십시오." };
            t["Every number must be finite; ratio / max quantity positive; monetary guards nonnegative."] = new[] { "所有數值必須為有限值；比例／最大數量須為正數；金額防護須為非負數。", "すべての数値は有限値であること；比率／最大数量は正の数；金額ガードは非負であること。", "모든 숫자는 유한 값이어야 합니다; 비율/최대 수량은 양수; 금액 가드는 음수가 아니어야 합니다." };
            t["Supply positive confirmed high-water and trail distance first."] = new[] { "請先提供正數的已確認高水位與追蹤距離。", "先に正の確認済みハイウォーターとトレール距離を指定してください。", "먼저 양수의 확인된 고점과 트레일 거리를 입력하십시오." };
            t["Unsupported settings version"] = new[] { "不支援的設定版本", "未対応の設定バージョン", "지원되지 않는 설정 버전" };
            t["Persisted lock"] = new[] { "已持久化的鎖定", "永続化されたロック", "저장된 잠금" };
            t["Invalid finite settings: "] = new[] { "有限數值設定無效： ", "有限値の設定が無効： ", "잘못된 유한 값 설정: " };
            t["Duplicate account settings"] = new[] { "帳戶設定重複", "口座設定が重複", "계좌 설정 중복" };
            t["Invalid latch journal"] = new[] { "鎖存日誌無效", "無効なラッチジャーナル", "잘못된 래치 저널" };
            t["\nSource PT "] = new[] { "\n來源 PT ", "\nソース PT ", "\n출처 PT " };
            t["\nSource: propfirmsdrama.com/full-comparison/ — 2026-10-02; TPT 2026-08-31. Verify current firm rules. Funded/eval stages differ; scaling may lower cap.\nDD amount is NOT an absolute equity floor. Con"] = new[] { "\n來源：propfirmsdrama.com/full-comparison/ — 2026-10-02；TPT 2026-08-31。請核對最新的公司規則。資助／評估階段不同；規模擴增可能降低上限。\nDD 金額並非絕對權益底線。Con", "\nソース：propfirmsdrama.com/full-comparison/ — 2026-10-02；TPT 2026-08-31。最新のファーム規則を確認すること。ファンデッド／評価の段階で異なる；スケーリングで上限が下がる場合あり。\nDD 金額は絶対的な資産下限ではありません。Con", "\n출처: propfirmsdrama.com/full-comparison/ — 2026-10-02; TPT 2026-08-31. 최신 회사 규정을 확인하십시오. 펀디드/평가 단계에 따라 다름; 스케일링으로 한도가 낮아질 수 있음.\nDD 금액은 절대적인 자산 하한이 아닙니다. Con" };
            t["No followers selected."] = new[] { "未選取跟單帳戶。", "フォロワーが選択されていません。", "선택된 팔로워가 없습니다." };
            t["Activity detected while arming. Start again only when flat."] = new[] { "武裝期間偵測到活動。請僅在持倉歸零時再次開始。", "アーム中にアクティビティを検出しました。フラットのときのみ再度開始してください。", "무장 중 활동이 감지되었습니다. 청산 상태일 때만 다시 시작하십시오." };
            t["Risk guard latched at start; review telemetry/baseline."] = new[] { "啟動時風控防護已鎖存；請檢視遙測資料／基準值。", "開始時にリスクガードがラッチされました；テレメトリ／ベースラインを確認してください。", "시작 시 리스크 가드가 래치되었습니다; 텔레메트리/기준값을 검토하십시오." };
            t[" UNPROTECTED (no protective exit working) — check NinjaTrader now and place or flatten by hand; this copier will not create it"] = new[] { " 未受保護（無保護出場單在運作）——請立即檢查 NinjaTrader，並手動掛出保護單或平倉；本跟單程式不會自行建立", " 保護なし（保護決済注文が稼働していません）——今すぐ NinjaTrader を確認し、手動で注文を出すか全決済してください；このコピー機能は自動では作成しません", " 보호 없음(작동 중인 보호 청산 주문 없음) — 지금 NinjaTrader를 확인하고 수동으로 주문하거나 청산하십시오; 이 복사기는 자동으로 생성하지 않습니다" };
            t["provider dispatcher stopped; call was NOT sent"] = new[] { "供應端調度器已停止；該呼叫未送出", "プロバイダーのディスパッチャーが停止；呼び出しは送信されませんでした", "제공자 디스패처 중지됨; 호출이 전송되지 않았습니다" };
            t["entry not sent: an earlier provider call for this account failed"] = new[] { "進場單未送出：此帳戶先前的供應端呼叫已失敗", "エントリー未送信：この口座で先行するプロバイダー呼び出しが失敗", "진입 미전송: 이 계좌의 이전 제공자 호출이 실패함" };
            t["Confirmed flat"] = new[] { "已確認平倉", "フラット確認済み", "청산 확인됨" };
            t["Protective coverage unknown — requested, provider acceptance unconfirmed; no retry / chase"] = new[] { "保護狀況不明——已提出請求，供應端是否接受未確認；不重試／不追單", "保護の状況は不明——要求済み、プロバイダーの受理は未確認；再試行／追随なし", "보호 상태 불명 — 요청됨, 제공자 수락 미확인; 재시도/추격 없음" };
            t["OFF — cancellation pending"] = new[] { "OFF——取消待處理", "OFF——キャンセル保留中", "OFF — 취소 대기 중" };
            t["OFF / exits managed"] = new[] { "OFF／出場管理中", "OFF／決済管理中", "OFF / 청산 관리 중" };
            t["Paused / exits managed"] = new[] { "已暫停／出場管理中", "一時停止中／決済管理中", "일시정지됨 / 청산 관리 중" };
            t["DISARMED — explicit Start required"] = new[] { "未武裝——須明確按下開始", "非アーム——明示的な開始が必要", "비무장 — 명시적인 시작 필요" };
            t["FLATTEN requested — new entries blocked; verify flat status"] = new[] { "已要求平倉——新進場已封鎖；請確認持倉歸零狀態", "全決済を要求——新規エントリーはブロック；フラット状態を確認してください", "청산 요청됨 — 신규 진입 차단; 청산 상태를 확인하십시오" };
            t["Manually abandoned after provider reconciliation; late close risk remains"] = new[] { "供應端對帳後已手動放棄；仍有延遲平倉風險", "プロバイダーの照合後に手動で放棄；遅延クローズのリスクが残ります", "제공자 대조 후 수동으로 포기됨; 지연 청산 위험이 남아 있음" };
            t["Daily target"] = new[] { "每日目標", "日次目標", "일일 목표" };
            t[" must be a plain amount like 1000 (0 = off): no commas, signs or text"] = new[] { " 必須為純數字金額，例如 1000（0＝關閉）：不可含逗號、正負號或文字", " は1000のような数値のみの金額にしてください（0＝オフ）：カンマ、符号、文字は不可", " 은(는) 1000과 같은 숫자만의 금액이어야 합니다 (0 = 끔): 쉼표, 부호, 문자 불가" };
            t["Max contracts must be a whole number >= 1"] = new[] { "最大合約數必須為 ≥ 1 的整數", "最大契約数は1以上の整数にしてください", "최대 계약 수는 1 이상의 정수여야 합니다" };
            t["Ratio must be a plain positive number like 1 or 0.5: no commas, signs or text"] = new[] { "比例必須為純正數，例如 1 或 0.5：不可含逗號、正負號或文字", "比率は1や0.5のような数値のみの正の数にしてください：カンマ、符号、文字は不可", "비율은 1 또는 0.5와 같은 숫자만의 양수여야 합니다: 쉼표, 부호, 문자 불가" };
            t["Max must be a whole number >= 1"] = new[] { "最大值必須為 ≥ 1 的整數", "Max は1以上の整数にしてください", "Max는 1 이상의 정수여야 합니다" };
            t["Close blocked: copier still manages positions / working orders. Guarded pause keeps exits managed. Flatten or resolve the selected instrument, then Stop when flat."] = new[] { "關閉已封鎖：跟單程式仍在管理持倉／掛單。保護性暫停會持續管理出場。請先平倉或處理所選商品，再於持倉歸零後停止。", "クローズをブロック：コピー機能がまだポジション／待機中の注文を管理しています。ガード付き一時停止は決済の管理を継続します。選択した銘柄を全決済または解決してから、フラット後に停止してください。", "닫기 차단: 복사기가 아직 포지션/대기 주문을 관리 중입니다. 보호 일시정지는 청산 관리를 유지합니다. 선택한 종목을 청산하거나 해결한 후 청산 후 중지하십시오." };
            t["Local risk controls"] = new[] { "本機風控", "ローカルリスク管理", "로컬 리스크 관리" };
            t["All values editable. Zero monetary limits = OFF. Values in account denomination. No compliance guarantee, no historical high inferred. Batch overwrites the shown settings on every selected account."] = new[] { "所有數值皆可編輯。金額上限為零＝OFF。數值以帳戶幣別計。不保證合規，不推斷歷史高點。批次會以顯示的設定覆寫每個所選帳戶。", "すべての値を編集可能。金額上限が0＝OFF。値は口座の通貨単位。コンプライアンスは保証されず、過去の高値は推定しません。一括適用は表示中の設定を選択した全口座に上書きします。", "모든 값을 편집할 수 있습니다. 금액 한도 0 = OFF. 값은 계좌 통화 기준. 규정 준수를 보장하지 않으며 과거 고점을 추정하지 않습니다. 일괄 적용은 표시된 설정을 선택한 모든 계좌에 덮어씁니다." };
            t["Custom: confirm your absolute floor and maximum instrument quantity manually."] = new[] { "自訂：請手動確認您的絕對底線與商品最大數量。", "カスタム：絶対下限と銘柄の最大数量を手動で確認してください。", "사용자 지정: 절대 하한과 종목 최대 수량을 수동으로 확인하십시오." };
            t["Custom settings"] = new[] { "自訂設定", "カスタム設定", "사용자 지정 설정" };
            t["Explicitly apply optional / soft DLL if source has an amount"] = new[] { "若來源含金額，明確套用選用／軟性 DLL", "ソースに金額がある場合、任意／ソフト DLL を明示的に適用", "소스에 금액이 있으면 선택/소프트 DLL을 명시적으로 적용" };
            t["Load selected template into editable fields"] = new[] { "將所選範本載入可編輯欄位", "選択したテンプレートを編集可能なフィールドに読み込む", "선택한 템플릿을 편집 가능한 필드로 불러오기" };
            t["Source mini cap cannot be safely mapped. Set MaxContracts manually for your exact instrument / current scaling."] = new[] { "來源迷你上限無法安全對應。請依您的確切商品／目前縮放比例手動設定 MaxContracts。", "ソースのミニ上限は安全にマッピングできません。正確な銘柄／現在のスケーリングに合わせて MaxContracts を手動で設定してください。", "소스의 미니 한도는 안전하게 매핑할 수 없습니다. 정확한 종목/현재 스케일링에 맞게 MaxContracts를 수동으로 설정하십시오." };
            t["Manual quantity required"] = new[] { "需手動輸入數量", "数量の手動入力が必要", "수량 수동 입력 필요" };
            t["Daily target (local observed P&L)"] = new[] { "每日目標（本機觀察損益）", "日次目標（ローカル観測 P&L）", "일일 목표(로컬 관측 손익)" };
            t["Daily loss (positive amount)"] = new[] { "每日虧損（正數金額）", "日次損失（正の金額）", "일일 손실(양수 금액)" };
            t["Confirmed absolute equity floor"] = new[] { "已確認的絕對淨值下限", "確認済みの絶対エクイティ下限", "확인된 절대 자기자본 하한" };
            t["P&L telemetry baseline (editable)"] = new[] { "損益遙測基準值（可編輯）", "P&L テレメトリのベースライン（編集可）", "손익 텔레메트리 기준값(편집 가능)" };
            t["User / observed local high water"] = new[] { "使用者／本機觀察高水位", "ユーザー／ローカル観測の高値水準", "사용자/로컬 관측 최고 수위" };
            t["Max exact-instrument contracts"] = new[] { "確切商品最大合約數", "銘柄指定の最大契約数", "정확한 종목 기준 최대 계약 수" };
            t["Invalid value — retained until corrected; Apply is blocked."] = new[] { "數值無效——保留至修正為止；無法套用。", "無効な値 — 修正されるまで保持されます。適用はブロックされています。", "잘못된 값 — 수정될 때까지 유지되며 적용이 차단됩니다." };
            t["Enable LOCAL intraday high-water guard (not firm EOD calculation)"] = new[] { "啟用本機盤中高水位防護（非公司收盤後計算）", "ローカルのイントラデイ高値水準ガードを有効化（会社の EOD 計算ではありません）", "로컬 장중 최고 수위 가드 사용(회사 EOD 계산 아님)" };
            t["BestDay / TotalProfit are supplied history, used for consistency advisory only. To unlock a persisted latch use the separate confirmed reset while disarmed. Baseline is never automatically reset on re"] = new[] { "BestDay / TotalProfit 為提供的歷史資料，僅用於一致性參考。若要解除已持久化的鎖存，請在未啟用狀態下使用另外的確認重設。基準值不會於重新啟動時自動重設。", "BestDay / TotalProfit は提供された履歴であり、整合性の参考にのみ使用されます。永続化されたラッチを解除するには、解除状態で別の確認付きリセットを使用してください。ベースラインは再起動時に自動リセットされません。", "BestDay / TotalProfit은 제공된 이력이며 일관성 참고용으로만 사용됩니다. 유지된 래치를 해제하려면 비활성 상태에서 별도의 확인 리셋을 사용하십시오. 기준값은 재시작 시 자동으로 리셋되지 않습니다." };
            t["Invalid settings"] = new[] { "設定無效", "無効な設定", "잘못된 설정" };
            t["Reset hour outside 0–23"] = new[] { "重設時刻超出 0–23", "リセット時刻が 0–23 の範囲外", "리셋 시각이 0–23 범위를 벗어남" };
            t["Previously unreadable settings; preserve original files, restore a verified backup before restarting."] = new[] { "先前設定無法讀取；請保留原始檔案，並在重新啟動前還原已驗證的備份。", "以前に読み取れなかった設定です。元のファイルを保存し、再起動前に検証済みのバックアップを復元してください。", "이전에 읽을 수 없던 설정입니다. 원본 파일을 보존하고 재시작 전에 검증된 백업을 복원하십시오." };
            t["Trading-day settings (local observation)"] = new[] { "交易日設定（本機觀察）", "取引日設定（ローカル観測）", "거래일 설정(로컬 관측)" };
            t["Windows timezone ID (DST aware)"] = new[] { "Windows 時區 ID（支援日光節約時間）", "Windows タイムゾーン ID（DST 対応）", "Windows 시간대 ID(DST 지원)" };
            t["Reset boundary hour 0–23 (no automatic latch / baseline reset)"] = new[] { "重設分界時刻 0–23（不會自動重設鎖存／基準值）", "リセット境界時刻 0–23（ラッチ／ベースラインの自動リセットなし）", "리셋 경계 시각 0–23(래치/기준값 자동 리셋 없음)" };
            t["Confirm local day boundary"] = new[] { "確認本機日界線", "ローカルの日付境界を確認", "로컬 일 경계 확인" };
            t["Hour must be 0–23"] = new[] { "時刻須為 0–23", "時刻は 0–23 で指定", "시각은 0–23이어야 합니다" };
            t["Invalid trading-day setting"] = new[] { "交易日設定無效", "無効な取引日設定", "잘못된 거래일 설정" };
            t["Unlock requires selected instrument flat / no working orders."] = new[] { "解除鎖定須所選商品已平倉且無掛單。", "ロック解除には、選択した銘柄がフラットで待機中の注文がないことが必要です。", "잠금 해제에는 선택한 종목이 청산 상태이고 대기 주문이 없어야 합니다." };
            t["Current telemetry / floor / local high-water still blocks unlock."] = new[] { "目前遙測／下限／本機高水位仍禁止解除鎖定。", "現在のテレメトリ／下限／ローカル高値水準により、ロック解除は引き続きブロックされています。", "현재 텔레메트리/하한/로컬 최고 수위로 인해 잠금 해제가 계속 차단됩니다." };
            t["Cancel"] = new[] { "取消", "キャンセル", "취소" };
            t["BestDay / TotalProfit are supplied history, used for consistency advisory only. To unlock a persisted latch use the separate confirmed reset while disarmed. Baseline is never automatically reset on restart."] = new[] { "BestDay / TotalProfit 為提供的歷史資料，僅用於一致性參考。若要解除已持久化的鎖存，請在未啟用狀態下使用另外的確認重設。基準值不會於重新啟動時自動重設。", "BestDay / TotalProfit は提供された履歴であり、整合性の参考にのみ使用されます。永続化されたラッチを解除するには、解除状態で別の確認付きリセットを使用してください。ベースラインは再起動時に自動リセットされません。", "BestDay / TotalProfit은 제공된 이력이며 일관성 참고용으로만 사용됩니다. 유지된 래치를 해제하려면 비활성 상태에서 별도의 확인 리셋을 사용하십시오. 기준값은 재시작 시 자동으로 리셋되지 않습니다." };
            t["Apply"] = new[] { "套用", "適用", "적용" };
            t["Active"] = new[] { "啟用中", "有効", "활성" };
            t["Connected"] = new[] { "已連線", "接続", "연결됨" };
            t["Connecting"] = new[] { "連線中", "接続中", "연결 중" };
            t["Synthetic"] = new[] { "模擬", "シンセティック", "합성" };
            t["On"] = new[] { "開", "オン", "켜짐" };
            t["Follower"] = new[] { "跟單帳戶", "フォロワー", "팔로워" };
            t["Target"] = new[] { "目標", "目標", "목표" };
            t["Loss"] = new[] { "虧損", "損失", "손실" };
            t["ENGINE ERROR: {0} — entries blocked; coverage unverified, manual recovery required"] = new[] { "引擎錯誤：{0} — 已封鎖進場；無法確認已有保護，需人工處理", "エンジンエラー: {0} — エントリーをブロックしました。保護は確認できません。手動での復旧が必要", "엔진 오류: {0} — 진입 차단됨. 보호 여부를 확인할 수 없음, 수동 복구 필요" };
            t["{0}: ATM exit mirroring OFF — nonmarket exits skipped; market closes only. No native ATM clone / autonomous protection."] = new[] { "{0}：ATM 出場鏡像為 OFF — 略過非市價出場單；僅執行市價平倉。無原生 ATM 複製／自主保護。", "{0}: ATM 決済ミラーリングが OFF — 成行以外の決済はスキップされ、成行決済のみ実行されます。ネイティブ ATM のクローン／自律的な保護はありません。", "{0}: ATM 청산 미러링 OFF — 시장가 외 청산은 건너뛰며 시장가 청산만 실행됩니다. 네이티브 ATM 복제/자율 보호 없음." };
            t["Armed explicitly: {0} simulation follower(s)."] = new[] { "已明確啟用：{0} 個模擬跟單帳戶。", "明示的に有効化: {0} 件のシミュレーションフォロワー。", "명시적으로 활성화됨: 시뮬레이션 팔로워 {0}개." };
            t["COPYING — {0} / {1}"] = new[] { "跟單中 — {0} / {1}", "コピー中 — {0} / {1}", "복사 중 — {0} / {1}" };
            t["Start refused: {0}"] = new[] { "拒絕開始：{0}", "開始を拒否: {0}", "시작 거부됨: {0}" };
            t["Order error: {0}"] = new[] { "委託錯誤：{0}", "注文エラー: {0}", "주문 오류: {0}" };
            t["Manage error: {0}"] = new[] { "管理錯誤：{0}", "管理エラー: {0}", "관리 오류: {0}" };
            t["PROVIDER DISPATCH ERROR: {0}"] = new[] { "供應端派送錯誤：{0}", "プロバイダーのディスパッチエラー: {0}", "제공자 디스패치 오류: {0}" };
            t["PROVIDER COMPLETION ERROR: {0}"] = new[] { "供應端完成處理錯誤：{0}", "プロバイダーの完了処理エラー: {0}", "제공자 완료 처리 오류: {0}" };
            t["{0}: provider call failed while engine stopping: {1}"] = new[] { "{0}：引擎停止期間供應端呼叫失敗：{1}", "{0}: エンジン停止中にプロバイダー呼び出しが失敗: {1}", "{0}: 엔진 중지 중 제공자 호출 실패: {1}" };
            t["Flatten {0}"] = new[] { "平倉 {0}", "全決済 {0}", "청산 {0}" };
            t["Change protective order to qty {0}"] = new[] { "將保護單數量變更為 {0}", "保護注文の数量を {0} に変更", "보호 주문 수량을 {0}(으)로 변경" };
            t["COPY {0} {1} {2} {3}"] = new[] { "跟單 {0} {1} {2} {3}", "コピー {0} {1} {2} {3}", "복사 {0} {1} {2} {3}" };
            t["{0}: leader exit ignored, nothing to close"] = new[] { "{0}：主帳戶出場已忽略，無可平倉部位", "{0}: リーダーの決済を無視、決済対象なし", "{0}: 리더 청산 무시됨, 청산할 대상 없음" };
            t["{0}: entry skipped, projected cap / floor quantity"] = new[] { "{0}：略過進場，預估數量達上限／下限", "{0}: エントリーをスキップ、予測数量が上限／下限に到達", "{0}: 진입 건너뜀, 예상 수량이 한도/하한에 도달" };
            t["Terminal fill quantity divergence — expected={0}, observed={1}; no makeup / chase, manual reconciliation required"] = new[] { "最終成交數量偏離 — 預期={0}，實際={1}；不補單／不追單，需人工對帳", "最終約定数量の乖離 — 期待={0}、観測={1}。補填／追随なし、手動での照合が必要", "최종 체결 수량 불일치 — 예상={0}, 관측={1}; 보충/추격 없음, 수동 대조 필요" };
            t["OCO cancellation unresolved: {0}"] = new[] { "OCO 取消未解決：{0}", "OCO のキャンセルが未解決: {0}", "OCO 취소 미해결: {0}" };
            t["Delayed exit error: {0}"] = new[] { "延遲出場錯誤：{0}", "遅延決済エラー: {0}", "지연 청산 오류: {0}" };
            t["Protection resize error: {0}"] = new[] { "保護單調整數量錯誤：{0}", "保護のサイズ変更エラー: {0}", "보호 크기 조정 오류: {0}" };
            t["{0} LOCK: {1}. Coverage not established; manual reconciliation may be required."] = new[] { "{0} 鎖定：{1}。無法確認已有保護；可能需要人工對帳。", "{0} ロック: {1}。保護は確認できません。手動での照合が必要になる場合があります。", "{0} 잠금: {1}. 보호 여부를 확인할 수 없음; 수동 대조가 필요할 수 있습니다." };
            t["{0} LOCK: {1}. Local guard only; coverage not established."] = new[] { "{0} 鎖定：{1}。僅為本機防護；無法確認已有保護。", "{0} ロック: {1}。ローカルガードのみ。保護は確認できません。", "{0} 잠금: {1}. 로컬 가드만 해당; 보호 여부를 확인할 수 없음." };
            t["{0}: Flat confirmed (position 0, nothing working) — OFF; re-follow explicitly to copy again"] = new[] { "{0}：已確認平倉（持倉 0，無掛單）— OFF；如需再次跟單，請明確重新跟隨", "{0}: フラット確認（ポジション 0、待機中の注文なし）— OFF。再度コピーするには明示的に再フォローしてください", "{0}: 청산 확인됨(포지션 0, 대기 주문 없음) — OFF; 다시 복사하려면 명시적으로 재팔로우하십시오" };
            t["Telemetry error: {0}"] = new[] { "遙測錯誤：{0}", "テレメトリエラー: {0}", "텔레메트리 오류: {0}" };
            t["Entry cancellation unresolved: {0}"] = new[] { "進場取消未解決：{0}", "エントリーのキャンセルが未解決: {0}", "진입 취소 미해결: {0}" };
            t["{0}: Follow refused — clean, connected, unlocked session required"] = new[] { "{0}：拒絕跟隨 — 需要乾淨、已連線且未鎖定的工作階段", "{0}: フォローを拒否 — クリーンで接続済み、ロックされていないセッションが必要", "{0}: 팔로우 거부됨 — 정상적이고 연결되어 있으며 잠기지 않은 세션 필요" };
            t["LIVE FLATTEN BLOCKED {0}"] = new[] { "即時平倉遭封鎖 {0}", "ライブ全決済がブロックされました {0}", "라이브 청산 차단됨 {0}" };
            t["Flatten request: {0} / {1} — verify flat, retry manually if error"] = new[] { "平倉請求：{0} / {1} — 請確認已平倉，若出錯請手動重試", "全決済リクエスト: {0} / {1} — フラットを確認し、エラーの場合は手動で再試行", "청산 요청: {0} / {1} — 청산 여부를 확인하고 오류 시 수동으로 재시도" };
            t["Flatten acceptance uncertain — existing protection retained; duplicate reductions blocked until explicit provider reconciliation: {0}"] = new[] { "平倉是否被接受無法確定 — 保留現有保護；在明確完成供應端對帳前，封鎖重複減倉：{0}", "全決済が受理されたか不明 — 既存の保護を維持。プロバイダーとの明示的な照合が完了するまで、重複した数量削減はブロックされます: {0}", "청산 수락 여부 불확실 — 기존 보호 유지; 제공자와 명시적으로 대조할 때까지 중복 감축 차단: {0}" };
            t["FLATTEN FAILED {0}: {1}"] = new[] { "平倉失敗 {0}：{1}", "全決済に失敗 {0}: {1}", "청산 실패 {0}: {1}" };
            t["Flat requested for {0} follower account(s); other accounts keep copying."] = new[] { "已對 {0} 個跟單帳戶請求平倉；其他帳戶持續跟單。", "{0} 件のフォロワー口座に決済をリクエストしました。他の口座はコピーを継続します。", "팔로워 계좌 {0}개에 청산을 요청했습니다. 다른 계좌는 계속 복사합니다." };
            t["{0}: flatten abandonment refused — in-flight / unknown working orders remain"] = new[] { "{0}：拒絕放棄平倉 — 仍有處理中／未知的掛單", "{0}: 全決済の中止を拒否 — 処理中／不明な待機中の注文が残っています", "{0}: 청산 포기 거부됨 — 처리 중/알 수 없는 대기 주문이 남아 있음" };
            t["{0}: flatten abandonment refused — exposure evidence unresolved"] = new[] { "{0}：拒絕放棄平倉 — 風險部位證據尚未解決", "{0}: 全決済の中止を拒否 — エクスポージャーの証拠が未解決", "{0}: 청산 포기 거부됨 — 익스포저 증거 미해결" };
            t["DURABLE LATCH WRITE FAILED: {0} / {1}. Entries paused; do not restart until reviewed."] = new[] { "持久鎖存寫入失敗：{0} / {1}。進場已暫停；審查完成前請勿重新啟動。", "永続ラッチの書き込みに失敗: {0} / {1}。エントリーを一時停止しました。確認が完了するまで再起動しないでください。", "영구 래치 쓰기 실패: {0} / {1}. 진입이 일시정지되었습니다. 검토 전에는 재시작하지 마십시오." };
            t["Settings index unreadable — Start blocked. {0}"] = new[] { "設定索引無法讀取 — 已封鎖開始。{0}", "設定インデックスを読み取れません — 開始をブロックしました。{0}", "설정 색인을 읽을 수 없음 — 시작이 차단되었습니다. {0}" };
            t["PERSISTENCE FAILURE — lock not durable; manual recovery required: {0}"] = new[] { "持久化失敗 — 鎖定未能持久保存；需人工處理：{0}", "永続化失敗 — ロックが永続化されていません；手動での復旧が必要：{0}", "영속화 실패 — 잠금이 영구 저장되지 않음; 수동 복구 필요: {0}" };
            t["Refresh error: {0}"] = new[] { "重新整理錯誤：{0}", "更新エラー：{0}", "새로고침 오류: {0}" };
            t["PERSISTENCE ERROR — entries paused, restart blocked until reviewed: {0}"] = new[] { "持久化錯誤 — 新進場已暫停，審查前禁止重新啟動：{0}", "永続化エラー — 新規エントリーは一時停止中、確認が完了するまで再起動をブロック：{0}", "영속화 오류 — 신규 진입 일시정지됨, 검토 전까지 재시작 차단: {0}" };
            t["UI action failed: {0}"] = new[] { "UI 操作失敗：{0}", "UI操作に失敗：{0}", "UI 작업 실패: {0}" };
            t["Invalid baseline telemetry: {0}"] = new[] { "基準值遙測資料無效：{0}", "ベースラインのテレメトリが無効：{0}", "잘못된 기준값 텔레메트리: {0}" };
            t["{0}: Follow refused — account outside managed session; stop flat and select scope again."] = new[] { "{0}：已拒絕跟單 — 帳戶不在受管理的工作階段內；請在持倉歸零後停止，並重新選擇範圍。", "{0}：フォローを拒否 — 口座が管理対象セッション外です；フラットの状態で停止し、範囲を再選択してください。", "{0}: 팔로우 거부됨 — 계좌가 관리 세션 밖에 있음; 청산 후 중지하고 범위를 다시 선택하세요." };
            t["Applied editable risk snapshot to {0} account(s); per-account ratios / latch unchanged for bulk edits."] = new[] { "已將可編輯的風控快照套用至 {0} 個帳戶；批次編輯不會變更各帳戶的比例／鎖存。", "編集可能なリスクスナップショットを {0} 口座に適用；一括編集では口座ごとの比率／ラッチは変更されません。", "편집 가능한 리스크 스냅샷을 {0}개 계좌에 적용함; 일괄 편집 시 계좌별 비율/래치는 변경되지 않음." };
            t["Confirmed unlock: {0}; still disarmed."] = new[] { "已確認解除鎖定：{0}；仍為未啟動狀態。", "ロック解除を確認：{0}；引き続きディスアーム状態。", "잠금 해제 확인됨: {0}; 여전히 해제(disarmed) 상태." };
            t["Unlock refused: {0}"] = new[] { "已拒絕解除鎖定：{0}", "ロック解除を拒否：{0}", "잠금 해제 거부됨: {0}" };
            t["Instrument \"{0}\" not recognised. Examples: MNQ, ES 12-26, NQZ6."] = new[] { "無法辨識商品「{0}」。範例：MNQ、ES 12-26、NQZ6。", "銘柄「{0}」を認識できません。例：MNQ、ES 12-26、NQZ6。", "종목 \"{0}\"을(를) 인식할 수 없음. 예: MNQ, ES 12-26, NQZ6." };
            t["Instrument {0} is not available in this NinjaTrader (check the contract month / Instrument Manager)."] = new[] { "此 NinjaTrader 中無法使用商品 {0}（請檢查合約月份／Instrument Manager）。", "この NinjaTrader では銘柄 {0} を利用できません（限月／Instrument Manager を確認）。", "이 NinjaTrader에서는 종목 {0}을(를) 사용할 수 없음 (계약월/Instrument Manager 확인)." };
            t["Account list updated: {0}"] = new[] { "帳戶清單已更新：{0}", "口座リストを更新：{0}", "계좌 목록 업데이트됨: {0}" };
            t["Account list changed ({0}) — not applied while copying. Stop when flat, then the list refreshes."] = new[] { "帳戶清單已變更（{0}）— 跟單期間不會套用。請在持倉歸零後停止，清單才會重新整理。", "口座リストが変更されました（{0}）— コピー中は適用されません。フラット後に停止すると、リストが更新されます。", "계좌 목록이 변경됨 ({0}) — 복사 중에는 적용되지 않음. 청산 후 중지하면 목록이 새로고침됩니다." };
            t["Account sync error: {0}"] = new[] { "帳戶同步錯誤：{0}", "口座同期エラー：{0}", "계좌 동기화 오류: {0}" };
            t["Trading-day boundary {0} {1}:00 (DST aware). New entries paused. Baseline / latch NOT auto-reset. Confirm new baseline and unlock while flat."] = new[] { "交易日界線 {0} {1}:00（已考慮日光節約時間）。新進場已暫停。基準值／鎖存不會自動重設。請在持倉歸零時確認新基準值並解除鎖定。", "取引日境界 {0} {1}:00（夏時間考慮）。新規エントリーは一時停止中。ベースライン／ラッチは自動リセットされません。フラットの状態で新しいベースラインを確認し、ロックを解除してください。", "거래일 경계 {0} {1}:00 (서머타임 반영). 신규 진입 일시정지됨. 기준값/래치는 자동 초기화되지 않음. 청산 상태에서 새 기준값을 확인하고 잠금을 해제하세요." };
            t["Restore failed — Start blocked: {0}"] = new[] { "還原失敗 — 已封鎖開始：{0}", "復元に失敗 — 開始をブロックしました：{0}", "복원 실패 — 시작이 차단되었습니다: {0}" };
            t["Bulk {0} applied to highlighted followers; follow ticks unchanged."] = new[] { "已將批次{0}套用至選取的跟單帳戶；跟單勾選不變。", "一括{0}を選択中のフォロワーに適用；フォローのチェックは変更なし。", "일괄 {0}을(를) 선택한 팔로워에 적용함; 팔로우 체크는 변경되지 않음." };
            t["Bulk {0} refused: {1}"] = new[] { "批次{0}已被拒絕：{1}", "一括{0}を拒否：{1}", "일괄 {0} 거부됨: {1}" };
            t["From Leader refused: {0}"] = new[] { "取自主帳戶已被拒絕：{0}", "リーダーからの取得を拒否：{0}", "리더에서 가져오기 거부됨: {0}" };
            t["{0} open order(s) [{1}]"] = new[] { "{0} 筆未平倉委託單 [{1}]", "未約定注文 {0} 件 [{1}]", "미체결 주문 {0}건 [{1}]" };
            t["Blocked by {0}."] = new[] { "已被 {0} 封鎖。", "{0} によりブロックされました。", "{0}에 의해 차단됨." };
            t["Selected instrument already has a position / working order. No adoption or catch-up. {0}"] = new[] { "所選商品已有持倉／掛單。不採納、不補單。{0}", "選択した銘柄には既にポジション／待機中の注文があります。引き継ぎ・キャッチアップは行いません。{0}", "선택한 종목에 이미 포지션/대기 주문이 있음. 인수 및 따라잡기 없음. {0}" };
            t["{0} lot UNPROTECTED (no protective exit working) — check NinjaTrader now and place or flatten by hand; this copier will not create it"] = new[] { "{0} 口未受保護（無保護出場單掛出）— 請立即檢查 NinjaTrader，並手動下單或平倉；本跟單程式不會建立保護單", "{0} ロットが保護なし（保護決済注文が待機していません）— 今すぐ NinjaTrader を確認し、手動で注文するか決済してください；このコピーツールは注文を作成しません", "{0}랏 보호 없음 (대기 중인 보호 청산 주문 없음) — 지금 NinjaTrader를 확인하고 수동으로 주문하거나 청산하세요; 이 복사기는 주문을 생성하지 않습니다" };
            t["{0} lots UNPROTECTED (no protective exit working) — check NinjaTrader now and place or flatten by hand; this copier will not create it"] = new[] { "{0} 口未受保護（無保護出場單掛出）— 請立即檢查 NinjaTrader，並手動下單或平倉；本跟單程式不會建立保護單", "{0} ロットが保護なし（保護決済注文が待機していません）— 今すぐ NinjaTrader を確認し、手動で注文するか決済してください；このコピーツールは注文を作成しません", "{0}랏 보호 없음 (대기 중인 보호 청산 주문 없음) — 지금 NinjaTrader를 확인하고 수동으로 주문하거나 청산하세요; 이 복사기는 주문을 생성하지 않습니다" };
            t["CashValue · {0} · {1}\nNetLiquidation (net equity, NOT cash balance): {2} {3}\nAccount.Get reports provider values; currency argument does not convert. Dash = unavailable/disconnected. Synthetic rows are not broker data."] = new[] { "CashValue · {0} · {1}\nNetLiquidation（淨值，非現金餘額）：{2} {3}\nAccount.Get 回報的是供應端的數值；幣別參數不會進行換算。破折號 = 無法取得／已斷線。合成列並非券商資料。", "CashValue · {0} · {1}\nNetLiquidation（純資産、現金残高ではありません）：{2} {3}\nAccount.Get はプロバイダーの値を返します；通貨引数では換算されません。ダッシュ = 取得不可／切断。合成行はブローカーのデータではありません。", "CashValue · {0} · {1}\nNetLiquidation (순자산, 현금 잔고 아님): {2} {3}\nAccount.Get은 제공자 값을 보고함; 통화 인수는 환산하지 않음. 대시 = 사용 불가/연결 끊김. 합성 행은 브로커 데이터가 아님." };
            t["→  {0} followers  "] = new[] { "→  {0} 個跟單帳戶  ", "→  フォロワー {0}  ", "→  팔로워 {0}명  " };
            t["{0} TICKED  /  {1} ACCTS     ·     P/L  {2}"] = new[] { "已勾選 {0}  /  帳戶 {1}     ·     P/L  {2}", "チェック {0}  /  口座 {1}     ·     P/L  {2}", "체크 {0}  /  계좌 {1}     ·     P/L  {2}" };
            t["{0} sel / {1} rows"] = new[] { "已選 {0} / 共 {1} 列", "選択 {0} / {1} 行", "선택 {0} / {1}행" };
            t["● {0} LOCKED · check NinjaTrader, then review each reason"] = new[] { "● {0} 已鎖定 · 請檢查 NinjaTrader，再逐一審查原因", "● {0} ロック中 · NinjaTrader を確認し、各理由を確認してください", "● {0} 잠김 · NinjaTrader를 확인한 후 각 사유를 검토하세요" };
            t["⚠ {0} account(s) with a settings note · hover State"] = new[] { "⚠ {0} 個帳戶有設定備註 · 請將游標移至狀態欄", "⚠ 設定メモのある口座 {0} 件 · 状態にカーソルを合わせてください", "⚠ 설정 메모가 있는 계좌 {0}개 · 상태에 마우스를 올리세요" };
            t["Ratio gives 0 lots for a {0}-lot leader order"] = new[] { "對於 {0} 口的主帳戶委託單，比例換算結果為 0 口", "リーダーの {0} ロット注文に対し、比率では 0 ロットになります", "리더의 {0}랏 주문에 대해 비율 적용 결과가 0랏임" };
            t["over Max {0}: that order is skipped, not reduced"] = new[] { "超過上限 {0}：該筆會整筆略過，不會減量", "最大 {0} を超えるため、その注文は丸ごと見送られます（減らしません）", "최대 {0} 초과: 그 주문은 줄이지 않고 통째로 건너뜁니다" };
            t["Over advisory limit: {0}%"] = new[] { "超出建議上限：{0}%", "推奨上限を超過：{0}%", "권고 한도 초과: {0}%" };
            t["Within advisory limit: {0}%"] = new[] { "在建議上限內：{0}%", "推奨上限内：{0}%", "권고 한도 이내: {0}%" };
            t["Start simulation copying to exactly {0} follower account(s) on {1}?\n\n{2}\n\nLocal copier guards only. No adoption; all selected accounts must be flat / no working instrument orders."] = new[] { "要對 {1} 上恰好 {0} 個跟單帳戶開始模擬跟單嗎？\n\n{2}\n\n僅有本機跟單防護。不採納既有部位；所有已選帳戶必須為平倉狀態，且該商品無掛單。", "{1} の正確に {0} 口座のフォロワーへシミュレーションコピーを開始しますか？\n\n{2}\n\nローカルのコピーガードのみ。引き継ぎは行いません；選択した全口座がフラットで、銘柄の待機中の注文がない必要があります。", "{1}에서 정확히 {0}개 팔로워 계좌로 시뮬레이션 복사를 시작할까요?\n\n{2}\n\n로컬 복사 가드만 적용됨. 인수 없음; 선택한 모든 계좌는 청산 상태이고 해당 종목의 대기 주문이 없어야 함." };
            t["Account {0} remains LOCKED: {1}.\nRequest unlock? This does NOT arm copying; current risk conditions will be rechecked, baseline unchanged."] = new[] { "帳戶 {0} 仍為已鎖定：{1}。\n要申請解除鎖定嗎？這不會啟動跟單；將重新檢查目前的風控條件，基準值不變。", "口座 {0} は引き続きロック中：{1}。\nロック解除を要求しますか？コピーは開始されません；現在のリスク条件を再確認し、ベースラインは変更されません。", "계좌 {0}은(는) 여전히 잠김: {1}.\n잠금 해제를 요청할까요? 복사는 시작되지 않음; 현재 리스크 조건을 다시 확인하며 기준값은 변경되지 않음." };
            t["{0}{1}FLATTEN exactly {2} account(s), only {3}.\n{4}\nNew copies will be blocked BEFORE flatten requests. This cancels working orders and closes positions on that instrument. Retry/error remains visible."] = new[] { "{0}{1}對恰好 {2} 個帳戶平倉，僅限 {3}。\n{4}\n在送出平倉請求之前，將先封鎖新的跟單。此操作會取消掛單並平掉該商品的持倉。重試／錯誤仍會顯示。", "{0}{1}正確に {2} 口座を全決済、対象は {3} のみ。\n{4}\n全決済リクエストの前に新規コピーをブロックします。この操作は待機中の注文をキャンセルし、その銘柄のポジションを決済します。再試行／エラーは引き続き表示されます。", "{0}{1}정확히 {2}개 계좌를 청산, {3}만 대상.\n{4}\n청산 요청 전에 새 복사가 차단됩니다. 이 작업은 대기 주문을 취소하고 해당 종목의 포지션을 청산합니다. 재시도/오류는 계속 표시됩니다." };
            t["*** DRY RUN: nothing will be sent; decisions are only logged. ***"] = new[] { "*** DRY RUN（試運行）：不會送出任何內容；決策僅記錄於日誌。***", "*** DRY RUN（ドライラン）：何も送信されません；判断はログに記録されるのみ。***", "*** DRY RUN (드라이런): 아무것도 전송되지 않음; 결정은 로그에만 기록됨. ***" };
            t["FLAT ONE ACCOUNT. "] = new[] { "單一帳戶平倉。 ", "単一口座を全決済。 ", "단일 계좌 청산. " };
            t["Risk settings — {0} account(s)"] = new[] { "風控設定 — {0} 個帳戶", "リスク設定 — {0} 口座", "리스크 설정 — 계좌 {0}개" };
            t["Apply to {0} account(s)"] = new[] { "套用至 {0} 個帳戶", "{0} 口座に適用", "계좌 {0}개에 적용" };
            t["Leader: {0}   Instrument: {1}"] = new[] { "主帳戶：{0}   商品：{1}", "リーダー：{0}   銘柄：{1}", "리더: {0}   종목: {1}" };
            t["{0} follower:"] = new[] { "{0} 個跟單帳戶：", "フォロワー {0}：", "팔로워 {0}명:" };
            t["{0} followers:"] = new[] { "{0} 個跟單帳戶：", "フォロワー {0}：", "팔로워 {0}명:" };
            t["  {0}  ratio invalid — will NOT copy"] = new[] { "  {0}  比例無效 — 將不會跟單", "  {0}  比率が無効 — コピーされません", "  {0}  비율 잘못됨 — 복사되지 않음" };
            t["  {0}  ratio {1}  → {2} lot when the leader trades {3}"] = new[] { "  {0}  比例 {1}  → 當主帳戶交易 {3} 時為 {2} 口", "  {0}  比率 {1}  → リーダーが {3} を取引すると {2} ロット", "  {0}  비율 {1}  → 리더가 {3}을(를) 거래하면 {2}랏" };
            t["  {0}  ratio {1}  → {2} lots when the leader trades {3}"] = new[] { "  {0}  比例 {1}  → 當主帳戶交易 {3} 時為 {2} 口", "  {0}  比率 {1}  → リーダーが {3} を取引すると {2} ロット", "  {0}  비율 {1}  → 리더가 {3}을(를) 거래하면 {2}랏" };
            t["(none)"] = new[] { "（無）", "（なし）", "(없음)" };
            t["Source PT {0}; DD {1} ({2}); DLL {3}; mini cap {4}; consistency {5}"] = new[] { "來源 PT {0}；DD {1}（{2}）；DLL {3}；迷你合約上限 {4}；一致性 {5}", "ソース PT {0}；DD {1}（{2}）；DLL {3}；ミニ上限 {4}；一貫性 {5}", "출처 PT {0}; DD {1} ({2}); DLL {3}; 미니 한도 {4}; 일관성 {5}" };
            t["Source: propfirmsdrama.com/full-comparison/ — 2026-10-02; TPT 2026-08-31. Verify current firm rules. Funded/eval stages differ; scaling may lower cap."] = new[] { "來源：propfirmsdrama.com/full-comparison/ — 2026-10-02；TPT 2026-08-31。請核實最新的公司規則。資金帳戶與評估階段不同；規模擴增可能降低上限。", "出典：propfirmsdrama.com/full-comparison/ — 2026-10-02；TPT 2026-08-31。最新の会社ルールを確認してください。ファンデッド／評価の段階で異なります；スケーリングで上限が下がる場合があります。", "출처: propfirmsdrama.com/full-comparison/ — 2026-10-02; TPT 2026-08-31. 최신 업체 규정을 확인하세요. 펀디드/평가 단계에 따라 다르며; 스케일링으로 한도가 낮아질 수 있음." };
            t["DD amount is NOT an absolute equity floor. Confirm your floor manually. Optional/soft DLL stays off unless chosen. Consistency is advisory, needs supplied history."] = new[] { "DD 金額並非絕對的淨值底線。請自行手動確認您的底線。選用／軟性 DLL 除非選擇，否則維持關閉。一致性僅供參考，且需提供歷史資料。", "DD 金額は絶対的なエクイティの下限ではありません。下限は手動で確認してください。オプション／ソフト DLL は選択しない限りオフのままです。一貫性は参考情報で、履歴データの提供が必要です。", "DD 금액은 절대적인 자산 하한이 아님. 하한은 직접 확인하세요. 선택/소프트 DLL은 선택하지 않으면 꺼진 상태로 유지됨. 일관성은 참고용이며 제공된 이력이 필요함." };
            t["Disarmed"] = new[] { "未啟動", "アーム解除", "해제됨" };
            t["Disconnected"] = new[] { "已斷線", "切断", "연결 끊김" };
            t["Copy"] = new[] { "跟單", "コピー", "복사" };
            t["Risk"] = new[] { "風控", "リスク", "리스크" };
            t["Logs"] = new[] { "日誌", "ログ", "로그" };
            t["Language"] = new[] { "語言", "言語", "언어" };
            t["Settings"] = new[] { "設定", "設定", "설정" };
            t["Language changed to {0}."] = new[] { "語言已切換為 {0}。", "言語を {0} に変更しました。", "언어가 {0}(으)로 변경되었습니다." };
            t["Language can only be changed while stopped (not attached to a session)."] = new[] { "僅能在未連接任何跟單工作階段（已停止）時切換語言。", "言語の変更は停止中（セッション未接続）のみ可能です。", "언어는 중지 상태(세션에 연결되지 않음)에서만 변경할 수 있습니다." };
            t["Interface language"] = new[] { "介面語言", "表示言語", "인터페이스 언어" };
            t["(would NOT copy)"] = new[] { "（不會跟單）", "（コピーされません）", "(복사되지 않음)" };
            t["  /  Trade Copier"] = new[] { "  /  交易複製器", "  /  トレードコピー", "  /  트레이드 복사기" };
            t["Alias"] = new[] { "別名", "別名", "별칭" };
            t["Ratio"] = new[] { "比例", "比率", "비율" };
            t["Conn"] = new[] { "連線", "接続", "연결" };
            t["Group"] = new[] { "群組", "グループ", "그룹" };
            t["{0}: leader exit refused — no independent exit capacity; existing protection retained. Use an explicit shared OCO for alternatives."] = new[] { "{0}：已拒絕主帳戶出場單 — 沒有獨立的出場容量；保留現有保護。如需替代出場單，請使用明確共用的 OCO。", "{0}: リーダーの決済を拒否 — 独立した決済の余力がありません。既存の保護は維持されます。代替が必要な場合は、明示的に共有した OCO を使用してください。", "{0}: 리더 청산 거부됨 — 독립적인 청산 수량 여유가 없음; 기존 보호 주문은 유지됨. 대안이 필요하면 명시적으로 공유한 OCO를 사용하세요." };
            t["Certified OCO cancellation unresolved: {0}"] = new[] { "已認證的 OCO 取消尚未解決：{0}", "認証済みの OCO キャンセルが未解決: {0}", "인증된 OCO 취소가 해결되지 않음: {0}" };
            t["Late execution after provider-confirmed Flat — captured evidence retained; manual reconciliation required"] = new[] { "供應端確認平倉後出現延遲成交 — 已保留擷取的證據；需人工對帳", "プロバイダーが全決済を確認した後に遅延約定 — 取得した証拠は保持；手動での照合が必要", "제공자가 청산을 확인한 뒤 늦은 체결 발생 — 수집된 증거 보존; 수동 대사 필요" };
            t["OCO group fill exceeds captured allocated entry lots — reversal / double fill evidence retained; manual recovery required"] = new[] { "OCO 群組成交量超過已擷取的分配進場口數 — 已保留反向／重複成交證據；需人工處理", "OCO グループの約定数量が取得済みの割当エントリー数量を超過 — 反転／二重約定の証拠は保持；手動での復旧が必要", "OCO 그룹 체결 수량이 수집된 배분 진입 수량을 초과함 — 반전/이중 체결 증거 보존; 수동 복구 필요" };
            t["Protective group terminal with uncovered allocated exposure — manual recovery required"] = new[] { "保護群組已終結，但分配的部位仍未受保護 — 需人工處理", "保護グループが終了したが、割り当てた建玉が未保護 — 手動での復旧が必要", "보호 그룹이 종료되었으나 배분된 포지션이 보호되지 않음 — 수동 복구 필요" };
            t["group support frontier changed before cancel authorization; cancellation NOT sent"] = new[] { "取消授權前群組支援邊界已變更；取消單未送出", "キャンセル承認の前にグループのサポート境界が変更されました。キャンセルは送信されていません", "취소 승인 전에 그룹 지원 경계가 변경됨; 취소는 전송되지 않음" };
            t["waiting protection frontier changed before submit authorization; submission NOT sent"] = new[] { "送出授權前等待中的保護邊界已變更；委託單未送出", "送信承認の前に待機中の保護境界が変更されました。送信されていません", "제출 승인 전에 대기 중인 보호 경계가 변경됨; 제출되지 않음" };
            t["Ask before:"] = new[] { "操作前確認：", "実行前に確認：", "실행 전 확인:" };
            t["Untick to skip that confirmation window. Unlock and risk warnings are always asked."] = new[] { "取消勾選即略過該確認視窗。解鎖及風險警告一律會詢問。", "チェックを外すとその確認ウィンドウを省略します。ロック解除とリスク警告は常に確認します。", "체크를 해제하면 해당 확인 창을 건너뜁니다. 잠금 해제와 위험 경고는 항상 확인합니다." };
            t["Start"] = new[] { "開始", "開始", "시작" };
            t["Flat (row)"] = new[] { "單一帳戶平倉", "行の決済", "행 청산" };
            t["Flatten selected / all"] = new[] { "選取／全部平倉", "選択／全決済", "선택/전체 청산" };
            t["Confirm before Start (follower list and ratios)"] = new[] { "開始前確認（跟單帳戶清單及比例）", "開始前に確認（フォロワー一覧と比率）", "시작 전 확인(팔로워 목록 및 비율)" };
            t["Confirm before a row's Flat button closes that one account"] = new[] { "單行平倉按鈕平掉該帳戶前確認", "行の決済ボタンでその口座を決済する前に確認", "행의 청산 버튼으로 해당 계좌를 청산하기 전에 확인" };
            t["Confirm before Flatten selected / Flatten all"] = new[] { "選取平倉／全部平倉前確認", "選択決済／全決済の前に確認", "선택 청산/전체 청산 전에 확인" };
            t["Limit flat on"] = new[] { "達限平倉：開", "上限で決済：オン", "한도 청산: 켜기" };
            t["Limit flat off"] = new[] { "達限平倉：關", "上限で決済：オフ", "한도 청산: 끄기" };
            t["When Tgt or DLL is hit: flatten that account once (never retried) for highlighted accounts"] = new[] { "選取的帳戶達到每日目標或每日虧損上限時：平倉該帳戶一次（不會重試）", "選択口座が日次目標または日次損失上限に達したとき：その口座を1回だけ決済（再試行なし）", "선택한 계좌가 일일 목표 또는 일일 손실 한도에 도달하면: 해당 계좌를 한 번 청산(재시도 없음)" };
            t["When Tgt or DLL is hit: only lock and stop copying (default) for highlighted accounts"] = new[] { "選取的帳戶達到每日目標或每日虧損上限時：只鎖定並停止跟單（預設）", "選択口座が日次目標または日次損失上限に達したとき：ロックしてコピーを停止するのみ（既定）", "선택한 계좌가 일일 목표 또는 일일 손실 한도에 도달하면: 잠그고 복사만 중지(기본값)" };
            t["Flat at limit: when this account hits its Tgt or DLL, flatten it ONCE (never retried). Off = only lock and stop copying."] = new[] { "達限平倉：此帳戶達到每日目標或每日虧損上限時平倉一次（不會重試）。關閉＝只鎖定並停止跟單。", "上限で決済：この口座が日次目標または日次損失上限に達したら1回だけ決済（再試行なし）。オフ＝ロックしてコピー停止のみ。", "한도 청산: 이 계좌가 일일 목표 또는 일일 손실 한도에 도달하면 한 번 청산(재시도 없음). 끄기 = 잠그고 복사만 중지." };
            t["Flat@Lim"] = new[] { "達限平倉", "上限決済", "한도청산" };
            t["Confirmation skipped (turned off on the Settings tab): {0}"] = new[] { "已略過確認（已在設定分頁關閉）：{0}", "確認を省略（設定タブでオフ）：{0}", "확인 생략(설정 탭에서 끔): {0}" };
            t["Confirmation turned back on: {0}"] = new[] { "已重新開啟確認：{0}", "確認を再びオン：{0}", "확인 다시 켬: {0}" };
            t["Confirmation turned off: {0} (risk warnings and Unlock are still always asked)"] = new[] { "已關閉確認：{0}（風險警告及解鎖仍一律詢問）", "確認をオフ：{0}（リスク警告とロック解除は常に確認）", "확인 끔: {0} (위험 경고와 잠금 해제는 항상 확인)" };
            t["Settings NOT saved: {0}"] = new[] { "設定未儲存：{0}", "設定は保存されていません：{0}", "설정이 저장되지 않았습니다: {0}" };
            t["{0}: dry run: no position was ever opened"] = new[] { "{0}：試運行：從未開倉", "{0}: ドライラン: ポジションは一度も建てられていません", "{0}: 드라이런: 포지션이 열린 적이 없습니다" };
            t["{0}: Flatten already in progress — not sent again; wait for confirmation or check NinjaTrader"] = new[] { "{0}：平倉已在進行中 — 不會重複傳送；請等待確認或查看 NinjaTrader", "{0}: 決済は処理中 — 再送信しません。確認を待つか NinjaTrader を確認してください", "{0}: 청산 진행 중 — 다시 보내지 않음; 확인을 기다리거나 NinjaTrader를 확인하십시오" };
            t["Exit order still working on a FLAT account — if it fills it opens a NEW position; cancel it in NinjaTrader"] = new[] { "已平倉帳戶仍有出場單在運作 — 一旦成交將開出新倉；請在 NinjaTrader 取消該委託", "決済済み口座で決済注文がまだ有効 — 約定すると新規ポジションになります。NinjaTrader で取消してください", "포지션이 없는 계좌에 청산 주문이 아직 작동 중 — 체결되면 새 포지션이 열립니다; NinjaTrader에서 취소하십시오" };
            t["To Tgt"] = new[] { "距目標", "目標まで", "목표까지" };
            t["Loss left"] = new[] { "可虧餘額", "損失余力", "손실 여유" };
            t["Amount remaining to daily target (read-only, computed from P/L)"] = new[] { "距每日目標尚餘金額（唯讀，由損益計算）", "日次目標までの残り金額（読み取り専用、損益から計算）", "일일 목표까지 남은 금액 (읽기 전용, 손익으로 계산)" };
            t["How much more can be lost before the daily loss limit (read-only, computed from P/L)"] = new[] { "觸及每日虧損上限前尚可虧損的金額（唯讀，由損益計算）", "日次損失上限に達するまでに失える残り金額（読み取り専用、損益から計算）", "일일 손실 한도까지 더 잃을 수 있는 금액 (읽기 전용, 손익으로 계산)" };
            t["Columns"] = new[] { "欄位", "列", "열" };
            t["Show all columns"] = new[] { "顯示全部欄位", "すべての列を表示", "모든 열 표시" };
            t["Show or hide columns (also: right-click a column header). Saved for next time."] = new[] { "顯示或隱藏欄位（亦可在欄位標題按右鍵）。設定會保存。", "列の表示／非表示（列見出しの右クリックでも可）。次回も保持されます。", "열 표시/숨기기 (열 머리글 우클릭으로도 가능). 다음에도 유지됩니다." };
            t["RISK WARNING: not confirmed as simulated: {0}\nKINFX cannot tell a prop-firm evaluation account from a funded or live account. If any of these is funded or live, REAL MONEY is at risk.\nYes = these are simulated / evaluation accounts; remember them (sim-allow.txt) so this is not asked again. No = cancel."] = new[] { "風險警告：以下帳戶尚未確認為模擬帳戶：{0}\nKINFX 無法分辨自營交易公司的考核帳戶與已注資或真實帳戶。若其中任何一個為已注資或真實帳戶，將涉及真實資金。\n是＝這些是模擬／考核帳戶，記住它們（sim-allow.txt），以後不再詢問。否＝取消。", "リスク警告：シミュレーションと未確認の口座：{0}\nKINFX はプロップファームの評価口座と資金提供済み／ライブ口座を区別できません。いずれかが資金提供済みまたはライブ口座の場合、実際の資金が危険にさらされます。\nはい＝これらはシミュレーション／評価口座です。記憶し（sim-allow.txt）、次回から確認しません。いいえ＝キャンセル。", "위험 경고: 시뮬레이션으로 확인되지 않은 계좌: {0}\nKINFX는 프롭펌 평가 계좌와 자금 지원 / 실거래 계좌를 구분할 수 없습니다. 이 중 하나라도 자금 지원 또는 실거래 계좌라면 실제 자금이 위험합니다.\n예 = 시뮬레이션 / 평가 계좌이며 기억합니다(sim-allow.txt), 다시 묻지 않습니다. 아니요 = 취소." };
            t["Cancelled at the risk warning: {0}"] = new[] { "已在風險警告取消：{0}", "リスク警告でキャンセル：{0}", "위험 경고에서 취소: {0}" };
            t["Account confirmation NOT saved, nothing sent: {0}"] = new[] { "帳戶確認未儲存，未傳送任何指令：{0}", "口座の確認は保存されず、何も送信されていません：{0}", "계좌 확인이 저장되지 않아 아무것도 전송되지 않았습니다: {0}" };
            t["Confirmed as simulated by you (saved in sim-allow.txt): {0}"] = new[] { "您已確認為模擬帳戶（已儲存於 sim-allow.txt）：{0}", "シミュレーションとして確認済み（sim-allow.txt に保存）：{0}", "시뮬레이션으로 확인됨 (sim-allow.txt에 저장): {0}" };
            t["Follow tick, state, Off / Flat, Qty and connection are always shown"] = new[] { "跟單勾選、狀態、OFF／平倉、數量及連線一律顯示", "フォローのチェック、状態、Off／決済、数量、接続は常に表示", "팔로우 체크, 상태, Off/청산, 수량, 연결은 항상 표시" };
            t["Unlock all"] = new[] { "全部解鎖", "すべてロック解除", "모두 잠금 해제" };
            t["Unlock every locked follower that is flat, connected and no longer at a risk limit (one confirmation). The others stay locked with their reason in Logs. Does NOT start copying."] = new[] { "解鎖所有已平倉、已連線且未再觸及風控限額的已鎖定跟單帳戶（只需確認一次）。其餘帳戶保持鎖定，原因列於日誌。不會開始跟單。", "フラットで接続済み、かつリスク上限に達していない、ロック中のすべてのフォロワーを解除します（確認は 1 回）。その他はロックのままで、理由はログに表示されます。コピーは開始しません。", "청산 상태이고 연결되어 있으며 더 이상 리스크 한도에 도달하지 않은 잠긴 팔로워를 모두 해제합니다(확인 1회). 나머지는 잠긴 상태로 남고 이유는 로그에 표시됩니다. 복사를 시작하지 않습니다." };
            t["Unlock all: no locked account."] = new[] { "全部解鎖：沒有已鎖定的帳戶。", "すべてロック解除：ロック中の口座はありません。", "모두 잠금 해제: 잠긴 계좌가 없습니다." };
            t["Unlock all: nothing can be unlocked yet. Still locked: {0}"] = new[] { "全部解鎖：目前沒有可解鎖的帳戶。仍鎖定：{0}", "すべてロック解除：まだ解除できる口座はありません。ロック継続：{0}", "모두 잠금 해제: 아직 해제할 수 있는 계좌가 없습니다. 계속 잠김: {0}" };
            t["Unlock {0} account(s)? Each one is flat, connected and no longer at a risk limit:\n{1}\nThis does NOT start copying; tick them again and press Start."] = new[] { "解鎖 {0} 個帳戶？每個帳戶均已平倉、已連線，且未再觸及風控限額：\n{1}\n此操作不會開始跟單；請重新勾選並按「開始」。", "{0} 口座のロックを解除しますか？いずれもフラット、接続済みで、リスク上限に達していません：\n{1}\nコピーは開始しません。再度チェックして Start を押してください。", "{0}개 계좌의 잠금을 해제할까요? 모두 청산 상태이고 연결되어 있으며 리스크 한도에 도달하지 않았습니다:\n{1}\n복사를 시작하지 않습니다. 다시 체크하고 Start를 누르십시오." };
            t["Stay locked ({0}):\n{1}"] = new[] { "保持鎖定（{0}）：\n{1}", "ロック継続（{0}）：\n{1}", "잠금 유지 ({0}):\n{1}" };
            t["Unlock all cancelled; nothing changed."] = new[] { "已取消全部解鎖；沒有任何變更。", "すべてロック解除を取り消しました。変更はありません。", "모두 잠금 해제를 취소했습니다. 변경 사항 없음." };
            t["Still locked: {0}"] = new[] { "仍鎖定：{0}", "ロック継続：{0}", "계속 잠김: {0}" };
            t["Position or working order on the instrument"] = new[] { "該商品仍有持倉或掛單", "銘柄にポジションまたは未約定注文あり", "종목에 포지션 또는 미체결 주문 있음" };
            t["Daily target still reached"] = new[] { "仍達每日目標", "日次目標にまだ到達中", "일일 목표에 아직 도달 상태" };
            t["Daily loss still reached"] = new[] { "仍達每日虧損上限", "日次損失上限にまだ到達中", "일일 손실 한도에 아직 도달 상태" };
            t["Drawdown floor still reached"] = new[] { "仍達回撤下限", "ドローダウン下限にまだ到達中", "드로다운 하한에 아직 도달 상태" };
            t["Issues"] = new[] { "問題", "問題", "문제" };
            t["No locks or notes."] = new[] { "沒有鎖定或設定提示。", "ロックや設定の注意はありません。", "잠금이나 설정 알림이 없습니다." };
            t["Open the Issues tab"] = new[] { "開啟「問題」分頁", "「問題」タブを開く", "'문제' 탭 열기" };
            t["Same side/qty does not mean same fill price. Badge only — no auto trade."] = new[] { "相同方向／數量不代表相同成交價。僅為標示 — 不會自動交易。", "同じ方向／数量でも、同じ約定価格とは限りません。表示のみ — 自動売買はありません。", "같은 방향/수량이라고 같은 체결가는 아닙니다. 표시 전용 — 자동 매매 없음." };
            t["Waiting for position reports"] = new[] { "等待持倉回報", "建玉レポートを待っています", "포지션 보고를 기다리는 중" };
            t["Quantity matches"] = new[] { "數量一致", "数量は一致", "수량 일치" };
            t["Quantity diverges"] = new[] { "數量偏離", "数量が乖離", "수량 불일치" };
            t["Average fill diverges"] = new[] { "成交均價偏差", "平均約定価格が乖離", "평균 체결가 불일치" };
            t["Unavailable: not comparable"] = new[] { "不可用：無法比較", "利用不可：比較できません", "사용 불가: 비교할 수 없음" };
            t["Unavailable: platform contract missing"] = new[] { "不可用：平台未提供均價或 tick 契約", "利用不可：プラットフォームに平均価格またはティック契約がありません", "사용 불가: 플랫폼에 평균가 또는 틱 계약이 없음" };
            t["Unavailable: disconnected"] = new[] { "不可用：已斷線", "利用不可：切断されています", "사용 불가: 연결 끊김" };
            t["Unavailable: instrument mismatch"] = new[] { "不可用：合約不符", "利用不可：銘柄が一致しません", "사용 불가: 종목 불일치" };
            t["Unavailable: stale report"] = new[] { "不可用：回報已過期", "利用不可：レポートの期限が切れています", "사용 불가: 보고가 만료됨" };
            t["Unavailable: leader flat"] = new[] { "不可用：主帳戶無持倉", "利用不可：リーダーに建玉がありません", "사용 불가: 리더 포지션 없음" };
            t["Unavailable: average price missing"] = new[] { "不可用：缺少均價", "利用不可：平均価格がありません", "사용 불가: 평균가 없음" };
            t["Unavailable: tick size invalid"] = new[] { "不可用：tick 大小無效", "利用不可：ティックサイズが無効です", "사용 불가: 틱 크기가 잘못됨" };
            t["Unavailable: scale settings invalid"] = new[] { "不可用：比例設定無效", "利用不可：比率設定が無効です", "사용 불가: 비율 설정이 잘못됨" };
            t["Leader row is the comparison baseline"] = new[] { "主帳戶列是比較基準", "リーダー行は比較の基準です", "리더 행은 비교 기준입니다" };
            t["Expected quantity {0} · actual delta {1}"] = new[] { "預期數量 {0} · 實際差額 {1}", "想定数量 {0} · 実際の差 {1}", "예상 수량 {0} · 실제 차이 {1}" };
            t["Average-price delta {0} ticks · adverse {1} ticks"] = new[] { "均價差 {0} ticks · 不利滑價 {1} ticks", "平均価格差 {0} ティック · 不利な滑り {1} ティック", "평균가 차이 {0}틱 · 불리한 슬리피지 {1}틱" };
            t["Avg drift {0}: {1} (display only — no order)"] = new[] { "均價偏差 {0}：{1}（僅顯示 — 不會下單）", "平均価格の乖離 {0}: {1}（表示のみ — 注文しません）", "평균가 괴리 {0}: {1} (표시 전용 — 주문 없음)" };
            t["Warn if avg differs by more than"] = new[] { "若均價相差超過", "平均価格の差が次を超えたら警告", "평균가 차이가 다음을 넘으면 경고" };
            t["ticks (display only)"] = new[] { "個 ticks（僅顯示）", "ティック（表示のみ）", "틱 (표시 전용)" };
            t["Warn if avg differs by more than N ticks (display only)"] = new[] { "若均價相差超過 N 個 ticks 則警告（僅顯示）", "平均価格が N ティックを超えて異なる場合に警告（表示のみ）", "평균가가 N틱을 넘어 다르면 경고 (표시 전용)" };
            t["Display preference ignored; avg warn ticks reset to the default. Badge only — no order."] = new[] { "顯示偏好已忽略；均價警告 ticks 已回復預設。僅為標示 — 不會下單。", "表示設定を無視しました。平均価格の警告ティックは既定に戻しました。表示のみ — 注文しません。", "표시 설정을 무시했습니다. 평균가 경고 틱을 기본값으로 되돌렸습니다. 표시 전용 — 주문 없음." };
            t["Avg warn ticks must be a whole number from 0 to 100 (display only)"] = new[] { "均價警告 ticks 必須是 0 到 100 的整數（僅顯示）", "平均価格の警告ティックは 0 から 100 の整数にしてください（表示のみ）", "평균가 경고 틱은 0에서 100 사이의 정수여야 합니다 (표시 전용)" };
            t["Position consistency display failed: {0}"] = new[] { "持倉一致性顯示失敗：{0}", "建玉の整合表示に失敗しました：{0}", "포지션 정합 표시 실패: {0}" };
            t["Drift"] = new[] { "偏差", "乖離", "괴리" };
            t["wait"] = new[] { "等待", "待機", "대기" };
            t["n/a"] = new[] { "不適用", "該当なし", "해당 없음" };
            t["Avg warn ticks NOT saved (used for this session only): {0}"] = new[] { "均價警告 ticks 未儲存（僅本次工作階段使用）：{0}", "平均価格の警告ティックは保存されていません（このセッションのみ使用）：{0}", "평균가 경고 틱이 저장되지 않음 (이번 세션에만 사용): {0}" };
            t["Micro Nasdaq-100"] = new[] { "微型納斯達克100", "マイクロ Nasdaq-100", "마이크로 나스닥100" };
            t["Nasdaq-100"] = new[] { "納斯達克100", "Nasdaq-100", "나스닥100" };
            t["Micro S&P 500"] = new[] { "微型標普500", "マイクロ S&P 500", "마이크로 S&P 500" };
            t["S&P 500"] = new[] { "標普500", "S&P 500", "S&P 500" };
            t["Micro Dow"] = new[] { "微型道瓊", "マイクロダウ", "마이크로 다우" };
            t["Dow"] = new[] { "道瓊", "ダウ", "다우" };
            t["Micro Russell 2000"] = new[] { "微型羅素2000", "マイクロ Russell 2000", "마이크로 러셀 2000" };
            t["Russell 2000"] = new[] { "羅素2000", "Russell 2000", "러셀 2000" };
            t["Micro Gold"] = new[] { "微型黃金", "マイクロゴールド", "마이크로 골드" };
            t["Gold"] = new[] { "黃金", "ゴールド", "골드" };
            t["Micro Silver"] = new[] { "微型白銀", "マイクロシルバー", "마이크로 실버" };
            t["Silver"] = new[] { "白銀", "シルバー", "실버" };
            t["Micro Crude Oil"] = new[] { "微型原油", "マイクロ原油", "마이크로 원유" };
            t["Crude Oil"] = new[] { "原油", "原油", "원유" };
            t["Equity index"] = new[] { "股指", "株価指数", "주가지수" };
            t["Metals"] = new[] { "金屬", "金属", "금속" };
            t["Energy"] = new[] { "能源", "エネルギー", "에너지" };
            t["Recent"] = new[] { "最近使用", "最近", "최근" };
            t["choose month"] = new[] { "選擇月份", "月を選択", "월 선택" };
            t["Stopped"] = new[] { "已停止", "停止中", "중지됨" };
            t["Pause new entries"] = new[] { "暫停新進場", "新規エントリーを一時停止", "신규 진입 일시정지" };
            t["Front contract is now {0}"] = new[] { "目前近月合約是 {0}", "現在の限月は {0} です", "현재 근월물은 {0}" };
            t["Use {0}"] = new[] { "使用 {0}", "{0} を使用", "{0} 사용" };
            t["Contract roll date estimated for {0}; check NinjaTrader's Instrument Manager."] = new[] { "合約換月日期為 {0} 之估計值；請在 NinjaTrader 的 Instrument Manager 中確認。", "{0} の限月ロール日は推定値です。NinjaTrader の Instrument Manager で確認してください。", "{0}의 롤오버 날짜는 추정값입니다. NinjaTrader의 Instrument Manager에서 확인하세요." };
            t["NinjaTrader {0} is not the tested version ({1}). It may still work; report problems."] = new[] { "NinjaTrader {0} 不是已測試版本（{1}）。可能仍可運作；如有問題請回報。", "NinjaTrader {0} はテスト済みバージョン（{1}）ではありません。動作する可能性はありますが、問題があれば報告してください。", "NinjaTrader {0}은(는) 테스트된 버전({1})이 아닙니다. 작동할 수 있지만 문제가 있으면 보고해 주세요." };
            t["Instrument picker"] = new[] { "商品選擇器", "銘柄ピッカー", "종목 선택기" };
            t["Switch the instrument to the current front contract"] = new[] { "將商品切換至目前近月合約", "銘柄を現在の限月に切り替える", "종목을 현재 근월물로 전환" };
            t["Monitor"] = new[] { "監察", "モニター", "모니터" };
            t["Locked accounts"] = new[] { "已鎖定帳戶", "ロック中の口座", "잠긴 계좌" };
            t["Unprotected lots"] = new[] { "無保護手數", "保護されていないロット", "미보호 로트" };
            t["Today P/L"] = new[] { "今日盈虧", "本日の損益", "오늘 손익" };
            t["Group by firm"] = new[] { "依公司分組", "会社でグループ化", "회사별 그룹" };
            t["Flatten selected"] = new[] { "平倉所選", "選択項目を全決済", "선택 항목 청산" };
            t["Flatten all"] = new[] { "全部平倉", "すべて全決済", "모두 청산" };
            t["This session"] = new[] { "本次工作階段", "このセッション", "이번 세션" };
            t["Bulk edit (highlighted follower accounts · only while stopped)"] = new[] { "批次編輯（已選取的跟單帳戶 · 僅限停止時）", "一括編集（選択中のフォロワー口座 · 停止中のみ）", "일괄 편집 (선택한 팔로워 계좌 · 정지 중에만)" };
            t["ATM exit mirroring"] = new[] { "ATM 出場單鏡像", "ATM 決済注文ミラーリング", "ATM 청산 주문 미러링" };
            t["Flat at limit"] = new[] { "達限平倉", "上限で全決済", "한도 도달 시 청산" };
            t["On divergence"] = new[] { "分歧時", "ダイバージェンス時", "다이버전스 시" };
            t["Notify"] = new[] { "通知", "通知", "알림" };
            t["Flatten once"] = new[] { "平倉一次", "一度だけ全決済", "1회 청산" };
            t["Max is still 1: a leader order larger than this is skipped entirely"] = new[] { "最大手數仍為 1：主帳戶下單手數大於此值時，該筆會被完全略過", "最大数量が 1 のまま：リーダーの注文数量がこれを超えると、その注文は完全にスキップされます", "최대 수량이 1인 상태: 리더 주문 수량이 이보다 크면 해당 주문이 완전히 건너뜁니다" };
            t["… and {0} more"] = new[] { "… 另有 {0} 項", "… ほか {0} 件", "… 외 {0}개" };
            t["Which setting to change on highlighted accounts"] = new[] { "要變更已選取帳戶的哪項設定", "選択中の口座で変更する設定", "선택한 계좌에서 변경할 설정" };
            t["Value for the highlighted accounts"] = new[] { "已選取帳戶的值", "選択中の口座に対する値", "선택한 계좌의 값" };
            t["Apply the chosen setting to the highlighted follower accounts"] = new[] { "將所選設定套用至已選取的跟單帳戶", "選択した設定を選択中のフォロワー口座に適用", "선택한 설정을 선택한 팔로워 계좌에 적용" };
            t["Max contracts"] = new[] { "手數上限", "最大枚数", "최대 계약 수" };
            t["Ratio for the highlighted accounts · plain positive number like 1 or 0.5"] = new[] { "已選取帳戶的比例 · 正數，例如 1 或 0.5", "選択中の口座の比率 · 1 や 0.5 などの正の数", "선택한 계좌의 비율 · 1 또는 0.5 같은 양수" };
            t["Max contracts for the highlighted accounts · whole number >= 1"] = new[] { "已選取帳戶的手數上限 · 整數，至少 1", "選択中の口座の最大枚数 · 1 以上の整数", "선택한 계좌의 최대 계약 수 · 1 이상의 정수" };
            t["Email alerts (through NinjaTrader's email share service)"] = new[] { "電郵提醒（經 NinjaTrader 電郵分享服務）", "メール通知（NinjaTrader のメール共有サービス経由）", "이메일 알림 (NinjaTrader 이메일 공유 서비스 사용)" };
            t["Send alerts to this address · empty = off"] = new[] { "提醒寄往此地址 · 留空 = 關閉", "この宛先に通知 · 空欄 = オフ", "이 주소로 알림 · 비워 두면 끔" };
            t["Alert email address"] = new[] { "提醒電郵地址", "通知メールアドレス", "알림 이메일 주소" };
            t["To"] = new[] { "收件人", "宛先", "받는 사람" };
            t["Account locked"] = new[] { "帳戶已鎖定", "口座ロック", "계좌 잠김" };
            t["Errors"] = new[] { "錯誤", "エラー", "오류" };
            t["Target / daily loss hit"] = new[] { "達到目標／每日虧損", "目標／日次損失に到達", "목표/일일 손실 도달" };
            t["Leader disconnected"] = new[] { "主帳戶斷線", "リーダー切断", "리더 연결 끊김" };
            t["Email alert"] = new[] { "電郵提醒", "メール通知", "이메일 알림" };
            t["Save"] = new[] { "儲存", "保存", "저장" };
            t["Save the email alert settings"] = new[] { "儲存電郵提醒設定", "メール通知の設定を保存", "이메일 알림 설정 저장" };
            t["Send test"] = new[] { "寄出測試", "テスト送信", "테스트 보내기" };
            t["Send one test mail now through NinjaTrader's default email share service"] = new[] { "立即經 NinjaTrader 預設電郵分享服務寄出一封測試郵件", "NinjaTrader の既定メール共有サービスで今すぐテストメールを 1 通送信", "NinjaTrader 기본 이메일 공유 서비스로 지금 테스트 메일 1통 보내기" };
            t["Set up an email share service in NinjaTrader: Tools > Options > General > Share services. KINFX stores no password. Alerts never place, change or cancel orders."] = new[] { "請先在 NinjaTrader 設定電郵分享服務：Tools > Options > General > Share services。KINFX 不儲存任何密碼。提醒絕不會下單、改單或取消委託。", "NinjaTrader でメール共有サービスを設定してください：Tools > Options > General > Share services。KINFX はパスワードを保存しません。通知が注文の発注・変更・取消を行うことはありません。", "NinjaTrader에서 이메일 공유 서비스를 설정하세요: Tools > Options > General > Share services. KINFX는 비밀번호를 저장하지 않습니다. 알림은 주문을 내거나 변경하거나 취소하지 않습니다." };
            t["Not saved: enter one plain email address like name@example.com"] = new[] { "未儲存：請輸入一個電郵地址，例如 name@example.com", "保存されていません：name@example.com のようなメールアドレスを 1 つ入力してください", "저장 안 됨: name@example.com 같은 이메일 주소를 하나 입력하세요" };
            t["Saved. Alerts are on."] = new[] { "已儲存。提醒已開啟。", "保存しました。通知はオンです。", "저장됨. 알림이 켜져 있습니다." };
            t["Saved. Alerts are off."] = new[] { "已儲存。提醒已關閉。", "保存しました。通知はオフです。", "저장됨. 알림이 꺼져 있습니다." };
            t["Enter one plain email address like name@example.com first."] = new[] { "請先輸入一個電郵地址，例如 name@example.com。", "先に name@example.com のようなメールアドレスを 1 つ入力してください。", "먼저 name@example.com 같은 이메일 주소를 하나 입력하세요." };
            t["Test handed to NinjaTrader. Check your inbox; if nothing arrives, check Tools > Options > General > Share services."] = new[] { "測試已交給 NinjaTrader。請查看收件匣；如未收到，請檢查 Tools > Options > General > Share services。", "テストを NinjaTrader に渡しました。受信箱を確認してください。届かない場合は Tools > Options > General > Share services を確認してください。", "테스트를 NinjaTrader에 넘겼습니다. 받은편지함을 확인하세요. 오지 않으면 Tools > Options > General > Share services를 확인하세요." };
            t["Email not sent: "] = new[] { "電郵未寄出：", "メール未送信：", "이메일 미발송: " };
            t["Email alerts on: {0}"] = new[] { "電郵提醒已開啟：{0}", "メール通知オン：{0}", "이메일 알림 켜짐: {0}" };
            t["Email alerts off."] = new[] { "電郵提醒已關閉。", "メール通知オフ。", "이메일 알림 꺼짐." };
            t["Email test handed to NinjaTrader for {0}; check the inbox and NinjaTrader's log."] = new[] { "測試電郵已交給 NinjaTrader（{0}）；請查看收件匣及 NinjaTrader 日誌。", "テストメールを NinjaTrader に渡しました（{0}）。受信箱と NinjaTrader のログを確認してください。", "테스트 메일을 NinjaTrader에 넘겼습니다 ({0}). 받은편지함과 NinjaTrader 로그를 확인하세요." };
            t["Email alert not sent (blocked by NinjaTrader share service): {0}"] = new[] { "電郵提醒未寄出（NinjaTrader 分享服務拒絕）：{0}", "メール通知未送信（NinjaTrader 共有サービスが拒否）：{0}", "이메일 알림 미발송 (NinjaTrader 공유 서비스 거부): {0}" };
            t["Resume new entries"] = new[] { "恢復新進場", "新規エントリーを再開", "신규 진입 재개" };
            t["Resume new entries after a pause · entries the leader opened while paused are NOT copied"] = new[] { "暫停後恢復新進場 · 暫停期間主帳戶的進場不會補跟", "一時停止後に新規エントリーを再開 · 停止中のリーダーのエントリーはコピーされません", "일시정지 후 신규 진입 재개 · 일시정지 중 리더 진입은 복사되지 않음" };
            t["Re-follow closed accounts"] = new[] { "重新跟隨已平倉帳戶", "決済済み口座を再フォロー", "청산된 계좌 다시 팔로우" };
            t["Re-follow closed accounts ({0})"] = new[] { "重新跟隨已平倉帳戶（{0}）", "決済済み口座を再フォロー（{0}）", "청산된 계좌 다시 팔로우 ({0})" };
            t["Tick again every follower that closed after a copied exit and is flat, connected and unlocked. Only LATER new entries are copied; nothing is caught up."] = new[] { "重新勾選所有因跟隨出場而平倉、且無持倉、已連線、未鎖定的跟單帳戶。只會跟隨之後的新進場，不會補單。", "コピーした決済で決済済みとなり、ノーポジション・接続中・未ロックのフォロワーを再度チェックします。以後の新規エントリーのみコピーし、追いかけ注文はしません。", "복사된 청산으로 청산되었고 포지션 없음·연결됨·잠금 해제된 팔로워를 다시 체크합니다. 이후 신규 진입만 복사하며 따라잡기 주문은 없습니다." };
            t["Auto re-follow"] = new[] { "自動重新跟隨", "自動再フォロー", "자동 다시 팔로우" };
            t["Auto re-follow: after this account's copied exit completes and the leader is flat, tick it again automatically. Only later new entries are copied. Off = re-tick by hand."] = new[] { "自動重新跟隨：此帳戶的跟隨出場完成且主帳戶已無持倉後，自動重新勾選。只會跟隨之後的新進場。關閉 = 手動重新勾選。", "自動再フォロー：この口座のコピー決済が完了し、リーダーがノーポジションになったら自動で再チェックします。以後の新規エントリーのみコピー。オフ = 手動で再チェック。", "자동 다시 팔로우: 이 계좌의 복사 청산이 끝나고 리더 포지션이 없으면 자동으로 다시 체크합니다. 이후 신규 진입만 복사. 끔 = 직접 다시 체크." };
            t["Resume NEW entries?\n\nEntries the leader opened while paused are NOT copied (no catch-up); only later new entries are. Locked or unticked followers stay as they are."] = new[] { "要恢復新進場嗎？\n\n暫停期間主帳戶的進場不會補跟；只會跟隨之後的新進場。已鎖定或未勾選的跟單帳戶維持不變。", "新規エントリーを再開しますか？\n\n停止中のリーダーのエントリーはコピーしません（追いかけなし）。以後の新規エントリーのみコピーします。ロック中・未チェックのフォロワーはそのままです。", "신규 진입을 재개할까요?\n\n일시정지 중 리더 진입은 복사하지 않습니다(따라잡기 없음). 이후 신규 진입만 복사합니다. 잠긴 계좌나 체크 해제된 팔로워는 그대로입니다." };
            t["Resume cancelled; still paused."] = new[] { "已取消恢復；仍然暫停。", "再開を取り消しました。一時停止のままです。", "재개 취소됨. 계속 일시정지 상태입니다." };
            t["Re-follow: no closed account is flat, connected and unlocked."] = new[] { "重新跟隨：沒有無持倉、已連線且未鎖定的已平倉帳戶。", "再フォロー：ノーポジション・接続中・未ロックの決済済み口座はありません。", "다시 팔로우: 포지션 없음·연결됨·잠금 해제된 청산 계좌가 없습니다." };
            t["Tick {0} closed account(s) again?\n{1}\n\nOnly LATER new entries are copied; nothing is caught up. Each account is checked again (flat, connected, unlocked)."] = new[] { "要重新勾選 {0} 個已平倉帳戶嗎？\n{1}\n\n只會跟隨之後的新進場，不會補單。每個帳戶會再檢查一次（無持倉、已連線、未鎖定）。", "決済済み口座 {0} 件を再チェックしますか？\n{1}\n\n以後の新規エントリーのみコピーし、追いかけ注文はしません。各口座を再確認します（ノーポジション・接続中・未ロック）。", "청산된 계좌 {0}개를 다시 체크할까요?\n{1}\n\n이후 신규 진입만 복사하며 따라잡기 주문은 없습니다. 각 계좌를 다시 확인합니다(포지션 없음·연결됨·잠금 해제)." };
            t["Re-follow cancelled; nothing changed."] = new[] { "已取消重新跟隨；沒有任何變更。", "再フォローを取り消しました。変更はありません。", "다시 팔로우 취소됨. 변경 사항 없음." };
            t["Re-follow: {0} — only later new entries are copied."] = new[] { "重新跟隨：{0} — 只會跟隨之後的新進場。", "再フォロー：{0} — 以後の新規エントリーのみコピー。", "다시 팔로우: {0} — 이후 신규 진입만 복사." };
            t["Auto re-follow: {0} — only later new entries are copied."] = new[] { "自動重新跟隨：{0} — 只會跟隨之後的新進場。", "自動再フォロー：{0} — 以後の新規エントリーのみコピー。", "자동 다시 팔로우: {0} — 이후 신규 진입만 복사." };
            t["Auto re-follow error: {0}"] = new[] { "自動重新跟隨錯誤：{0}", "自動再フォローエラー：{0}", "자동 다시 팔로우 오류: {0}" };
            t["Pause new entries at the boundary (off = notice only)"] = new[] { "於交易日分界暫停新進場（關閉 = 只作提示）", "境界で新規エントリーを一時停止（オフ = 通知のみ）", "경계 시각에 신규 진입 일시정지 (끔 = 알림만)" };
            t["Trading-day boundary {0} {1}:00 (DST aware). Notice only: entries NOT paused. Baseline / latch NOT auto-reset."] = new[] { "交易日界線 {0} {1}:00（已考慮夏令時間）。只作提示：新進場未暫停。基準與鎖定不會自動重設。", "取引日の境界 {0} {1}:00（夏時間対応）。通知のみ：新規エントリーは停止していません。基準値・ロックは自動リセットされません。", "거래일 경계 {0} {1}:00 (서머타임 반영). 알림만: 신규 진입은 일시정지되지 않았습니다. 기준값/잠금은 자동 초기화되지 않습니다." };
            t["Resumed NEW entries. Leader entries seen while paused are NOT copied; only later new entries are. Locked or unticked followers stay as they are."] = new[] { "已恢復新進場。暫停期間主帳戶的進場不會補跟；只會跟隨之後的新進場。已鎖定或未勾選的跟單帳戶維持不變。", "新規エントリーを再開しました。停止中のリーダーのエントリーはコピーしません。以後の新規エントリーのみコピーします。ロック中・未チェックのフォロワーはそのままです。", "신규 진입을 재개했습니다. 일시정지 중 리더 진입은 복사하지 않고 이후 신규 진입만 복사합니다. 잠긴 계좌나 체크 해제된 팔로워는 그대로입니다." };
            t["Resume refused: {0}."] = new[] { "拒絕恢復：{0}。", "再開を拒否：{0}。", "재개 거부: {0}." };
            t["no managed session"] = new[] { "沒有管理中的工作階段", "管理中のセッションがありません", "관리 중인 세션 없음" };
            t["entries are already on"] = new[] { "新進場已經開啟", "新規エントリーはすでにオンです", "신규 진입이 이미 켜져 있음" };
            t["entries were stopped by a safety block, not by a pause; Stop when flat, then Start"] = new[] { "新進場是被安全封鎖停止，不是暫停；請在無持倉時停止，再重新開始", "新規エントリーは一時停止ではなく安全ブロックで停止しました。ノーポジションで停止してから開始してください", "신규 진입은 일시정지가 아닌 안전 차단으로 중지되었습니다. 포지션이 없을 때 중지 후 다시 시작하세요" };
            t["flatten / unresolved execution evidence / persistence failure"] = new[] { "平倉中／未解決的成交紀錄／儲存失敗", "決済中／未解決の約定記録／保存失敗", "청산 중/미해결 체결 기록/저장 실패" };
            t["leader disconnected"] = new[] { "主帳戶已斷線", "リーダー切断", "리더 연결 끊김" };
            t["session history bound reached"] = new[] { "工作階段紀錄已達上限", "セッション履歴の上限に到達", "세션 기록 한도 도달" };
            t["a follower has an unresolved provider fault or flatten"] = new[] { "有跟單帳戶的券商錯誤或平倉尚未解決", "フォロワーに未解決のプロバイダー障害または決済があります", "팔로워에 미해결 공급자 오류 또는 청산이 있음" };
            t["Resume refused: entries were stopped by a safety block, not by a pause; Stop when flat, then Start."] = new[] { "拒絕恢復：新進場是被安全封鎖停止，不是暫停；請在無持倉時停止，再重新開始。", "再開を拒否：新規エントリーは一時停止ではなく安全ブロックで停止しました。ノーポジションで停止してから開始してください。", "재개 거부: 신규 진입은 일시정지가 아닌 안전 차단으로 중지되었습니다. 포지션이 없을 때 중지 후 다시 시작하세요." };
            t["Closed · re-follow"] = new[] { "已平倉 · 待重新跟隨", "決済済み · 再フォロー待ち", "청산됨 · 다시 팔로우 대기" };
            t["Trading-day boundary"] = new[] { "交易日分界", "取引日の境界", "거래일 경계" };
            t["Re-follow"] = new[] { "重新跟隨", "再フォロー", "다시 팔로우" };
            t["Closed"] = new[] { "已平倉", "決済済み", "청산됨" };
            t["Info"] = new[] { "資訊", "情報", "정보" };
            t["Order"] = new[] { "委託", "注文", "주문" };
            t["Warn"] = new[] { "警告", "警告", "경고" };
            t["Lock"] = new[] { "鎖定", "ロック", "잠금" };
            t["General"] = new[] { "一般", "一般", "일반" };
            t["Session"] = new[] { "連線階段", "セッション", "세션" };
            t["Accounts"] = new[] { "帳戶", "アカウント", "계정" };
            t["Account"] = new[] { "帳戶", "アカウント", "계정" };
            t["Level"] = new[] { "等級", "レベル", "레벨" };
            t["Time"] = new[] { "時間", "時刻", "시간" };
            t["Category"] = new[] { "分類", "分類", "분류" };
            t["Message"] = new[] { "訊息", "メッセージ", "메시지" };
            t["Auto-scroll"] = new[] { "自動捲動", "自動スクロール", "자동 스크롤" };
            t["Plain text"] = new[] { "純文字", "プレーンテキスト", "일반 텍스트" };
            t["Export (account names masked)"] = new[] { "匯出（帳戶名稱已遮蔽）", "エクスポート（口座名をマスク）", "내보내기 (계정명 마스킹)" };
            t["Show only one severity level"] = new[] { "僅顯示一種嚴重程度", "1つの重要度のみ表示", "심각도 하나만 표시" };
            t["Show only one account"] = new[] { "僅顯示一個帳戶", "1つの口座のみ表示", "계정 하나만 표시" };
            t["Search the log text"] = new[] { "搜尋日誌內容", "ログのテキストを検索", "로그 텍스트 검색" };
            t["Write this log to a file with account names masked"] = new[] { "將此日誌寫入檔案，帳戶名稱已遮蔽", "口座名をマスクしてこのログをファイルに書き出す", "계정명을 마스킹하여 이 로그를 파일로 저장" };
            t["Log exported to {0} (account names masked)."] = new[] { "日誌已匯出至 {0}（帳戶名稱已遮蔽）。", "ログを {0} にエクスポートしました（口座名はマスク済み）。", "로그를 {0}에 내보냈습니다(계정명 마스킹됨)." };
            t["Log export failed: {0}"] = new[] { "日誌匯出失敗：{0}", "ログのエクスポートに失敗: {0}", "로그 내보내기 실패: {0}" };
            t["Log file write failed (daily log disabled for this session): {0}"] = new[] { "日誌檔案寫入失敗（本次工作階段停用每日日誌）：{0}", "ログファイルの書き込みに失敗（このセッションでは日次ログを無効化）: {0}", "로그 파일 쓰기 실패(이 세션의 일일 로그 비활성화): {0}" };
#if KINFX_SIM_TEST
            t["Dry run"] = new[] { "試運行", "ドライラン", "드라이런" };
            t["DRY RUN: decide and log every order, cancel and flatten as \"DRY RUN would send ...\" but send NOTHING. Fixed for the whole session: change it only while stopped."] = new[] { "DRY RUN（試運行）：決定並記錄每一筆下單、取消與平倉為「DRY RUN would send ...」，但不會送出任何內容。整個連線期間固定：僅能在停止時變更。", "DRY RUN（ドライラン）：すべての注文・取消・全決済を判断し「DRY RUN would send ...」としてログに記録しますが、何も送信しません。セッション中は固定：変更は停止中のみ可能。", "DRY RUN(드라이런): 모든 주문, 취소, 청산을 판단하여 \"DRY RUN would send ...\"로 기록하지만 아무것도 전송하지 않습니다. 세션 전체 동안 고정: 중지 상태에서만 변경할 수 있습니다." };
            t["SIM TEST: Start refused — missing leader or follower list."] = new[] { "SIM TEST：已拒絕開始 — 缺少主帳戶或跟單帳戶清單。", "SIM TEST: 開始を拒否 — リーダーまたはフォロワーの一覧がありません。", "SIM TEST: 시작 거부 — 리더 또는 팔로워 목록이 없습니다." };
            t["SIM TEST: Start refused — every account must be Simulator / Playback. Live accounts are never accepted."] = new[] { "SIM TEST：已拒絕開始 — 所有帳戶都必須是 Simulator / Playback。絕不接受實盤帳戶。", "SIM TEST: 開始を拒否 — すべての口座が Simulator / Playback である必要があります。実口座は一切受け付けません。", "SIM TEST: 시작 거부 — 모든 계좌는 Simulator / Playback이어야 합니다. 실계좌는 절대 허용되지 않습니다." };
            t["SIM TEST: Flatten refused — no instrument. Nothing was sent."] = new[] { "SIM TEST：已拒絕平倉 — 未指定商品。未送出任何請求。", "SIM TEST: 全決済を拒否 — 銘柄なし。何も送信されていません。", "SIM TEST: 청산 거부 — 종목 없음. 아무것도 전송되지 않았습니다." };
            t["SIM TEST: Flatten refused — scope contains a non-Simulator/Playback or missing account. Nothing was sent."] = new[] { "SIM TEST：已拒絕平倉 — 範圍內含有非 Simulator/Playback 或不存在的帳戶。未送出任何請求。", "SIM TEST: 全決済を拒否 — 対象範囲に Simulator/Playback 以外または存在しない口座が含まれています。何も送信されていません。", "SIM TEST: 청산 거부 — 범위에 Simulator/Playback이 아니거나 없는 계좌가 포함되어 있습니다. 아무것도 전송되지 않았습니다." };
            t["Dry run cannot be changed while a session is attached; stop flat first."] = new[] { "工作階段連線期間無法變更 Dry run（試運行）；請先於持倉歸零後停止。", "セッション接続中は Dry run（ドライラン）を変更できません；先にフラット後に停止してください。", "세션이 연결된 동안에는 Dry run(드라이런)을 변경할 수 없습니다; 먼저 청산 후 중지하십시오." };
            t["SIM TEST: account is not Simulator / Playback at send time; call was NOT sent"] = new[] { "模擬測試：送出時帳戶並非 Simulator / Playback；該呼叫未送出", "SIM テスト：送信時に口座が Simulator / Playback ではありません；呼び出しは送信されませんでした", "SIM 테스트: 전송 시점에 계좌가 Simulator / Playback이 아닙니다; 호출이 전송되지 않았습니다" };
            t["DRY RUN would send to {0}: {1}"] = new[] { "DRY RUN（試運行）將傳送至 {0}：{1}", "DRY RUN（ドライラン）: {0} に送信予定: {1}", "DRY RUN(드라이런) {0}(으)로 전송 예정: {1}" };
            t["Dry run is ON: nothing will be sent."] = new[] { "試運行已開啟：不會傳送任何指令。", "ドライランはオン：何も送信されません。", "드라이런 켜짐: 아무것도 전송되지 않습니다." };
            t["Dry run is OFF: real simulation orders WILL be sent."] = new[] { "試運行已關閉：將會傳送實際的模擬委託。", "ドライランはオフ：実際のシミュレーション注文が送信されます。", "드라이런 꺼짐: 실제 시뮬레이션 주문이 전송됩니다." };
            t["SIM TEST BUILD · demo / prop-firm evaluation accounts"] = new[] { "SIM 測試版 · 模擬／自營交易公司考核帳戶", "SIMテストビルド · デモ／プロップファーム評価口座", "SIM 테스트 빌드 · 데모 / 프롭펌 평가 계좌" };
            t["SIM TEST BUILD: copies between simulated accounts. An account not yet confirmed as simulated shows a risk warning first."] = new[] { "SIM 測試版：在模擬帳戶之間跟單。尚未確認為模擬的帳戶會先顯示風險警告。", "SIMテストビルド：シミュレーション口座間でコピーします。シミュレーションと未確認の口座は先にリスク警告を表示します。", "SIM 테스트 빌드: 시뮬레이션 계좌 간에 복사합니다. 시뮬레이션으로 확인되지 않은 계좌는 먼저 위험 경고를 표시합니다." };
            t["SIM TEST BUILD: every NinjaTrader account is listed. Simulator / Playback accounts and accounts you confirmed as simulated (sim-allow.txt, filled in for you when you answer the risk warning) can Start / Flatten. ATM means order mirroring, not a follower native ATM. Orders ARE sent to those accounts."] = new[] { "SIM 測試版：列出所有 NinjaTrader 帳戶。Simulator / Playback 帳戶及您已確認為模擬的帳戶（sim-allow.txt，回答風險警告後自動填寫）可使用 Start / Flatten。ATM 指委託單鏡像，並非跟單帳戶的原生 ATM。委託單「會」發送至這些帳戶。", "SIMテストビルド：NinjaTrader の全口座を表示します。Simulator / Playback 口座と、シミュレーションと確認した口座（sim-allow.txt、リスク警告に回答すると自動記入）で Start / Flatten が可能。ATM は注文のミラーリングで、フォロワーのネイティブ ATM ではありません。これらの口座には注文が送信されます。", "SIM 테스트 빌드: 모든 NinjaTrader 계좌를 표시합니다. Simulator / Playback 계좌와 시뮬레이션으로 확인한 계좌(sim-allow.txt, 위험 경고에 답하면 자동 기록)는 Start / Flatten을 사용할 수 있습니다. ATM은 주문 미러링이며 팔로워의 네이티브 ATM이 아닙니다. 해당 계좌로 주문이 전송됩니다." };
            t["Dry run: on"] = new[] { "試運行：開", "ドライラン：オン", "드라이런: 켜짐" };
            t["Dry run: off"] = new[] { "試運行：關", "ドライラン：オフ", "드라이런: 꺼짐" };
#endif
            return t;
        }
        sealed class Pat { public string Key; public System.Text.RegularExpressions.Regex Regex; public int Holes; }
        static readonly System.Collections.Generic.List<Pat> Patterns = BuildPatterns();
        static System.Collections.Generic.List<Pat> BuildPatterns()
        {
            var list = new System.Collections.Generic.List<Pat>();
            foreach (var key in Table.Keys)
            {
                var holes = System.Text.RegularExpressions.Regex.Matches(key, @"\{(\d+)\}");
                if (holes.Count == 0) continue;
                var sb = new System.Text.StringBuilder("^"); int pos = 0; var seen = new System.Collections.Generic.HashSet<int>(); int max = -1;
                foreach (System.Text.RegularExpressions.Match h in holes)
                {
                    sb.Append(System.Text.RegularExpressions.Regex.Escape(key.Substring(pos, h.Index - pos)));
                    int n = int.Parse(h.Groups[1].Value, CultureInfo.InvariantCulture); if (n > max) max = n;
                    sb.Append(seen.Add(n) ? "(?<h" + n + ">.*?)" : "\\k<h" + n + ">");
                    pos = h.Index + h.Length;
                }
                sb.Append(System.Text.RegularExpressions.Regex.Escape(key.Substring(pos))).Append("$");
                if (seen.Count != max + 1) continue;
                list.Add(new Pat { Key = key, Regex = new System.Text.RegularExpressions.Regex(sb.ToString(), System.Text.RegularExpressions.RegexOptions.Singleline), Holes = max + 1 });
            }
            // Longer literal templates first so a specific sentence wins over a generic "Start refused: {0}".
            list.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
            return list;
        }
        public static int Count { get { return Table.Count; } }
        public static System.Collections.Generic.IEnumerable<string> Keys { get { return Table.Keys; } }
        public static string[] Row(string en) { string[] row; return Table.TryGetValue(en, out row) ? row : null; }
    }
// END LANG TABLE
// END KINFX PURE
    internal sealed class AccountTelemetry
    {
        public string Name, Currency, Connection, State, Reason;
        public double Baseline;
        public double Pnl, Equity, Realized, Unrealized, Sod, HighWater;
        public int Position, FollowGeneration, ProcessedFollowGeneration, SessionGeneration;
        public bool Locked, Paused, Following, DisablePending;
        // Following turned off by the engine because this follower completed its exit (closed; re-follow is explicit).
        public bool ExitCompleted;
        public string Direction;
    }
    internal sealed class EngineAccount
    {
        public Account Account;
        public RiskSettings Settings;
        public RiskLatch Latch;
        public bool Paused, Flattening;
        public volatile bool Following=true;
        public int FollowGeneration, ProcessedFollowGeneration;
        public bool DisablePending, ExitCompleted, Cancelling, EvidenceUnresolved, LateEntryCoverageWarning;
        public volatile bool AllocationUncertain;
        // Issue #4: provider calls for THIS account run on its own serial thread, so one stalled provider call cannot delay other accounts.
        // Engine state is still mutated only on the engine worker; completions are posted back to it.
        public SerialWorker Dispatcher;
        // Set on the ACCOUNT thread the moment any provider call for this account throws. Entry commands still queued behind it are not sent
        // (the engine-thread Lock arrives later), which preserves the old "after a failure no further entry is attempted" behaviour.
        public volatile bool ProviderFaulted;
        public string EvidenceReason;
        public readonly HashSet<string> Ignored=new HashSet<string>();
        // Orders that were already terminal when the session started (the platform reloads today's filled orders after a restart). They are history, never unknown live activity.
        public readonly HashSet<Order> PreSessionTerminal=new HashSet<Order>();
        // Provider ids of that terminal history. After a reconnect NinjaTrader rebuilds the same filled history as NEW Order objects with the same id.
        // id -> the terminal state and filled quantity captured then. A rebuilt order is history only if id, state AND filled all match (more fills = new exposure).
        public readonly Dictionary<string,KeyValuePair<OrderState,int>> PreSessionTerminalIds=new Dictionary<string,KeyValuePair<OrderState,int>>(StringComparer.Ordinal);
        public bool KnownHistory(Order o,OrderState state,string providerId){return KnownHistory(o,state,providerId,o==null?0:o.Filled);}
        public bool KnownHistory(Order o,OrderState state,string providerId,int filled)
        {
            if(o!=null&&PreSessionTerminal.Contains(o))return true;
            KeyValuePair<OrderState,int> seen;
            return Order.IsTerminalState(state)&&!string.IsNullOrEmpty(providerId)&&PreSessionTerminalIds.TryGetValue(providerId,out seen)&&seen.Key==state&&seen.Value==filled;
        }
        public void AddHistory(Order o){PreSessionTerminal.Add(o);if(!string.IsNullOrEmpty(o.OrderId)&&Order.IsTerminalState(o.OrderState))PreSessionTerminalIds[o.OrderId]=new KeyValuePair<OrderState,int>(o.OrderState,o.Filled);}
        // Copier orders left in Initialized before Start (never sent). Exempt from the unknown-order lock ONLY while they stay Initialized and unfilled.
        public readonly HashSet<Order> PreSessionGhost=new HashSet<Order>();
        public int DryRunRetired; // dry-run Submits retired as never-sent ghosts; compiled in both builds, only ever non-zero in the SIM TEST build
        public bool LimitFlattenSent; // FlatOnLimit already used this session. Backstop: the "!was" edge already fires it once, as no in-session unlock exists (mutant l4 shows the flag alone is redundant today).
        // When the last follower fill evidence arrived. NinjaTrader raises the order Filled callback ~10 ms BEFORE the PositionUpdate, so for a moment the ledger and Position disagree.
        public DateTime LastFillUtc=DateTime.MinValue;
        // Per-account Flat (user pressed Flat): when the request was queued, when the provider call RETURNED (not threw), and the last follower order event.
        // FlatOffset credits the ledger once the provider proves the account flat: the close order is not a copier link, so without it owned exposure never returns to 0.
        public DateTime FlattenRequestedUtc=DateTime.MinValue, FlattenAcceptedUtc=DateTime.MinValue, LastOrderEventUtc=DateTime.MinValue;
        public int FlatOffset;
        // Set when a leader exit was dropped as "nothing to close". A fill of an entry that arrives LATER means the account now holds lots that no copied exit covers.
        public bool ExitDroppedNothingToClose;
        // Sequence of that dropped exit: only an entry copied BEFORE it can be the uncovered late fill; an entry copied after it is a new trade.
        public int ExitDroppedAtSequence;
        public double HighWater;
        public readonly Dictionary<string,ExitGroup> ExitGroups=new Dictionary<string,ExitGroup>();
        public long CapturedCallbacks, AppliedCallbacks;
    }
    internal sealed class OrderSnapshot
    {
        public Order Source;
        public Account Origin;
        public string InstrumentName;
        public int Session, Execution;
        public string Id, Oco, Name;
        public OrderState State;
        public OrderAction Action;
        public OrderType Type;
        public OrderEntry Entry;
        public TimeInForce Tif;
        public DateTime Gtd;
        public int Quantity;
        public double Limit, Stop;
        // NinjaTrader's Order Ticket / Chart Trader send Sell to OPEN a short and Buy to COVER one, so the action alone cannot say entry vs exit.
        // ExitResolved is set once per leader order (first sight) by CopierEngine.ClassifyLeaderOrder; until then the legacy rule applies.
        public bool? ExitResolved;
        public bool Exit { get { return ExitResolved.HasValue ? ExitResolved.Value : (Action==OrderAction.Sell || Action==OrderAction.BuyToCover); } }
    }
    internal sealed class CopyLink
    {
        public EngineAccount Follower;
        public Order Order;
        public OrderSnapshot Latest;
        public bool Exit, Waiting, Cancelled;
        // Set ONLY when the ENGINE retired this exit because its OCO group completed (never when the leader cancelled it).
        // A retired sibling no longer reserves exit capacity for a DIFFERENT OCO group; a leader-cancelled order that the provider
        // has not acknowledged is still a working order and keeps reserving.
        public bool OcoRetired, OwnershipUncertain, FlatRetired;
        public DateTime WaitingSince=DateTime.UtcNow;
        public string Oco, ProviderId;
        // True while ProviderId is only the client-side id NinjaTrader assigns before submission (event state Initialized). It may be replaced ONCE by the exchange id.
        public bool ProviderIdPreSubmit;
        public int Desired, ObservedFilled, RequestedQuantity, ReservedQuantity;
        public int EntrySequence;
        public ExitGroup Group;
        public OrderAction SubmittedAction;
        public OrderState ObservedState;
        public bool CancellationRequested, SubmissionInFlight, AcceptanceConfirmed;
        // Change commands dispatched but not yet returned from the provider, and the values the LAST one requested. While any is in flight the
        // "nothing to change" comparison must use these, not the order's applied values, or a revert to the old price is silently dropped.
        public int ChangesInFlight;
        public readonly List<int> PendingChangeQuantities=new List<int>();
        public double SentLimit, SentStop; public int SentQty;
        // Set when a Change is handed to the provider; cleared only by the provider's answer (Accepted / Working / TriggerPending / PartFilled or terminal).
        // Real NT (Tradovate): Account.Change returns at once and raises ChangePending with the OLD price; the new price arrives ~1 s later.
        // Comparing against the order's own price in that window re-sent Change on every callback (140-350 Changes per account for 2 leader moves).
        public bool AwaitingChangeAck;
        public readonly HashSet<Order> Wrappers=new HashSet<Order>();
        public readonly List<FollowerSnapshot> ExecutionEvidence=new List<FollowerSnapshot>();
    }
    // A claim is a fixed ordinal interval of ONE copied entry's cumulative captured fills.
    // It is an allocation chosen by this copier, not a broker/ATM parent-child assertion.
    internal sealed class EntryLotClaim
    {
        public readonly CopyLink Entry;
        public readonly int First, Count;
        public EntryLotClaim(CopyLink entry,int first,int count){Entry=entry;First=first;Count=count;}
        public int Captured {get{return Math.Max(0,Math.Min(Count,Entry.ObservedFilled-First));}}
    }
    internal sealed class ExitGroup
    {
        public readonly string Id;
        public readonly int Session;
        public readonly List<EntryLotClaim> Claims=new List<EntryLotClaim>();
        public readonly List<CopyLink> Members=new List<CopyLink>();
        public decimal Demand;
        public int Quota, PlannedQuota, Revision, PendingRetirements;
        public bool Completed, Frozen, FlatRetired;
        public ExitGroup(string id,int session){Id=id;Session=session;}
    }
    internal sealed class GroupSupportCertificate
    {
        public readonly string GroupId;
        public readonly int Session, Revision, Quota, EntryFilled, ExitFilled, Held, Future;
        public readonly long Frontier, LeaderFrontier;
        public readonly bool Supported, Complete, Settled;
        public GroupSupportCertificate(string groupId,int session,int revision,int quota,int entryFilled,int exitFilled,long frontier,long leaderFrontier,bool supported,bool settled,bool retired)
        {
            GroupId=groupId;
            Session=session;Revision=revision;Quota=quota;EntryFilled=entryFilled;ExitFilled=exitFilled;Frontier=frontier;LeaderFrontier=leaderFrontier;
            Held=retired?0:Math.Max(0,entryFilled-exitFilled);Future=retired?0:Math.Max(0,quota-entryFilled);
            Supported=supported;Settled=settled;Complete=supported&&settled&&quota>0&&entryFilled==quota&&exitFilled==quota;
        }
    }
    internal sealed class ProtectionPlan
    {
        public readonly int Session, Owned, Covered, Unprotected;
        public readonly long LeaderFrontier, FollowerFrontier;
        public readonly GroupSupportCertificate[] Groups;
        public ProtectionPlan(int session,long leader,long follower,int owned,int covered,GroupSupportCertificate[] groups)
        {Session=session;LeaderFrontier=leader;FollowerFrontier=follower;Owned=owned;Covered=covered;Unprotected=Math.Max(0,owned-covered);Groups=groups;}
    }
    // This refusal occurs before a provider call; it must not masquerade as unknown acceptance.
    internal sealed class GroupSupportChangedException : InvalidOperationException
    {
        public GroupSupportChangedException(string message):base(message){}
    }
    internal sealed class FollowerSnapshot
    {
        public readonly Account Account;
        public readonly Order Wrapper;
        public readonly string InstrumentName, ProviderId;
        public readonly OrderAction Action;
        public readonly OrderType Type;
        public readonly OrderState State;
        public readonly int Filled, Quantity, Session, WrapperQuantity, WrapperQuantityChanged;
        public readonly double Limit, Stop;
        public FollowerSnapshot(Account account,OrderEventArgs e,int session)
        {
            Account=account; Wrapper=e.Order; Session=session; State=e.OrderState; Quantity=e.Quantity; Limit=e.LimitPrice; Stop=e.StopPrice;
            InstrumentName=Wrapper.Instrument==null?null:Wrapper.Instrument.FullName; ProviderId=e.OrderId;
            // Event payload is execution evidence; wrapper supplies only metadata absent from OrderEventArgs.
            Filled=e.Filled; Action=Wrapper.OrderAction; Type=Wrapper.OrderType;
            // Contradiction hints only: neither wrapper value acknowledges acceptance or adds owned fills.
            WrapperQuantity=Wrapper.Quantity;WrapperQuantityChanged=Wrapper.QuantityChanged;
        }
    }
    // All broker API calls are on one serial worker OUTSIDE queue/collection locks.
    // Account callbacks capture data only; no WPF reads, no Account API under a lock.
    internal sealed class CopierEngine : IDisposable
    {
        readonly SerialWorker worker;
        readonly Action<string> log;
        readonly Action<AccountTelemetry> telemetry;
        readonly Action<bool,string> stateChanged;
        readonly Action<string,string> durableLock;
        Account[] sessionAccounts=new Account[0];
        readonly Dictionary<string,List<CopyLink>> links=new Dictionary<string,List<CopyLink>>();
        readonly OcoMap oco=new OcoMap();
        readonly Dictionary<Order,string> identities=new Dictionary<Order,string>();
        readonly Dictionary<Account,Dictionary<string,string>> providerIdentities=new Dictionary<Account,Dictionary<string,string>>();
        readonly Dictionary<Order,string> wrapperProviderIds=new Dictionary<Order,string>();
        // Wrappers whose recorded provider id is still the pre-submit client id (first seen in state Initialized). Real NT (Tradovate, 2026-10-15):
        // that id is replaced by the exchange id exactly once when the order is submitted; the first replacement is the same order, any later change is not.
        readonly HashSet<Order> preSubmitProviderIds=new HashSet<Order>();
        readonly object identityGate=new object();
        readonly List<EngineAccount> followers=new List<EngineAccount>();
        volatile EngineAccount[] followerView=new EngineAccount[0];
        readonly HashSet<string> tombstones=new HashSet<string>();
        readonly HashSet<EngineAccount> persistenceFailed=new HashSet<EngineAccount>();
        readonly Dictionary<EngineAccount,string> flattenConfirmations=new Dictionary<EngineAccount,string>();
        EventHandler<OrderEventArgs> leaderHandler, followerHandler;
        Account leader;
        Instrument instrument;
        volatile bool entries, attached, disposed, suppressNew, ledgerUnresolved;
        // True only while entries are off because of GuardedPause (user button or trading-day boundary). Every other way entries go off
        // (engine error, leader disconnect, invalid / ambiguous leader order, history bound, persistence failure, flatten) clears it, so
        // ResumeEntries can never undo a safety block; those still need Stop flat + Start.
        volatile bool pauseOnly;
        int pollPending, controlGeneration, sessionGeneration;
        long leaderCaptured, leaderApplied;
        int pendingFollowerCallbacks, nextEntrySequence;
        int providerCallsInFlight;
        readonly List<SerialWorker> dispatchers=new List<SerialWorker>();
        internal int ProviderCallsInFlight { get { return Volatile.Read(ref providerCallsInFlight); } }
        readonly object controlGate=new object();
        readonly object admissionGate=new object();
        bool mirrorAtm=true;
        const string LateEntryReason="Late uncovered entry fill after exit completion — manual recovery required; coverage not established";
        public bool Attached { get { return attached; } }
#if KINFX_SIM_TEST
        // DRY RUN (SIM TEST build only): every provider call is decided and logged as "DRY RUN would ..." but never sent. Fixed for the whole session.
        volatile bool dryRun;
        public bool DryRun
        {
            get { return dryRun; }
            set { if(attached)throw new InvalidOperationException("Dry run cannot be changed while a session is attached; stop flat first."); dryRun=value; }
        }
#endif
        public bool EntriesArmed { get { return entries; } }
        public int SessionGeneration {get{return Volatile.Read(ref sessionGeneration);}}
        public CopierEngine(Action<string> logger,Action<AccountTelemetry> onTelemetry,Action<bool,string> onState,Action<string,string> persistLatch)
        {
            log=logger; telemetry=onTelemetry; stateChanged=onState;durableLock=persistLatch;
            worker=new SerialWorker(ex=>{ entries=false;pauseOnly=false; log("ENGINE ERROR: "+ex.Message+" — entries blocked; coverage unverified, manual recovery required"); stateChanged(attached,"Error — guarded pause; verify exposure manually"); });
        }
        public static string AliasFor(string name)
        {
#if KINFX_SIM_TEST
            return SimAlias(name);
#else
            return "";
#endif
        }
        public static bool Sim(Account a)
        {
#if KINFX_SIM_TEST
            return a.Provider==Provider.Simulator || a.Provider==Provider.Playback || SimAllowed(a.Name);
#else
            return a.Provider==Provider.Simulator || a.Provider==Provider.Playback;
#endif
        }
        // Shown in the account list and tickable. SIM TEST build: every account (an unconfirmed one asks the risk warning before Start / Flatten,
        // and the engine still refuses it until it is confirmed). Default build: Simulator / Playback only, as before.
        // Only CONNECTED accounts, like NinjaTrader's own Accounts tab: NT keeps Account objects of a removed / closed / max-loss account after its
        // connection is gone (seen on real NT: 5 closed Tradeify accounts, Playback101 and Backtest listed as Disconnected with no balance).
        // A follower that disconnects DURING a session stays in the table (the table is never rebuilt while attached) and is locked as before.
        public static bool Listable(Account a){return a!=null&&Connected(a)&&(DashboardSafety.ListAllAccounts||Sim(a));}
#if KINFX_SIM_TEST
        // SIM TEST BUILD ONLY. Prop-firm evaluation accounts (Lucid / Tradeify on Tradovate) hang off a connection NinjaTrader labels Mode=Live,
        // so Provider alone cannot tell them apart from a funded account. The operator attests per account by exact name in
        // Documents\NinjaTrader 8\templates\KINFXTradeCopier\sim-allow.txt (one name per line, # comments). Loaded once per compile/restart.
        // Empty or missing file = Simulator / Playback only. Sim() feeds the account list, Start/Flatten scope and the send-time recheck in Dispatch.
        // Order matters: simAlias must be initialised BEFORE LoadSimAllow() runs (C# static field initialisers execute in textual order).
        static Dictionary<string,string> simAlias=new Dictionary<string,string>(StringComparer.Ordinal);
        static HashSet<string> simAllow=LoadSimAllow();
        static HashSet<string> LoadSimAllow()
        {
            var h=new HashSet<string>(StringComparer.Ordinal);
            try
            {
                string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"NinjaTrader 8","templates","KINFXTradeCopier","sim-allow.txt");
                foreach(var kv in LoadAllowFile(path)){h.Add(kv.Key);if(kv.Value.Length>0)simAlias[kv.Key]=kv.Value;}
            }
            catch{ h.Clear(); simAlias.Clear(); }
            return h;
        }
        // Reads one allow file into name -> alias ("" when none). Missing file = empty. Any read error throws so the caller refuses everything.
        public static Dictionary<string,string> LoadAllowFile(string path)
        {
            var d=new Dictionary<string,string>(StringComparer.Ordinal);
            if(!File.Exists(path))return d;
            foreach(var line in File.ReadAllLines(path)){string name,alias;if(DashboardChoices.ParseAllowLine(line,out name,out alias))d[name]=alias;}
            return d;
        }
        public static string SimAlias(string name){string a;return name!=null&&simAlias.TryGetValue(name,out a)?a:"";}
        public static bool SimAllowed(string name){var h=Volatile.Read(ref simAllow);return name!=null&&h.Contains(name);}
        public static void SetSimAllow(IEnumerable<string> names){Volatile.Write(ref simAllow,new HashSet<string>(names??new string[0],StringComparer.Ordinal));}
        public static string AllowPath {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"NinjaTrader 8","templates","KINFXTradeCopier","sim-allow.txt");}}
        // Called ONLY after the user answered Yes to the risk warning that names these accounts. Appends them to sim-allow.txt (existing lines and aliases
        // kept, no duplicates), then admits them in memory. The file is written first: if it cannot be written nothing is admitted. Returns null or the reason.
        public static string RememberSimulated(IEnumerable<string> names,string path=null)
        {
            try
            {
                path=path??AllowPath;
                var want=(names??new string[0]).Distinct(StringComparer.Ordinal).ToArray();
                if(want.Length==0)return "no account";
                foreach(var n in want)if(string.IsNullOrEmpty(n)||n!=n.Trim()||n.StartsWith("#")||n.IndexOf('=')>=0||n.IndexOf('\n')>=0||n.IndexOf('\r')>=0)return "account name cannot be stored safely: "+n;
                var have=new HashSet<string>(LoadAllowFile(path).Keys,StringComparer.Ordinal);
                var lines=File.Exists(path)?File.ReadAllLines(path).ToList():new List<string>{"# KINFX Trade Copier: accounts you confirmed as simulated / prop-firm evaluation. One exact NinjaTrader account name per line, optional \"= alias\". Delete a line to be asked again."};
                foreach(var n in want)if(!have.Contains(n))lines.Add(n);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                SettingsStore.AtomicWriteLines(path,lines.ToArray());
                var now=new HashSet<string>(Volatile.Read(ref simAllow),StringComparer.Ordinal);foreach(var n in want)now.Add(n);Volatile.Write(ref simAllow,now);
                return null;
            }
            catch(Exception ex){return ex.Message;}
        }
        // Scope check for the KINFX_SIM_TEST build. A scope is allowed only when it is non-empty, has no null member and EVERY account is
        // Simulator / Playback. One Live (or any other provider) account refuses the whole scope. Pure: reads Provider only.
        public static bool SimScope(IEnumerable<Account> scope)
        {
            if(scope==null)return false;
            int n=0;
            foreach(var a in scope){ if(a==null||!Sim(a))return false; n++; }
            return n>0;
        }
#endif
        static bool Connected(Account a) { return a.Connection!=null && a.Connection.Status==ConnectionStatus.Connected; }
        public static int Position(Account a,Instrument inst)
        {
            lock(a.Positions) { var p=a.Positions.FirstOrDefault(x=>x.Instrument.FullName==inst.FullName); return p==null ? 0 : p.Quantity*(p.MarketPosition==MarketPosition.Long?1:p.MarketPosition==MarketPosition.Short?-1:0); }
        }
        // Names every account that blocks Start and why, so the user can flatten / cancel the right one instead of guessing.
        public static string DirtyReport(IEnumerable<Account> accounts,Instrument inst)
        {
            var parts=new List<string>();
            foreach(var a in accounts)
            {
                if(Clean(a,inst))continue;
                int p=Position(a,inst); string w="";
                lock(a.Orders){var open=a.Orders.Where(o=>BlocksStart(o,inst)).ToArray();if(open.Length>0)w=open.Length+" open order(s) ["+string.Join(",",open.Select(o=>o.OrderAction+" "+o.OrderState))+"]";}
                parts.Add(a.Name+": "+(p!=0?"position "+p.ToString("+0;-0"):"")+(p!=0&&w.Length>0?", ":"")+w);
            }
            return "Blocked by "+string.Join("; ",parts)+".";
        }
        // A COPIER order (Name "KTC>...") still in state Initialized was created but never sent (left behind by a dry run or a failed submit). It is not on the market and cannot fill,
        // so it must not block a new session. Any other Initialized order (manual / another tool) still blocks: it may be about to submit.
        static bool BlocksStart(Order o,Instrument inst){return o.Instrument!=null&&o.Instrument.FullName==inst.FullName&&!Order.IsTerminalState(o.OrderState)&&!(o.OrderState==OrderState.Initialized&&o.Name!=null&&o.Name.StartsWith("KTC>",StringComparison.Ordinal));}
        public static bool Clean(Account a,Instrument inst)
        {
            if(Position(a,inst)!=0)return false;
            lock(a.Orders) return !a.Orders.Any(o=>BlocksStart(o,inst));
        }
        public void Start(Account selected,Instrument inst,IEnumerable<Tuple<Account,RiskSettings,RiskLatch>> selectedFollowers,bool copyAtm=true)
        {
            // PUBLIC ALPHA: trading DISABLED for ALL providers, including Sim / Playback.
            // Only same-artifact synthetic Account fixtures may reach offline paths; not a certified firewall.
#if !KINFX_SIM_TEST
            if(typeof(Account).Assembly!=typeof(CopierEngine).Assembly)
            { BlockEntries();log("PUBLIC ALPHA SAFETY HOLD — Start blocked for ALL providers, including Sim / Playback. Trading DISABLED.");return; }
#endif
#if !KINFX_OFFLINE_FAKE && !KINFX_SIM_TEST
            BlockEntries();log("ALL-ACCOUNT SAFETY HOLD — Start blocked, including Simulator / Playback. UI preview only; provider acceptance unverified.");return;
#else
#if KINFX_SIM_TEST
            // SIM TEST BUILD: a scope is accepted only if EVERY account is Simulator / Playback. Anything else (Live, null, empty) is refused here, before any state changes.
            if(selected==null||selectedFollowers==null){ BlockEntries();log("SIM TEST: Start refused — missing leader or follower list.");return; }
            var scopeCheck=selectedFollowers.ToArray();selectedFollowers=scopeCheck; // verified list == used list: enumerate once
            if(scopeCheck.Any(x=>x==null||x.Item1==null||x.Item2==null||x.Item3==null)||!SimScope(scopeCheck.Select(x=>x.Item1).Concat(new[]{selected}))){ BlockEntries();log("SIM TEST: Start refused — every account must be Simulator / Playback. Live accounts are never accepted.");return; }
#endif
            var list=selectedFollowers.Select(x=>Tuple.Create(x.Item1,x.Item2.Clone(),new RiskLatch{Locked=x.Item3.Locked,Reason=x.Item3.Reason})).ToArray();
            int request=Volatile.Read(ref controlGeneration);
            worker.Post(()=>
            {
                if(disposed || request!=Volatile.Read(ref controlGeneration))return;
                if(attached) { log("Already managing a session. Stop while flat first."); return; }
                // Live blocked in v0.2: OCO double-fill, provider execution ordering and partial-fill divergence require simulation acceptance first.
                if(!Sim(selected) || list.Any(x=>!Sim(x.Item1))) { log("LIVE BLOCKED in v0.2. Only Simulator / Playback; no live override is provided."); return; }
                if(list.Length==0 || list.Select(x=>x.Item1.Name).Distinct().Count()!=list.Length || list.Any(x=>x.Item1==selected || x.Item1.Name==selected.Name || !x.Item2.Valid() || x.Item3.Locked)) { log("Start refused: select unlocked, valid followers."); return; }
                var accounts=list.Select(x=>x.Item1).Concat(new[]{selected}).Distinct().ToArray();
                if(accounts.Any(a=>!Connected(a))) { log("Start refused: all selected accounts must be connected."); return; }
                if(!SessionOwnership.Acquire(accounts.Select(a=>"account:"+a.Name),this)) { log("Start refused: another copier window owns one of these accounts."); return; }
                try
                {
                    if(accounts.Any(a=>!Clean(a,inst))) throw new InvalidOperationException("Selected instrument already has a position / working order. No adoption or catch-up. "+DirtyReport(accounts,inst));
                    Interlocked.Increment(ref sessionGeneration);
                    leader=selected; instrument=inst;mirrorAtm=copyAtm;sessionAccounts=accounts; DisposeDispatchers(); followers.Clear(); links.Clear(); tombstones.Clear();
                    lock(identityGate) { identities.Clear(); providerIdentities.Clear(); wrapperProviderIds.Clear(); preSubmitProviderIds.Clear(); }
                    foreach(var item in list) followers.Add(new EngineAccount { Dispatcher=NewDispatcher(),Account=item.Item1,Settings=item.Item2.Clone(),Latch=new RiskLatch {Locked=item.Item3.Locked,Reason=item.Item3.Reason},HighWater=item.Item2.HighWater });
                    foreach(var f in followers){ Order[] existing; lock(f.Account.Orders)existing=f.Account.Orders.ToArray(); foreach(var o in existing){ if(o==null||o.Instrument==null||o.Instrument.FullName!=inst.FullName)continue; if(Order.IsTerminalState(o.OrderState))f.AddHistory(o); else if(o.OrderState==OrderState.Initialized&&o.Name!=null&&o.Name.StartsWith("KTC>",StringComparison.Ordinal))f.PreSessionGhost.Add(o); } }
                    foreach(var f in followers.Where(x=>!x.Settings.CopyAtm||!copyAtm))log(f.Account.Name+": ATM exit mirroring OFF — nonmarket exits skipped; market closes only. No native ATM clone / autonomous protection.");
                    followerView=followers.ToArray();
                    attached=true; entries=false;pauseOnly=false;suppressNew=false;
                    int session=sessionGeneration; Account origin=leader; string instrumentName=instrument.FullName;
                    leaderHandler=(sender,args)=>LeaderUpdate(sender,args,session,origin,instrumentName);
                    followerHandler=(sender,args)=>FollowerUpdate(sender,args,session,instrumentName);
                    leader.OrderUpdate+=leaderHandler;
                    foreach(var f in followers) f.Account.OrderUpdate+=followerHandler;
                    // Recheck after subscribing. External manual activity during arming is fail-closed.
                    if(accounts.Any(a=>!Clean(a,inst))) throw new InvalidOperationException("Activity detected while arming. Start again only when flat.");
                    foreach(var f in followers) CheckRisk(f);
                    if(followers.Any(f=>f.Latch.Locked)) throw new InvalidOperationException("Risk guard latched at start; review telemetry/baseline.");
                    lock(controlGate) { if(request!=controlGeneration)throw new InvalidOperationException("Start superseded by guard"); entries=true; } stateChanged(true,"COPYING — "+leader.Name+" / "+instrument.FullName); log("Armed explicitly: "+followers.Count+" simulation follower(s).");
                }
                catch(Exception ex) { Detach(); log("Start refused: "+ex.Message); }
            });
#endif
        }
        string Identity(Order order,string providerId,OrderState state)
        {
            lock(identityGate)
            {
                string id, prior; string provider=providerId??"";
                Dictionary<string,string> accountIds;
                if(!providerIdentities.TryGetValue(order.Account,out accountIds))providerIdentities[order.Account]=accountIds=new Dictionary<string,string>(StringComparer.Ordinal);
                if(identities.TryGetValue(order,out id))
                {
                    bool preSubmitReplacement=preSubmitProviderIds.Contains(order)&&state!=OrderState.Initialized&&!string.IsNullOrEmpty(provider);
                    if(wrapperProviderIds.TryGetValue(order,out prior)&&!string.IsNullOrEmpty(prior)&&provider!=prior&&!preSubmitReplacement)return null;
                    if(!string.IsNullOrEmpty(provider))
                    {
                        string mapped; if(accountIds.TryGetValue(provider,out mapped)&&mapped!=id)return null;
                        accountIds[provider]=id; wrapperProviderIds[order]=provider;
                        if(state!=OrderState.Initialized)preSubmitProviderIds.Remove(order);
                    }
                    return id;
                }
                if(!string.IsNullOrEmpty(provider)&&accountIds.TryGetValue(provider,out id)) { identities[order]=id; wrapperProviderIds[order]=provider;return id; }
                // Blank IDs can only start at the initial state; a later unknown wrapper is ambiguous.
                if(string.IsNullOrEmpty(provider)&&state!=OrderState.Submitted)return null;
                id=Guid.NewGuid().ToString("N"); identities[order]=id;
                if(!string.IsNullOrEmpty(provider)){accountIds[provider]=id;wrapperProviderIds[order]=provider;if(state==OrderState.Initialized)preSubmitProviderIds.Add(order);}
                return id;
            }
        }
        void LeaderUpdate(object sender,OrderEventArgs e,int generation,Account subscribedLeader,string subscribedInstrument)
        {
            if(generation!=Volatile.Read(ref sessionGeneration)||subscribedLeader!=leader)return;
            int execution=Volatile.Read(ref controlGeneration);
            Account origin=sender as Account; Instrument selectedInstrument=instrument;
            Order order=e.Order;
            if(origin!=leader || order==null || order.Account!=origin || selectedInstrument==null)return;
            if(disposed || !attached || order==null || order.Instrument==null || order.Instrument.FullName!=subscribedInstrument || (order.Name ?? "").StartsWith("KTC>"))return;
            var s=new OrderSnapshot {Source=order,Origin=origin,InstrumentName=selectedInstrument.FullName,Session=generation,Execution=execution,Id=Identity(order,e.OrderId,e.OrderState),Oco=order.Oco,Name=order.Name,State=e.OrderState,Action=order.OrderAction,Type=order.OrderType,Entry=order.OrderEntry,Tif=order.TimeInForce,Gtd=order.Gtd,Quantity=e.Quantity,Limit=e.LimitPrice,Stop=e.StopPrice};
            lock(controlGate)Interlocked.Increment(ref leaderCaptured);
            if(!worker.Post(()=>{try{Process(s);}finally{Interlocked.Increment(ref leaderApplied);foreach(var f in followers)MaintainProtection(f);}}))
                Interlocked.Increment(ref leaderApplied);
        }
        static bool Alive(OrderState s) { return s==OrderState.Submitted || s==OrderState.Accepted || s==OrderState.Working || s==OrderState.TriggerPending || s==OrderState.PartFilled; }
        // Entry or exit? Evaluated on every leader event but only USED when the order is first seen: it fixes CopyLink.Exit, and later events of the same order
        // take the update path, which never reads Exit again (so a Position change mid-order cannot re-classify it).
        // Bracket legs (any OCO) keep the legacy rule. A plain Sell/Buy is an exit only when it reduces exposure: the leader's own position decides; if the leader shows no
        // position (callback lag, synthetic fixtures) the followers' owned exposure decides. No exposure anywhere => the order OPENS a position and is copied as an entry.
        bool ClassifyLeaderOrder(OrderSnapshot s)
        {
            bool exit=s.Action==OrderAction.Sell||s.Action==OrderAction.BuyToCover;
            if(string.IsNullOrEmpty(s.Oco)&&(s.Action==OrderAction.Sell||s.Action==OrderAction.Buy))
            {
                int dir=s.Action==OrderAction.Sell?1:-1;              // exposure sign that this order would reduce
                int lp=Position(leader,instrument);
                if(lp!=0)exit=Math.Sign(lp)==dir;
                else exit=followers.Any(f=>Math.Sign(OwnedPosition(f))==dir);
            }
            return exit;
        }
        void Process(OrderSnapshot s)
        {
            if(!attached||s.Origin!=leader||s.Source.Account!=leader||s.Session!=Volatile.Read(ref sessionGeneration)||s.InstrumentName!=instrument.FullName)return;
            if(s.Id==null) { BlockEntries();foreach(var f in followers){f.AllocationUncertain=true;Lock(f,"Ambiguous provider order identity / transition — no catch-up, manual recovery required");}return; }
            s.ExitResolved=ClassifyLeaderOrder(s);
            if(!s.Exit&&s.Execution!=Volatile.Read(ref controlGeneration)) { tombstones.Add(s.Id); return; }
            // Session-frozen mode. Market closes still mirror when bracket copying is off.
            if(s.Exit&&s.Type!=OrderType.Market&&!mirrorAtm)return;
            if(!RiskPolicy.Finite(s.Limit)||!RiskPolicy.Finite(s.Stop)||s.Limit<0||s.Stop<0||s.Quantity<=0||s.Quantity>1000000 ||
                !(s.Action==OrderAction.Buy||s.Action==OrderAction.SellShort||s.Action==OrderAction.Sell||s.Action==OrderAction.BuyToCover) ||
                !(s.Type==OrderType.Market||s.Type==OrderType.Limit||s.Type==OrderType.StopMarket||s.Type==OrderType.StopLimit||s.Type==OrderType.MIT))
            { BlockEntries(); foreach(var f in followers)Lock(f,"Invalid / unsupported leader order — entries blocked"); return; }
            if(!Connected(leader)) { entries=false;pauseOnly=false; foreach(var f in followers) Lock(f,"Leader disconnected — no catch-up"); return; }
            List<CopyLink> copies;
            if(!links.TryGetValue(s.Id,out copies))
            {
                if(tombstones.Contains(s.Id) || !Alive(s.State)) return;
                if(suppressNew){tombstones.Add(s.Id);return;}
                if(links.Count+tombstones.Count>=10000) { entries=false;pauseOnly=false; log("Session history bound reached — guarded pause; stop flat and restart."); return; }
                copies=new List<CopyLink>(); links[s.Id]=copies; // register BEFORE any Create/Submit and before reentrant callbacks.
                if(!CopyLedger.AllowNew(entries,s.Exit)) { tombstones.Add(s.Id); return; }
                foreach(var f in followers)
                {
                    try
                    {
                        CheckRisk(f);
                        if(s.Exit&&s.Type!=OrderType.Market&&!f.Settings.CopyAtm)continue;
                        if(f.Flattening || !Connected(f.Account))continue;
                        if(!s.Exit && (!entries || !f.Following || f.Paused || f.Latch.Locked || f.Ignored.Contains(s.Id)))continue;
                        int desired=RiskPolicy.Quantity(s.Quantity,f.Settings.Ratio);
                        if(s.Quantity*f.Settings.Ratio>1000000) { Lock(f,"Oversize follower quantity"); continue; }
                        if(desired<=0&&(!s.Exit||string.IsNullOrEmpty(s.Oco)))continue;
                        var link=new CopyLink {Follower=f,Latest=s,Exit=s.Exit,Desired=desired,Oco=oco.Get(f.Account.Name,s.Oco),Waiting=true};
                        link.EntrySequence=++nextEntrySequence;copies.Add(link);RegisterGroup(link);
                        Submit(link);
                    }
                    catch(Exception ex) { Lock(f,"Order error: "+ex.Message); }
                }
                return;
            }
            foreach(CopyLink link in copies)
            {
                try
                {
                    if(link.Cancelled)continue;
                    if(s.Action!=link.Latest.Action||s.Type!=link.Latest.Type||s.Oco!=link.Latest.Oco||s.Tif!=link.Latest.Tif)
                    { link.OwnershipUncertain=true;if(link.Group!=null)link.Group.Frozen=true;Lock(link.Follower,"Unsupported leader action / type / OCO / time-in-force transition — submitted order unchanged; manual recovery required");continue; }
                    link.Latest=s;
                    if(s.State==OrderState.Cancelled || s.State==OrderState.Rejected)
                    {
                        link.Cancelled=true; link.Waiting=false;
                        if(link.Order!=null && !Order.IsTerminalState(link.Order.OrderState)) { var cf=link.Follower; var co=link.Order; Dispatch(cf,()=>cf.Account.Cancel(new[]{co}),err=>{ if(err!=null)Lock(cf,"Manage error: "+err.Message); },"Cancel "+co.OrderAction+" "+co.OrderType+" x"+co.Quantity); }
                        continue;
                    }
                    if(Alive(s.State))
                    {
                        link.Desired=RiskPolicy.Quantity(s.Quantity,link.Follower.Settings.Ratio);
                        if(link.Group!=null)RefreshGroupPlan(link.Group);
                        if(link.Waiting){if(!link.Follower.Flattening)Submit(link);} else Change(link);
                    }
                }
                catch(Exception ex) { Lock(link.Follower,"Manage error: "+ex.Message); }
            }
            // Filled links retained: follower fills may lag, and protective exits can arrive later.
        }
        decimal ScaledGroupQuantity(EngineAccount f,int quantity)
        { return quantity*decimal.Parse(f.Settings.Ratio.ToString("R",CultureInfo.InvariantCulture),NumberStyles.Float,CultureInfo.InvariantCulture); }
        void RegisterGroup(CopyLink link)
        {
            if(!link.Exit||string.IsNullOrEmpty(link.Oco))return;
            var f=link.Follower;ExitGroup group;
            if(!f.ExitGroups.TryGetValue(link.Oco,out group))
            {group=new ExitGroup(link.Oco,Volatile.Read(ref sessionGeneration));f.ExitGroups.Add(link.Oco,group);}
            link.Group=group;group.Members.Add(link);RefreshGroupPlan(group);
        }
        bool CertifiedWaitingProtection(CopyLink link)
        {
            if(link.Group==null||link.Cancelled||link.Group.Completed||!Alive(link.Latest.State))return false;
            var proof=GroupCertificate(link.Group);
            return proof.Supported&&proof.Held>0;
        }
        void RefreshGroupPlan(ExitGroup group)
        {
            var f=group.Members[0].Follower;
            // Cancelled intent keeps its allocation until a captured terminal acknowledgement.
            // Once every member is acknowledged, keep only the consumed prefix; another explicit
            // group can then claim the unused suffix without reusing already exited lots.
            decimal demand=group.Members.Where(x=>!x.Cancelled||x.Order!=null&&(!Order.IsTerminalState(x.ObservedState)||x.SubmissionInFlight||Volatile.Read(ref x.ChangesInFlight)>0||x.RequestedQuantity!=x.ReservedQuantity))
                .Select(x=>ScaledGroupQuantity(f,x.Latest.Quantity)).DefaultIfEmpty(group.Members.Sum(x=>(decimal)x.ObservedFilled)).Max();
            if(group.Completed){foreach(var x in group.Members.Where(x=>x.Order==null)){x.Cancelled=true;x.Waiting=false;}return;}
            if(demand!=group.Demand)
            {
                group.Demand=demand;group.Revision++;
                decimal total=0;long previous=0;
                foreach(var g in f.ExitGroups.Values.OrderBy(x=>x.Members[0].EntrySequence))
                {
                    total+=g.Demand;long whole=checked((long)decimal.Floor(total));
                    g.PlannedQuota=checked((int)(whole-previous));previous=whole;
                }
            }
            AllocateGroupLots(f);
            foreach(var member in group.Members)
            {
                member.Desired=Math.Min(group.Quota,checked((int)decimal.Ceiling(ScaledGroupQuantity(f,member.Latest.Quantity))));
                if(member.Order==null&&!member.Cancelled)member.Waiting=member.Desired>0;
            }
        }
        void AllocateGroupLots(EngineAccount f)
        {
            // Only an explicit, captured reduction can release still-unconsumed ordinals.
            // A mutable wrapper, a returned Change, or a pending callback cannot release ownership.
            if(Volatile.Read(ref f.CapturedCallbacks)!=Volatile.Read(ref f.AppliedCallbacks))return;
            foreach(var g in f.ExitGroups.Values.OrderBy(x=>x.Members[0].EntrySequence))
            {
                if(g.Completed||g.Frozen||g.FlatRetired||g.PlannedQuota>=g.Quota)continue;
                int filled=g.Members.Sum(x=>x.ObservedFilled);
                if(filled>g.PlannedQuota||g.Members.Any(x=>x.SubmissionInFlight||Volatile.Read(ref x.ChangesInFlight)>0||x.RequestedQuantity!=x.ReservedQuantity||CapturedRemaining(x)>g.PlannedQuota-filled))continue;
                int keep=g.PlannedQuota;
                var retained=new List<EntryLotClaim>();
                foreach(var claim in g.Claims)
                {
                    int count=Math.Min(keep,claim.Count);if(count>0)retained.Add(new EntryLotClaim(claim.Entry,claim.First,count));keep-=count;
                }
                g.Claims.Clear();g.Claims.AddRange(retained);g.Quota=g.PlannedQuota;g.Revision++;
            }
            foreach(var g in f.ExitGroups.Values.OrderBy(x=>x.Members[0].EntrySequence))
            {
                if(g.Completed||g.Frozen||g.FlatRetired)continue;
                int remaining=Math.Max(0,g.PlannedQuota-g.Quota);
                var side=g.Members[0].Latest.Action==OrderAction.Sell?OrderAction.Buy:OrderAction.SellShort;
                var entries=links.Values.SelectMany(v=>v).Where(x=>x.Follower==f&&!x.Exit&&!x.FlatRetired&&x.Order!=null&&(x.SubmittedAction==side||(side==OrderAction.SellShort&&x.SubmittedAction==OrderAction.Sell))).OrderBy(x=>x.EntrySequence);
                int legacy=links.Values.SelectMany(v=>v).Where(x=>x.Follower==f&&x.Exit&&x.Group==null).Sum(x=>x.ObservedFilled);
                foreach(var entry in entries)
                {
                    int consumed=Math.Min(legacy,entry.ObservedFilled);legacy-=consumed;
                    // A terminal entry (cancelled / rejected / filled, from captured callbacks) can never fill more: only its observed fills are lots.
                    // Counting its unfilled request let an OCO exit claim a lot of a CANCELLED entry and leave the real fill unprotected (real NT 2026-10-15).
                    int capacity=Order.IsTerminalState(entry.ObservedState)?entry.ObservedFilled:Math.Max(entry.ObservedFilled,entry.RequestedQuantity);
                    var used=f.ExitGroups.Values.SelectMany(x=>x.Claims).Where(x=>x.Entry==entry).OrderBy(x=>x.First).ToArray();
                    int cursor=consumed;
                    foreach(var claim in used.Concat(new[]{new EntryLotClaim(entry,capacity,0)}))
                    {
                        int count=Math.Min(remaining,Math.Max(0,claim.First-cursor));
                        if(count>0){g.Claims.Add(new EntryLotClaim(entry,cursor,count));g.Quota+=count;remaining-=count;g.Revision++;}
                        cursor=Math.Max(cursor,claim.First+claim.Count);
                        if(remaining==0)break;
                    }
                    if(remaining==0)break;
                }
                foreach(var member in g.Members)
                {
                    member.Desired=Math.Min(g.Quota,checked((int)decimal.Ceiling(ScaledGroupQuantity(f,member.Latest.Quantity))));
                    if(member.Order==null&&!member.Cancelled)member.Waiting=member.Desired>0;
                }
            }
        }
        GroupSupportCertificate GroupCertificate(ExitGroup group,bool requireSettledProof=true)
        {
            var f=group.Members[0].Follower;
            int entryFilled=group.Claims.Sum(x=>x.Captured),exitFilled=group.Members.Sum(x=>x.ObservedFilled);
            long frontier=Volatile.Read(ref f.CapturedCallbacks),leaderFrontier=Volatile.Read(ref leaderCaptured);
            bool settled=frontier==Volatile.Read(ref f.AppliedCallbacks)&&leaderFrontier==Volatile.Read(ref leaderApplied)
                &&group.Members.All(x=>!x.SubmissionInFlight&&Volatile.Read(ref x.ChangesInFlight)==0&&x.RequestedQuantity==x.ReservedQuantity);
            bool supported=group.Session==Volatile.Read(ref sessionGeneration)&&!group.FlatRetired&&!group.Frozen&&!f.ProviderFaulted&&!f.AllocationUncertain
                &&frontier==Volatile.Read(ref f.AppliedCallbacks)&&(!requireSettledProof||settled)&&group.Claims.Sum(x=>x.Count)==group.Quota
                &&exitFilled<=entryFilled&&group.Claims.All(x=>!x.Entry.OwnershipUncertain&&x.Entry.Latest.Session==group.Session&&x.Entry.Latest.Id!=null)
                &&group.Members.All(x=>!x.OwnershipUncertain&&x.Latest.Action==group.Members[0].Latest.Action)
                ;
            // Every currently held group lot must still be supported by the captured account-owned net.
            int held=f.ExitGroups.Values.Where(g=>!g.FlatRetired).Sum(g=>Math.Max(0,g.Claims.Sum(x=>x.Captured)-g.Members.Sum(x=>x.ObservedFilled)));
            if(held>Math.Abs((long)OwnedPosition(f)))supported=false;
            foreach(var entry in f.ExitGroups.Values.SelectMany(g=>g.Claims).GroupBy(x=>x.Entry))
            {
                int end=0;foreach(var claim in entry.OrderBy(x=>x.First)){if(claim.First<end||claim.First<0||claim.Count<=0)supported=false;end=claim.First+claim.Count;}
            }
            return new GroupSupportCertificate(group.Id,group.Session,group.Revision,group.Quota,entryFilled,exitFilled,frontier,leaderFrontier,supported,settled,group.FlatRetired);
        }
        void ReconcileGroups(EngineAccount f)
        {
            AllocateGroupLots(f);
            foreach(var group in f.ExitGroups.Values.ToArray())
            {
                if(group.Completed||group.FlatRetired||group.PendingRetirements!=0)continue;
                var certificate=GroupCertificate(group);
                if(group.Members.Sum(x=>x.ObservedFilled)>group.Claims.Sum(x=>x.Captured))
                {group.Frozen=true;Lock(f,"OCO group fill exceeds captured allocated entry lots — reversal / double fill evidence retained; manual recovery required");continue;}
                if(!certificate.Complete||f.Flattening||group.Members.Any(x=>x.SubmissionInFlight))continue;
                group.Completed=true;
                foreach(var member in group.Members)
                {
                    member.Cancelled=true;member.Waiting=false;
                    if(member.Order!=null&&!Order.IsTerminalState(member.ObservedState))
                    {
                        member.OcoRetired=true;group.PendingRetirements++;var order=member.Order;
                        Dispatch(f,()=>{
                            lock(controlGate)
                            {
                                if(f.ProviderFaulted||f.AllocationUncertain||group.Frozen||certificate.Session!=Volatile.Read(ref sessionGeneration)||certificate.Revision!=Volatile.Read(ref group.Revision)||certificate.Frontier!=Volatile.Read(ref f.CapturedCallbacks)||certificate.LeaderFrontier!=Volatile.Read(ref leaderCaptured))
                                    throw new GroupSupportChangedException("group support frontier changed before cancel authorization; cancellation NOT sent");
                            }
                            // This is a local authorization, never a promise of provider OCO atomicity.
                            f.Account.Cancel(new[]{order});
                        },err=>{
                            group.PendingRetirements--;
                            if(err is GroupSupportChangedException)
                            {
                                // No Cancel was sent. Retain the order until a NEW settled proof authorizes retirement.
                                group.Completed=false;group.Revision++;member.Cancelled=false;member.OcoRetired=false;
                                log(f.Account.Name+": "+err.Message);
                            }
                            else if(err!=null){group.Frozen=true;member.OcoRetired=false;Lock(f,"Certified OCO cancellation unresolved: "+err.Message);}
                        });
                    }
                }
            }
            if(f.ExitGroups.Values.Any(g=>g.Completed||g.Members.Any(x=>x.ObservedState==OrderState.Filled&&x.ObservedFilled>0))&&OwnedPosition(f)==0)
            {
                // Stop new entries; account-flat never proves another group's completion.
                // An explicit waiting exit may continue only with its own settled ownership certificate.
                f.ExitCompleted=true;f.Following=false;CancelEntries(f);
                foreach(var waiting in links.Values.SelectMany(v=>v).Where(x=>x.Follower==f&&x.Exit&&x.Order==null&&x.Group==null)){waiting.Cancelled=true;waiting.Waiting=false;}
            }
        }
        // Lots this copier owns that no non-terminal, non-cancelled exit order of this copier is covering. Never optimistic:
        // an exit still shown Working counts only up to its remaining quantity, so the result can overstate but never understate the gap.
        ProtectionPlan ProtectionPlanFor(EngineAccount f)
        {
            var groups=f.ExitGroups.Values.OrderBy(x=>x.Members[0].EntrySequence).ToArray();
            var certificates=groups.Select(g=>GroupCertificate(g)).ToArray();
            int owned=Math.Abs(OwnedPosition(f)),covered=0;
            for(int i=0;i<groups.Length;i++)
            {
                int actual=groups[i].Members.Where(x=>x.Order!=null&&x.AcceptanceConfirmed&&!x.Cancelled&&!Order.IsTerminalState(x.ObservedState)).Select(CapturedRemaining).DefaultIfEmpty(0).Max();
                covered+=Math.Min(certificates[i].Held,actual);
            }
            int independent=links.Values.SelectMany(v=>v).Where(x=>x.Follower==f&&x.Exit&&x.Group==null&&x.Order!=null&&x.AcceptanceConfirmed&&!x.Cancelled&&!Order.IsTerminalState(x.ObservedState)).Sum(CapturedRemaining);
            int assigned=certificates.Sum(x=>x.Held);
            covered+=Math.Min(Math.Max(0,owned-assigned),independent);
            return new ProtectionPlan(Volatile.Read(ref sessionGeneration),Volatile.Read(ref leaderCaptured),Volatile.Read(ref f.CapturedCallbacks),owned,Math.Min(owned,covered),certificates);
        }
        int UnprotectedLots(EngineAccount f) {return ProtectionPlanFor(f).Unprotected;}
        string UnprotectedText(EngineAccount f)
        {
            int n=UnprotectedLots(f);
            return n>0?string.Format(CultureInfo.InvariantCulture,n==1?"{0} lot UNPROTECTED (no protective exit working) — check NinjaTrader now and place or flatten by hand; this copier will not create it":"{0} lots UNPROTECTED (no protective exit working) — check NinjaTrader now and place or flatten by hand; this copier will not create it",n):"";
        }
        int OwnedPosition(EngineAccount f)
        {
            long net=0;
            foreach(var x in links.Values.SelectMany(v=>v).Where(x=>x.Follower==f&&x.Order!=null))
            {
                net+=(x.SubmittedAction==OrderAction.Buy||x.SubmittedAction==OrderAction.BuyToCover?1L:-1L)*x.ObservedFilled;
            }
            net+=f.FlatOffset;
            if(net>int.MaxValue||net<int.MinValue) { Lock(f,"Exposure ledger overflow"); return int.MaxValue; }
            return (int)net;
        }
        CopyLink FindFollowerLink(EngineAccount f,Order wrapper,string provider)
        {
            var all=links.Values.SelectMany(v=>v).Where(x=>x.Follower==f&&x.Order!=null).ToArray();
            var direct=all.FirstOrDefault(x=>x.Order==wrapper||x.Wrappers.Contains(wrapper));
            if(direct!=null)return direct;
            if(string.IsNullOrEmpty(provider))return null;
            var matches=all.Where(x=>x.ProviderId==provider).ToArray();
            return matches.Length==1?matches[0]:null;
        }
        int EntryPosition(EngineAccount f)
        {
            int p=Position(f.Account,instrument), owned=OwnedPosition(f);
            if(p!=0&&(owned==0||Math.Sign(p)!=Math.Sign(owned)||Math.Abs((long)p)>Math.Abs((long)owned)))
                Lock(f,"Position / owned execution mismatch — no adoption");
            return Math.Abs((long)p)>Math.Abs((long)owned)?p:owned;
        }
        int ExitPosition(EngineAccount f)
        {
            int actual=Position(f.Account,instrument), owned=OwnedPosition(f);
            if(actual==0)return 0; // Position may lag: never manufacture closable exposure.
            if(owned==0||Math.Sign(actual)!=Math.Sign(owned)) { Lock(f,"Unknown / opposite exposure — exit adoption blocked"); return 0; }
            if(Math.Abs((long)actual)>Math.Abs((long)owned))Lock(f,"Unknown excess position — protect owned subset only");
            return Math.Sign(actual)*(int)Math.Min(Math.Abs((long)actual),Math.Abs((long)owned));
        }
        Order[] ExposureOrders(EngineAccount f)
        {
            Order[] actual; lock(f.Account.Orders)actual=f.Account.Orders.ToArray();
            return actual.Select(o=>{var mapped=FindFollowerLink(f,o,o.OrderId);return mapped==null?o:mapped.Order;}).Concat(links.Values.SelectMany(v=>v).Where(x=>x.Follower==f&&x.Order!=null).Select(x=>x.Order)).Distinct().ToArray();
        }
        int PendingEntries(EngineAccount f,Order excluding=null)
        {
            // Owned capacity is released by captured acknowledgements, never by future wrapper mutation.
            return links.Values.SelectMany(v=>v).Where(x=>x.Follower==f&&!x.Exit&&x.Order!=null&&x.Order!=excluding).Sum(CapturedRemaining);
        }
        static int CapturedRemaining(CopyLink link)
        { return Order.IsTerminalState(link.ObservedState)?0:Math.Max(0,link.ReservedQuantity-link.ObservedFilled); }
        int ExitReserved(CopyLink current)
        {
            var f=current.Follower;
            // A leader MARKET exit closes everything it can; the copier's OWN working non-market exits (TP / SL) for this account are made obsolete by its fill. Letting them
            // reserve capacity made a market close wait 10 s and lock a protected follower (seen on real NinjaTrader after a per-account Flat of the other follower).
            // Only the copier's own non-market links are ignored here: unknown / manual orders below still reserve, and the market quantity stays bounded by the closable
            // position in AllowedRemaining. Those protective orders are then cancelled by the existing Change path (qty<=filled once the market exit is accepted) and, for OCO
            // members, by the exit-fill path; MarketExitFillCancelsOwnOrphanedStopWithoutOco pins that they are cancelled exactly once.
            bool marketExit=current.Exit&&current.Latest.Type==OrderType.Market;
            var reservations=links.Values.SelectMany(v=>v).Where(x=>x.Follower==f&&x!=current&&x.Exit&&x.Order!=null&&x.SubmittedAction==current.Latest.Action&&!(marketExit&&x.Latest!=null&&x.Latest.Type!=OrderType.Market))
                // Issue #3: a sibling the engine retired because its OCO group completed no longer reserves capacity for ANOTHER group.
                // Its stale captured Working state would otherwise make a still-open group look like it has no closable exposure and
                // get its sole protection cancelled. Leader-cancelled links (Cancelled but not OcoRetired) still reserve until acknowledged.
                // A NEW exit cannot reuse capacity of a leader-cancelled same-OCO member before its terminal ACK.
                // Existing siblings retain their shared reservation: do not resize/cancel sole protection merely because its peer is cancelling.
                .Where(x=>!x.OcoRetired)
                .Select(x=>Tuple.Create(current.Order==null&&x.Cancelled&&x.Oco==current.Oco?"":x.Oco,CapturedRemaining(x))).ToList();
            // Unknown manual work consumes capacity but is never adopted. Owned wrappers use captured evidence only.
            Order[] actual; lock(f.Account.Orders)actual=f.Account.Orders.ToArray();
            reservations.AddRange(actual.Where(o=>o.Instrument!=null&&o.Instrument.FullName==instrument.FullName&&o.OrderAction==current.Latest.Action&&!Order.IsTerminalState(o.OrderState)&&!(f.PreSessionGhost.Contains(o)&&o.OrderState==OrderState.Initialized&&o.Filled==0)&&FindFollowerLink(f,o,o.OrderId)==null)
                .Select(o=>Tuple.Create(o.Oco,Math.Max(0,o.Quantity-o.Filled))));
            return reservations.Where(x=>string.IsNullOrEmpty(x.Item1)).Sum(x=>x.Item2)+
                reservations.Where(x=>!string.IsNullOrEmpty(x.Item1)&&x.Item1!=current.Oco).GroupBy(x=>x.Item1).Sum(g=>g.Max(x=>x.Item2));
        }
        int AllowedRemaining(CopyLink link)
        {
            int p=link.Exit?ExitPosition(link.Follower):EntryPosition(link.Follower);
            if(link.Exit)
            {
                int closable=link.Latest.Action==OrderAction.Sell ? Math.Max(0,p) : Math.Max(0,-p);
                int ownBudget=closable;
                if(link.Group!=null){var support=GroupCertificate(link.Group);if(!support.Supported||link.Group.Completed)return 0;ownBudget=Math.Min(closable,support.Held);}
                return CopyLedger.ExitBudget(RiskPolicy.ExitRemaining(link.Desired,link.ObservedFilled,ownBudget),closable,ExitReserved(link));
            }
            bool opposite;
            opposite=links.Values.SelectMany(v=>v).Any(x=>x.Follower==link.Follower&&x!=link&&!x.Exit&&x.Order!=null&&CapturedRemaining(x)>0&&x.SubmittedAction!=link.Latest.Action);
            if(opposite||(link.Latest.Action==OrderAction.Buy && p<0)||((link.Latest.Action==OrderAction.SellShort||link.Latest.Action==OrderAction.Sell) && p>0)) { Lock(link.Follower,"Ambiguous reversal blocked"); return 0; }
            int remaining=link.Desired-link.ObservedFilled;
            return RiskPolicy.CanEnter(link.Follower.Settings,link.Follower.Latch,p,PendingEntries(link.Follower,link.Order),remaining) ? remaining : 0;
        }
        SerialWorker NewDispatcher()
        {
            var d=new SerialWorker(ex=>log("PROVIDER DISPATCH ERROR: "+ex.Message));
            lock(dispatchers)dispatchers.Add(d);
            return d;
        }
        void DisposeDispatchers()
        {
            SerialWorker[] all; lock(dispatchers){all=dispatchers.ToArray();dispatchers.Clear();}
            foreach(var d in all)d.Dispose();
        }
        // Runs ONE provider call on the account's own thread. `done` runs on the ENGINE worker with null on success or the exception.
        // The call counts as in flight until `done` has run, so CanClose / Dispose refuse while any provider call is unresolved.
        // A refused post (engine stopping) never leaks the counter and is never reported as a successful send.
        void Dispatch(EngineAccount f,Action call,Action<Exception> done,string what=null)
        {
#if KINFX_SIM_TEST
            var leaderAtQueue=leader;
#endif
            Interlocked.Increment(ref providerCallsInFlight);
            Action<Exception> finish=err=>
            {
                Action fin=()=>{ try { done(err); } catch(Exception ex) { log("PROVIDER COMPLETION ERROR: "+ex.Message); } finally { Interlocked.Decrement(ref providerCallsInFlight);ReconcileGroups(f); } };
                if(!worker.Post(fin)) { Interlocked.Decrement(ref providerCallsInFlight); if(err!=null)log(f.Account.Name+": provider call failed while engine stopping: "+err.Message); }
            };
            if(!f.Dispatcher.Post(()=>{ Exception err=null; try {
#if KINFX_SIM_TEST
                // SIM TEST BUILD: provider is re-read when the call is actually sent, not only at Start. Fail closed: the call is NOT sent and the account is locked by the normal error path.
                if(f.Account==null||!Sim(f.Account)||leaderAtQueue==null||!Sim(leaderAtQueue))throw new InvalidOperationException("SIM TEST: account is not Simulator / Playback at send time; call was NOT sent");
                if(dryRun){ log("DRY RUN would send to "+f.Account.Name+": "+(what??"provider call")); finish(null); return; }
#endif
                call(); } catch(Exception ex) { err=ex;if(!(ex is GroupSupportChangedException))f.ProviderFaulted=true; } finish(err); }))
                finish(new InvalidOperationException("provider dispatcher stopped; call was NOT sent"));
        }
        void Submit(CopyLink link,string errPrefix="Order error: ")
        {
            var f=link.Follower;
            if(!Connected(f.Account)) { link.Waiting=false; Lock(f,"Disconnected — no catch-up"); return; }
            if(!link.Exit && (!entries||!f.Following||f.Paused||f.Latch.Locked||link.Cancelled)) { link.Waiting=false; return; }
            if(link.Group!=null&&link.Desired==0){link.Waiting=false;return;}
            if(link.Group!=null&&link.Group.Members.Any(x=>x!=link&&(x.SubmissionInFlight||x.Order!=null&&!x.AcceptanceConfirmed&&!Order.IsTerminalState(x.ObservedState))))return;
            var authorization=link.Group!=null?GroupCertificate(link.Group):null;
            if(authorization!=null&&!authorization.Supported)return;
            int qty=AllowedRemaining(link);
            if(qty<=0)
            {
                // Independent exits cannot share capacity. Refuse excess intent without timing out an already protected account.
                if(link.Exit&&link.Group==null&&link.Latest.Type!=OrderType.Market&&!HasPendingEntryWork(f)&&!f.AllocationUncertain&&OwnedPosition(f)!=0&&ExitPosition(f)==OwnedPosition(f)&&
                    ProtectionPlanFor(f).Unprotected==0&&links.Values.SelectMany(v=>v).Where(x=>x.Follower==f&&x.Exit&&x!=link&&x.Order!=null&&x.SubmittedAction==link.Latest.Action&&CapturedRemaining(x)>0)
                        .All(x=>x.AcceptanceConfirmed&&!x.Cancelled&&x.RequestedQuantity==x.ReservedQuantity&&Volatile.Read(ref x.ChangesInFlight)==0))
                {
                    link.Cancelled=true;link.Waiting=false;
                    log(f.Account.Name+": leader exit refused — no independent exit capacity; existing protection retained. Use an explicit shared OCO for alternatives.");
                    return;
                }
                // Only retain delayed protection if THIS copier has an entry link; never adopt an unrelated later position.
                if(link.Exit && (!f.ExitCompleted||link.Group!=null&&!link.Group.Completed) && (HasPendingEntryWork(f)||OwnedPosition(f)!=0) && links.Values.SelectMany(x=>x).Any(x=>x.Follower==f&&!x.Exit&&x.Order!=null&&(!x.Cancelled||x.ObservedFilled>0)))
                { if(DateTime.UtcNow-link.WaitingSince>TimeSpan.FromSeconds(10)){link.Waiting=false;Lock(f,"Exit could not obtain closable exposure within 10s — review protection");}else link.Waiting=true;return; }
                link.Waiting=false; if(link.Exit){f.ExitDroppedNothingToClose=true;f.ExitDroppedAtSequence=link.EntrySequence;log(f.Account.Name+": leader exit ignored, nothing to close");if(f.DryRunRetired>0)log(f.Account.Name+": dry run: no position was ever opened");} else log(f.Account.Name+": entry skipped, projected cap / floor quantity"); return;
            }
            var s=link.Latest;
            if(!(s.Type==OrderType.Market||s.Type==OrderType.Limit||s.Type==OrderType.StopMarket||s.Type==OrderType.StopLimit||s.Type==OrderType.MIT)) { Lock(f,"Unsupported order type"); link.Waiting=false; return; }
            Order order=f.Account.CreateOrder(instrument,s.Action,s.Type,s.Entry,s.Tif,qty,s.Limit,s.Stop,link.Oco,"KTC>"+Guid.NewGuid().ToString("N"),s.Tif==TimeInForce.Gtd?s.Gtd:Core.Globals.MaxDate,null);
            // Linearization: OFF and this authorization share controlGate. Broker calls NEVER hold it.
            // Already authorized work can arrive after OFF; cancellation / terminal acknowledgement is best effort.
            lock(controlGate)
            {
                if(!link.Exit&&(!entries||!f.Following||f.Paused||f.Latch.Locked||link.Cancelled)) { link.Cancelled=true; link.Waiting=false; return; }
                link.Order=order;link.Wrappers.Add(order); link.SubmittedAction=s.Action; link.RequestedQuantity=qty; link.ReservedQuantity=qty; link.ObservedState=OrderState.Submitted; link.Waiting=false; link.SubmissionInFlight=true; ledgerUnresolved=true;
            }
            Dispatch(f,()=>{
                if(!link.Exit&&f.ProviderFaulted)throw new InvalidOperationException("entry not sent: an earlier provider call for this account failed");
                if(authorization!=null)lock(controlGate)
                {
                    if(link.Cancelled||f.ProviderFaulted||f.AllocationUncertain||link.Group.Frozen||authorization.Session!=Volatile.Read(ref sessionGeneration)||authorization.Revision!=Volatile.Read(ref link.Group.Revision)||authorization.Frontier!=Volatile.Read(ref f.CapturedCallbacks)||authorization.LeaderFrontier!=Volatile.Read(ref leaderCaptured))
                        throw new GroupSupportChangedException("waiting protection frontier changed before submit authorization; submission NOT sent");
                }
                f.Account.Submit(new[]{order});
            },err=>
            {
                try {
                    if(err is GroupSupportChangedException)
                    {
                        // No Submit was sent. Discard only this unsent wrapper, then re-evaluate captured evidence.
                        // The wrapper stays in Account.Orders as Initialized: record it as a never-sent copier ghost (inert unless it ever fills).
                        f.PreSessionGhost.Add(order);
                        link.Wrappers.Remove(order);link.Order=null;link.RequestedQuantity=link.ReservedQuantity=0;link.Waiting=!link.Cancelled;
                        log(f.Account.Name+": "+err.Message);
                    }
                    else if(err==null){
#if KINFX_SIM_TEST
                        // DRY RUN: nothing reached the provider, so this wrapper can never be acknowledged or fill. Retire it as a never-sent ghost at once;
                        // otherwise the ledger waits on it forever: the leader exit then locks every follower after 10 s and Stop is refused (real NT 2026-10-14).
                        if(dryRun){link.Cancelled=true;link.Waiting=false;link.ObservedState=OrderState.Cancelled;f.PreSessionGhost.Add(order);f.DryRunRetired++;}else
#endif
                        log("COPY "+f.Account.Name+" "+s.Action+" "+qty+" "+s.Type);
                    }else Lock(f,errPrefix+err.Message);
                }
                finally { lock(controlGate)link.SubmissionInFlight=false; RecomputeDisablePending(f);if(authorization!=null)MaintainProtection(f);
#if KINFX_SIM_TEST
                    if(dryRun)ReconcileCloseEvidence(); // no provider callback will ever arrive for a dry-run wrapper
#endif
                }
            },"Submit "+s.Action+" "+qty+" "+s.Type+(s.Exit?" (exit)":" (entry)")+" on "+instrument.FullName);
        }
        void Change(CopyLink link,string errPrefix="Manage error: ")
        {
            var f=link.Follower; var order=link.Order; var s=link.Latest;
            if(order==null||Order.IsTerminalState(link.ObservedState)||Order.IsTerminalState(order.OrderState)||f.Flattening||!link.AcceptanceConfirmed)return;
            if(!Connected(f.Account)) { Lock(f,"Disconnected — protection may be stale");return; }
            CheckRisk(f);
            if(Order.IsTerminalState(order.OrderState)||link.Cancelled)return;
            if(!link.Exit&&(!f.Following||f.Latch.Locked))return;
            // Risk latch/pause must never cancel protection or prohibit price changes / reductions.
            int p=link.Exit?ExitPosition(f):EntryPosition(f);
            if(link.Exit&&p==0&&OwnedPosition(f)!=0) { Lock(f,"Ambiguous flat Position conflicts with owned executions — existing exit left unchanged; manual reconciliation required, coverage unverified");return; }
            // F01: Position can lag behind captured owned fills (or a legitimate external reduction may not be captured yet).
            // While Position is behind owned evidence, never SHRINK any acknowledged exit quantity; price moves still go through.
            // This is a quantity freeze, not a lock: no latch, no cancellation, no shrink. A real reduction is applied once
            // Position and owned evidence agree again; until then the exit may be larger than the position (reversal risk the
            // user must reconcile by hand, see docs/DO-NOT-DO.md).
            bool positionBehindOwned=false;
            if(link.Exit)
            {
                int ownedClosable=s.Action==OrderAction.Sell?Math.Max(0,OwnedPosition(f)):Math.Max(0,-OwnedPosition(f));
                int positionClosable=s.Action==OrderAction.Sell?Math.Max(0,p):Math.Max(0,-p);
                positionBehindOwned=p!=0&&positionClosable<ownedClosable;
            }
            int closable=link.Exit?(s.Action==OrderAction.Sell?Math.Max(0,p):Math.Max(0,-p)):Math.Abs(p);
            int filled=link.ObservedFilled;
            int accountClosable=closable;
            if(link.Group!=null)
            {
                // Existing amendments retain their ordered command stream and conservative reservation.
                // Retirement/new submissions still require the fully settled certificate.
                var support=GroupCertificate(link.Group,false);
                if(!support.Supported)return; // No speculative resize/cancel of sole protection.
                closable=Math.Min(closable,support.Held);
            }
            int qty=RiskPolicy.ChangeTotal(link.ReservedQuantity,filled,link.Desired,link.Exit,closable,link.Exit?Math.Max(0,ExitReserved(link)-(accountClosable-closable)):PendingEntries(f,order),f.Settings.MaxContracts,entries&&f.Following&&!f.Paused&&!f.Latch.Locked);
            // Effective quantity the provider holds or is about to hold: a Change still in flight counts, not the stale applied value.
            bool changePending=Volatile.Read(ref link.ChangesInFlight)>0||link.AwaitingChangeAck;
            int effectiveQty=changePending?link.SentQty:order.Quantity;
            if(positionBehindOwned&&link.Exit&&qty<effectiveQty)qty=effectiveQty; // freeze quantity, keep price updates
            if(qty<=filled)
            {
                if(link.Group!=null){var proof=GroupCertificate(link.Group);if(!proof.Supported||proof.Held>0||proof.Future>0)return;}
                link.Cancelled=true; link.Waiting=false; Dispatch(f,()=>f.Account.Cancel(new[]{order}),err=>{ if(err!=null)Lock(f,errPrefix+err.Message); },"Cancel working order");
                if(link.Exit&&p==0&&OwnedPosition(f)!=0)Lock(f,"Flat Position conflicts with owned executions — exits cancelled to prevent reversal; review protection");
                return;
            }
            int curQty=effectiveQty; double curLimit=changePending?link.SentLimit:order.LimitPrice, curStop=changePending?link.SentStop:order.StopPrice;
            if(curQty==qty && Math.Abs(curLimit-s.Limit)<1e-9 && Math.Abs(curStop-s.Stop)<1e-9)return;
            link.RequestedQuantity=qty; link.ReservedQuantity=Math.Max(link.ReservedQuantity,qty);
            // The requested values are captured NOW, in a command applied on the account's own thread. Writing them to the shared
            // Order object here and sending later would let a newer Change overwrite an older, still-queued one.
            double wantLimit=s.Limit,wantStop=s.Stop; int wantQty=qty;
            link.SentQty=wantQty; link.SentLimit=wantLimit; link.SentStop=wantStop; link.AwaitingChangeAck=true; Interlocked.Increment(ref link.ChangesInFlight);link.PendingChangeQuantities.Add(wantQty);
            Dispatch(f,()=>{ try { order.QuantityChanged=wantQty; order.LimitPriceChanged=wantLimit; order.StopPriceChanged=wantStop; f.Account.Change(new[]{order}); } finally { Interlocked.Decrement(ref link.ChangesInFlight); } },err=>{ link.PendingChangeQuantities.Remove(wantQty);if(err!=null)Lock(f,errPrefix+err.Message); },"Change protective order to qty "+wantQty);
        }
        void FollowerUpdate(object sender,OrderEventArgs e,int generation,string subscribedInstrument)
        {
            if(generation!=Volatile.Read(ref sessionGeneration))return;
            Account account=sender as Account;
            if(e.Order==null||account==null||e.Order.Account!=account||e.Order.Instrument==null||e.Order.Instrument.FullName!=subscribedInstrument)return;
            // Real NinjaTrader raises an Initialized OrderUpdate from CreateOrder, BEFORE Submit, carrying a client id. For a copier wrapper ("KTC>") that is not
            // provider evidence: nothing was sent, nothing can fill. Counting it moved the protection frontier and refused every TP / SL (real NT, 11 followers).
            // Only this exact shape is skipped; a fill, any later state, or a foreign Initialized order is still captured.
            if(e.OrderState==OrderState.Initialized&&e.Filled==0&&e.Order.Name!=null&&e.Order.Name.StartsWith("KTC>",StringComparison.Ordinal))return;
            var capturedAccount=followerView.FirstOrDefault(x=>x.Account==account);
            if(capturedAccount!=null){lock(controlGate)Interlocked.Increment(ref capturedAccount.CapturedCallbacks);}
            var snapshot=new FollowerSnapshot(account,e,generation);
            Order order=snapshot.Wrapper; OrderState eventState=snapshot.State;
            lock(admissionGate)
            {
                if(!attached||snapshot.Session!=Volatile.Read(ref sessionGeneration))return;
                if(snapshot.Filled>0)ledgerUnresolved=true; // Never close across queued execution evidence.
                Interlocked.Increment(ref pendingFollowerCallbacks);
            }
            bool accepted=worker.Post(()=>
            {
                try
                {
                if(!attached||snapshot.Session!=Volatile.Read(ref sessionGeneration)||snapshot.InstrumentName!=instrument.FullName)return;
                var f=followers.FirstOrDefault(x=>x.Account==snapshot.Account); if(f==null)return;
                var link=FindFollowerLink(f,order,snapshot.ProviderId);
                f.LastOrderEventUtc=DateTime.UtcNow;
                if(link==null&&!f.Flattening&&!f.KnownHistory(order,eventState,snapshot.ProviderId,snapshot.Filled)){f.AllocationUncertain=true;Lock(f,"Unknown follower order activity — no adoption / manual review");}
                if(link!=null)
                {
                    // Real providers replace the pre-submit client id (state Initialized) by the exchange id exactly once. Any other change stays ambiguous.
                    bool preSubmitReplacement=link.ProviderIdPreSubmit&&!string.IsNullOrEmpty(snapshot.ProviderId)&&eventState!=OrderState.Initialized;
                    if(!string.IsNullOrEmpty(link.ProviderId)&&snapshot.ProviderId!=link.ProviderId&&!preSubmitReplacement) { link.OwnershipUncertain=true;Lock(f,"Follower provider identity transition ambiguous — captured fill evidence retained, manual recovery required"); }
                    else if(!string.IsNullOrEmpty(snapshot.ProviderId)){link.ProviderId=snapshot.ProviderId;link.ProviderIdPreSubmit=eventState==OrderState.Initialized;}
                    link.Wrappers.Add(order);link.Order=order;
                    if(eventState==OrderState.Accepted||eventState==OrderState.Working||eventState==OrderState.TriggerPending||eventState==OrderState.PartFilled||snapshot.Filled>0)link.AcceptanceConfirmed=true;
                    int prior=link.ObservedFilled;
                    if(snapshot.Filled!=prior||Order.IsTerminalState(snapshot.State))link.ExecutionEvidence.Add(snapshot);
                    if(snapshot.Filled<prior||snapshot.Action!=link.SubmittedAction||snapshot.Type!=link.Latest.Type)Lock(f,"Follower execution metadata divergence — manual recovery required");
                    bool terminalRegression=(link.ObservedState==OrderState.Filled&&eventState!=OrderState.Filled)||(Order.IsTerminalState(link.ObservedState)&&!Order.IsTerminalState(eventState));
                    if(terminalRegression)Lock(f,"Follower terminal-state evidence divergence — older active state cannot revoke terminal acknowledgement; manual recovery required");
                    if(snapshot.Filled!=prior)f.LastFillUtc=DateTime.UtcNow;
                    link.ObservedFilled=Math.Max(prior,snapshot.Filled);
                    if(link.FlatRetired&&snapshot.Filled>prior){f.AllocationUncertain=true;link.OwnershipUncertain=true;Lock(f,"Late execution after provider-confirmed Flat — captured evidence retained; manual reconciliation required");}
                    if(!terminalRegression)
                    {
                        link.ObservedState=eventState;
                        // Cleared only by an answer that carries what was sent (or a terminal state). An answer with other values is either an older
                        // Change still being answered or a Change the provider refused: never re-send in a loop; the next leader move is compared to what was sent.
                        if(Order.IsTerminalState(eventState)||(eventState==OrderState.Accepted||eventState==OrderState.Working||eventState==OrderState.TriggerPending||eventState==OrderState.PartFilled)
                            &&snapshot.Quantity==link.SentQty&&Math.Abs(snapshot.Limit-link.SentLimit)<1e-9&&Math.Abs(snapshot.Stop-link.SentStop)<1e-9)link.AwaitingChangeAck=false;
                        // An old ACK can equal the newest quantity after a 2 -> 4 -> 2 sequence.
                        // Never release a larger reservation when the captured wrapper/dispatch intent
                        // still contradicts that reduction. These hints can refuse release, not certify acceptance.
                        bool reductionCoherent=snapshot.Quantity>=snapshot.WrapperQuantity&&snapshot.Quantity>=snapshot.WrapperQuantityChanged;
                        // Mutable wrapper quantities cannot reveal a larger Change still queued on the dispatcher.
                        // Engine-owned pending command intent can veto release, never certify acceptance.
                        bool pendingLarger=link.PendingChangeQuantities.Any(q=>q>snapshot.Quantity);
                        link.ReservedQuantity=snapshot.Quantity==link.RequestedQuantity&&reductionCoherent&&!pendingLarger?snapshot.Quantity:Math.Max(link.ReservedQuantity,snapshot.Quantity);
                    }
                    if(snapshot.Action!=link.SubmittedAction||snapshot.Type!=link.Latest.Type||(eventState==OrderState.Filled&&snapshot.Filled!=link.RequestedQuantity))
                    {link.OwnershipUncertain=true;if(link.Group!=null)link.Group.Frozen=true;}
                    if(eventState==OrderState.Filled&&snapshot.Filled!=link.RequestedQuantity)
                        Lock(f,"Terminal fill quantity divergence — expected="+link.RequestedQuantity+", observed="+snapshot.Filled+"; no makeup / chase, manual reconciliation required");
                    if(link.Exit&&snapshot.Filled>prior&&!string.IsNullOrEmpty(link.Oco)&&links.Values.SelectMany(v=>v).Any(x=>x!=link&&x.Follower==f&&x.Exit&&x.Oco==link.Oco&&x.ObservedFilled>0))
                        Lock(f,"OCO multiple exits filled — durable divergence lock; provider atomicity not guaranteed, manual recovery required");
                    if(link.Exit&&link.Group==null&&eventState==OrderState.Filled)
                    {
                        f.ExitCompleted=true; f.Following=false; link.Cancelled=true; link.Waiting=false;
                        CancelEntries(f);
                        foreach(var waiting in links.Values.SelectMany(v=>v).Where(x=>x.Follower==f&&x.Exit&&x.Waiting&&x.Order==null&&x.Group==null)) { waiting.Cancelled=true; waiting.Waiting=false; }
                        foreach(var sibling in links.Values.SelectMany(v=>v).Where(x=>x.Follower==f&&x.Exit&&x!=link&&!string.IsNullOrEmpty(link.Oco)&&x.Oco==link.Oco).ToArray())
                        {
                            sibling.Cancelled=true; sibling.Waiting=false; sibling.OcoRetired=true;
                            if(sibling.Order!=null&&!Order.IsTerminalState(sibling.Order.OrderState)) { var so=sibling.Order; Dispatch(f,()=>f.Account.Cancel(new[]{so}),err=>{ if(err!=null)Lock(f,"OCO cancellation unresolved: "+err.Message); },"Cancel OCO sibling"); }
                        }
                    }
                    if(link.Exit&&link.Group==null&&(eventState==OrderState.Cancelled||eventState==OrderState.Rejected))
                    {
                        f.ExitCompleted=true; f.Following=false; link.Cancelled=true; link.Waiting=false;
                        CancelEntries(f);
                        foreach(var waiting in links.Values.SelectMany(v=>v).Where(x=>x.Follower==f&&x.Exit&&x.Waiting&&x.Order==null&&x.Group==null)) { waiting.Cancelled=true;waiting.Waiting=false; }
                        bool protectedElsewhere=links.Values.SelectMany(v=>v).Any(x=>x.Follower==f&&x.Exit&&x!=link&&x.Order!=null&&!Order.IsTerminalState(x.Order.OrderState)&&!x.Cancelled);
                        if(OwnedPosition(f)!=0&&!protectedElsewhere&&!f.Flattening)Lock(f,"Protective exit terminal with uncovered exposure — manual review required"+(UnprotectedLots(f)>0?" — "+UnprotectedText(f):""));
                    }
                    if(!link.Exit&&snapshot.Filled>prior&&f.ExitDroppedNothingToClose&&link.EntrySequence<f.ExitDroppedAtSequence&&!f.Flattening&&!f.Latch.Locked)Lock(f,"Entry fill arrived AFTER the leader exit was ignored as 'nothing to close' — no copied exit covers it; manual recovery required; coverage not established"+(UnprotectedLots(f)>0?" — "+UnprotectedText(f):""));
                    if(!link.Exit&&snapshot.Filled>prior&&f.ExitCompleted){f.LateEntryCoverageWarning=true;Lock(f,LateEntryReason+(UnprotectedLots(f)>0?" — "+UnprotectedText(f):""));}
                }
                if(link!=null&&link.Group!=null&&(eventState==OrderState.Cancelled||eventState==OrderState.Rejected))
                {
                    link.Cancelled=true;link.Waiting=false;
                    foreach(var waiting in link.Group.Members.Where(x=>x.Order==null)){waiting.Cancelled=true;waiting.Waiting=false;}
                    RefreshGroupPlan(link.Group);
                    if(!f.Flattening&&link.Group.Claims.Sum(x=>x.Captured)>link.Group.Members.Sum(x=>x.ObservedFilled)&&!link.Group.Members.Any(x=>x.Order!=null&&!Order.IsTerminalState(x.ObservedState)&&!x.Cancelled))
                        Lock(f,"Protective group terminal with uncovered allocated exposure — manual recovery required"+(UnprotectedLots(f)>0?" — "+UnprotectedText(f):""));
                }
                RecomputeDisablePending(f);
                if(eventState==OrderState.Rejected&&!(link==null&&f.KnownHistory(order,eventState,snapshot.ProviderId,snapshot.Filled))) Lock(f,"Follower order rejected — review protection"); // rebuilt rejected history is not a new rejection
                CheckRisk(f);
                // An amend received before Submit acceptance must be applied after the captured ACK.
                if(link!=null&&!link.Exit&&link.AcceptanceConfirmed&&link.Desired!=link.RequestedQuantity)Change(link);
                MaintainProtection(f);
                }
                finally { Interlocked.Decrement(ref pendingFollowerCallbacks);if(capturedAccount!=null){Interlocked.Increment(ref capturedAccount.AppliedCallbacks);ReconcileGroups(capturedAccount);MaintainProtection(capturedAccount);} ReconcileCloseEvidence(); }
            });
            // A stopped mailbox refuses the work: drop the pending count so CanClose/Dispose cannot wait for it forever.
            // The captured fill evidence (snapshot.Filled>0 => ledgerUnresolved) stays raised on purpose: the fill was never applied.
            if(!accepted)Interlocked.Decrement(ref pendingFollowerCallbacks);
        }
        void ReconcileCloseEvidence()
        {
            if(Volatile.Read(ref pendingFollowerCallbacks)!=0)return;
            // Authorization remains durably latched after a proven clean session detaches.
            bool unresolved=followers.Any(f=>f.EvidenceUnresolved||persistenceFailed.Contains(f)||f.Flattening||OwnedPosition(f)!=0||HasPendingEntryWork(f)||links.Values.SelectMany(v=>v).Any(x=>x.Follower==f&&(x.SubmissionInFlight||(x.Order!=null&&!Order.IsTerminalState(x.ObservedState)))));
            lock(admissionGate){if(pendingFollowerCallbacks==0)ledgerUnresolved=unresolved;}
        }
        void MaintainProtection(EngineAccount f)
        {
            ReconcileGroups(f);
            if(suppressNew||f.Flattening)return;
            foreach(var link in links.Values.SelectMany(x=>x).Where(x=>x.Follower==f && x.Waiting && !x.Cancelled && (!x.Exit||!f.ExitCompleted||CertifiedWaitingProtection(x))).ToArray())
            {try{Submit(link,"Delayed exit error: ");}catch(Exception ex){link.Waiting=false;Lock(f,"Delayed exit error: "+ex.Message);}}
            foreach(var link in links.Values.SelectMany(x=>x).Where(x=>x.Follower==f && x.Exit && x.Order!=null && !x.Cancelled && !Order.IsTerminalState(x.Order.OrderState)).ToArray())
            {try{Change(link,"Protection resize error: ");}catch(Exception ex){Lock(f,"Protection resize error: "+ex.Message);}}
        }
        void Lock(EngineAccount f,string reason)
        {
            f.EvidenceUnresolved=true; ledgerUnresolved=true;
            f.EvidenceReason=reason;
            if(!f.Latch.Locked) { f.Latch.Locked=true; f.Latch.Reason=reason; PersistLock(f); log(f.Account.Name+" LOCK: "+f.Latch.Reason+". Coverage not established; manual reconciliation may be required."); }
            f.Following=false; CancelEntries(f);
            Publish(f);
        }
        void PersistLock(EngineAccount f)
        {
            try { durableLock(f.Account.Name,f.Latch.Reason); }
            catch(Exception ex) { persistenceFailed.Add(f); BlockEntries(); f.Latch.Reason="PERSISTENCE FAILURE — lock not durable; manual recovery required: "+ex.Message+" | "+f.Latch.Reason; log(f.Account.Name+": "+f.Latch.Reason); }
        }
        // A Position update that trails the order fill by a few milliseconds is normal. Only a divergence that outlives this window is unknown exposure.
        static readonly TimeSpan PositionLagGrace=TimeSpan.FromSeconds(3);
        internal static TimeSpan FlattenConfirmTimeout=TimeSpan.FromSeconds(20);
        // How long a copier exit may stay live on a follower that is already flat before the lock names it (rival 1.7.1.0 lesson: a fill there opens a NEW position).
        internal static TimeSpan LiveExitOnFlatGrace=TimeSpan.FromSeconds(10);
        bool FlatConfirmable(EngineAccount f)
        {
            if(!Connected(f.Account)||!Clean(f.Account,instrument)||HasPendingEntryWork(f)||Volatile.Read(ref pendingFollowerCallbacks)!=0)return false;
            if(links.Values.SelectMany(v=>v).Any(x=>x.Follower==f&&(x.SubmissionInFlight||x.ChangesInFlight>0)))return false;
            var last=f.FlattenAcceptedUtc; if(f.LastFillUtc>last)last=f.LastFillUtc; if(f.LastOrderEventUtc>last)last=f.LastOrderEventUtc;
            return DateTime.UtcNow-last>PositionLagGrace;
        }
        void ConfirmFlat(EngineAccount f)
        {
            int raw=OwnedPosition(f); f.FlatOffset-=raw;
            foreach(var link in links.Values.SelectMany(v=>v).Where(x=>x.Follower==f)) { link.Cancelled=true; link.Waiting=false;link.FlatRetired=true; }
            // Provider-confirmed Flat consumes the prior round; keep evidence but never allocate its entry lots again.
            foreach(var group in f.ExitGroups.Values){group.FlatRetired=true;group.Demand=0;group.PlannedQuota=0;group.Revision++;}
            Order[] seen; lock(f.Account.Orders)seen=f.Account.Orders.ToArray();
            // The close order is history, never unknown activity. Only TERMINAL orders: a never-sent KTC> ghost (Initialized) passes Clean but must stay
            // exempt only while it stays Initialized and unfilled; recording it as history would wave through a later fill of that object.
            foreach(var o in seen)if(o!=null&&o.Instrument!=null&&o.Instrument.FullName==instrument.FullName&&Order.IsTerminalState(o.OrderState))f.AddHistory(o);
            lock(controlGate){ f.Flattening=false; f.ExitCompleted=true; f.Following=false; }
            // Provider evidence now shows this account flat with nothing working, so it can no longer hold the session open. The risk latch is NOT touched:
            // a locked account stays locked and OFF until the user unlocks it (real NT 2026-10-14: Stop stayed refused after Flatten all until NT restarted).
            if(f.EvidenceUnresolved){f.EvidenceUnresolved=false;f.EvidenceReason=null;log(f.Account.Name+": confirmed flat clears its open-evidence hold; lock kept ("+(f.Latch.Locked?f.Latch.Reason:"not locked")+")");}
            flattenConfirmations[f]="Confirmed flat";
            RecomputeDisablePending(f);
            log(f.Account.Name+": Flat confirmed (position 0, nothing working) — OFF; re-follow explicitly to copy again");
            ReconcileCloseEvidence();
        }
        void CheckRisk(EngineAccount f)
        {
            if(f.Flattening)
            {
                // A user Flat is confirmed ONLY by provider evidence: the call returned (did not throw), Position is 0, nothing is working, and the account has been quiet longer
                // than the position-lag grace. Then the ledger is credited once (FlatOffset), the account stays OFF and must be re-followed explicitly. Until then entries/exits for it stay blocked.
                // If the provider has not shown flat within FlattenConfirmTimeout the account locks for manual review; a thrown Flatten locks at once (see flattenDone) and is never retried.
                if(f.FlattenAcceptedUtc!=DateTime.MinValue&&FlatConfirmable(f))ConfirmFlat(f);
                else if(DateTime.UtcNow-f.FlattenRequestedUtc>FlattenConfirmTimeout)
                { flattenConfirmations[f]="Awaiting provider flat / order confirmation"; Lock(f,"Flatten unconfirmed — no concurrent recovery exits authorized; verify provider state manually before explicit abandonment"); }
                else if(!flattenConfirmations.ContainsKey(f)||flattenConfirmations[f]=="Requested")flattenConfirmations[f]=f.FlattenAcceptedUtc==DateTime.MinValue?"Requested":"Awaiting provider flat / order confirmation";
            }
            if(!f.Flattening)
            {
                Order[] actual; lock(f.Account.Orders)actual=f.Account.Orders.ToArray();
                if(actual.Any(o=>o.Instrument!=null&&o.Instrument.FullName==instrument.FullName&&(!Order.IsTerminalState(o.OrderState)||o.Filled>0)&&!f.KnownHistory(o,o.OrderState,o.OrderId)&&!(f.PreSessionGhost.Contains(o)&&o.OrderState==OrderState.Initialized&&o.Filled==0)&&FindFollowerLink(f,o,o.OrderId)==null)){f.AllocationUncertain=true;Lock(f,"Unknown follower order / terminal fill evidence — no adoption; manual recovery required");}
                if(Volatile.Read(ref pendingFollowerCallbacks)==0&&links.Values.SelectMany(v=>v).Any(x=>x.Follower==f&&x.Order!=null&&(x.Order.Filled!=x.ObservedFilled||(Order.IsTerminalState(x.Order.OrderState)&&x.Order.OrderState!=x.ObservedState))))
                    Lock(f,"Provider wrapper diverges from captured order / fill evidence — reservations retained; manual recovery required");
                int position=Position(f.Account,instrument), execution=OwnedPosition(f);
                // A copier exit still live on a FLAT follower long after the last fill: if it fills it opens a new position. Name it; never retry the cancel.
                if(position==0&&(DateTime.UtcNow-f.LastFillUtc)>LiveExitOnFlatGrace&&links.Values.SelectMany(v=>v).Any(x=>x.Follower==f&&x.Exit&&x.Order!=null&&!Order.IsTerminalState(x.Order.OrderState)&&(x.Order.OrderState!=OrderState.Initialized||x.Order.Filled>0)))
                {const string why="Exit order still working on a FLAT account — if it fills it opens a NEW position; cancel it in NinjaTrader";if(f.EvidenceReason!=why)Lock(f,why);}
                if(Volatile.Read(ref pendingFollowerCallbacks)<=1&&position!=0&&(execution==0||Math.Sign(position)!=Math.Sign(execution)||Math.Abs((long)position)>Math.Abs((long)execution))&&(DateTime.UtcNow-f.LastFillUtc)>PositionLagGrace)Lock(f,"Position diverges from owned execution ledger — manual recovery required");
            }
            double raw=f.Account.Get(AccountItem.RealizedProfitLoss,f.Account.Denomination)+f.Account.Get(AccountItem.UnrealizedProfitLoss,f.Account.Denomination);
            double equity=f.Account.Get(AccountItem.NetLiquidation,f.Account.Denomination);
            if(!f.Settings.BaselineSet && RiskPolicy.Finite(raw)) { f.Settings.Baseline=raw; f.Settings.BaselineSet=true; }
            f.HighWater=Math.Max(f.HighWater,equity);
            var settings=f.Settings.Clone();
            if(settings.LocalTrail && settings.TrailDistance>0) settings.Floor=Math.Max(settings.Floor,f.HighWater-settings.TrailDistance);
            bool was=f.Latch.Locked;
            RiskPolicy.Evaluate(settings,f.Latch,raw-f.Settings.Baseline,equity,Connected(f.Account));
            if(!was&&f.Latch.Locked){PersistLock(f);log(f.Account.Name+" LOCK: "+f.Latch.Reason+". Local guard only; coverage not established.");}
            if(f.Latch.Locked) { f.Following=false; CancelEntries(f); }
            if(!was&&f.Latch.Locked&&f.Settings.FlatOnLimit&&!f.LimitFlattenSent&&(f.Latch.Reason=="Target hit"||f.Latch.Reason=="Daily loss"))
            {
                f.LimitFlattenSent=true; // one request per session, whatever happens next
                log(f.Account.Name+": "+f.Latch.Reason+" with Flat on limit ON — one Flatten request for "+instrument.FullName+", never retried");
                var target=f.Account;var inst=instrument;
                worker.Post(()=>{ if(attached&&!f.Flattening&&Position(target,inst)!=0)Flatten(new[]{target},inst); });
            }
            Publish(f);
        }
        void Publish(EngineAccount f)
        {
            int generation,processed,session; bool following,pending,locked,paused,armed; string reason;
            lock(controlGate) { generation=f.FollowGeneration; processed=f.ProcessedFollowGeneration; session=sessionGeneration; following=f.Following; pending=f.DisablePending; locked=f.Latch.Locked; reason=f.Latch.Reason; paused=f.Paused; armed=entries; }
            string evidenceReason=f.EvidenceReason;
            if(f.LateEntryCoverageWarning)
            {
                string current=UnprotectedLots(f)>0?LateEntryReason+" — "+UnprotectedText(f):"Late entry fill after exit completion — no currently uncovered lots in captured working-exit evidence; entries remain OFF, review required";
                if(reason!=null&&reason.StartsWith(LateEntryReason,StringComparison.Ordinal))reason=current;
                if(evidenceReason!=null&&evidenceReason.StartsWith(LateEntryReason,StringComparison.Ordinal))evidenceReason=current;
            }
            if(!string.IsNullOrEmpty(evidenceReason)&&reason!=evidenceReason)reason=(string.IsNullOrEmpty(reason)?"":reason+" | ")+evidenceReason;
            bool coverageUnknown=links.Values.SelectMany(v=>v).Any(x=>x.Follower==f&&x.Exit&&x.Order!=null&&!Order.IsTerminalState(x.ObservedState)&&!x.AcceptanceConfirmed);
            if(coverageUnknown)reason=(string.IsNullOrEmpty(reason)?"":reason+" | ")+"Protective coverage unknown — requested, provider acceptance unconfirmed; no retry / chase";
            double realized=f.Account.Get(AccountItem.RealizedProfitLoss,f.Account.Denomination), unrealized=f.Account.Get(AccountItem.UnrealizedProfitLoss,f.Account.Denomination);
            telemetry(new AccountTelemetry {Name=f.Account.Name,Baseline=f.Settings.Baseline,Currency=f.Account.Denomination.ToString(),Connection=Connected(f.Account)?"Connected":"Disconnected",Pnl=realized+unrealized-f.Settings.Baseline,Equity=f.Account.Get(AccountItem.NetLiquidation,f.Account.Denomination),Realized=realized,Unrealized=unrealized,Sod=f.Account.Get(AccountItem.SodLiquidatingValue,f.Account.Denomination),HighWater=f.HighWater,Position=Position(f.Account,instrument),Direction=Position(f.Account,instrument)>0?"Long":Position(f.Account,instrument)<0?"Short":"Flat",Following=following,ExitCompleted=!following&&f.ExitCompleted,FollowGeneration=generation,ProcessedFollowGeneration=processed,SessionGeneration=session,DisablePending=pending,Locked=locked,Reason=reason,Paused=paused,State=locked||coverageUnknown?reason:pending?"OFF — cancellation pending":!following?"OFF / exits managed":paused||!armed?"Paused / exits managed":"Active"});
        }
        public void Poll()
        {
            if(Interlocked.Exchange(ref pollPending,1)!=0)return;
            if(!worker.Post(()=>{ try { if(attached) { if(!Connected(leader)){entries=false;pauseOnly=false;foreach(var f in followers)Lock(f,"Leader disconnected — no catch-up");} foreach(var f in followers)try {CheckRisk(f);MaintainProtection(f);}catch(Exception ex){Lock(f,"Telemetry error: "+ex.Message);} } } finally {Interlocked.Exchange(ref pollPending,0);} }))Interlocked.Exchange(ref pollPending,0); // refused by a stopped mailbox: do not leave the flag stuck
        }
        void CancelEntries(EngineAccount f)
        {
            if(f.Cancelling)return; f.Cancelling=true;
            try
            {
            foreach(var link in links.Values.SelectMany(x=>x).Where(x=>x.Follower==f&&!x.Exit).ToArray())
            {
                link.Cancelled=true; link.Waiting=false;
                if(link.Order!=null&&!Order.IsTerminalState(link.ObservedState)&&!Order.IsTerminalState(link.Order.OrderState)&&!link.CancellationRequested)
                    { link.CancellationRequested=true; var eo=link.Order; Dispatch(f,()=>f.Account.Cancel(new[]{eo}),err=>{ if(err!=null) { link.CancellationRequested=false; f.DisablePending=true; Lock(f,"Entry cancellation unresolved: "+err.Message); } },"Cancel entry (OFF)"); }
            }
            RecomputeDisablePending(f);
            } finally { f.Cancelling=false; }
        }
        // A never-sent copier ghost (recorded by this session, still Initialized, nothing filled) is not on the market and cannot fill: it never blocks re-follow or abandonment.
        // It stops being inert the moment its state or fill changes; then every normal check applies again.
        static bool Inert(EngineAccount f,Order o){return o!=null&&f.PreSessionGhost.Contains(o)&&o.OrderState==OrderState.Initialized&&o.Filled==0;}
        bool HasPendingEntryWork(EngineAccount f)
        { return links.Values.SelectMany(x=>x).Any(x=>x.Follower==f&&!x.Exit&&(x.SubmissionInFlight||(x.Order!=null&&!Order.IsTerminalState(x.ObservedState)))); }
        void RecomputeDisablePending(EngineAccount f)
        { lock(controlGate)f.DisablePending=!f.Following&&HasPendingEntryWork(f); }
        public int SetFollowing(string accountName,bool enabled)
        {
            var f=followerView.FirstOrDefault(x=>x.Account.Name==accountName); if(f==null)return -1;
            int request;
            lock(controlGate) { request=++f.FollowGeneration; if(!enabled)f.Following=false; }
            bool accepted=worker.Post(()=>
            {
                if(!attached||!followers.Contains(f))return;
                if(!enabled) { CancelEntries(f); lock(controlGate)f.ProcessedFollowGeneration=Math.Max(f.ProcessedFollowGeneration,request); Publish(f); return; }
                CheckRisk(f);
                bool clean=Clean(f.Account,instrument)&&OwnedPosition(f)==0&&!ExposureOrders(f).Any(o=>o.Instrument!=null&&o.Instrument.FullName==instrument.FullName&&!Order.IsTerminalState(o.OrderState)&&!Inert(f,o))&&!f.DisablePending&&!f.Latch.Locked&&Connected(f.Account)&&entries&&!f.Flattening;
                lock(controlGate)
                {
                    if(request!=f.FollowGeneration)return;
                    if(clean) { lock(identityGate)foreach(var id in identities.Values)f.Ignored.Add(id); f.ExitCompleted=false; f.ExitDroppedNothingToClose=false; f.Following=true; }
                    f.ProcessedFollowGeneration=request;
                }
                if(!clean)log(accountName+": Follow refused — clean, connected, unlocked session required");
                Publish(f);
            });
            // A stopped mailbox refused the request: it can never be applied, so never leave it pending and never follow.
            // Following was already forced false for OFF above; an ON request is simply not honoured.
            if(!accepted){lock(controlGate)f.ProcessedFollowGeneration=Math.Max(f.ProcessedFollowGeneration,request);}
            return request;
        }
        public void PauseAccount(string name,bool paused) { worker.Post(()=>{var f=followers.FirstOrDefault(x=>x.Account.Name==name);if(f!=null){f.Paused=paused;Publish(f);}}); }
        void BlockEntries() { lock(controlGate) { ++controlGeneration; entries=false; pauseOnly=false; } }
        public bool CanResume { get { return attached && !entries && pauseOnly && !disposed; } }
        public void GuardedPause()
        { lock(controlGate) { ++controlGeneration; pauseOnly=attached&&(entries||pauseOnly); entries=false; } worker.Post(()=>{ // a pause never turns an earlier safety block into a resumable pause
stateChanged(attached,"GUARDED PAUSE — no new entries; existing lifecycle + new exits still managed");log("Paused NEW entries. Working entries may still fill; new exits and changes/cancels remain mirrored. Flatten explicitly to remove exposure.");}); }
        // Resume NEW entries after a guarded pause only. No catch-up: every leader entry seen before this call (including entries opened while
        // paused) stays tombstoned, because the control generation moves forward here and LeaderUpdate stamps each event with the generation
        // it was captured under. It never unlocks, never re-follows and never sends anything by itself. Refused after any safety block.
        public void ResumeEntries()
        {
            int request=Volatile.Read(ref controlGeneration);
            worker.Post(()=>
            {
                string why=null;
                if(disposed||!attached)why="no managed session";
                else if(entries)why="entries are already on";
                else if(!pauseOnly||request!=Volatile.Read(ref controlGeneration))why="entries were stopped by a safety block, not by a pause; Stop when flat, then Start";
                else if(suppressNew||ledgerUnresolved||persistenceFailed.Count>0)why="flatten / unresolved execution evidence / persistence failure";
                else if(!Connected(leader))why="leader disconnected";
                else if(links.Count+tombstones.Count>=10000)why="session history bound reached";
                else if(followers.Any(f=>f.AllocationUncertain||f.ProviderFaulted||f.Flattening))why="a follower has an unresolved provider fault or flatten";
                if(why!=null){log("Resume refused: "+why+".");return;}
                lock(controlGate)
                {
                    if(request!=controlGeneration||!pauseOnly){log("Resume refused: entries were stopped by a safety block, not by a pause; Stop when flat, then Start.");return;}
                    ++controlGeneration;entries=true;pauseOnly=false;
                }
                stateChanged(true,"COPYING — "+leader.Name+" / "+instrument.FullName);
                log("Resumed NEW entries. Leader entries seen while paused are NOT copied; only later new entries are. Locked or unticked followers stay as they are.");
                foreach(var f in followers)Publish(f);
            });
        }
        public void StopFlat()
        {
            BlockEntries();
            worker.Post(()=>{ if(!attached)return; if(!TryDetach()) { stateChanged(true,"GUARDED PAUSE — stop blocked until owned executions reconcile flat / no working orders");log("Stop refused: unresolved execution / order evidence. Manual reconciliation required; coverage is not guaranteed."); return;} });
        }
        bool TryDetach()
        {
            if(!CanClose())return false; // Collection reads stay outside the admission gate.
            lock(admissionGate)
            {
                // Already detached (for example StopFlat, then Dispose): the session is closed, report success but do not run the detach side effects a second time.
                if(!attached)return true;
                if(pendingFollowerCallbacks!=0||providerCallsInFlight!=0||ledgerUnresolved)return false;
                entries=false;attached=false;Interlocked.Increment(ref sessionGeneration);followerView=new EngineAccount[0];
            }
            FinishDetach();return true;
        }
        void Detach()
        {
            lock(admissionGate){entries=false;attached=false;Interlocked.Increment(ref sessionGeneration);followerView=new EngineAccount[0];}
            FinishDetach();
        }
        void FinishDetach()
        {
            if(leader!=null&&leaderHandler!=null)leader.OrderUpdate-=leaderHandler;
            foreach(var f in followers)if(followerHandler!=null)f.Account.OrderUpdate-=followerHandler;
            SessionOwnership.Release(this);stateChanged(false,"DISARMED — explicit Start required");
        }
        public void Flatten(Account[] targets,Instrument inst)
        {
            // Unconditional external-Account hold, even with KINFX_OFFLINE_FAKE defined. Only the explicit KINFX_SIM_TEST build lifts it, and then only for Simulator / Playback.
#if !KINFX_SIM_TEST
            if(typeof(Account).Assembly!=typeof(CopierEngine).Assembly)
            { BlockEntries();log("PUBLIC ALPHA SAFETY HOLD — Flatten blocked for ALL providers, including Sim / Playback. Trading DISABLED.");return; }
#endif
#if !KINFX_OFFLINE_FAKE && !KINFX_SIM_TEST
            BlockEntries();log("ALL-ACCOUNT SAFETY HOLD — Flatten blocked for every account. No trading actions authorized.");return;
#else
#if KINFX_SIM_TEST
            // SIM TEST BUILD: one Live / null / unknown-provider account refuses the WHOLE request; nothing is flattened, not even the Sim accounts in it.
            if(targets!=null)targets=(Account[])targets.Clone();
            if(inst==null){ log("SIM TEST: Flatten refused — no instrument. Nothing was sent."); return; }
            if(!SimScope(targets)){ log("SIM TEST: Flatten refused — scope contains a non-Simulator/Playback or missing account. Nothing was sent."); return; }
#endif
            // Per-account Flat: when the scope holds only FOLLOWERS of a live session, nothing about the leader changes, so the other followers keep copying; only the targeted
            // accounts are switched OFF and blocked. A scope that includes the leader (or any session-less call) keeps the old rule: block ALL new copies during the request.
            var leaderNow=leader; bool followerOnly=attached&&targets!=null&&leaderNow!=null&&!targets.Contains(leaderNow);
            if(!followerOnly){BlockEntries();suppressNew=true;} // block ALL new copies during the flatten request.
            bool flattenAccepted=worker.Post(()=>
            {
                if(followerOnly&&!attached)followerOnly=false;
                if(!followerOnly&&!suppressNew){BlockEntries();suppressNew=true;}
                if(!SessionOwnership.Acquire(targets.Select(a=>"account:"+a.Name),this)){if(!followerOnly)suppressNew=false;log("Flatten blocked: another copier owns an account in this scope.");return;}
                if(attached && targets.Contains(leader) && followers.Any(f=>!targets.Contains(f.Account) && !Clean(f.Account,instrument)))
                {suppressNew=false;log("Flatten leader blocked: other managed followers still have exposure/working orders. Include all managed followers or flatten followers first.");return;}
                // Never a second Flatten for an account whose earlier Flatten is still unconfirmed: on the provider a second close while the first is in flight
                // can close the same lots twice and open a reverse position. That account is dropped from this request; it waits for confirmation or manual abandonment.
                var pending=followers.Where(f=>f.Flattening&&targets.Contains(f.Account)).Select(f=>f.Account).ToArray();
                foreach(var p in pending)log(p.Name+": Flatten already in progress — not sent again; wait for confirmation or check NinjaTrader");
                if(pending.Length>0){targets=targets.Where(a=>!pending.Contains(a)).ToArray();if(targets.Length==0){if(!followerOnly)suppressNew=false;if(!attached)SessionOwnership.Release(this);return;}}
                foreach(var f in followers.Where(f=>targets.Contains(f.Account)))
                {
                    lock(controlGate){ f.Flattening=true; f.Following=false; f.FlattenRequestedUtc=DateTime.UtcNow; f.FlattenAcceptedUtc=DateTime.MinValue; }
                    flattenConfirmations[f]="Requested";
                    CancelEntries(f); Publish(f);
                }
                stateChanged(attached,"FLATTEN requested — new entries blocked; verify flat status");
                foreach(var account in targets)
                {
                    if(!Sim(account)) {log("LIVE FLATTEN BLOCKED "+account.Name);continue;}
                    var target=account; var tf=followers.FirstOrDefault(x=>x.Account==target);
                    Action<Exception> flattenDone=ex=>
                    {
                        if(ex==null){if(tf!=null)tf.FlattenAcceptedUtc=DateTime.UtcNow;log("Flatten request: "+target.Name+" / "+inst.FullName+" — verify flat, retry manually if error");return;}
                        BlockEntries(); // a thrown Flatten is an uncertain state: whatever the scope, stop new copies engine-wide (guarded pause); never retried.
                        if(tf!=null) { flattenConfirmations[tf]="Request threw — provider acceptance unknown"; Lock(tf,"Flatten acceptance uncertain — existing protection retained; duplicate reductions blocked until explicit provider reconciliation: "+ex.Message); }
                        log("FLATTEN FAILED "+target.Name+": "+ex.Message);
                    };
                    // A follower's Flatten goes through ITS dispatcher so it can never overtake a Submit/Change/Cancel already queued for the same account.
                    if(tf!=null)Dispatch(tf,()=>target.Flatten(new[]{inst}),flattenDone,"Flatten "+inst.FullName);
                    else {
#if KINFX_SIM_TEST
                        // No follower dispatcher for this account (the leader): the direct call must honour dry run too.
                        if(dryRun){ log("DRY RUN would send to "+target.Name+": Flatten "+inst.FullName); flattenDone(null); continue; }
#endif
                        try { target.Flatten(new[]{inst}); flattenDone(null); } catch(Exception ex) { flattenDone(ex); } }
                }
                if(attached)suppressNew=false; // selected followers excluded; unflattened followers still need new protective exits.
                if(followerOnly)log("Flat requested for "+targets.Length+" follower account(s); other accounts keep copying.");
                if(!attached)SessionOwnership.Release(this);
            });
            // Refused by a stopped mailbox: nothing will run to clear the flag, and nothing reached an account.
            if(!flattenAccepted){if(!followerOnly)suppressNew=false;log("Flatten refused: copier is stopped; no request was sent.");}
#endif
        }
        // Manual provider reconciliation acknowledgement, NOT an autonomous timeout/retry.
        // A late provider close remains possible: broker acceptance cannot be retracted by this acknowledgement.
        public void AcknowledgeFlattenAbandoned(string accountName)
        {
            worker.Post(()=>
            {
                var f=followers.FirstOrDefault(x=>x.Account.Name==accountName);
                if(f==null||!f.Flattening)return;
                var known=new HashSet<Order>(links.Values.SelectMany(v=>v).Where(x=>x.Follower==f&&x.Order!=null).Select(x=>x.Order));
                if(!Connected(f.Account)||HasPendingEntryWork(f)||ExposureOrders(f).Any(o=>o.Instrument!=null&&o.Instrument.FullName==instrument.FullName&&!Order.IsTerminalState(o.OrderState)&&!known.Contains(o)&&!Inert(f,o)))
                { log(accountName+": flatten abandonment refused — in-flight / unknown working orders remain");return; }
                int actual=Position(f.Account,instrument),owned=OwnedPosition(f);
                if(actual==0||actual!=owned){log(accountName+": flatten abandonment refused — exposure evidence unresolved");return;}
                flattenConfirmations[f]="Manually abandoned after provider reconciliation; late close risk remains";
                f.Flattening=false; Lock(f,"Manual flatten abandonment — entries locked; only explicit leader exits managed, no autonomous protection / provider guarantee");MaintainProtection(f);
            });
        }
        public bool CanClose()
        {
            if(!attached)return true;
            int session=Volatile.Read(ref sessionGeneration);Account[] accounts=sessionAccounts;
            if(Volatile.Read(ref pendingFollowerCallbacks)!=0||Volatile.Read(ref providerCallsInFlight)!=0||ledgerUnresolved||instrument==null)return false;
            if(!accounts.All(a=>Clean(a,instrument)))return false;
            // Provider collection reads can block while immutable callbacks are admitted or applied.
            // The frontier checked BEFORE those reads cannot certify their eventual clean result.
            return attached&&session==Volatile.Read(ref sessionGeneration)&&Volatile.Read(ref pendingFollowerCallbacks)==0
                &&Volatile.Read(ref providerCallsInFlight)==0&&!ledgerUnresolved;
        }
        public void Dispose()
        {
            if(disposed)return;BlockEntries();
            if(!CanClose()){log("Close refused: owned executions / orders unresolved; callbacks and ownership retained. Resolve manually before closing.");return;}
            Action close=()=>
            {
                if(disposed)return;
                if(!TryDetach()){log("Close refused: owned executions / orders unresolved; callbacks and ownership retained. Resolve manually before closing.");return;}
                disposed=true;worker.Dispose();DisposeDispatchers();
            };
            if(worker.IsCurrentThread){close();return;}
            var complete=new System.Threading.Tasks.TaskCompletionSource<bool>();
            if(!worker.Post(()=>{try{close();}finally{complete.TrySetResult(true);}}))return;
            // A timeout never stops admission or discards accepted work; the queued decision still runs.
            complete.Task.Wait(3000);
        }
    }
    public sealed class AccountRow : INotifyPropertyChanged
    {
        internal Account Account;
        public RiskSettings Settings = new RiskSettings();
        public RiskLatch Latch = new RiskLatch();
        public string Name { get; set; }
        public string Role { get; set; }
        // 0 for the leader so the account tables always list it first (user request); every column sort keeps it on top.
        public int LeaderRank {get{return Role=="Leader"?0:1;}}
        public string Currency { get; set; }
        public string Connection { get; set; }
        public string Status { get; set; }
        // Shown text for the Settings table: Status / Connection hold English codes (logic and sorting use them); the cell shows the picked language.
        public string StatusShown {get{return ExitClosed?Lang.T("Closed · re-follow"):Lang.Show(Status??"");}}
        public string ConnectionShown {get{return Lang.Show(Connection??"");}}
        public double Pnl { get; set; }
        // 1 profit, -1 loss, 0 flat or unknown (NaN). Cents below half a cent count as zero so rounding noise is not coloured.
        public int PnlSign {get{return double.IsNaN(Pnl)||double.IsInfinity(Pnl)||Math.Abs(Pnl)<0.005?0:Pnl>0?1:-1;}}
        public double Equity { get; set; }
        public double CashBalance {get;set;}=double.NaN;
        public string CurrencyCode {get{return string.IsNullOrWhiteSpace(Currency)?"CCY?":MonitorSummary.IsoCode(Currency);}}
        public string BalanceText {get{return (Connection=="Connected"||Connection=="Synthetic")&&RiskPolicy.Finite(CashBalance)?MonitorSummary.Money(CashBalance,Currency):"\u2014";}}
        // Tooltip of the balance cell, in the picked language. {3} stays empty: the amount already carries its currency symbol.
        public string BalanceDetail {get{return Lang.F("CashValue · {0} · {1}\nNetLiquidation (net equity, NOT cash balance): {2} {3}\nAccount.Get reports provider values; currency argument does not convert. Dash = unavailable/disconnected. Synthetic rows are not broker data.",Name,BalanceText,(Connection=="Connected"||Connection=="Synthetic")&&RiskPolicy.Finite(Equity)?MonitorSummary.Money(Equity,Currency):"\u2014","");}}
        public double Realized { get; set; }
        public double Unrealized { get; set; }
        public double Sod { get; set; }
        public int Position { get; set; }
        EvidenceMark evidence;
        PositionConsistencyStatus consistencyKind=PositionConsistencyStatus.Unavailable;
        PositionConsistencyResult consistencyResult;
        public string ConsistencyBadge { get { return consistencyResult==null?PositionConsistency.Dash:consistencyResult.Badge; } }
        // Shown text: "wait" / "n/a" translated; qty±N, avg ±Nt and adverse stay as documented tokens (unknown keys fall back to English).
        public string ConsistencyBadgeShown { get { return Lang.T(ConsistencyBadge); } }
        public string ConsistencyStatus { get { return consistencyKind.ToString(); } }
        public string ConsistencyTip { get { return PositionConsistency.FormatTip(consistencyResult); } }
        public Brush ConsistencyInk
        {
            get
            {
                switch(consistencyKind)
                {
                    case PositionConsistencyStatus.QtyDiverge: return DashboardView.Bad;
                    case PositionConsistencyStatus.PriceDiverge: return DashboardView.Warn;
                    case PositionConsistencyStatus.WaitingReports: return DashboardView.Warn;
                    case PositionConsistencyStatus.QtyMatch: return DashboardView.Good;
                    case PositionConsistencyStatus.Unavailable: return DashboardView.Muted;
                    default: throw PositionConsistency.Unexpected(consistencyKind);
                }
            }
        }
        internal PositionSnap ObservedSnap;
        internal PositionSnap Observe(string instrument,bool connected,bool contractsVerified,bool readSucceeded,int signedQty,bool hasAverage,double average,DateTime nowUtc)
        {
            var snap=new PositionSnap{Instrument=instrument??"",Connected=connected,ContractsVerified=contractsVerified,Ratio=Settings==null?double.NaN:Settings.Ratio,MaxContracts=Settings==null?0:Settings.MaxContracts,SignedQuantity=signedQty};
            if(readSucceeded&&contractsVerified)
            {
                evidence=PositionConsistency.Note(evidence,signedQty,hasAverage,average,nowUtc);
                snap.ReadUtc=nowUtc;snap.HasAveragePrice=hasAverage;snap.AveragePrice=hasAverage?average:0;
            }
            snap.EvidenceVersion=evidence.Version;snap.EvidenceUtc=evidence.EvidenceUtc;
            ObservedSnap=snap;return snap;
        }
        internal void ApplyConsistency(PositionConsistencyResult result)
        {
            var next=result??PositionConsistency.NotComparable();
            if(consistencyResult!=null&&consistencyKind==next.Status&&consistencyResult.Badge==next.Badge&&consistencyResult.Reason==next.Reason&&consistencyResult.QtyDelta==next.QtyDelta&&consistencyResult.PriceDeltaTicks==next.PriceDeltaTicks&&consistencyResult.AdverseTicks==next.AdverseTicks&&consistencyResult.ExpectedQuantity==next.ExpectedQuantity)return;
            consistencyResult=next;consistencyKind=next.Status;Changed();
        }
        public bool Paused { get; set; }
        bool enabled;
        public bool DisablePending {get;set;}
        public event Action<AccountRow,bool> FollowRequested;
        public bool Enabled {get{return enabled;}set{if(value&&(!CanFollow||Latch.Locked||DisablePending))return;if(enabled==value&&(value||!FollowPending))return;enabled=value;Changed();var h=FollowRequested;if(h!=null)h(this,value);}}
        int latestFollowSession=-1,latestFollowGeneration;
        public bool FollowPending {get;private set;}
        internal void SyncFollowing(bool following,bool pending){enabled=following&&!Latch.Locked;DisablePending=pending;FollowPending=false;Changed();}
        internal void InvalidateSessionActivity(){ExitClosed=false;UserFlattened=false;FollowPending=false;DisablePending=false;Paused=false;Status="Disarmed";latestFollowSession=-1;latestFollowGeneration=0;Changed();}
        internal void TrackFollowRequest(int session,int generation,bool following)
        {
            if(session!=latestFollowSession){latestFollowSession=session;latestFollowGeneration=0;}
            latestFollowGeneration=Math.Max(latestFollowGeneration,generation);enabled=false;FollowPending=following;DisablePending=!following;Changed();
        }
        internal bool ApplyTelemetry(AccountTelemetry t,int currentSession)
        {
            if(t.Name!=Name||t.SessionGeneration!=currentSession)return false;
            if(latestFollowSession==t.SessionGeneration&&t.FollowGeneration<latestFollowGeneration)return false;
            latestFollowSession=t.SessionGeneration;latestFollowGeneration=t.FollowGeneration;
            Pnl=t.Pnl;Equity=t.Equity;Realized=t.Realized;Unrealized=t.Unrealized;Sod=t.Sod;Position=t.Position;Connection=t.Connection;Status=t.State;Paused=t.Paused;ExitClosed=t.ExitCompleted&&!t.Following&&!t.Locked;if(t.Following)UserFlattened=false;
            if(t.Locked&&!Latch.Locked){Latch.Locked=true;Latch.Reason=t.Reason;}
            Settings.HighWater=t.HighWater;Settings.Baseline=t.Baseline;Settings.BaselineSet=true;
            if(t.ProcessedFollowGeneration==latestFollowGeneration)SyncFollowing(t.Following,t.DisablePending);else Changed();return true;
        }
        public bool CopyAtm {get{return Settings.CopyAtm;}set{Settings.CopyAtm=value;Changed();}}
        public bool FlatOnLimit {get{return Settings.FlatOnLimit;}set{Settings.FlatOnLimit=value;Changed();}}
        public bool AutoRefollow {get{return Settings.AutoRefollow;}set{Settings.AutoRefollow=value;Changed();}}
        // The engine turned following off because this follower's copied exit completed ("closed · re-follow"). A user OFF never sets it.
        public bool ExitClosed {get;private set;}
        // Set when the user confirmed a Flat / Flatten for this account; cleared once the engine reports it following again. Auto re-follow skips it.
        public bool UserFlattened {get;set;}
        public string OnDivergence {get{return Settings.OnDivergence;}set{if(!DivergenceChoice.IsValid(value))throw new ArgumentException(Lang.T("On divergence must be Notify or FlattenOnce"));Settings.OnDivergence=value;Changed();}}
        public bool CanFlat {get{return Role!="Leader"&&CanFollow&&Connection!="Disconnected"&&DashboardSafety.CanExecute("flattenSelected");}}
        public bool CanFollow {get{return Role!="Leader"&&(Account!=null?CopierEngine.Listable(Account):Connection=="Synthetic");}}
        public Brush ConnLight {get{return Connection=="Connected"?DashboardView.Good:Connection=="Disconnected"?DashboardView.Bad:Connection=="Synthetic"||Connection=="Connecting"?DashboardView.Warn:DashboardView.Off;}}
        public Brush StateLight {get{return Latch.Locked||Status=="Telemetry error"?DashboardView.Bad:Paused||Status=="Target hit"||(Status??"").IndexOf("pause",StringComparison.OrdinalIgnoreCase)>=0?DashboardView.Warn:Status=="Active"?DashboardView.Good:DashboardView.Off;}}
        public string AccountPrefix {get{string n=Name??"";return n.Length>12?n.Substring(0,n.Length-4):n;}}
        public string AccountSuffix {get{string n=Name??"";return n.Length>12?n.Substring(n.Length-4):"";}}
        public string StateText {get{return Lang.T(StateLabel);}}
        public string StateLabel {get{return DisablePending?"Cancelling":FollowPending?"Rechecking":Latch.Locked?"Locked":Status=="Telemetry error"?"Error":Paused?"Paused":Role=="Leader"?"Leader":Status=="Active"&&Enabled?"Copying":Connection=="Disconnected"?"Offline":Status=="Demo only"?"Demo":ExitClosed?"Closed":Enabled?"Selected":"Off";}}
        public Brush StateInk {get{return DisablePending?DashboardView.Warn:Latch.Locked||Status=="Telemetry error"?DashboardView.Bad:StateLabel=="Copying"?DashboardView.Good:Paused?DashboardView.Warn:DashboardView.Muted;}}
        // Advisory only (never blocks). Reference lot size is the dashboard's current reference; see SettingsAdvice.
        internal static int AdviceReferenceLots=2;
        public string Advice {get{return SettingsAdvice.For(Settings,Role,AdviceReferenceLots);}}
        public string StateDetail {get{return Name+" · "+Lang.T(Role)+" · "+Lang.Show(Status)+(DisablePending?Lang.T(" · cancellation not confirmed"):"")+(Latch.Locked?" · "+Lang.Show(Latch.Reason):"")+(Advice.Length>0?Lang.T(" · NOTE: ")+Lang.Show(Advice):"")+" · "+Currency;}}
        public string Alias { get {return Settings.Alias;} set {Settings.Alias=value ?? "";} }
        public string Group { get {return Settings.Group;} set {Settings.Group=value ?? "";} }
        public double Ratio { get {return Settings.Ratio;} set {if(!RiskPolicy.Finite(value)||value<=0)throw new ArgumentException(Lang.T("This account Ratio must be finite and > 0"));ratioDraft=null;Settings.Ratio=value;Changed();} }
        public double Target { get {return Settings.Target;} set {if(!RiskPolicy.Finite(value)||value<0)throw new ArgumentException(Lang.T("Daily target must be a finite amount >= 0 (0 = off)"));targetDraft=null;Settings.Target=value;} }
        public double Loss { get {return Settings.Loss;} set {if(!RiskPolicy.Finite(value)||value<0)throw new ArgumentException(Lang.T("Daily loss must be a finite amount >= 0 (0 = off)"));lossDraft=null;Settings.Loss=value;} }
        // Row-owned text drafts: what the user typed survives broad refreshes, filtering and row recycling, and a malformed
        // draft blocks commit/persist for this row. The accepted numeric value is never replaced by a half-parsed one.
        string targetDraft,lossDraft,ratioDraft,maxDraft;
        public string TargetText {get{return targetDraft!=null?targetDraft:Target.ToString("0.################",CultureInfo.InvariantCulture);}set{SetAmountText(value,true);}}
        public string LossText {get{return lossDraft!=null?lossDraft:Loss.ToString("0.################",CultureInfo.InvariantCulture);}set{SetAmountText(value,false);}}
        public bool HasDraftError {get{return targetDraft!=null||lossDraft!=null||ratioDraft!=null||maxDraft!=null;}}
        void SetAmountText(string text,bool target)
        {
            double v;
            if(BulkEdit.TryParseAmount(text,out v)){if(target)Target=v;else Loss=v;return;}
            if(target)targetDraft=text??"";else lossDraft=text??"";
            throw new ArgumentException((target?"Daily target":"Daily loss")+Lang.T(" must be a plain amount like 1000 (0 = off): no commas, signs or text"));
        }
        public int MaxContracts { get {return Settings.MaxContracts;} set {if(value<1)throw new ArgumentException(Lang.T("Max contracts must be a whole number >= 1"));maxDraft=null;Settings.MaxContracts=value;} }
        public string RatioText {get{return ratioDraft!=null?ratioDraft:Ratio.ToString("0.################",CultureInfo.InvariantCulture);}set{SetRatioText(value);}}
        public string MaxText {get{return maxDraft!=null?maxDraft:MaxContracts.ToString(CultureInfo.InvariantCulture);}set{SetMaxText(value);}}
        void SetRatioText(string text)
        {
            double v; string t=(text??"").Trim();
            if(t.Length>0&&double.TryParse(t,NumberStyles.AllowDecimalPoint|NumberStyles.AllowExponent,CultureInfo.InvariantCulture,out v)&&RiskPolicy.Finite(v)&&v>0){Ratio=v;return;}
            ratioDraft=text??"";
            throw new ArgumentException(Lang.T("Ratio must be a plain positive number like 1 or 0.5: no commas, signs or text"));
        }
        void SetMaxText(string text)
        {
            int v; string t=(text??"").Trim();
            if(t.Length>0&&int.TryParse(t,NumberStyles.None,CultureInfo.InvariantCulture,out v)&&v>=1){MaxContracts=v;return;}
            maxDraft=text??"";
            throw new ArgumentException(Lang.T("Max must be a whole number >= 1"));
        }
        public string TargetRemaining { get {return Target>0?Math.Max(0,Target-Pnl).ToString("N2"):Lang.T("Off");} }
        public string LossRemaining { get {return Loss>0?Math.Max(0,Loss+Pnl).ToString("N2"):Lang.T("Off");} }
        public string Preset { get {return Settings.Preset;} }
        // True when the row still carries the default contract cap and no firm template: a leader order larger than Max is skipped entirely.
        public bool MaxIsDefault {get{return (Preset==null||Preset.Length==0||Preset=="Custom")&&MaxContracts==1;}}
        public string ConsistencyAdvice {get{return RiskPolicy.ConsistencyAdvice(Settings);}}
        public event PropertyChangedEventHandler PropertyChanged;
        public void Changed() {var h=PropertyChanged;if(h!=null)h(this,new PropertyChangedEventArgs(""));}
    }
    public sealed class DashboardView : Grid
    {
        public readonly ObservableCollection<AccountRow> Rows=new ObservableCollection<AccountRow>();
        public DataGrid AccountGrid, RiskGrid;
        public TabControl Sections;
        internal ComboBox LeaderBox;
        internal TextBox InstrumentBox, LogBox;
        internal Button StartButton, ResumeButton, RefollowButton;
        internal void ShowRefollow(int n){if(RefollowButton!=null){RefollowButton.Content=n>0?Lang.F("Re-follow closed accounts ({0})",n):Lang.T("Re-follow closed accounts");RefollowButton.IsEnabled=n>0&&SessionAttached&&DashboardSafety.CanExecute("refollow");}}
        // Shown only while the engine reports a resumable guarded pause (set by the tab every tick).
        internal void ShowResume(bool canResume){if(ResumeButton!=null){ResumeButton.Visibility=canResume?Visibility.Visible:Visibility.Collapsed;ResumeButton.IsEnabled=canResume&&DashboardSafety.CanExecute("resume");}}
        internal TextBlock StatusText,SessionText,FollowCountText;
        internal ComboBox LanguageBox;
        // Returns true if the language was applied (the owner refuses while a session is attached).
        internal Func<string,bool> LanguageChanging;
        internal CheckBox ShowLive;CheckBox groupBox;
#if KINFX_SIM_TEST
        internal CheckBox DryRunBox;
#endif
        internal Action<string> Command;
        internal Action<AccountRow,bool> PauseRow;
        internal Action<AccountRow> RowSettings;
        internal Action<AccountRow,bool> FollowRow;
        // Per-account Flat: one account, one click. The tab confirms (names the account and instrument) before anything is sent.
        internal Action<AccountRow> FlatRow;
        // "Ask before" switches (Settings tab). Ticked = the confirmation window is shown. Unlock and every risk warning are always asked and have no switch.
        internal CheckBox AskStartBox, AskFlatOneBox, AskFlattenBox;
        // Email alerts (Settings page). The tab loads / saves notify.txt and sends; the view only edits a draft.
        internal TextBox NotifyToBox;internal readonly Dictionary<string,CheckBox> NotifyKindBoxes=new Dictionary<string,CheckBox>(StringComparer.Ordinal);
        internal Action<NotifyPrefs.Settings> NotifySave;internal Action<NotifyPrefs.Settings> NotifyTest;internal TextBlock NotifyStatus;
        internal NotifyPrefs.Settings NotifyDraft()
        {
            var s=new NotifyPrefs.Settings{To=NotifyToBox==null?"":NotifyToBox.Text.Trim()};
            foreach(var kv in NotifyKindBoxes)if(kv.Value.IsChecked==true)s.On.Add(kv.Key);
            return s;
        }
        internal void ShowNotify(NotifyPrefs.Settings s)
        {
            if(s==null||NotifyToBox==null)return;NotifyToBox.Text=s.To;
            foreach(var kv in NotifyKindBoxes)kv.Value.IsChecked=s.On.Contains(kv.Key);
        }
        void BuildNotifySection(Panel host)
        {
            var box=new Border{Background=Panel,BorderBrush=Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(7),Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,0,10),Tag="notifySection"};
            var body=new StackPanel();body.Children.Add(Text(Lang.T("Email alerts (through NinjaTrader's email share service)"),12,Muted));
            var row=new WrapPanel{Margin=new Thickness(0,6,0,0)};
            NotifyToBox=Input("",220);NotifyToBox.Tag="notifyTo";NotifyToBox.ToolTip=Lang.T("Send alerts to this address · empty = off");System.Windows.Automation.AutomationProperties.SetName(NotifyToBox,Lang.T("Alert email address"));
            row.Children.Add(Labeled(Lang.T("To"),NotifyToBox));
            foreach(var kind in NotifyPrefs.Kinds)
            {
                var cb=new CheckBox{Template=PlainCheckTemplate(),Content=Lang.T(NotifyPrefs.Label(kind)),Foreground=Muted,Margin=new Thickness(0,6,10,0),Tag="notify:"+kind};
                System.Windows.Automation.AutomationProperties.SetName(cb,Lang.T("Email alert")+": "+Lang.T(NotifyPrefs.Label(kind)));
                NotifyKindBoxes[kind]=cb;row.Children.Add(cb);
            }
            row.Children.Add(TextToolFree(Lang.T("Save"),Lang.T("Save the email alert settings"),"notifySave",()=>{var cb=NotifySave;if(cb!=null)cb(NotifyDraft());}));
            row.Children.Add(TextToolFree(Lang.T("Send test"),Lang.T("Send one test mail now through NinjaTrader's default email share service"),"notifyTest",()=>{var cb=NotifyTest;if(cb!=null)cb(NotifyDraft());}));
            body.Children.Add(row);
            NotifyStatus=Text(Lang.T("Set up an email share service in NinjaTrader: Tools > Options > General > Share services. KINFX stores no password. Alerts never place, change or cancel orders."),11,Muted);NotifyStatus.TextWrapping=TextWrapping.Wrap;NotifyStatus.Margin=new Thickness(0,6,0,0);NotifyStatus.Tag="notifyStatus";
            body.Children.Add(NotifyStatus);
            box.Child=body;host.Children.Add(box);
            ShowNotify(NotifyPrefs.Defaults());
        }
        internal Action<string,bool> AskChanged;
        // ==== WP3 log page ====
        internal DataGrid LogGrid;
        internal ComboBox LogLevelBox, LogAccountBox;
        internal TextBox LogSearchBox;
        internal CheckBox LogPlainBox, LogAutoBox;
        internal int LogUnseen;
        internal Action ExportLog;   // tab writes the masked export file (allowed while attached)
        internal Action ClearQueue;  // tab clears the in-memory queue (Clear log button)
        readonly ObservableCollection<LogLine> logRows=new ObservableCollection<LogLine>();
        ICollectionView logView;
        TabItem logTab;
        // Column visibility. The tab owns the file; the view only applies a set and reports a user change.
        internal Action<HashSet<string>> ColumnsChanged;
        HashSet<string> hiddenColumns=new HashSet<string>(StringComparer.Ordinal);
        Panel columnsBarCopy, columnsBarRisk;
        internal HashSet<string> HiddenColumns{get{return new HashSet<string>(hiddenColumns,StringComparer.Ordinal);}}
        internal void ApplyColumns(HashSet<string> hidden)
        {
            hiddenColumns=new HashSet<string>((hidden??new HashSet<string>()).Where(x=>{int c=x.IndexOf(':');return c>0&&ColumnPrefs.CanHide(x.Substring(0,c),x.Substring(c+1));}),StringComparer.Ordinal);
            foreach(var pair in new[]{Tuple.Create(ColumnPrefs.Copy,AccountGrid),Tuple.Create(ColumnPrefs.Risk,RiskGrid)})
                if(pair.Item2!=null)foreach(var c in pair.Item2.Columns)c.Visibility=ColumnPrefs.Visible(pair.Item1,ColumnKey(c),hiddenColumns)?Visibility.Visible:Visibility.Collapsed;
        }
        internal void SetColumnVisible(string grid,string key,bool visible)
        {
            if(!ColumnPrefs.CanHide(grid,key))return;
            var next=HiddenColumns;string id=grid+":"+key;if(visible)next.Remove(id);else next.Add(id);
            ApplyColumns(next);var cb=ColumnsChanged;if(cb!=null)cb(HiddenColumns);
        }
        // One menu per grid: a tick per hideable column, plus "Show all". Opened from the Columns button and from a right-click on any column header.
        ContextMenu ColumnMenu(string grid,DataGrid g)
        {
            var menu=new ContextMenu();
            menu.Opened+=(s,e)=>
            {
                menu.Items.Clear();
                foreach(var c in g.Columns)
                {
                    string key=ColumnKey(c);if(!ColumnPrefs.CanHide(grid,key))continue;
                    var item=new MenuItem{Header=(c.Header as string)??key,IsCheckable=true,IsChecked=c.Visibility==Visibility.Visible,StaysOpenOnClick=true,Tag="col:"+grid+":"+key};
                    // Checked / Unchecked, not Click: keyboard, UI Automation Toggle and mouse all change IsChecked, but only the mouse raises Click.
                    string k=key;item.Checked+=(s2,e2)=>SetColumnVisible(grid,k,true);item.Unchecked+=(s2,e2)=>SetColumnVisible(grid,k,false);menu.Items.Add(item);
                }
                menu.Items.Add(new Separator());
                var all=new MenuItem{Header=Lang.T("Show all columns"),Tag="col:"+grid+":*"};
                all.Click+=(s2,e2)=>{var next=HiddenColumns;next.RemoveWhere(x=>x.StartsWith(grid+":",StringComparison.Ordinal));ApplyColumns(next);var cb=ColumnsChanged;if(cb!=null)cb(HiddenColumns);};
                menu.Items.Add(all);
                var note=new MenuItem{Header=Lang.T("Follow tick, state, Off / Flat, Qty and connection are always shown"),IsEnabled=false};menu.Items.Add(note);
            };
            return menu;
        }
        void AttachColumnMenu(string grid,DataGrid g,Panel bar)
        {
            var menu=ColumnMenu(grid,g);
            var hs=new Style(typeof(DataGridColumnHeader),g.ColumnHeaderStyle);hs.Setters.Add(new Setter(FrameworkElement.ContextMenuProperty,menu));g.ColumnHeaderStyle=hs;
            // Add to each column's own header style (keeps its tooltip setter first-level); wrap only a style that is already sealed.
            foreach(var c in g.Columns)if(c.HeaderStyle!=null){if(!c.HeaderStyle.IsSealed)c.HeaderStyle.Setters.Add(new Setter(FrameworkElement.ContextMenuProperty,menu));else{var own=new Style(typeof(DataGridColumnHeader),c.HeaderStyle);own.Setters.Add(new Setter(FrameworkElement.ContextMenuProperty,menu));c.HeaderStyle=own;}}
            Button b=null;b=Button(Lang.T("Columns"),()=>{menu.PlacementTarget=b;menu.Placement=PlacementMode.Bottom;menu.IsOpen=true;});
            b.FontSize=12;b.Height=30;b.MinWidth=0;b.Padding=new Thickness(9,0,9,0);b.Margin=new Thickness(8,0,4,6);b.Tag="columns:"+grid;
            string tip=Lang.T("Show or hide columns (also: right-click a column header). Saved for next time.");b.ToolTip=tip;System.Windows.Automation.AutomationProperties.SetName(b,tip);
            bar.Children.Add(b);
        }
        internal ContextMenu ColumnMenuFor(string grid){var g=grid==ColumnPrefs.Copy?AccountGrid:RiskGrid;var hs=g.Columns.Select(c=>c.HeaderStyle).FirstOrDefault(x=>x!=null);if(hs==null)return null;foreach(var st in hs.Setters.OfType<Setter>())if(st.Property==FrameworkElement.ContextMenuProperty)return st.Value as ContextMenu;var bs=hs.BasedOn;while(bs!=null){foreach(var st in bs.Setters.OfType<Setter>())if(st.Property==FrameworkElement.ContextMenuProperty)return st.Value as ContextMenu;bs=bs.BasedOn;}return null;}
        internal Action<int> AvgWarnChanged;
        internal TextBox AvgWarnBox;
        public int AvgWarnTicks { get; private set; } = DisplayPrefs.DefaultAvgWarnTicks;
        public void SetAvgWarnTicks(int ticks){int n;if(!DisplayPrefs.TryParseTicks(ticks.ToString(CultureInfo.InvariantCulture),out n))return;AvgWarnTicks=n;if(AvgWarnBox!=null)AvgWarnBox.Text=n.ToString(CultureInfo.InvariantCulture);}
        internal CheckBox AskBox(string kind){return kind==ConfirmPrefs.Start?AskStartBox:kind==ConfirmPrefs.FlatOne?AskFlatOneBox:kind==ConfirmPrefs.Flatten?AskFlattenBox:null;}
        readonly ICollectionView view;
        TextBox search;
        ComboBox filter;
        TextBlock summary,selection;TextBlock adviceLine,lockedLine;
        TextBlock cardCopying,cardLocked,cardUnprotected,cardPnl;
        StackPanel issuesLines;
        ComboBox bulkField,bulkValueCombo;TextBox bulkValue;
        readonly bool demo;
        readonly List<Button> editButtons=new List<Button>(); Panel bulkBar;
        bool editable=true;
        public string SearchText {get{return search.Text;}set{search.Text=value;}}
        public int VisibleCount {get{return view.Cast<AccountRow>().Count();}}
        public int LockedCount {get{return Rows.Count(r=>r.Role!="Leader"&&r.Latch.Locked);}}
        public string LockedText {get{int n=LockedCount;return n==0?"":Lang.F("● {0} LOCKED · check NinjaTrader, then review each reason",n);}}
        public int AdviceCount {get{return Rows.Count(r=>r.Role!="Leader"&&r.Advice.Length>0);}}
        public string AdviceText {get{int n=AdviceCount;return n==0?"":Lang.F("⚠ {0} account(s) with a settings note · hover State",n);}}
        static Brush Brush(string c){var b=(Brush)new BrushConverter().ConvertFromString(c);b.Freeze();return b;}
        public static readonly Brush Ink=Brush("#ECEAF1"),Muted=Brush("#AAA4B5"),Panel=Brush("#201E29"),Line=Brush("#35313F"),Accent=Brush("#D8C1A1"),Canvas=Brush("#17161F"),Good=Brush("#83C7A3"),Warn=Brush("#D8B87C"),Bad=Brush("#D98792"),Off=Brush("#00514C5C");
        static TextBlock Text(string s,double size=12,Brush color=null){return new TextBlock{Text=s,FontSize=size,Foreground=color??Ink,VerticalAlignment=VerticalAlignment.Center};}
        static Button Button(string s,Action action,bool primary=false)
        {
            var b=new Button{Style=new Style(typeof(Button)),MinWidth=0,MinHeight=0,FontSize=13,Content=s,Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,0,6,0),Background=primary?Accent:Panel,Foreground=primary?Canvas:Ink,BorderBrush=Line,Cursor=System.Windows.Input.Cursors.Hand};
            var t=new ControlTemplate(typeof(Button));var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(5));border.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));border.SetValue(Border.BorderBrushProperty,new TemplateBindingExtension(Control.BorderBrushProperty));border.SetValue(Border.BorderThicknessProperty,new Thickness(1));
            var content=new FrameworkElementFactory(typeof(ContentPresenter));content.SetValue(FrameworkElement.MarginProperty,new TemplateBindingExtension(Control.PaddingProperty));content.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);content.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);border.AppendChild(content);t.VisualTree=border;b.Template=t;
            b.MouseEnter+=(o,e)=>b.BorderBrush=Accent;b.MouseLeave+=(o,e)=>b.BorderBrush=Line;b.GotKeyboardFocus+=(o,e)=>b.BorderBrush=Accent;b.LostKeyboardFocus+=(o,e)=>b.BorderBrush=Line;b.IsEnabledChanged+=(o,e)=>b.Opacity=b.IsEnabled?1:0.45;b.Click+=(o,e)=>action();return b;
        }
        Button Icon(string glyph,string tip,string tag,Action action,bool edits=false)
        {
            var b=Button(glyph,action);b.FontFamily=new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");b.FontSize=15;b.Width=32;b.Height=30;b.Padding=new Thickness(0);b.ToolTip=tip;b.Tag=tag;
            System.Windows.Automation.AutomationProperties.SetName(b,tip);ToolTipService.SetShowOnDisabled(b,true);
            if(edits)editButtons.Add(b);return b;
        }
        // Text-labelled tool button for bulk actions: never reuses a glyph from Flatten / untick.
        Button TextTool(string label,string tip,string tag,Action action)
        {
            var b=Button(label,action);b.FontSize=12;b.Height=30;b.MinWidth=0;b.Padding=new Thickness(9,0,9,0);b.Margin=new Thickness(0,0,4,0);b.ToolTip=tip;b.Tag=tag;
            System.Windows.Automation.AutomationProperties.SetName(b,tip);ToolTipService.SetShowOnDisabled(b,true);editButtons.Add(b);return b;
        }
        // Text-labelled tool button that is NOT an edit button (stays enabled while a session is attached): log export.
        Button TextToolFree(string label,string tip,string tag,Action action)
        {
            var b=Button(label,action);b.FontSize=12;b.Height=30;b.MinWidth=0;b.Padding=new Thickness(9,0,9,0);b.Margin=new Thickness(0,0,4,0);b.ToolTip=tip;b.Tag=tag;
            System.Windows.Automation.AutomationProperties.SetName(b,tip);ToolTipService.SetShowOnDisabled(b,true);return b;
        }
        static TextBox Input(string text,double width){return new TextBox{Style=new Style(typeof(TextBox)),MinWidth=0,MinHeight=0,FontSize=13,Text=text,Width=width,Padding=new Thickness(8,5,8,5),Margin=new Thickness(0,0,8,0),Background=Panel,Foreground=Ink,BorderBrush=Line,VerticalContentAlignment=VerticalAlignment.Center};}
        // Flat dark template for the drop-down arrow. WPF's default template paints a white / light-blue fill on hover and when disabled; NinjaTrader's own controls never do.
        static ControlTemplate ComboToggleTemplate()
        {
            var t=new ControlTemplate(typeof(System.Windows.Controls.Primitives.ToggleButton));
            var b=new FrameworkElementFactory(typeof(Border),"Face");b.SetValue(Border.BackgroundProperty,Panel);b.SetValue(Border.BorderBrushProperty,Line);b.SetValue(Border.BorderThicknessProperty,new Thickness(1));
            var p=new FrameworkElementFactory(typeof(ContentPresenter));p.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Right);p.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);p.SetValue(FrameworkElement.MarginProperty,new Thickness(0,0,8,0));
            b.AppendChild(p);t.VisualTree=b;
            var hover=new Trigger{Property=UIElement.IsMouseOverProperty,Value=true};hover.Setters.Add(new Setter(Border.BorderBrushProperty,Accent,"Face"));t.Triggers.Add(hover);
            var off=new Trigger{Property=UIElement.IsEnabledProperty,Value=false};off.Setters.Add(new Setter(Control.ForegroundProperty,Muted));t.Triggers.Add(off);
            return t;
        }
        internal static ComboBox Combo(double width)
        {
            var c=new ComboBox{Style=new Style(typeof(ComboBox)),MinWidth=0,MinHeight=0,FontSize=13,Width=width,Margin=new Thickness(0,0,8,0),Padding=new Thickness(8,5,8,5),Background=Panel,Foreground=Ink,BorderBrush=Line};
            var t=new ControlTemplate(typeof(ComboBox));var g=new FrameworkElementFactory(typeof(Grid));
            var toggle=new FrameworkElementFactory(typeof(ToggleButton));toggle.SetValue(Control.BackgroundProperty,Panel);toggle.SetValue(Control.BorderBrushProperty,Line);toggle.SetValue(Control.ForegroundProperty,Ink);toggle.SetValue(ContentControl.ContentProperty,"▾");toggle.SetValue(Control.TemplateProperty,ComboToggleTemplate());toggle.SetValue(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Right);toggle.SetValue(Control.PaddingProperty,new Thickness(8,5,8,5));toggle.SetBinding(ToggleButton.IsCheckedProperty,new Binding("IsDropDownOpen"){RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent),Mode=BindingMode.TwoWay});g.AppendChild(toggle);
            var selected=new FrameworkElementFactory(typeof(ContentPresenter));selected.SetValue(FrameworkElement.MarginProperty,new Thickness(9,5,25,5));selected.SetValue(UIElement.IsHitTestVisibleProperty,false);selected.SetBinding(ContentPresenter.ContentProperty,new Binding("SelectionBoxItem"){RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)});g.AppendChild(selected);
            var popup=new FrameworkElementFactory(typeof(Popup),"PART_Popup");popup.SetValue(Popup.PlacementProperty,PlacementMode.Bottom);popup.SetValue(Popup.AllowsTransparencyProperty,true);popup.SetBinding(Popup.IsOpenProperty,new Binding("IsDropDownOpen"){RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)});
            var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.BackgroundProperty,Panel);border.SetValue(Border.BorderBrushProperty,Line);border.SetValue(Border.BorderThicknessProperty,new Thickness(1));border.SetBinding(FrameworkElement.MinWidthProperty,new Binding("ActualWidth"){RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)});var scroll=new FrameworkElementFactory(typeof(ScrollViewer));scroll.SetValue(FrameworkElement.MaxHeightProperty,300.0);scroll.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));border.AppendChild(scroll);popup.AppendChild(border);g.AppendChild(popup);t.VisualTree=g;c.Template=t;return c;
        }
        static Style TabStyle()
        {
            var s=new Style(typeof(TabItem));s.Setters.Add(new Setter(Control.ForegroundProperty,Muted));s.Setters.Add(new Setter(Control.BackgroundProperty,Canvas));s.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(18,9,18,9)));
            var t=new ControlTemplate(typeof(TabItem));var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));border.SetValue(Border.BorderBrushProperty,new TemplateBindingExtension(Control.BorderBrushProperty));border.SetValue(Border.BorderThicknessProperty,new Thickness(0,0,0,2));var p=new FrameworkElementFactory(typeof(ContentPresenter));p.SetValue(ContentPresenter.ContentSourceProperty,"Header");p.SetValue(FrameworkElement.MarginProperty,new TemplateBindingExtension(Control.PaddingProperty));border.AppendChild(p);t.VisualTree=border;s.Setters.Add(new Setter(Control.TemplateProperty,t));
            var selected=new Trigger{Property=TabItem.IsSelectedProperty,Value=true};selected.Setters.Add(new Setter(Control.ForegroundProperty,Ink));selected.Setters.Add(new Setter(Control.BorderBrushProperty,Accent));selected.Setters.Add(new Setter(Control.BackgroundProperty,Panel));s.Triggers.Add(selected);return s;
        }
        static DataGrid GridTable()
        {
            var g=new DataGrid{AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,SelectionMode=DataGridSelectionMode.Extended,SelectionUnit=DataGridSelectionUnit.FullRow,EnableRowVirtualization=true,EnableColumnVirtualization=true,RowHeight=28,HeadersVisibility=DataGridHeadersVisibility.Column,Background=Canvas,Foreground=Ink,BorderBrush=Line,GridLinesVisibility=DataGridGridLinesVisibility.Horizontal,HorizontalGridLinesBrush=Brush("#292632"),AlternatingRowBackground=Brush("#1D1B25"),RowBackground=Canvas,FontSize=13,CanUserSortColumns=true,FrozenColumnCount=2};
            VirtualizingPanel.SetVirtualizationMode(g,VirtualizationMode.Recycling);ScrollViewer.SetCanContentScroll(g,true);VirtualizingPanel.SetIsVirtualizingWhenGrouping(g,true);
            var cell=new Style(typeof(DataGridCell));cell.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0)));cell.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(9,0,9,0)));cell.Setters.Add(new Setter(Control.ForegroundProperty,Ink));var sel=new Trigger{Property=DataGridCell.IsSelectedProperty,Value=true};sel.Setters.Add(new Setter(Control.BackgroundProperty,Brush("#383043")));sel.Setters.Add(new Setter(Control.ForegroundProperty,Ink));cell.Triggers.Add(sel);g.CellStyle=cell;
            var row=new Style(typeof(DataGridRow));row.Setters.Add(new Setter(Control.ForegroundProperty,Ink));var hover=new Trigger{Property=UIElement.IsMouseOverProperty,Value=true};hover.Setters.Add(new Setter(Control.BackgroundProperty,Panel));row.Triggers.Add(hover);g.RowStyle=row;
            var h=new Style(typeof(DataGridColumnHeader));h.Setters.Add(new Setter(Control.BackgroundProperty,Panel));h.Setters.Add(new Setter(Control.ForegroundProperty,Muted));h.Setters.Add(new Setter(Control.FontSizeProperty,12.0));h.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(9,8,9,8)));h.Setters.Add(new Setter(Control.BorderBrushProperty,Line));h.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0,0,0,1)));g.ColumnHeaderStyle=h;
            var edit=new Style(typeof(TextBox));edit.Setters.Add(new Setter(Control.BackgroundProperty,Panel));edit.Setters.Add(new Setter(Control.ForegroundProperty,Ink));edit.Setters.Add(new Setter(Control.BorderBrushProperty,Accent));g.Resources[typeof(TextBox)]=edit;return g;
        }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DataGridColumn,string> ColumnKeys=new System.Runtime.CompilerServices.ConditionalWeakTable<DataGridColumn,string>();
        // English key of a column header: widths and tests use it, so switching language cannot break layout.
        internal static string ColumnKey(DataGridColumn c){string k;return ColumnKeys.TryGetValue(c,out k)?k:(c.Header as string)??"";}
        static void Key(DataGridColumn c,string english){string old;if(ColumnKeys.TryGetValue(c,out old))ColumnKeys.Remove(c);ColumnKeys.Add(c,english);}
        static void Header(DataGrid g,DataGridColumn c,string tip){var h=new Style(typeof(DataGridColumnHeader),g.ColumnHeaderStyle);h.Setters.Add(new Setter(FrameworkElement.ToolTipProperty,tip));c.HeaderStyle=h;}
        static void Column(DataGrid g,string title,string path,double width,bool ro,string tip,string format=null,bool number=false)
        {
            var c=new DataGridTextColumn{Header=Lang.T(title),SortMemberPath=path.EndsWith("Text")?path.Substring(0,path.Length-4):path,Binding=new Binding(path){Mode=ro?BindingMode.OneWay:BindingMode.TwoWay,StringFormat=format,UpdateSourceTrigger=UpdateSourceTrigger.LostFocus,ValidatesOnExceptions=!ro,ConverterCulture=CultureInfo.InvariantCulture},Width=width,IsReadOnly=ro};
            var s=new Style(typeof(TextBlock));s.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center));s.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(9,0,9,0)));if(number){var hs=new Style(typeof(DataGridColumnHeader),g.ColumnHeaderStyle);hs.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Right));g.Resources["NumericHeader"]=hs;s.Setters.Add(new Setter(TextBlock.TextAlignmentProperty,TextAlignment.Right));s.Setters.Add(new Setter(TextBlock.FontFamilyProperty,new FontFamily("Consolas")));}if(path=="Pnl")PnlColors(s);if(path=="Alias")s.Setters.Add(new Setter(FrameworkElement.ToolTipProperty,new Binding("Name")));c.ElementStyle=s;Key(c,title);Header(g,c,tip);if(number){var hs=new Style(typeof(DataGridColumnHeader),c.HeaderStyle);hs.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Right));c.HeaderStyle=hs;}g.Columns.Add(c);
        }
        // P/L text colour (user request): profit green, loss red, zero / no data muted. Same theme colours as the state lamps.
        internal static void PnlColors(Style s)
        {
            s.Setters.Add(new Setter(TextBlock.ForegroundProperty,Muted));
            var up=new DataTrigger{Binding=new Binding("PnlSign"),Value=1};up.Setters.Add(new Setter(TextBlock.ForegroundProperty,Good));s.Triggers.Add(up);
            var down=new DataTrigger{Binding=new Binding("PnlSign"),Value=-1};down.Setters.Add(new Setter(TextBlock.ForegroundProperty,Bad));s.Triggers.Add(down);
        }
        static void RatioColumn(DataGrid g)
        {
            var input=new FrameworkElementFactory(typeof(TextBox));input.SetValue(FrameworkElement.TagProperty,"rowRatio");input.SetValue(Control.BackgroundProperty,Panel);input.SetValue(Control.ForegroundProperty,Ink);input.SetValue(Control.BorderBrushProperty,Line);input.SetValue(Control.BorderThicknessProperty,new Thickness(1));input.SetValue(Control.PaddingProperty,new Thickness(4,1,4,1));input.SetValue(FrameworkElement.MarginProperty,new Thickness(4,1,4,1));input.SetValue(FrameworkElement.MinWidthProperty,0.0);input.SetValue(Control.FontSizeProperty,13.0);input.SetValue(TextBox.TextAlignmentProperty,TextAlignment.Right);input.SetValue(Control.VerticalContentAlignmentProperty,VerticalAlignment.Center);
            string tip=Lang.T("Ratio for THIS account only · finite > 0 · follower qty = floor(leader qty × ratio) · locked while attached");
            input.SetValue(FrameworkElement.ToolTipProperty,tip);input.SetValue(System.Windows.Automation.AutomationProperties.NameProperty,tip);
            input.SetBinding(TextBox.TextProperty,new Binding("RatioText"){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.LostFocus,ValidatesOnExceptions=true});
            var style=new Style(typeof(TextBox));var locked=new DataTrigger{Binding=new Binding("IsReadOnly"){RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(DataGrid),1)},Value=true};locked.Setters.Add(new Setter(UIElement.IsEnabledProperty,false));style.Triggers.Add(locked);input.SetValue(FrameworkElement.StyleProperty,style);
            var c=new DataGridTemplateColumn{Header=Lang.T("Ratio"),SortMemberPath="Ratio",Width=65,CellTemplate=new DataTemplate{VisualTree=input}};Header(g,c,tip);Key(c,"Ratio");g.Columns.Add(c);
        }
        // Flat dark template for ordinary labelled check boxes (Live (RO), Grp, Dry run, dialog options). WPF's default draws a pure-white square.
        internal static ControlTemplate PlainCheckTemplate()
        {
            var t=new ControlTemplate(typeof(CheckBox));var row=new FrameworkElementFactory(typeof(StackPanel));row.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal);row.SetValue(System.Windows.Controls.Panel.BackgroundProperty,Brush("#00000000"));
            var box=new FrameworkElementFactory(typeof(Border),"Box");box.SetValue(FrameworkElement.WidthProperty,15.0);box.SetValue(FrameworkElement.HeightProperty,15.0);box.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);box.SetValue(Border.CornerRadiusProperty,new CornerRadius(3));box.SetValue(Border.BackgroundProperty,Canvas);box.SetValue(Border.BorderBrushProperty,Muted);box.SetValue(Border.BorderThicknessProperty,new Thickness(1));
            var mark=new FrameworkElementFactory(typeof(TextBlock),"Mark");mark.SetValue(TextBlock.TextProperty,"✓");mark.SetValue(TextBlock.FontSizeProperty,11.0);mark.SetValue(TextBlock.ForegroundProperty,Canvas);mark.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);mark.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);mark.SetValue(UIElement.VisibilityProperty,Visibility.Hidden);box.AppendChild(mark);row.AppendChild(box);
            var text=new FrameworkElementFactory(typeof(ContentPresenter));text.SetValue(FrameworkElement.MarginProperty,new Thickness(7,0,0,0));text.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);text.SetValue(ContentPresenter.RecognizesAccessKeyProperty,true);row.AppendChild(text);t.VisualTree=row;
            var on=new Trigger{Property=ToggleButton.IsCheckedProperty,Value=true};on.Setters.Add(new Setter(Border.BackgroundProperty,Accent,"Box"));on.Setters.Add(new Setter(Border.BorderBrushProperty,Accent,"Box"));on.Setters.Add(new Setter(UIElement.VisibilityProperty,Visibility.Visible,"Mark"));t.Triggers.Add(on);
            var hover=new Trigger{Property=UIElement.IsMouseOverProperty,Value=true};hover.Setters.Add(new Setter(Border.BorderBrushProperty,Accent,"Box"));t.Triggers.Add(hover);
            var focus=new Trigger{Property=UIElement.IsKeyboardFocusedProperty,Value=true};focus.Setters.Add(new Setter(Border.BorderBrushProperty,Accent,"Box"));t.Triggers.Add(focus);
            var off=new Trigger{Property=UIElement.IsEnabledProperty,Value=false};off.Setters.Add(new Setter(UIElement.OpacityProperty,0.45));t.Triggers.Add(off);
            return t;
        }
        static ControlTemplate FollowTemplate()
        {
            var t=new ControlTemplate(typeof(CheckBox));var hit=new FrameworkElementFactory(typeof(Grid));hit.SetValue(FrameworkElement.WidthProperty,32.0);hit.SetValue(FrameworkElement.HeightProperty,26.0);hit.SetValue(System.Windows.Controls.Panel.BackgroundProperty,Brush("#00000000"));var box=new FrameworkElementFactory(typeof(Border),"TickBox");box.SetValue(FrameworkElement.WidthProperty,17.0);box.SetValue(FrameworkElement.HeightProperty,17.0);box.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);box.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);box.SetValue(Border.CornerRadiusProperty,new CornerRadius(4));box.SetValue(Border.BackgroundProperty,Canvas);box.SetValue(Border.BorderBrushProperty,Muted);box.SetValue(Border.BorderThicknessProperty,new Thickness(1));var mark=new FrameworkElementFactory(typeof(TextBlock),"Mark");mark.SetValue(TextBlock.TextProperty,"✓");mark.SetValue(TextBlock.FontSizeProperty,12.0);mark.SetValue(TextBlock.ForegroundProperty,Canvas);mark.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);mark.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);mark.SetValue(UIElement.VisibilityProperty,Visibility.Hidden);box.AppendChild(mark);hit.AppendChild(box);t.VisualTree=hit;var on=new Trigger{Property=ToggleButton.IsCheckedProperty,Value=true};on.Setters.Add(new Setter(Border.BackgroundProperty,Accent,"TickBox"));on.Setters.Add(new Setter(Border.BorderBrushProperty,Accent,"TickBox"));on.Setters.Add(new Setter(UIElement.VisibilityProperty,Visibility.Visible,"Mark"));t.Triggers.Add(on);var disabled=new Trigger{Property=UIElement.IsEnabledProperty,Value=false};disabled.Setters.Add(new Setter(UIElement.OpacityProperty,0.45,"TickBox"));t.Triggers.Add(disabled);var focus=new Trigger{Property=UIElement.IsKeyboardFocusedProperty,Value=true};focus.Setters.Add(new Setter(Border.BorderBrushProperty,Ink,"TickBox"));focus.Setters.Add(new Setter(Border.BorderThicknessProperty,new Thickness(2),"TickBox"));t.Triggers.Add(focus);return t;
        }
        static void TickColumn(DataGrid g)
        {
            var tick=new FrameworkElementFactory(typeof(CheckBox));tick.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);tick.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);tick.SetValue(FrameworkElement.TagProperty,"follow");tick.SetValue(Control.TemplateProperty,FollowTemplate());tick.SetValue(System.Windows.Automation.AutomationProperties.NameProperty,Lang.T("Follow account; untick stops new entries"));tick.SetValue(FrameworkElement.ToolTipProperty,Lang.T("Explicit follow / OFF · pending cancels and latch may block re-enable"));tick.SetBinding(ToggleButton.IsCheckedProperty,new Binding("Enabled"){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});var style=new Style(typeof(CheckBox));style.Setters.Add(new Setter(UIElement.IsEnabledProperty,new Binding("CanFollow")));tick.SetValue(FrameworkElement.StyleProperty,style);
            var c=new DataGridTemplateColumn{Header=Lang.T("On"),Width=40,CellTemplate=new DataTemplate{VisualTree=tick},SortMemberPath="Enabled"};Header(g,c,Lang.T("Follower enabled · one click"));Key(c,"On");g.Columns.Add(c);
        }
        static void AccountColumn(DataGrid g)
        {
            var line=new FrameworkElementFactory(typeof(DockPanel));line.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);line.SetValue(FrameworkElement.MarginProperty,new Thickness(9,0,9,0));line.SetBinding(FrameworkElement.ToolTipProperty,new Binding("Name"));line.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty,new Binding("Name"));var suffix=new FrameworkElementFactory(typeof(TextBlock));suffix.SetValue(DockPanel.DockProperty,Dock.Right);suffix.SetBinding(TextBlock.TextProperty,new Binding("AccountSuffix"){Mode=BindingMode.OneWay});suffix.SetValue(TextBlock.ForegroundProperty,Ink);suffix.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);line.AppendChild(suffix);var prefix=new FrameworkElementFactory(typeof(TextBlock));prefix.SetBinding(TextBlock.TextProperty,new Binding("AccountPrefix"){Mode=BindingMode.OneWay});prefix.SetValue(TextBlock.ForegroundProperty,Muted);prefix.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);line.AppendChild(prefix);var c=new DataGridTemplateColumn{Header=Lang.T("Acct"),Width=new DataGridLength(1,DataGridLengthUnitType.Star),MinWidth=175,SortMemberPath="Name",IsReadOnly=true,CellTemplate=new DataTemplate{VisualTree=line}};Header(g,c,Lang.T("Exact broker identity · full name on hover · differing suffix emphasized"));Key(c,"Acct");g.Columns.Add(c);
        }
        static void StateColumn(DataGrid g)
        {
            var border=new FrameworkElementFactory(typeof(Border));border.SetValue(FrameworkElement.MarginProperty,new Thickness(5,4,5,4));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(4));border.SetValue(Border.BackgroundProperty,Panel);border.SetBinding(FrameworkElement.ToolTipProperty,new Binding("StateDetail"));var text=new FrameworkElementFactory(typeof(TextBlock));text.SetBinding(TextBlock.TextProperty,new Binding("StateText"));text.SetBinding(TextBlock.ForegroundProperty,new Binding("StateInk"));text.SetValue(TextBlock.FontSizeProperty,11.0);text.SetValue(TextBlock.FontWeightProperty,FontWeights.Medium);text.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);text.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);border.AppendChild(text);var c=new DataGridTemplateColumn{Header=Lang.T("State"),Width=96,IsReadOnly=true,SortMemberPath="StateLabel",CellTemplate=new DataTemplate{VisualTree=border}};Header(g,c,Lang.T("Observed copy state, NOT connection or follower selection"));Key(c,"State");g.Columns.Add(c);
        }
        static ControlTemplate AtmTemplate()
        {
            var t=new ControlTemplate(typeof(CheckBox));var border=new FrameworkElementFactory(typeof(Border),"Switch");border.SetValue(Border.CornerRadiusProperty,new CornerRadius(4));border.SetValue(Border.BackgroundProperty,Canvas);border.SetValue(Border.BorderBrushProperty,Line);border.SetValue(Border.BorderThicknessProperty,new Thickness(1));border.SetValue(FrameworkElement.WidthProperty,38.0);border.SetValue(FrameworkElement.HeightProperty,20.0);var text=new FrameworkElementFactory(typeof(TextBlock),"Label");text.SetValue(TextBlock.TextProperty,Lang.T("Off"));text.SetValue(TextBlock.FontSizeProperty,11.0);text.SetValue(TextBlock.ForegroundProperty,Warn);text.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);text.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);border.AppendChild(text);t.VisualTree=border;var on=new Trigger{Property=ToggleButton.IsCheckedProperty,Value=true};on.Setters.Add(new Setter(TextBlock.TextProperty,Lang.T("On"),"Label"));on.Setters.Add(new Setter(TextBlock.ForegroundProperty,Ink,"Label"));on.Setters.Add(new Setter(Border.BackgroundProperty,Panel,"Switch"));t.Triggers.Add(on);var focus=new Trigger{Property=UIElement.IsKeyboardFocusedProperty,Value=true};focus.Setters.Add(new Setter(Border.BorderBrushProperty,Accent,"Switch"));t.Triggers.Add(focus);var disabled=new Trigger{Property=UIElement.IsEnabledProperty,Value=false};disabled.Setters.Add(new Setter(UIElement.OpacityProperty,0.45,"Switch"));t.Triggers.Add(disabled);return t;
        }
        // Per-account "flatten once when Tgt / DLL is hit". Same disabled/locked rules as the ATM tick.
        static void FlatOnLimitColumn(DataGrid g)
        {
            var tick=new FrameworkElementFactory(typeof(CheckBox));tick.SetValue(FrameworkElement.TagProperty,"limitFlat");tick.SetValue(Control.TemplateProperty,AtmTemplate());
            string tip=Lang.T("Flat at limit: when this account hits its Tgt or DLL, flatten it ONCE (never retried). Off = only lock and stop copying.");
            tick.SetValue(System.Windows.Automation.AutomationProperties.NameProperty,tip);tick.SetValue(FrameworkElement.ToolTipProperty,tip);
            tick.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);tick.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
            tick.SetBinding(ToggleButton.IsCheckedProperty,new Binding("FlatOnLimit"){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});
            var style=new Style(typeof(CheckBox));style.Setters.Add(new Setter(UIElement.IsEnabledProperty,new Binding("CanFollow")));
            var locked=new DataTrigger{Binding=new Binding("IsReadOnly"){RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(DataGrid),1)},Value=true};locked.Setters.Add(new Setter(UIElement.IsEnabledProperty,false));style.Triggers.Add(locked);
            tick.SetValue(FrameworkElement.StyleProperty,style);
            var c=new DataGridTemplateColumn{Header=Lang.T("Flat@Lim"),Width=74,SortMemberPath="FlatOnLimit",CellTemplate=new DataTemplate{VisualTree=tick}};Header(g,c,tip);Key(c,"FlatLim");g.Columns.Add(c);
        }
        static void AutoRefollowColumn(DataGrid g)
        {
            var tick=new FrameworkElementFactory(typeof(CheckBox));tick.SetValue(FrameworkElement.TagProperty,"autoRefollow");tick.SetValue(Control.TemplateProperty,AtmTemplate());
            string tip=Lang.T("Auto re-follow: after this account's copied exit completes and the leader is flat, tick it again automatically. Only later new entries are copied. Off = re-tick by hand.");
            tick.SetValue(System.Windows.Automation.AutomationProperties.NameProperty,tip);tick.SetValue(FrameworkElement.ToolTipProperty,tip);
            tick.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);tick.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
            tick.SetBinding(ToggleButton.IsCheckedProperty,new Binding("AutoRefollow"){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});
            var style=new Style(typeof(CheckBox));style.Setters.Add(new Setter(UIElement.IsEnabledProperty,new Binding("CanFollow")));
            var locked=new DataTrigger{Binding=new Binding("IsReadOnly"){RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(DataGrid),1)},Value=true};locked.Setters.Add(new Setter(UIElement.IsEnabledProperty,false));style.Triggers.Add(locked);
            tick.SetValue(FrameworkElement.StyleProperty,style);
            var c=new DataGridTemplateColumn{Header=Lang.T("Re-follow"),Width=74,SortMemberPath="AutoRefollow",CellTemplate=new DataTemplate{VisualTree=tick}};Header(g,c,tip);Key(c,"AutoRefollow");g.Columns.Add(c);
        }
        static void AtmColumn(DataGrid g)
        {
            var tick=new FrameworkElementFactory(typeof(CheckBox));tick.SetValue(FrameworkElement.TagProperty,"atm");tick.SetValue(Control.TemplateProperty,AtmTemplate());tick.SetValue(System.Windows.Automation.AutomationProperties.NameProperty,Lang.T("Per-account ATM exit mirroring"));tick.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);tick.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);tick.SetValue(FrameworkElement.ToolTipProperty,Lang.T("Per-account ATM exit mirroring, NOT a native ATM clone. OFF ignores non-market SL / TP: follower can be unprotected. Market closes still copy. Frozen while attached."));tick.SetBinding(ToggleButton.IsCheckedProperty,new Binding("CopyAtm"){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});var style=new Style(typeof(CheckBox));style.Setters.Add(new Setter(UIElement.IsEnabledProperty,new Binding("CanFollow")));var locked=new DataTrigger{Binding=new Binding("IsReadOnly"){RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(DataGrid),1)},Value=true};locked.Setters.Add(new Setter(UIElement.IsEnabledProperty,false));style.Triggers.Add(locked);tick.SetValue(FrameworkElement.StyleProperty,style);var c=new DataGridTemplateColumn{Header=Lang.T("ATM"),Width=56,SortMemberPath="CopyAtm",CellTemplate=new DataTemplate{VisualTree=tick}};Header(g,c,Lang.T("Independent ATM exit mirror · OFF can leave exposure unprotected"));Key(c,"ATM");g.Columns.Add(c);
        }
        static ControlTemplate OffTemplate()
        {
            var t=new ControlTemplate(typeof(Button));var border=new FrameworkElementFactory(typeof(Border),"Power");border.SetValue(Border.CornerRadiusProperty,new CornerRadius(4));border.SetValue(Border.BackgroundProperty,Panel);border.SetValue(Border.BorderThicknessProperty,new Thickness(1));border.SetValue(Border.BorderBrushProperty,Line);border.SetValue(FrameworkElement.WidthProperty,28.0);border.SetValue(FrameworkElement.HeightProperty,22.0);var p=new FrameworkElementFactory(typeof(ContentPresenter));p.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);p.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);border.AppendChild(p);t.VisualTree=border;var disabled=new Trigger{Property=UIElement.IsEnabledProperty,Value=false};disabled.Setters.Add(new Setter(UIElement.OpacityProperty,0.35,"Power"));t.Triggers.Add(disabled);var hover=new Trigger{Property=UIElement.IsMouseOverProperty,Value=true};hover.Setters.Add(new Setter(Border.BorderBrushProperty,Bad,"Power"));t.Triggers.Add(hover);var focus=new Trigger{Property=UIElement.IsKeyboardFocusedProperty,Value=true};focus.Setters.Add(new Setter(Border.BorderBrushProperty,Accent,"Power"));t.Triggers.Add(focus);return t;
        }
        static void OffColumn(DataGrid g)
        {
            var button=new FrameworkElementFactory(typeof(Button));CompactCellButton(button,32);button.SetValue(ContentControl.ContentProperty,"\uE7E8");button.SetValue(FrameworkElement.TagProperty,"off");button.SetValue(Control.FontFamilyProperty,new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"));button.SetValue(Control.BackgroundProperty,Panel);button.SetValue(Control.ForegroundProperty,Bad);button.SetValue(Control.BorderBrushProperty,Line);button.SetValue(Control.PaddingProperty,new Thickness(4,1,4,1));button.SetValue(Control.TemplateProperty,OffTemplate());button.SetValue(FrameworkElement.ToolTipProperty,Lang.T("OFF: block new entries / request owned entry cancellation; retain exits. Pending fills may still occur. Explicit follow required again; latch remains."));button.SetValue(System.Windows.Automation.AutomationProperties.NameProperty,Lang.T("Stop following this account"));button.SetBinding(UIElement.IsEnabledProperty,new Binding("CanFollow"));button.AddHandler(ButtonBase.ClickEvent,new RoutedEventHandler((s,e)=>{var r=(s as FrameworkElement).DataContext as AccountRow;if(r!=null)r.Enabled=false;e.Handled=true;}));var c=new DataGridTemplateColumn{Header=Lang.T("Off"),Width=42,IsReadOnly=true,CellTemplate=new DataTemplate{VisualTree=button}};Header(g,c,Lang.T("One-click OFF is not Flatten; current positions / SL / TP remain"));Key(c,"Off");g.Columns.Add(c);
        }
        void FlatColumn(DataGrid g)
        {
            var button=new FrameworkElementFactory(typeof(Button));CompactCellButton(button,44);
            button.SetValue(ContentControl.ContentProperty,Lang.T("Flat"));button.SetValue(FrameworkElement.TagProperty,"flat");
            button.SetValue(Control.BackgroundProperty,Panel);button.SetValue(Control.ForegroundProperty,Bad);button.SetValue(Control.BorderBrushProperty,Line);button.SetValue(Control.FontSizeProperty,11.0);button.SetValue(Control.PaddingProperty,new Thickness(2,1,2,1));
            button.SetValue(Control.TemplateProperty,FlatTemplate());
            button.SetValue(FrameworkElement.ToolTipProperty,Lang.T("FLAT this account only: cancels its working orders and closes its position in the session instrument after you confirm. Other accounts keep copying. The account goes OFF; re-follow explicitly. Sent once, never retried."));
            button.SetValue(System.Windows.Automation.AutomationProperties.NameProperty,Lang.T("Flatten this account only"));
            button.SetBinding(UIElement.IsEnabledProperty,new Binding("CanFlat"));
            button.AddHandler(ButtonBase.ClickEvent,new RoutedEventHandler((s,e)=>{e.Handled=true;if(demo)return;var r=(s as FrameworkElement).DataContext as AccountRow;var cb=FlatRow;if(r!=null&&r.CanFlat&&cb!=null)cb(r);}));
            var c=new DataGridTemplateColumn{Header=Lang.T("Flat"),Width=52,IsReadOnly=true,CellTemplate=new DataTemplate{VisualTree=button}};
            Header(g,c,Lang.T("Flatten ONE account (confirmation names it). Not the same as OFF: OFF keeps the position, Flat closes it."));Key(c,"Flat");g.Columns.Add(c);
        }
        static void DriftColumn(DataGrid g)
        {
            var text=new FrameworkElementFactory(typeof(TextBlock));
            text.SetValue(FrameworkElement.MarginProperty,new Thickness(9,0,9,0));text.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
            text.SetValue(TextBlock.FontFamilyProperty,new FontFamily("Consolas"));text.SetValue(TextBlock.FontSizeProperty,12.0);text.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);
            text.SetBinding(TextBlock.TextProperty,new Binding("ConsistencyBadgeShown"){Mode=BindingMode.OneWay});
            text.SetBinding(TextBlock.ForegroundProperty,new Binding("ConsistencyInk"){Mode=BindingMode.OneWay});
            text.SetBinding(FrameworkElement.ToolTipProperty,new Binding("ConsistencyTip"){Mode=BindingMode.OneWay});
            text.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty,new Binding("ConsistencyTip"){Mode=BindingMode.OneWay});
            var c=new DataGridTemplateColumn{Header=Lang.T(PositionConsistency.ColumnHeader),Width=130,IsReadOnly=true,SortMemberPath="ConsistencyBadge",CellTemplate=new DataTemplate{VisualTree=text}};
            Header(g,c,Lang.T(PositionConsistency.Disclaimer));Key(c,"Drift");g.Columns.Add(c);
        }
        StackPanel AvgWarnControl()
        {
            var panel=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(8,0,8,6)};
            panel.Children.Add(Text(Lang.T(PositionConsistency.WarnLabel)+"  ",11,Muted));
            AvgWarnBox=Input(AvgWarnTicks.ToString(CultureInfo.InvariantCulture),46);
            AvgWarnBox.ToolTip=Lang.T(PositionConsistency.WarnTip);AvgWarnBox.Tag="avgWarn";
            System.Windows.Automation.AutomationProperties.SetName(AvgWarnBox,Lang.T(PositionConsistency.WarnTip));
            AvgWarnBox.LostKeyboardFocus+=(s,e)=>CommitAvgWarn();
            AvgWarnBox.KeyDown+=(s,e)=>{if(e.Key==System.Windows.Input.Key.Enter){CommitAvgWarn();e.Handled=true;}};
            panel.Children.Add(AvgWarnBox);panel.Children.Add(Text("  "+Lang.T(PositionConsistency.WarnSuffix),11,Muted));return panel;
        }
        internal void CommitAvgWarn()
        {
            if(AvgWarnBox==null)return;
            int n;
            if(!DisplayPrefs.TryParseTicks(AvgWarnBox.Text,out n))
            {
                AvgWarnBox.Text=AvgWarnTicks.ToString(CultureInfo.InvariantCulture);
                var note=BulkNotice;if(note!=null)note(PositionConsistency.WarnInvalid);return;
            }
            if(n==AvgWarnTicks)return;
            AvgWarnTicks=n;var changed=AvgWarnChanged;if(changed!=null)changed(n);
        }
        static ControlTemplate FlatTemplate()
        {
            var t=new ControlTemplate(typeof(Button));var border=new FrameworkElementFactory(typeof(Border),"Box");border.SetValue(Border.CornerRadiusProperty,new CornerRadius(4));border.SetValue(Border.BackgroundProperty,Panel);border.SetValue(Border.BorderThicknessProperty,new Thickness(1));border.SetValue(Border.BorderBrushProperty,Line);border.SetValue(FrameworkElement.WidthProperty,42.0);border.SetValue(FrameworkElement.HeightProperty,22.0);
            var p=new FrameworkElementFactory(typeof(ContentPresenter));p.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);p.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);border.AppendChild(p);t.VisualTree=border;
            var disabled=new Trigger{Property=UIElement.IsEnabledProperty,Value=false};disabled.Setters.Add(new Setter(UIElement.OpacityProperty,0.35,"Box"));t.Triggers.Add(disabled);
            var hover=new Trigger{Property=UIElement.IsMouseOverProperty,Value=true};hover.Setters.Add(new Setter(Border.BorderBrushProperty,Bad,"Box"));t.Triggers.Add(hover);
            var focus=new Trigger{Property=UIElement.IsKeyboardFocusedProperty,Value=true};focus.Setters.Add(new Setter(Border.BorderBrushProperty,Accent,"Box"));t.Triggers.Add(focus);return t;
        }
        static void LightColumn(DataGrid g,string title,string path,string detail)
        {
            var light=new FrameworkElementFactory(typeof(System.Windows.Shapes.Ellipse));light.SetValue(FrameworkElement.WidthProperty,8.0);light.SetValue(FrameworkElement.HeightProperty,8.0);light.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);light.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);light.SetBinding(System.Windows.Shapes.Shape.FillProperty,new Binding(path));light.SetBinding(FrameworkElement.ToolTipProperty,new Binding(detail));var c=new DataGridTemplateColumn{Header=Lang.T(title),Width=54,IsReadOnly=true,SortMemberPath=detail,CellTemplate=new DataTemplate{VisualTree=light}};Key(c,title);Header(g,c,detail+Lang.T(" · green / amber / red / off"));g.Columns.Add(c);
        }
        // Narrow text column: long values end in "…" and the whole text is the cell tooltip, so nothing is lost.
        static void FullTextTip(DataGrid g,string path)
        {
            var c=(DataGridTextColumn)g.Columns.Last();
            c.ElementStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis));
            c.ElementStyle.Setters.Add(new Setter(FrameworkElement.ToolTipProperty,new Binding(path){Mode=BindingMode.OneWay}));
        }
        static void ProportionalCopyColumns(DataGrid g)
        {
            // Star allocation across ALL data columns; fixed toggles/status lamps never stretch.
            // Minimums are the worst-case content widths measured from the real window (Bal "-$1,250,000.00" ~103 px now that the currency is a symbol, State "Rechecking" ~87 px, P/L "-123,456.78" ~97 px).
            // Extra width goes to the identity columns (Acct, Alias) that actually fill up, not evenly to short numeric columns. Weights are relative star sizes.
            var minimums=new Dictionary<string,double>{{"Acct",150},{"Alias",95},{"Ratio",60},{"Tgt",75},{"DLL",75},{"Qty",50},{"P/L",100},{"Bal",105},{"State",90}};
            var weights=new Dictionary<string,double>{{"Acct",5},{"Alias",3},{"Ratio",1},{"Tgt",1},{"DLL",1},{"Qty",1},{"P/L",1},{"Bal",1},{"State",1}};
            foreach(var c in g.Columns)
            {
                double minimum;
                if(minimums.TryGetValue(ColumnKey(c),out minimum)){c.MinWidth=minimum;c.Width=new DataGridLength(minimum);}
                else {double width=c.Width.Value;c.MinWidth=width;c.MaxWidth=width;}
            }
            g.HorizontalScrollBarVisibility=ScrollBarVisibility.Auto;
            // Widths are computed from the real viewport instead of WPF star sizing: with frozen columns the star pass overshot
            // by ~10 px, so on real NT at 1160 px the Flat buttons were cut off and a horizontal scrollbar showed.
            // Refit only when the viewport width or the visible column set changes (column menu hide/show).
            // Column virtualization off for this table: it has 14 columns that always fit, and with it on a width refit left some cells unrealized.
            g.EnableColumnVirtualization=false;
            // Refit when the viewport width or the visible column set changes (ScrollChanged reports both), not on every layout pass.
            double lastViewport=-1;string lastVisible="";
            g.AddHandler(ScrollViewer.ScrollChangedEvent,new ScrollChangedEventHandler((s,e)=>
            {
                var sv=e.OriginalSource as ScrollViewer;if(sv==null||sv.TemplatedParent!=g)return;
                double viewport=sv.ViewportWidth;if(viewport<=0)return;
                var visible=g.Columns.Where(c=>c.Visibility==Visibility.Visible).ToArray();
                string sig=string.Join("|",visible.Select(ColumnKey));
                if(Math.Abs(viewport-lastViewport)<0.5&&sig==lastVisible)return;
                lastViewport=viewport;lastVisible=sig;
                var flex=visible.Where(c=>minimums.ContainsKey(ColumnKey(c))).ToArray();
                double fixedWidth=visible.Where(c=>!minimums.ContainsKey(ColumnKey(c))).Sum(c=>c.Width.Value);
                double extra=Math.Floor(viewport-1-fixedWidth-flex.Sum(c=>minimums[ColumnKey(c)]));
                double weight=flex.Sum(c=>weights[ColumnKey(c)]);
                foreach(var c in flex){string k=ColumnKey(c);double w=minimums[k]+(extra>0&&weight>0?Math.Floor(extra*weights[k]/weight):0);if(Math.Abs(c.Width.Value-w)>0.5)c.Width=new DataGridLength(w);}
            }));
        }
        static void CompactCellButton(FrameworkElementFactory button,double width)
        {
            button.SetValue(FrameworkElement.StyleProperty,new Style(typeof(Button)));
            button.SetValue(FrameworkElement.MinWidthProperty,0.0);button.SetValue(FrameworkElement.MinHeightProperty,0.0);
            button.SetValue(FrameworkElement.WidthProperty,width);button.SetValue(FrameworkElement.HeightProperty,26.0);
            button.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);
            button.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
            button.SetValue(FrameworkElement.MarginProperty,new Thickness(0));button.SetValue(Control.FontSizeProperty,13.0);
        }
        static void AddRow(Grid g,UIElement e,int row){Grid.SetRow(e,row);g.Children.Add(e);}
        static StackPanel Labeled(string label,Control control,double size=11)
        {var group=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,4,6)};group.Children.Add(Text(label+"  ",size,Muted));group.Children.Add(control);return group;}
        // Summary card: a Panel box with a muted label over a bright value.
        static Border CardBox(string tag,string label,TextBlock value)
        {
            var stack=new StackPanel();
            stack.Children.Add(Text(label,11,Muted));
            value.Margin=new Thickness(0,2,0,0);
            stack.Children.Add(value);
            return new Border{Background=Panel,BorderBrush=Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(7),Padding=new Thickness(14,8,14,8),Margin=new Thickness(0,0,10,0),Child=stack,Tag=tag};
        }
        // Group header "Firm (count) · P/L sum". Pure display: no collapse state (WPF's CollectionViewGroup has no IsExpanded; see report).
        sealed class GroupHeaderConverter : System.Windows.Data.IValueConverter
        {
            public object Convert(object value,System.Type targetType,object parameter,System.Globalization.CultureInfo culture)
            {
                var g=value as System.Windows.Data.CollectionViewGroup;if(g==null)return "";
                string name=(g.Name as string)??"";
                double sum=0;string ccy=null;bool mixed=false;
                foreach(var it in g.Items){var r=it as AccountRow;if(r==null)continue;if(!RiskPolicy.Finite(r.Pnl))continue;sum+=r.Pnl;if(ccy==null)ccy=r.Currency;else if(!string.Equals(ccy,r.Currency,StringComparison.Ordinal))mixed=true;}
                string pnl=mixed?Lang.T("Mixed CCY"):MonitorSummary.Money(sum,ccy);
                return name+" ("+g.ItemCount+")"+(pnl.Length>0?" · "+pnl:"");
            }
            public object ConvertBack(object value,System.Type targetType,object parameter,System.Globalization.CultureInfo culture){return System.Windows.Data.Binding.DoNothing;}
        }
        GroupStyle GroupHeaderStyle()
        {
            var gs=new GroupStyle();
            var border=new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty,Panel);
            border.SetValue(Border.BorderBrushProperty,Line);
            border.SetValue(Border.BorderThicknessProperty,new Thickness(0,0,0,1));
            border.SetValue(Border.PaddingProperty,new Thickness(9,5,9,5));
            var tb=new FrameworkElementFactory(typeof(TextBlock));
            tb.SetBinding(TextBlock.TextProperty,new Binding(){Converter=new GroupHeaderConverter()});
            tb.SetValue(TextBlock.ForegroundProperty,Accent);
            tb.SetValue(TextBlock.FontSizeProperty,12.0);
            border.AppendChild(tb);
            gs.HeaderTemplate=new DataTemplate{VisualTree=border};
            return gs;
        }
        // Small page-switch link (never an edit button: navigation stays available while attached).
        Button NavTool(string label,string tag,int tabIndex)
        {
            var b=Button(label,()=>Sections.SelectedIndex=tabIndex);b.FontSize=11;b.Height=22;b.MinWidth=0;b.Padding=new Thickness(6,0,6,0);b.Margin=new Thickness(8,0,0,0);b.Foreground=Accent;b.Tag=tag;return b;
        }
        // Bulk edit fields, most consequential first (size, then limits, then exit options). Codes are what BulkApply takes.
        internal static readonly string[] BulkFields={"Ratio","Max","Tgt","DLL","LimitFlat","ATM","Diverge"};
        internal static int BulkFieldIndex(string code){return Array.IndexOf(BulkFields,code);}
        static string BulkFieldLabel(string code){return Lang.T(code=="Ratio"?"Ratio":code=="Max"?"Max contracts":code=="Tgt"?"Daily target":code=="DLL"?"Daily loss":code=="LimitFlat"?"Flat at limit":code=="ATM"?"ATM exit mirroring":"On divergence");}
        static bool BulkIsAmount(string code){return code=="Ratio"||code=="Max"||code=="Tgt"||code=="DLL";}
        string BulkFieldCode(){int i=bulkField==null?0:bulkField.SelectedIndex;return i>=0&&i<BulkFields.Length?BulkFields[i]:BulkFields[0];}
        string BulkFieldValue(){string c=BulkFieldCode();if(BulkIsAmount(c))return bulkValue==null?"":bulkValue.Text;if(c=="Diverge")return bulkValueCombo==null?"Notify":(bulkValueCombo.SelectedIndex==0?"Notify":"FlattenOnce");return bulkValueCombo==null?"on":(bulkValueCombo.SelectedIndex==0?"on":"off");}
        void UpdateBulkValueControl()
        {
            if(bulkField==null||bulkValue==null||bulkValueCombo==null)return;
            string code=BulkFieldCode();bool amount=BulkIsAmount(code);
            bulkValue.Visibility=amount?Visibility.Visible:Visibility.Collapsed;
            bulkValueCombo.Visibility=amount?Visibility.Collapsed:Visibility.Visible;
            if(amount)bulkValue.ToolTip=Lang.T(code=="Ratio"?"Ratio for the highlighted accounts · plain positive number like 1 or 0.5":code=="Max"?"Max contracts for the highlighted accounts · whole number >= 1":"Amount for the highlighted accounts · account currency · 0 = off");
            if(!amount){bulkValueCombo.ItemsSource=code=="Diverge"?new[]{Lang.T("Notify"),Lang.T("Flatten once")}:new[]{Lang.T("On"),Lang.T("Off")};bulkValueCombo.SelectedIndex=0;}
        }
        public DashboardView(bool demoMode=false)
        {
            demo=demoMode;Background=Canvas;Margin=new Thickness(16);TextElement.SetFontFamily(this,new FontFamily("Segoe UI Variable Text, Segoe UI"));
            // Do not inherit NT8 application implicit dimensions/fonts for compact dashboard controls.
            foreach(Type type in new[]{typeof(Button),typeof(ToggleButton),typeof(CheckBox),typeof(TextBox),typeof(ComboBox),typeof(TabControl),typeof(DataGrid)})
            {var scope=new Style(type);scope.Setters.Add(new Setter(FrameworkElement.MinWidthProperty,0.0));scope.Setters.Add(new Setter(FrameworkElement.MinHeightProperty,0.0));scope.Setters.Add(new Setter(Control.FontSizeProperty,13.0));scope.Setters.Add(new Setter(Control.ForegroundProperty,Ink));Resources[type]=scope;}
            RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            BuildHeader();
            BuildSections();
            BuildMonitorPage();
            BuildSettingsPage();
            BuildLogPage();
            BuildIssuesPage();
            BuildFooter();
            Rows.CollectionChanged+=(s,e)=>{if(e.OldItems!=null)foreach(AccountRow r in e.OldItems)r.FollowRequested-=OnFollowRequested;if(e.NewItems!=null)foreach(AccountRow r in e.NewItems)r.FollowRequested+=OnFollowRequested;};view=CollectionViewSource.GetDefaultView(Rows);view.Filter=Matches;((ListCollectionView)view).CustomSort=new LeaderFirstOrder(Rows,null,ListSortDirection.Ascending);AccountGrid.Sorting+=SortKeepingLeaderFirst;RiskGrid.Sorting+=SortKeepingLeaderFirst;AccountGrid.ItemsSource=view;RiskGrid.ItemsSource=view;AttachColumnMenu(ColumnPrefs.Copy,AccountGrid,columnsBarCopy);AttachColumnMenu(ColumnPrefs.Risk,RiskGrid,columnsBarRisk);
            PreviewKeyDown+=(s,e)=>{if(e.Key==System.Windows.Input.Key.F&&System.Windows.Input.Keyboard.Modifiers==System.Windows.Input.ModifierKeys.Control){FocusSearch();e.Handled=true;}else if(e.Key==System.Windows.Input.Key.Escape&&search.IsKeyboardFocusWithin){SearchText="";e.Handled=true;}};search.TextChanged+=(s,e)=>{view.Refresh();UpdateCards();};filter.SelectionChanged+=(s,e)=>{view.Refresh();UpdateCards();};groupBox.Checked+=(s,e)=>GroupByAccount=true;groupBox.Unchecked+=(s,e)=>GroupByAccount=false;ShowLive.Checked+=(s,e)=>Send("refresh");ShowLive.Unchecked+=(s,e)=>Send("refresh");AccountGrid.SelectionChanged+=(s,e)=>UpdateCards();RiskGrid.SelectionChanged+=(s,e)=>UpdateCards();Sections.SelectionChanged+=(s,e)=>{if(summary!=null)UpdateCards();};
            if(demo){for(int i=1;i<=30;i++)Rows.Add(new AccountRow{Name="Demo "+i.ToString("00"),Alias="Eval "+i,Group=i<=10?"Tradeify":i<=20?"Lucid":"Topstep",Role=i==1?"Leader":"Follower",Connection="Synthetic",Enabled=i>1,Currency="USD",Status=i%7==0?"Target hit":i%4==0?"Paused":"Demo only",Paused=i%4==0,Pnl=i%4==0?-120:i*24.5,Settings=new RiskSettings{Alias="Eval "+i,Group=i<=10?"Tradeify":i<=20?"Lucid":"Topstep",Target=1000,Loss=500,Ratio=1,MaxContracts=4,Preset=Lang.T("Demo / not broker data")}});LeaderBox.ItemsSource=new[]{"Demo 01"};LeaderBox.SelectedIndex=0;ShowLive.IsEnabled=false;}UpdateCards();
        }
        // Row 0 brand / language / build badge, row 1 session bar (leader, instrument, Start / pause / stop / refresh). Owner: UI v3 header work.
        // Extension hooks (one per work package, so parallel edits never touch the same lines). Keep each body inside its own package.
        // ==== WP1 hooks ====
        void EditableHeader(bool canEdit){if(InstPickButton!=null)InstPickButton.IsEnabled=canEdit;if(InstrumentPopup!=null&&!canEdit)InstrumentPopup.IsOpen=false;}
        void UpdateHeaderState()   // called from UpdateCards
        {
            if(StateBadge!=null)
            {
                bool anyPaused=Rows.Any(r=>r.Paused);
                if(!SessionAttached){StateBadge.Text=Lang.T("Stopped");StateBadge.Foreground=Muted;}
                else if(anyPaused){StateBadge.Text=Lang.T("Paused");StateBadge.Foreground=Warn;}
                else{StateBadge.Text=Lang.T("Copying");StateBadge.Foreground=Good;}
            }
#if KINFX_SIM_TEST
            if(DryBadge!=null)DryBadge.Text=(DryRunBox!=null&&DryRunBox.IsChecked==true)?Lang.T("Dry run: on"):Lang.T("Dry run: off");
#endif
            string front=FrontForBox();
            if(FrontNotice!=null){bool stale=front!=null;FrontNotice.Visibility=stale?Visibility.Visible:Visibility.Collapsed;if(stale)FrontNotice.Text=Lang.F("Front contract is now {0}",front);}
            if(UseFrontTool!=null){bool stale=front!=null;UseFrontTool.Visibility=stale?Visibility.Visible:Visibility.Collapsed;if(stale)UseFrontTool.Content=Lang.F("Use {0}",front);}
        }
        // The current front contract for a stale quarterly box, or null when the box already holds the front (or is not quarterly).
        string FrontForBox()
        {
            var text=(InstrumentBox==null?"":InstrumentBox.Text??"").Trim();
            if(text.Length==0)return null;
            var now=InstrumentCatalog.Now();
            var normalized=ContractRoll.Normalize(text,now);
            if(normalized==null)return null;
            string root=normalized.Split(' ')[0];
            string front=ContractRoll.Front(root,now);
            if(front==null||string.Equals(normalized,front,StringComparison.Ordinal))return null;
            return front;
        }
        // Opens/closes the instrument picker; the list is rebuilt each time it opens so the injected clock / recent roots are respected.
        void TogglePicker()
        {
            if(InstrumentPopup==null)
            {
                InstrumentPopup=new Popup{PlacementTarget=InstPickButton,Placement=PlacementMode.Bottom,StaysOpen=false,AllowsTransparency=true};
                InstrumentList=new Border{Background=Panel,BorderBrush=Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(5),MinWidth=320,MaxHeight=420};
                // A Popup inherits fonts from its PlacementTarget: the icon button uses Segoe Fluent Icons, which turned every row into boxes on real NT.
                TextElement.SetFontFamily(InstrumentList,new FontFamily("Segoe UI Variable Text, Segoe UI"));TextElement.SetForeground(InstrumentList,Ink);
                var items=new StackPanel{Background=Panel};
                InstrumentList.Child=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,Background=Panel,Content=items};
                InstrumentPopup.Child=InstrumentList;
            }
            if(InstrumentPopup.IsOpen){InstrumentPopup.IsOpen=false;return;}
            BuildPickerItems();
            InstrumentPopup.IsOpen=true;
        }
        // Set on every picker TextBlock: on real NT the popup text still drew as boxes with only an inherited font on the list border.
        static readonly FontFamily PickerFont=new FontFamily("Segoe UI Variable Text, Segoe UI");
        void BuildPickerItems()
        {
            var panel=(StackPanel)((ScrollViewer)InstrumentList.Child).Content;
            panel.Children.Clear();
            var recent=RecentRoots!=null?RecentRoots():null;
            foreach(var entry in InstrumentCatalog.Build(recent??new string[0]))
            {
                if(entry.Header){panel.Children.Add(new TextBlock{Text=Lang.T(entry.Description),FontFamily=PickerFont,FontSize=11,Foreground=Muted,Margin=new Thickness(12,7,12,2)});continue;}
                panel.Children.Add(PickerRow(entry));
            }
        }
        FrameworkElement PickerRow(InstrumentCatalog.Entry entry)
        {
            var stack=new StackPanel{Orientation=Orientation.Horizontal};
            stack.Children.Add(new TextBlock{Text=entry.Root,FontFamily=PickerFont,FontSize=13,Foreground=Ink,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center});
            stack.Children.Add(new TextBlock{Text="  "+entry.Front+"  ",FontFamily=PickerFont,FontSize=13,Foreground=Accent,VerticalAlignment=VerticalAlignment.Center});
            stack.Children.Add(new TextBlock{Text=entry.ChooseMonth?Lang.T(entry.Description)+" · "+Lang.T("choose month"):Lang.T(entry.Description),FontFamily=PickerFont,FontSize=12,Foreground=Muted,VerticalAlignment=VerticalAlignment.Center});
            var b=new Button{Content=stack,Tag=entry.Root,HorizontalContentAlignment=HorizontalAlignment.Left,Background=Panel,BorderBrush=Line,Margin=new Thickness(6,1,6,1),Padding=new Thickness(9,5,9,5),Cursor=System.Windows.Input.Cursors.Hand,MinWidth=0,MinHeight=0};
            var t=new ControlTemplate(typeof(Button));var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(4));border.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));border.SetValue(Border.BorderBrushProperty,new TemplateBindingExtension(Control.BorderBrushProperty));border.SetValue(Border.BorderThicknessProperty,new Thickness(1));
            var cp=new FrameworkElementFactory(typeof(ContentPresenter));cp.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Left);cp.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);cp.SetValue(FrameworkElement.MarginProperty,new TemplateBindingExtension(Control.PaddingProperty));border.AppendChild(cp);t.VisualTree=border;b.Template=t;
            b.MouseEnter+=(s,e)=>b.BorderBrush=Accent;b.MouseLeave+=(s,e)=>b.BorderBrush=Line;
            b.Click+=(s,e)=>PickInstrument(entry);
            return b;
        }
        void PickInstrument(InstrumentCatalog.Entry entry){InstrumentBox.Text=entry.Front;if(InstrumentPopup!=null)InstrumentPopup.IsOpen=false;UpdateHeaderState();var ev=InstrumentPicked;if(ev!=null)ev();}
        // "Use {front}" only rewrites the text box (never a command); normalization happens through InstrumentPicked like a pick.
        void UseFront(){var f=FrontForBox();if(f==null)return;InstrumentBox.Text=f;UpdateHeaderState();var ev=InstrumentPicked;if(ev!=null)ev();}
        //
        //
        //
        // ==== WP2 hooks ====
        void EditableMonitor(bool canEdit){}
        void EditableSettings(bool canEdit){if(bulkField!=null)bulkField.IsEnabled=canEdit;if(bulkValue!=null)bulkValue.IsEnabled=canEdit;if(bulkValueCombo!=null)bulkValueCombo.IsEnabled=canEdit;}
        void UpdateMonitor()
        {
            if(cardCopying==null)return;
            var followers=Rows.Where(r=>r.Role!="Leader").ToArray();
            int ticked=followers.Count(r=>r.Enabled);
            cardCopying.Text=ticked+" / "+followers.Length;
            cardLocked.Text=LockedCount.ToString(CultureInfo.InvariantCulture);
            int unprotected=0;foreach(var r in followers)unprotected+=MonitorSummary.UnprotectedLots(r.Latch.Reason);
            cardUnprotected.Text=unprotected.ToString(CultureInfo.InvariantCulture);
            string ccy=MonitorSummary.SingleCurrency(followers.Select(r=>r.Currency));
            double pnl=MonitorSummary.SumFinite(followers.Select(r=>r.Pnl));
            cardPnl.Text=ccy==null?Lang.T("Mixed CCY"):MonitorSummary.Money(pnl,ccy);
            if(issuesLines==null)return;
            // Rebuilt only when the issue set changes (UpdateCards runs every second and on every telemetry message).
            var sigB=new System.Text.StringBuilder(Lang.Current).Append('|').Append(HeaderNotice);
            foreach(var r in Rows.Where(x=>x.Role!="Leader").OrderBy(x=>x.Name,StringComparer.Ordinal)){if(r.Latch.Locked)sigB.Append("\nL").Append(r.Name).Append('\t').Append(r.Alias).Append('\t').Append(r.Latch.Reason);if(r.Advice.Length>0)sigB.Append("\nA").Append(r.Name).Append('\t').Append(r.Alias).Append('\t').Append(r.Advice);}
            string sig=sigB.ToString();if(sig==issuesSignature)return;issuesSignature=sig;
            issuesLines.Children.Clear();
            var lines=new List<UIElement>();
            foreach(var r in Rows.Where(x=>x.Role!="Leader"&&x.Latch.Locked))
            {
                var row=new DockPanel();
                var nb=NavTool(Lang.T("Logs"),"issue:logs",2);DockPanel.SetDock(nb,Dock.Right);row.Children.Add(nb);
                string reason=Lang.Show(r.Latch.Reason);if(string.IsNullOrEmpty(reason))reason=r.Latch.Reason;if(reason.Length>90)reason=reason.Substring(0,90)+"…";
                var t=Text((r.Alias.Length>0?r.Alias:r.Name)+": "+reason,12,Bad);t.TextWrapping=TextWrapping.Wrap;row.Children.Add(t);
                lines.Add(row);
            }
            foreach(var r in Rows.Where(x=>x.Role!="Leader"&&x.Advice.Length>0))
            {
                var row=new DockPanel();
                var sb=NavTool(Lang.T("Settings"),"issue:settings",1);DockPanel.SetDock(sb,Dock.Right);row.Children.Add(sb);
                var t=Text((r.Alias.Length>0?r.Alias:r.Name)+": "+Lang.Show(r.Advice),12,Warn);t.TextWrapping=TextWrapping.Wrap;row.Children.Add(t);
                lines.Add(row);
            }
            if(HeaderNotice.Length>0){var t=Text(Lang.Show(HeaderNotice),12,Warn);t.TextWrapping=TextWrapping.Wrap;lines.Add(t);}
            foreach(var l in lines){var fe=l as FrameworkElement;if(fe!=null)fe.Margin=new Thickness(0,0,0,6);issuesLines.Children.Add(l);}
            issuesLines.Visibility=lines.Count==0?Visibility.Collapsed:Visibility.Visible;
            if(issuesEmpty!=null)issuesEmpty.Visibility=lines.Count==0?Visibility.Visible:Visibility.Collapsed;
            IssueCount=lines.Count;UpdateIssuesTabHeader();
        }
        //
        //
        //
        // ==== WP3 hooks ====
        void EditableLogs(bool canEdit){/* Log filters, export and clear stay usable while attached. Export is not an edit button; Clear log is an edit button and disables with the rest. */}
        //
        //
        //
        // Free-text notice another package can raise (WP1 sets it for an untested NinjaTrader version); WP2 shows it on the Monitor page.
        internal string HeaderNotice="";
        // ==== WP1 header / instrument picker fields ====
        internal Button InstPickButton, UseFrontTool;
        internal TextBlock StateBadge, FrontNotice;
        internal Popup InstrumentPopup;
        internal Border InstrumentList;
        internal Func<string[]> RecentRoots;
        internal event Action InstrumentPicked;
#if KINFX_SIM_TEST
        internal TextBlock DryBadge;
#endif
        void BuildHeader()
        {
            var header=new DockPanel{Margin=new Thickness(0,0,0,14)};StatusText=Text(demo?Lang.T("DEMO · NO TRADES"):DashboardSafety.HeaderShown,11,Warn);DockPanel.SetDock(StatusText,Dock.Right);header.Children.Add(StatusText);LanguageBox=Combo(118);LanguageBox.ToolTip=Lang.T("Interface language");System.Windows.Automation.AutomationProperties.SetName(LanguageBox,Lang.T("Language"));LanguageBox.ItemsSource=Lang.Names;LanguageBox.SelectedIndex=Math.Max(0,Array.IndexOf(Lang.Codes,Lang.Current));LanguageBox.Margin=new Thickness(0,0,14,0);DockPanel.SetDock(LanguageBox,Dock.Right);header.Children.Add(LanguageBox);bool languageGuard=false;LanguageBox.SelectionChanged+=(s,e)=>{if(languageGuard||LanguageBox.SelectedIndex<0)return;string code=Lang.Codes[LanguageBox.SelectedIndex];if(code==Lang.Current)return;var ask=LanguageChanging;if(ask!=null&&!ask(code)){languageGuard=true;LanguageBox.SelectedIndex=Array.IndexOf(Lang.Codes,Lang.Current);languageGuard=false;}};var brand=new StackPanel{Orientation=Orientation.Horizontal};brand.Children.Add(Text("KINFX",20,Accent));brand.Children.Add(new TextBlock{Text=Lang.T("  /  Trade Copier"),FontSize=17,Foreground=Ink,VerticalAlignment=VerticalAlignment.Center});header.Children.Add(brand);AddRow(this,header,0);
            var session=new Grid{Margin=new Thickness(0,0,0,6)};session.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});session.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
            var left=new WrapPanel{VerticalAlignment=VerticalAlignment.Center};LeaderBox=Combo(232);LeaderBox.ToolTip=Lang.T("Simulation leader account · no live opt-in");left.Children.Add(Labeled(Lang.T("Lead"),LeaderBox));InstrumentBox=Input("MES 12-26",126);InstrumentBox.ToolTip=Lang.T("Exact instrument");var instGroup=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,4,6)};instGroup.Children.Add(Text(Lang.T("Inst")+"  ",11,Muted));instGroup.Children.Add(InstrumentBox);InstPickButton=Icon("\uE70D",Lang.T("Instrument picker"),"instPick",TogglePicker);instGroup.Children.Add(InstPickButton);left.Children.Add(instGroup);FollowCountText=Text(Lang.T("→  0 followers  "),12,Muted);left.Children.Add(FollowCountText);FrontNotice=Text("",11,Warn);FrontNotice.Visibility=Visibility.Collapsed;FrontNotice.Margin=new Thickness(0,0,8,6);left.Children.Add(FrontNotice);UseFrontTool=TextTool("",Lang.T("Switch the instrument to the current front contract"),"useFront",UseFront);UseFrontTool.Visibility=Visibility.Collapsed;UseFrontTool.Margin=new Thickness(0,0,8,6);left.Children.Add(UseFrontTool);Grid.SetColumn(left,0);session.Children.Add(left);
            var right=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center};right.Children.Add(Text(Lang.T("State")+"  ",11,Muted));StateBadge=Text(Lang.T("Stopped"),11,Muted);StateBadge.Tag="stateBadge";StateBadge.FontWeight=FontWeights.SemiBold;StateBadge.Margin=new Thickness(0,0,14,6);right.Children.Add(StateBadge);
#if KINFX_SIM_TEST
            DryBadge=Text(Lang.T("Dry run: off"),11,Warn);DryBadge.Tag="dryBadge";DryBadge.Margin=new Thickness(0,0,14,6);right.Children.Add(DryBadge);
#endif
            StartButton=Button(Lang.T("▶  Start"),()=>Send("start"),true);StartButton.ToolTip=DashboardSafety.StartTipShown;ToolTipService.SetShowOnDisabled(StartButton,true);StartButton.IsEnabled=DashboardSafety.CanExecute("start")&&!demo;right.Children.Add(StartButton);var pause=Button(Lang.T("Pause new entries"),()=>Send("pause"));pause.Tag="pause";pause.ToolTip=Lang.T("Guarded pause · new entries only");ToolTipService.SetShowOnDisabled(pause,true);right.Children.Add(pause);ResumeButton=Button(Lang.T("Resume new entries"),()=>Send("resume"));ResumeButton.Tag="resume";ResumeButton.ToolTip=Lang.T("Resume new entries after a pause · entries the leader opened while paused are NOT copied");ToolTipService.SetShowOnDisabled(ResumeButton,true);ResumeButton.Visibility=Visibility.Collapsed;right.Children.Add(ResumeButton);right.Children.Add(Icon("\uE71A",Lang.T("Stop when flat"),"stop",()=>Send("stop")));right.Children.Add(Icon("\uE72C",Lang.T("Refresh accounts"),"refresh",()=>Send("refresh")));Grid.SetColumn(right,1);session.Children.Add(right);AddRow(this,new Border{Background=Panel,BorderBrush=Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(7),Padding=new Thickness(12,8,12,0),Margin=new Thickness(0,0,0,10),Child=session},1);
        }
        // Row 2: the page container. Pages are added in order: Monitor (0), Settings (1), Logs (2).
        void BuildSections()
        {
            Sections=new TabControl{Background=Canvas,Foreground=Ink,BorderBrush=Line,Padding=new Thickness(0,10,0,0),ItemContainerStyle=TabStyle()};AddRow(this,Sections,2);
        }
        // Page 0 (was "Copy"). Owner: UI v3 page 1 work.
        // Page 0 (Monitor). Owner: UI v3 page 1 work.
        void BuildMonitorPage()
        {
            var copy=new Grid();copy.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});copy.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});copy.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});copy.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var cards=new WrapPanel{Margin=new Thickness(0,0,0,10)};cardCopying=Text("0",18,Ink);cardLocked=Text("0",18,Ink);cardUnprotected=Text("0",18,Ink);cardPnl=Text("0",18,Ink);cards.Children.Add(CardBox("card:copying",Lang.T("Copying"),cardCopying));var lockedCard=CardBox("card:locked",Lang.T("Locked accounts"),cardLocked);lockedCard.Cursor=System.Windows.Input.Cursors.Hand;lockedCard.ToolTip=Lang.T("Open the Issues tab");lockedCard.MouseLeftButtonUp+=(s,e)=>{if(issuesTab!=null)Sections.SelectedItem=issuesTab;};cards.Children.Add(lockedCard);cards.Children.Add(CardBox("card:unprotected",Lang.T("Unprotected lots"),cardUnprotected));cards.Children.Add(CardBox("card:pnl",Lang.T("Today P/L"),cardPnl));AddRow(copy,cards,0);
            var toolbar=new WrapPanel{Margin=new Thickness(0,0,0,9)};
            search=Input("",185);search.ToolTip=Lang.T("Search account / alias / group / preset · Ctrl+F · Escape clears");toolbar.Children.Add(Labeled("⌕",search,17));filter=Combo(120);filter.ToolTip=Lang.T("Filter displayed account states");filter.ItemsSource=new[]{Lang.T("All"),Lang.T("On"),Lang.T("Paused"),Lang.T("Target")};filter.SelectedIndex=0;toolbar.Children.Add(filter);var ticks=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,6)};ticks.Children.Add(Icon("\uE73A",Lang.T("Tick all visible followers"),"tickAll",()=>TickVisible(true),true));ticks.Children.Add(Icon("\uE711",Lang.T("Untick all visible followers"),"tickNone",()=>TickVisible(false),true));toolbar.Children.Add(ticks);groupBox=new CheckBox{Template=PlainCheckTemplate(),Content=Lang.T("Group by firm"),Foreground=Muted,Margin=new Thickness(8,6,0,0),ToolTip=Lang.T("Group account rows")};System.Windows.Automation.AutomationProperties.SetName(groupBox,Lang.T("Group by firm"));toolbar.Children.Add(groupBox);AddRow(copy,toolbar,1);columnsBarCopy=toolbar;
            AccountGrid=GridTable();TickColumn(AccountGrid);AccountColumn(AccountGrid);Column(AccountGrid,"Alias","Alias",100,false,Lang.T("Optional display alias"));RatioColumn(AccountGrid);Column(AccountGrid,"Tgt","TargetText",78,false,Lang.T("Independent daily local profit target · account CCY / 0 = off"),null,true);Column(AccountGrid,"DLL","LossText",78,false,Lang.T("Independent daily loss limit · positive amount / 0 = off"),null,true);AtmColumn(AccountGrid);Column(AccountGrid,"Qty","Position",55,true,Lang.T("Signed position · + long / − short"),null,true);DriftColumn(AccountGrid);Column(AccountGrid,"P/L","Pnl",95,true,Lang.T("Observed session P/L in account currency · baseline is not historical daily accounting"),"N2",true);Column(AccountGrid,"Bal","BalanceText",130,true,Lang.T("CashValue in account currency; not NetLiquidation / net equity"),null,true);AccountGrid.Columns.Last().SortMemberPath="CashBalance";((DataGridTextColumn)AccountGrid.Columns.Last()).ElementStyle.Setters.Add(new Setter(FrameworkElement.ToolTipProperty,new Binding("BalanceDetail")));((DataGridTextColumn)AccountGrid.Columns.Last()).ElementStyle.Setters.Add(new Setter(System.Windows.Automation.AutomationProperties.NameProperty,new Binding("BalanceDetail")));LightColumn(AccountGrid,"Conn","ConnLight","Connection");StateColumn(AccountGrid);OffColumn(AccountGrid);FlatColumn(AccountGrid);OrderColumns(AccountGrid,CopyColumnOrder);AccountGrid.FrozenColumnCount=2;ProportionalCopyColumns(AccountGrid);AccountGrid.GroupStyle.Add(GroupHeaderStyle());AddRow(copy,AccountGrid,2);
            // Flatten is an emergency action: NOT an edit button, so it stays usable while a session is attached (as in 121c44e).
            var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0)};var fs=TextToolFree(Lang.T("Flatten selected"),DashboardSafety.CanExecute("flattenSelected")?Lang.T("Flatten highlighted accounts (market-closes their positions)"):Lang.T("Flatten highlighted accounts · review hold"),"flattenSelected",()=>Send("flattenSelected"));fs.Foreground=Bad;fs.IsEnabled=DashboardSafety.CanExecute("flattenSelected");var fa=TextToolFree(Lang.T("Flatten all"),DashboardSafety.CanExecute("flattenAll")?Lang.T("Flatten ALL displayed accounts (market-closes every position shown)"):Lang.T("Flatten all displayed accounts · review hold"),"flattenAll",()=>Send("flattenAll"));fa.Foreground=Bad;fa.IsEnabled=DashboardSafety.CanExecute("flattenAll");RefollowButton=TextToolFree(Lang.T("Re-follow closed accounts"),Lang.T("Tick again every follower that closed after a copied exit and is flat, connected and unlocked. Only LATER new entries are copied; nothing is caught up."),"refollow",()=>Send("refollow"));RefollowButton.IsEnabled=false;actions.Children.Insert(0,RefollowButton);actions.Children.Add(fs);actions.Children.Add(fa);AddRow(copy,actions,3);
            Sections.Items.Add(new TabItem{Header=Lang.T("Monitor"),Content=copy});
        }
        // Page 1 (was "Risk"). Owner: UI v3 page 2 work.
        // Page 1 (Settings). Owner: UI v3 page 2 work.
        void BuildSettingsPage()
        {
            var risk=new Grid();risk.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});risk.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});risk.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});risk.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var session=new Border{Background=Panel,BorderBrush=Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(7),Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,0,10)};var sessionBody=new StackPanel();sessionBody.Children.Add(Text(Lang.T("This session"),12,Muted));var tools=new WrapPanel{Margin=new Thickness(0,6,0,0)};
            var batch=Icon("\uE713",DashboardSafety.CanExecute("batch")?Lang.T("Batch risk / presets"):Lang.T("Batch risk / presets · review hold"),"batch",()=>Send("batch"));batch.IsEnabled=DashboardSafety.CanExecute("batch");tools.Children.Add(batch);tools.Children.Add(Icon("\uE74E",Lang.T("Save settings"),"save",()=>Send("save")));tools.Children.Add(Icon("\uE787",Lang.T("Trading-day boundary / timezone"),"daySettings",()=>Send("daySettings")));tools.Children.Add(Icon("\uE769",Lang.T("Pause highlighted accounts"),"pauseSelected",()=>SetSelectedPause(true)));tools.Children.Add(Icon("\uE768",Lang.T("Resume highlighted accounts · latch unchanged / no auto-arm"),"resumeSelected",()=>SetSelectedPause(false)));
            ShowLive=new CheckBox{Template=PlainCheckTemplate(),Content=Lang.T("Live (RO)"),Foreground=Muted,Margin=new Thickness(12,6,16,0),ToolTip=Lang.T("Display live accounts read-only · live copying blocked")};tools.Children.Add(ShowLive);if(DashboardSafety.ListAllAccounts)ShowLive.Visibility=Visibility.Collapsed;
            tools.Children.Add(new TextBlock{Text=Lang.T("Ask before:"),Foreground=Muted,FontSize=12,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(16,6,6,0),ToolTip=Lang.T("Untick to skip that confirmation window. Unlock and risk warnings are always asked.")});
            Func<string,string,string,CheckBox> askSwitch=(kind,label,tip)=>{var cb=new CheckBox{Template=PlainCheckTemplate(),Content=label,Foreground=Muted,IsChecked=true,Margin=new Thickness(0,6,10,0),ToolTip=tip,Tag="ask:"+kind};System.Windows.Automation.AutomationProperties.SetName(cb,Lang.T("Ask before:")+" "+label);cb.Checked+=(s2,e2)=>{var cbk=AskChanged;if(cbk!=null)cbk(kind,true);};cb.Unchecked+=(s2,e2)=>{var cbk=AskChanged;if(cbk!=null)cbk(kind,false);};tools.Children.Add(cb);return cb;};
            AskStartBox=askSwitch(ConfirmPrefs.Start,Lang.T("Start"),Lang.T("Confirm before Start (follower list and ratios)"));
            AskFlatOneBox=askSwitch(ConfirmPrefs.FlatOne,Lang.T("Flat (row)"),Lang.T("Confirm before a row's Flat button closes that one account"));
            AskFlattenBox=askSwitch(ConfirmPrefs.Flatten,Lang.T("Flatten selected / all"),Lang.T("Confirm before Flatten selected / Flatten all"));
#if KINFX_SIM_TEST
            DryRunBox=new CheckBox{Template=PlainCheckTemplate(),Content=Lang.T("Dry run"),Foreground=Warn,Margin=new Thickness(16,6,0,0),ToolTip=Lang.T("DRY RUN: decide and log every order, cancel and flatten as \"DRY RUN would send ...\" but send NOTHING. Fixed for the whole session: change it only while stopped.")};tools.Children.Add(DryRunBox);
#endif
            tools.Children.Add(AvgWarnControl());
            sessionBody.Children.Add(tools);columnsBarRisk=tools;session.Child=sessionBody;var top=new StackPanel();top.Children.Add(session);BuildNotifySection(top);AddRow(risk,top,0);
            var bulkSection=new Border{Background=Panel,BorderBrush=Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(7),Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,0,10)};var bulkBody=new StackPanel();bulkBody.Children.Add(Text(Lang.T("Bulk edit (highlighted follower accounts · only while stopped)"),12,Muted));var bulkRow=new WrapPanel{Margin=new Thickness(0,6,0,0)};
            bulkField=Combo(150);bulkField.Tag="bulkField";bulkField.ToolTip=Lang.T("Which setting to change on highlighted accounts");bulkField.ItemsSource=BulkFields.Select(BulkFieldLabel).ToArray();bulkField.SelectedIndex=0;bulkRow.Children.Add(bulkField);
            bulkValue=Input("",78);bulkValue.Tag="bulkValue";bulkValue.ToolTip=Lang.T("Amount for the highlighted accounts · account currency · 0 = off");System.Windows.Automation.AutomationProperties.SetName(bulkValue,Lang.T("Bulk amount"));bulkRow.Children.Add(bulkValue);
            bulkValueCombo=Combo(130);bulkValueCombo.Tag="bulkValueCombo";bulkValueCombo.ToolTip=Lang.T("Value for the highlighted accounts");bulkRow.Children.Add(bulkValueCombo);
            bulkRow.Children.Add(TextTool(Lang.T("Apply"),Lang.T("Apply the chosen setting to the highlighted follower accounts"),"bulkApply",()=>BulkClick(BulkFieldCode(),BulkFieldValue())));
            bulkRow.Children.Add(TextTool(Lang.T("From Leader"),Lang.T("Copy the leader row's Tgt / DLL / ATM to highlighted followers (all visible if none highlighted). One-time copy; edit any row afterwards. Ratio and ticks are not copied."),"bulkLeader",()=>BulkLeaderClick()));
            bulkBar=bulkRow;bulkBar.Tag="bulkbar";bulkField.SelectionChanged+=(s,e)=>UpdateBulkValueControl();UpdateBulkValueControl();bulkBody.Children.Add(bulkRow);bulkSection.Child=bulkBody;AddRow(risk,bulkSection,1);
            RiskGrid=GridTable();Column(RiskGrid,"Acct","Name",140,true,Lang.T("Broker account"));Column(RiskGrid,"Alias","Alias",100,false,Lang.T("Display alias"));Column(RiskGrid,"Grp","Group",80,false,Lang.T("Account group"));Column(RiskGrid,"Tgt","TargetText",100,false,Lang.T("Daily local profit target · 0 = off"),null,true);Column(RiskGrid,"DLL","LossText",100,false,Lang.T("Daily loss limit · positive value / 0 = off"),null,true);FlatOnLimitColumn(RiskGrid);AutoRefollowColumn(RiskGrid);Column(RiskGrid,"Max","MaxText",60,false,Lang.T("Maximum exact-instrument contracts"),null,true);RiskGrid.Columns.Last().SortMemberPath="MaxContracts";var maxCol=(DataGridTextColumn)RiskGrid.Columns.Last();var maxCellStyle=new Style(typeof(DataGridCell),RiskGrid.CellStyle);var maxDefault=new DataTrigger{Binding=new Binding("MaxIsDefault"),Value=true};maxDefault.Setters.Add(new Setter(Control.BorderBrushProperty,Warn));maxDefault.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(1)));maxDefault.Setters.Add(new Setter(FrameworkElement.ToolTipProperty,Lang.T("Max is still 1: a leader order larger than this is skipped entirely")));maxCellStyle.Triggers.Add(maxDefault);maxCol.CellStyle=maxCellStyle;RatioColumn(RiskGrid);Column(RiskGrid,"P/L","Pnl",95,true,Lang.T("Observed session P/L"),"N2",true);Column(RiskGrid,"CCY","Currency",55,true,Lang.T("Account currency"));Column(RiskGrid,"To Tgt","TargetRemaining",80,true,Lang.T("Amount remaining to daily target (read-only, computed from P/L)"),null,true);Column(RiskGrid,"Loss left","LossRemaining",80,true,Lang.T("How much more can be lost before the daily loss limit (read-only, computed from P/L)"),null,true);Column(RiskGrid,"Qty","Position",50,true,Lang.T("Signed position"),null,true);Column(RiskGrid,"Conn","ConnectionShown",85,true,Lang.T("Connection status"));((DataGridTextColumn)RiskGrid.Columns.Last()).SortMemberPath="Connection";Column(RiskGrid,"St","StatusShown",95,true,Lang.T("Copier / risk state"));((DataGridTextColumn)RiskGrid.Columns.Last()).SortMemberPath="Status";FullTextTip(RiskGrid,"StatusShown");Column(RiskGrid,"Preset","Preset",95,true,Lang.T("Editable firm template"));FullTextTip(RiskGrid,"Preset");Column(RiskGrid,"Cons","ConsistencyAdvice",95,true,Lang.T("Consistency advisory · not a broker guarantee"));FullTextTip(RiskGrid,"ConsistencyAdvice");OrderColumns(RiskGrid,RiskColumnOrder);
            var action=new FrameworkElementFactory(typeof(Button));CompactCellButton(action,28);action.SetValue(Control.TemplateProperty,OffTemplate());action.SetValue(ContentControl.ContentProperty,"\uE713");action.SetValue(Control.FontFamilyProperty,new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"));action.SetValue(Control.BackgroundProperty,Panel);action.SetValue(Control.ForegroundProperty,Ink);action.SetValue(Control.BorderBrushProperty,Line);action.SetValue(Control.PaddingProperty,new Thickness(4,1,4,1));action.SetValue(FrameworkElement.ToolTipProperty,Lang.T("Account risk / template / history"));var gearStyle=new Style(typeof(Button));var gearLocked=new DataTrigger{Binding=new Binding("IsReadOnly"){RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(DataGrid),1)},Value=true};gearLocked.Setters.Add(new Setter(UIElement.IsEnabledProperty,false));gearStyle.Triggers.Add(gearLocked);action.SetValue(FrameworkElement.StyleProperty,gearStyle);action.AddHandler(ButtonBase.ClickEvent,new RoutedEventHandler((s,e)=>{if(!editable){e.Handled=true;return;}var r=(s as FrameworkElement).DataContext as AccountRow;if(r!=null&&RowSettings!=null)RowSettings(r);}));RiskGrid.Columns.Add(new DataGridTemplateColumn{Header="⚙",Width=40,CellTemplate=new DataTemplate{VisualTree=action}});AddRow(risk,RiskGrid,2);
            AddRow(risk,new TextBlock{Text=Lang.T("OFF cancels owned entries, retains exits · Re-follow explicit / only when clean · ATM OFF may be unprotected · Local guards only"),Foreground=Muted,FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0)},3);Sections.Items.Add(new TabItem{Header=Lang.T("Settings"),Content=risk});
        }
        // Page 2 (Logs). Owner: UI v3 page 3 work.
        void BuildLogPage()
        {
            var logPanel=new Grid();logPanel.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});logPanel.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});logPanel.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
            AddRow(logPanel,new TextBlock{Text=DashboardSafety.LogNoteShown,Foreground=Warn,FontSize=12,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)},0);
            var logTools=new WrapPanel{Margin=new Thickness(0,0,0,8)};
            LogLevelBox=Combo(96);LogLevelBox.Tag="logLevel";LogLevelBox.ItemsSource=new[]{Lang.T("All")}.Concat(LogClassifier.Levels.Select(x=>Lang.T(x))).ToArray();LogLevelBox.SelectedIndex=0;LogLevelBox.ToolTip=Lang.T("Show only one severity level");LogLevelBox.SelectionChanged+=(s,e)=>{if(logView!=null)logView.Refresh();};logTools.Children.Add(Labeled(Lang.T("Level"),LogLevelBox));
            LogAccountBox=Combo(150);LogAccountBox.Tag="logAccount";LogAccountBox.ToolTip=Lang.T("Show only one account");LogAccountBox.DropDownOpened+=(s,e)=>RefreshLogAccounts();LogAccountBox.SelectionChanged+=(s,e)=>{if(logView!=null)logView.Refresh();};RefreshLogAccounts();logTools.Children.Add(Labeled(Lang.T("Account"),LogAccountBox));
            LogSearchBox=Input("",150);LogSearchBox.Tag="logSearch";LogSearchBox.ToolTip=Lang.T("Search the log text");LogSearchBox.TextChanged+=(s,e)=>{if(logView!=null)logView.Refresh();};logTools.Children.Add(Labeled("⌕",LogSearchBox,17));
            LogAutoBox=new CheckBox{Template=PlainCheckTemplate(),Content=Lang.T("Auto-scroll"),Foreground=Muted,IsChecked=true,Margin=new Thickness(0,2,10,0),Tag="logAuto"};logTools.Children.Add(LogAutoBox);
            LogPlainBox=new CheckBox{Template=PlainCheckTemplate(),Content=Lang.T("Plain text"),Foreground=Muted,Margin=new Thickness(0,2,10,0),Tag="logPlain"};logTools.Children.Add(LogPlainBox);
            logTools.Children.Add(TextToolFree(Lang.T("Export (account names masked)"),Lang.T("Write this log to a file with account names masked"),"logExport",()=>{var cb=ExportLog;if(cb!=null)cb();}));
            logTools.Children.Add(TextTool(Lang.T("Clear log"),Lang.T("Clear this log view (does not affect settings, orders or saved latches)"),"logClear",()=>{ClearLog();var cb=ClearQueue;if(cb!=null)cb();}));
            AddRow(logPanel,logTools,1);
            var body=new Grid();
            LogGrid=GridTable();LogGrid.IsReadOnly=true;LogGrid.Tag="logGrid";BuildLogColumns();
            LogBox=Input("",double.NaN);LogBox.IsReadOnly=true;LogBox.FontFamily=new FontFamily("Consolas");LogBox.FontSize=12;LogBox.Foreground=Brush("#E4E1EC");LogBox.VerticalContentAlignment=VerticalAlignment.Top;LogBox.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;LogBox.HorizontalScrollBarVisibility=ScrollBarVisibility.Auto;LogBox.Visibility=Visibility.Collapsed;LogBox.Tag="logPlainBox";
            body.Children.Add(LogGrid);body.Children.Add(LogBox);
            LogPlainBox.Checked+=(s,e)=>SetLogView(true);LogPlainBox.Unchecked+=(s,e)=>SetLogView(false);
            AddRow(logPanel,body,2);
            logTab=new TabItem{Header=Lang.T("Logs"),Content=logPanel};Sections.Items.Add(logTab);
            logView=CollectionViewSource.GetDefaultView(logRows);logView.Filter=LogMatches;LogGrid.ItemsSource=logView;
            Sections.SelectionChanged+=(s,e)=>{if(Sections.SelectedIndex==2){LogUnseen=0;UpdateLogTabHeader();RefreshLogAccounts();}};
            Sections.SelectedIndex=0;
        }
        void BuildLogColumns()
        {
            LogGrid.Columns.Add(new DataGridTextColumn{Header=Lang.T("Time"),Binding=new Binding("Time"){Mode=BindingMode.OneWay,StringFormat="HH:mm:ss",ConverterCulture=CultureInfo.InvariantCulture},Width=78,IsReadOnly=true});
            var lvl=new FrameworkElementFactory(typeof(TextBlock));lvl.SetBinding(TextBlock.TextProperty,new Binding("LevelText"){Mode=BindingMode.OneWay});lvl.SetValue(FrameworkElement.MarginProperty,new Thickness(9,0,9,0));lvl.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
            // No local Foreground on the cell: a local value outranks these style triggers and every level would stay Muted.
            var lvStyle=new Style(typeof(TextBlock));lvStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty,Muted));LogLevelTrigger(lvStyle,"Info",Muted);LogLevelTrigger(lvStyle,"Order",Good);LogLevelTrigger(lvStyle,"Warn",Warn);LogLevelTrigger(lvStyle,"Lock",Bad);LogLevelTrigger(lvStyle,"Error",Bad,true);lvl.SetValue(FrameworkElement.StyleProperty,lvStyle);
            LogGrid.Columns.Add(new DataGridTemplateColumn{Header=Lang.T("Level"),Width=62,IsReadOnly=true,CellTemplate=new DataTemplate{VisualTree=lvl}});
            var acct=new DataGridTextColumn{Header=Lang.T("Account"),Binding=new Binding("Account"){Mode=BindingMode.OneWay},Width=150,IsReadOnly=true};acct.ElementStyle=LogTextStyle("Account");LogGrid.Columns.Add(acct);
            LogGrid.Columns.Add(new DataGridTextColumn{Header=Lang.T("Category"),Binding=new Binding("CategoryText"){Mode=BindingMode.OneWay},Width=92,IsReadOnly=true});
            var msg=new DataGridTextColumn{Header=Lang.T("Message"),Binding=new Binding("MessageText"){Mode=BindingMode.OneWay},Width=new DataGridLength(1,DataGridLengthUnitType.Star),MinWidth=200,IsReadOnly=true};msg.ElementStyle=LogTextStyle("MessageText");LogGrid.Columns.Add(msg);
        }
        static Style LogTextStyle(string tooltipPath)
        {
            var s=new Style(typeof(TextBlock));s.Setters.Add(new Setter(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis));if(!string.IsNullOrEmpty(tooltipPath))s.Setters.Add(new Setter(FrameworkElement.ToolTipProperty,new Binding(tooltipPath){Mode=BindingMode.OneWay}));return s;
        }
        static void LogLevelTrigger(Style s,string level,Brush brush,bool bold=false)
        {
            var t=new DataTrigger{Binding=new Binding("Level"),Value=level};t.Setters.Add(new Setter(TextBlock.ForegroundProperty,brush));if(bold)t.Setters.Add(new Setter(TextBlock.FontWeightProperty,FontWeights.Bold));s.Triggers.Add(t);
        }
        void RefreshLogAccounts()
        {
            int keep=LogAccountBox.SelectedIndex;
            var items=new List<string>{Lang.T("All")};
            foreach(var r in Rows)items.Add(string.IsNullOrEmpty(r.Alias)?r.Name:r.Alias);
            LogAccountBox.ItemsSource=items.ToArray();LogAccountBox.SelectedIndex=(keep>=0&&keep<items.Count)?keep:0;
            if(logView!=null)logView.Refresh();
        }
        bool LogMatches(object o)
        {
            var l=o as LogLine;if(l==null)return false;
            int li=LogLevelBox==null?0:LogLevelBox.SelectedIndex;if(li>0&&li<=LogClassifier.Levels.Length&&l.Level!=LogClassifier.Levels[li-1])return false;
            if(LogAccountBox!=null&&LogAccountBox.SelectedIndex>0){string sel=LogAccountBox.SelectedItem as string;string want=null;if(sel!=null){var row=Rows.FirstOrDefault(r=>r.Name==sel||r.Alias==sel);if(row!=null)want=row.Name;}if(want!=null&&l.Account!=want)return false;}
            string q=LogSearchBox==null?"":(LogSearchBox.Text??"").Trim();if(q.Length>0&&(l.Text??"").IndexOf(q,StringComparison.OrdinalIgnoreCase)<0)return false;
            return true;
        }
        void SetLogView(bool plain)
        {
            if(LogGrid!=null)LogGrid.Visibility=plain?Visibility.Collapsed:Visibility.Visible;
            if(LogBox!=null)LogBox.Visibility=plain?Visibility.Visible:Visibility.Collapsed;
            RebuildPlain();
            if(plain&&LogBox!=null)LogBox.ScrollToEnd();
        }
        // Plain text is only kept while the plain view is shown (review: an always-on string copy grew without bound on the UI thread).
        // It is rebuilt from logRows (capped at 2000) when the view is opened, then appended with AppendText (no full copy per line).
        bool PlainShown{get{return LogPlainBox!=null&&LogPlainBox.IsChecked==true;}}
        static string PlainLine(LogLine l){return l.Time.ToString("HH:mm:ss",CultureInfo.InvariantCulture)+"  "+Lang.Show(l.Text);}
        void AppendLogPlain(LogLine l)
        {
            if(LogBox==null||!PlainShown)return;
            if(LogBox.LineCount>2200){RebuildPlain();return;}
            LogBox.AppendText((LogBox.Text.Length==0?"":Environment.NewLine)+PlainLine(l));
        }
        void RebuildPlain(){if(LogBox==null)return;LogBox.Text=PlainShown?string.Join(Environment.NewLine,logRows.Select(PlainLine)):"";}
        void UpdateLogTabHeader(){if(logTab!=null)logTab.Header=LogUnseen>0?Lang.T("Logs")+" ("+LogUnseen+")":Lang.T("Logs");}
        void ScrollLogToEnd()
        {
            if(LogGrid!=null&&logRows.Count>0)LogGrid.ScrollIntoView(logRows[logRows.Count-1]);
            if(LogBox!=null&&LogPlainBox!=null&&LogPlainBox.IsChecked==true)LogBox.ScrollToEnd();
        }
        internal void AddLog(LogLine l)
        {
            if(l==null)return;
            while(logRows.Count>=2000)logRows.RemoveAt(0);
            logRows.Add(l);
            if((l.Level=="Warn"||l.Level=="Lock"||l.Level=="Error")&&Sections.SelectedIndex!=2)LogUnseen++;
            UpdateLogTabHeader();AppendLogPlain(l);
            if(LogAutoBox!=null&&LogAutoBox.IsChecked==true&&Sections.SelectedIndex==2)ScrollLogToEnd();
        }
        internal void RebuildLog(IEnumerable<LogLine> lines,string plainText)
        {
            logRows.Clear();if(lines!=null)foreach(var l in lines)if(l!=null)logRows.Add(l);
            if(LogBox!=null){RebuildPlain();LogBox.ScrollToEnd();}
            UpdateLogTabHeader();
        }
        // Display order for the account tables: leader first, then the clicked column (if any), then the original account order.
        // A full comparer (not SortDescriptions) because WPF's sort is not stable and would shuffle followers with equal keys.
        sealed class LeaderFirstOrder : System.Collections.IComparer
        {
            readonly IList<AccountRow> rows;readonly System.Reflection.PropertyInfo prop;readonly int sign;
            public LeaderFirstOrder(IList<AccountRow> rows,string path,ListSortDirection dir){this.rows=rows;prop=string.IsNullOrEmpty(path)?null:typeof(AccountRow).GetProperty(path);sign=dir==ListSortDirection.Descending?-1:1;}
            public int Compare(object x,object y)
            {
                var a=x as AccountRow;var b=y as AccountRow;if(a==null||b==null)return 0;
                int c=a.LeaderRank.CompareTo(b.LeaderRank);if(c!=0)return c;
                if(prop!=null){c=sign*System.Collections.Comparer.DefaultInvariant.Compare(prop.GetValue(a,null),prop.GetValue(b,null));if(c!=0)return c;}
                return rows.IndexOf(a).CompareTo(rows.IndexOf(b));
            }
        }
        // Column-header sort that keeps the leader row on top. Replaces DataGrid's default, which clears every SortDescription.
        void SortKeepingLeaderFirst(object sender,DataGridSortingEventArgs e)
        {
            var g=sender as DataGrid;var col=e.Column;if(g==null||col==null||string.IsNullOrEmpty(col.SortMemberPath))return;
            e.Handled=true;
            try{g.CommitEdit(DataGridEditingUnit.Row,true);}catch(InvalidOperationException){return;}
            var dir=col.SortDirection==ListSortDirection.Ascending?ListSortDirection.Descending:ListSortDirection.Ascending;
            foreach(var grid in new[]{AccountGrid,RiskGrid})foreach(var c in grid.Columns)if(c!=col)c.SortDirection=null;
            col.SortDirection=dir;
            ((ListCollectionView)view).CustomSort=new LeaderFirstOrder(Rows,col.SortMemberPath,dir);
        }
        // Re-sorts after the leader changes (Role is not a live-sorted property). Skipped while a cell is being edited.
        public void ResortLeaderFirst()
        {
            try{AccountGrid.CommitEdit(DataGridEditingUnit.Row,true);RiskGrid.CommitEdit(DataGridEditingUnit.Row,true);view.Refresh();}
            catch(InvalidOperationException){}
        }
        // Column order by importance (user request). Monitor: who -> is it safe (state, connection) -> exposure (qty, P/L, balance) ->
        // sizing and limits -> row actions on the far right. Settings: who -> sizing (Ratio, Max) -> limits -> distance to limits -> live values -> notes.
        internal static readonly string[] CopyColumnOrder={"On","Acct","Alias","State","Conn","Qty","Drift","P/L","Bal","Ratio","Tgt","DLL","ATM","Off","Flat"};
        internal static readonly string[] RiskColumnOrder={"Acct","Alias","Grp","Preset","Ratio","Max","Tgt","DLL","FlatLim","AutoRefollow","To Tgt","Loss left","P/L","Qty","CCY","St","Conn","Cons"};
        static void OrderColumns(DataGrid g,string[] keys)
        {
            int at=0;
            foreach(var k in keys){var c=g.Columns.FirstOrDefault(x=>ColumnKey(x)==k);if(c==null)continue;int from=g.Columns.IndexOf(c);if(from!=at)g.Columns.Move(from,at);at++;}
        }
        // Page 3 (Issues): locks, settings notes and the header notice, with Unlock all. Its own tab so the Monitor table keeps
        // the full height when many accounts are listed (user request). Appended last so the Monitor/Settings/Logs indexes do not move.
        TabItem issuesTab;int IssueCount;string issuesSignature;
        void BuildIssuesPage()
        {
            var page=new Grid{Tag="banner"};page.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});page.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
            var head=new DockPanel{Margin=new Thickness(0,0,0,10)};
            var unlock=TextTool(Lang.T("Unlock all"),Lang.T("Unlock every locked follower that is flat, connected and no longer at a risk limit (one confirmation). The others stay locked with their reason in Logs. Does NOT start copying."),"unlockAll",()=>Send("unlockAll"));
            DockPanel.SetDock(unlock,Dock.Left);head.Children.Add(unlock);
            issuesEmpty=Text(Lang.T("No locks or notes."),12,Muted);issuesEmpty.Margin=new Thickness(12,0,0,0);head.Children.Add(issuesEmpty);
            AddRow(page,head,0);
            issuesLines=new StackPanel{Tag="issues"};
            var scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Content=issuesLines};
            AddRow(page,scroll,1);
            issuesTab=new TabItem{Header=Lang.T("Issues"),Content=page};Sections.Items.Add(issuesTab);
        }
        TextBlock issuesEmpty;
        void UpdateIssuesTabHeader(){if(issuesTab==null)return;issuesTab.Header=IssueCount>0?Lang.T("Issues")+" ("+IssueCount+")":Lang.T("Issues");issuesTab.Foreground=IssueCount>0?Warn:null;}
        // Row 3 footer: counts, session text, advice and locked lines.
        void BuildFooter()
        {
            var footer=new DockPanel{Margin=new Thickness(0,10,0,0)};lockedLine=Text("",12,Bad);lockedLine.FontWeight=FontWeights.SemiBold;lockedLine.Margin=new Thickness(12,0,12,0);DockPanel.SetDock(lockedLine,Dock.Right);footer.Children.Add(lockedLine);adviceLine=Text("",12,Warn);adviceLine.Margin=new Thickness(12,0,12,0);DockPanel.SetDock(adviceLine,Dock.Right);footer.Children.Add(adviceLine);selection=Text("",12,Muted);DockPanel.SetDock(selection,Dock.Right);footer.Children.Add(selection);SessionText=Text(Lang.T("Disarmed"),11,Muted);SessionText.Margin=new Thickness(12,0,12,0);DockPanel.SetDock(SessionText,Dock.Right);footer.Children.Add(SessionText);summary=Text("",12,Muted);footer.Children.Add(summary);AddRow(this,footer,3);
        }
        void OnFollowRequested(AccountRow row,bool enabled){if(!demo&&FollowRow!=null)FollowRow(row,enabled);UpdateCards();}
        bool Matches(object value){var r=value as AccountRow;if(r==null)return false;string q=search.Text.Trim();if(q.Length>0&&!(r.Name+" "+r.Alias+" "+r.Group+" "+r.Preset).ToLowerInvariant().Contains(q.ToLowerInvariant()))return false;int f=filter.SelectedIndex;return f==1?r.Enabled:f==2?r.Paused||r.Latch.Locked:f==3?r.Status=="Target hit":true;}
        void Send(string c){if(demo||!DashboardSafety.CanExecute(c))return;var cmd=Command;if(cmd!=null)cmd(c);}
        public bool GroupByAccount{get{return view.GroupDescriptions.Count>0;}set{view.GroupDescriptions.Clear();if(value)view.GroupDescriptions.Add(new PropertyGroupDescription("Group"));if(groupBox!=null&&groupBox.IsChecked!=value)groupBox.IsChecked=value;}}
        public bool FocusSearch(){if(!search.IsEnabled)return false;Sections.SelectedIndex=0;search.Focus();search.SelectAll();return true;}
        public void SelectAllAccounts(){search.Clear();filter.SelectedIndex=0;view.Refresh();AccountGrid.SelectAll();}
        public void SelectVisible(){AccountGrid.UnselectAll();foreach(var r in view.Cast<AccountRow>())AccountGrid.SelectedItems.Add(r);}
        public void TickVisible(bool on){if(!editable)return;foreach(var r in view.Cast<AccountRow>().ToArray())if(r.CanFollow){r.Enabled=on;r.Changed();}UpdateCards();}

        public string SelectionSummary {get{return selection.Text;}}
        static IEnumerable<TextBox> Inputs(DependencyObject parent)
        {for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);var input=child as TextBox;if(input!=null)yield return input;foreach(var nested in Inputs(child))yield return nested;}}
        internal bool TryCommitSettings()
        {
            var inputs=Inputs(AccountGrid).Concat(Inputs(RiskGrid)).ToArray();
            foreach(var input in inputs){var binding=input.GetBindingExpression(TextBox.TextProperty);if(binding!=null)binding.UpdateSource();}
            return !inputs.Any(Validation.GetHasError)&&!Rows.Any(r=>r.HasDraftError)&&AccountGrid.CommitEdit(DataGridEditingUnit.Cell,true)&&AccountGrid.CommitEdit(DataGridEditingUnit.Row,true)&&RiskGrid.CommitEdit(DataGridEditingUnit.Cell,true)&&RiskGrid.CommitEdit(DataGridEditingUnit.Row,true)&&Rows.All(r=>r.Settings.Valid());
        }
        // Bulk settings: Tgt / DLL / ATM only, highlighted follower rows only. Returns null on success or a refusal reason.
        // Validates first and mutates nothing on refusal. Never touches Ratio, the follow tick, the latch or any engine path.
        public string BulkApply(string field,string text)
        {
            if(!editable)return "Locked while attached or on review hold";
            var targets=SelectedRows().Where(r=>r.CanFollow).ToArray();
            if(targets.Length==0)return "Highlight at least one follower account first";
            double amount=0;bool atm=false;int max=0;string t0=(text??"").Trim();
            if(field=="Ratio"){if(!(t0.Length>0&&double.TryParse(t0,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out amount)&&RiskPolicy.Finite(amount)&&amount>0))return "Ratio must be a plain positive number like 1 or 0.5: no commas, signs or text";}
            else if(field=="Max"){if(!(t0.Length>0&&int.TryParse(t0,NumberStyles.None,CultureInfo.InvariantCulture,out max)&&max>=1))return "Max must be a whole number >= 1";}
            else if(field=="Tgt"||field=="DLL"){if(!BulkEdit.TryParseAmount(text,out amount))return "Enter a plain amount like 1000 (0 = off); no commas, signs or text";}
            else if(field=="ATM"||field=="LimitFlat"){if(text=="on")atm=true;else if(text=="off")atm=false;else return "ATM value must be on or off";}
            else if(field=="Diverge"){if(!DivergenceChoice.IsValid(text))return "On divergence must be Notify or FlattenOnce";}
            else return "Unknown bulk field";
            foreach(var r in targets)
            {
                if(field=="Ratio")r.Ratio=amount;else if(field=="Max")r.MaxContracts=max;else if(field=="Tgt")r.Target=amount;else if(field=="DLL")r.Loss=amount;else if(field=="Diverge")r.OnDivergence=text;else if(field=="LimitFlat")r.FlatOnLimit=atm;else r.CopyAtm=atm;
                r.Changed();
            }
            UpdateCards();return null;
        }
        // One-shot copy of the leader row's Tgt / DLL / ATM onto highlighted followers (or every visible follower when none are
        // highlighted). Not a live link: later edits on either side do not propagate. Ratio, ticks and all other fields stay put.
        public string BulkFromLeader()
        {
            if(!editable)return "Locked while attached or on review hold";
            var leader=Rows.FirstOrDefault(r=>r.Role=="Leader");
            if(leader==null)return "No leader row to copy from";
            var picked=SelectedRows().Where(r=>r.CanFollow).ToArray();
            var targets=picked.Length>0?picked:view.Cast<AccountRow>().Where(r=>r.CanFollow).ToArray();
            if(targets.Length==0)return "No follower accounts to update";
            double tgt=leader.Target,dll=leader.Loss;bool atm=leader.CopyAtm;
            if(!RiskPolicy.Finite(tgt)||!RiskPolicy.Finite(dll)||tgt<0||dll<0)return "Leader Tgt / DLL are not valid amounts";
            string div=leader.OnDivergence;bool lf=leader.FlatOnLimit;foreach(var r in targets){r.Target=tgt;r.Loss=dll;r.CopyAtm=atm;r.OnDivergence=div;r.FlatOnLimit=lf;r.Changed();}
            UpdateCards();return null;
        }
        public void ClearLog(){LogBox.Text="";logRows.Clear();LogUnseen=0;UpdateLogTabHeader();}
        public bool BulkBarPresent{get{return bulkBar!=null&&RiskGrid.Parent!=null;}}
        internal Action<string> BulkNotice;
        void BulkLeaderClick(){var reason=BulkFromLeader();var n=BulkNotice;if(n!=null)n(reason==null?"Copied leader Tgt / DLL / ATM to followers (one-time); Ratio and ticks unchanged. Edit any row to fine-tune.":"From Leader refused: "+reason);}
        void BulkClick(string field,string text){var reason=BulkApply(field,text);var n=BulkNotice;if(n!=null)n(reason==null?"Bulk "+field+" applied to highlighted followers; follow ticks unchanged.":"Bulk "+field+" refused: "+reason);}
        public void SetSelectedPause(bool paused){foreach(var r in SelectedRows().Where(r=>r.CanFollow)){if(!demo&&PauseRow!=null)PauseRow(r,paused);else{r.Paused=paused;r.Changed();}}UpdateCards();}
        public AccountRow[] SelectedRows(){return (Sections.SelectedIndex==1?RiskGrid:AccountGrid).SelectedItems.Cast<AccountRow>().ToArray();}
        public void UpdateCards(){if(summary==null)return;int ticked=Rows.Count(r=>r.Enabled&&r.Role!="Leader");FollowCountText.Text=Lang.F("→  {0} followers  ",ticked);var ccy=Rows.Select(r=>r.Currency).Where(s=>!string.IsNullOrEmpty(s)).Distinct().ToArray();summary.Text=Lang.F("{0} TICKED  /  {1} ACCTS     ·     P/L  {2}",ticked,Rows.Count,ccy.Length<=1?MonitorSummary.Money(Rows.Sum(r=>RiskPolicy.Finite(r.Pnl)?r.Pnl:0),ccy.FirstOrDefault()):Lang.T("Mixed CCY"));selection.Text=Lang.F("{0} sel / {1} rows",SelectedRows().Length,VisibleCount);if(adviceLine!=null)adviceLine.Text=AdviceText;if(lockedLine!=null)lockedLine.Text=LockedText;UpdateHeaderState();UpdateMonitor();}
        internal bool TrySetSessionState(bool attached,string status,int callbackSession,int currentSession,bool currentAttached){if(callbackSession!=currentSession||attached!=currentAttached)return false;SetSessionState(attached,status);return true;}
        // Last session state shown (true = attached to a copy session). Read by the header badge (WP1).
        internal bool SessionAttached{get;private set;}
        internal void SetSessionState(bool attached,string status){SessionAttached=attached;if(!attached)foreach(var row in Rows)row.InvalidateSessionActivity();SessionText.Text=DashboardSafety.CanExecute("start")?Lang.Show(status):(attached?Lang.T("REVIEW HOLD · unexpected attachment"):Lang.T("Disarmed"));SessionText.ToolTip=Lang.Show(status);Editable(!attached);UpdateCards();}
        internal void Editable(bool canEdit){editable=canEdit;LeaderBox.IsEnabled=canEdit;InstrumentBox.IsEnabled=canEdit;ShowLive.IsEnabled=canEdit&&!demo;AccountGrid.IsReadOnly=!canEdit;RiskGrid.IsReadOnly=!canEdit;foreach(var b in editButtons)b.IsEnabled=canEdit;
#if KINFX_SIM_TEST
            if(DryRunBox!=null)DryRunBox.IsEnabled=canEdit;
#endif
            StartButton.IsEnabled=canEdit&&!demo&&DashboardSafety.CanExecute("start");EditableHeader(canEdit);EditableMonitor(canEdit);EditableSettings(canEdit);EditableLogs(canEdit);}
    }
    public class KINFXTradeCopier : AddOnBase
    {
        readonly Dictionary<Window,Tuple<NTMenuItem,NTMenuItem>> menus=new Dictionary<Window,Tuple<NTMenuItem,NTMenuItem>>();
        protected override void OnStateChange() {if(State==State.SetDefaults){Name="KINFXTradeCopier";Description="KINFX multi-account simulation copier with local risk Dashboard";}else if(State==State.Active)Live=this;else if(State==State.Terminated&&ReferenceEquals(Live,this))Live=null;}
        // The active AddOn instance: email alerts go through its NinjaScript.SendMail (NinjaTrader's default email share service).
        internal static volatile KINFXTradeCopier Live;
        internal void Mail(string to,string subject,string body){SendMail(to,subject,body);}
        protected override void OnWindowCreated(Window window)
        {
            if(!(window is ControlCenter))return;var parent=window.FindFirst("ControlCenterMenuItemNew") as NTMenuItem;if(parent==null)return;
            var item=new NTMenuItem {Header="KINFX Trade Copier",Style=Application.Current.TryFindResource("MainMenuItem") as Style};item.Click+=Open;parent.Items.Add(item);menus[window]=Tuple.Create(parent,item);
        }
        void Open(object s,RoutedEventArgs e) {Core.Globals.RandomDispatcher.BeginInvoke(new Action(()=>new KINFXTradeCopierWindow().Show()));}
        protected override void OnWindowDestroyed(Window window) {Tuple<NTMenuItem,NTMenuItem> pair;if(menus.TryGetValue(window,out pair)){pair.Item2.Click-=Open;pair.Item1.Items.Remove(pair.Item2);menus.Remove(window);}}
    }
    public class KINFXTradeCopierWindow : NTWindow,IWorkspacePersistence
    {
        public KINFXTradeCopierWindow()
        {
            Caption="KINFX Trade Copier";Width=1160;Height=840;MinWidth=960;MinHeight=560;
            var tabs=new TabControl();TabControlManager.SetIsMovable(tabs,false);TabControlManager.SetCanAddTabs(tabs,false);TabControlManager.SetCanRemoveTabs(tabs,false);TabControlManager.SetFactory(tabs,new KINFXTradeCopierWindowFactory());Content=tabs;tabs.AddNTTabPage(new KINFXTradeCopierTab());
            Loaded+=(s,e)=>{if(WorkspaceOptions==null)WorkspaceOptions=new WorkspaceOptions("KINFX-"+Guid.NewGuid().ToString("N"),this);};
            Closing+=(s,e)=>{foreach(var tab in tabs.Items.OfType<KINFXTradeCopierTab>())if(!tab.CanClose()){e.Cancel=true;MessageBox.Show(this,Lang.T("Close blocked: copier still manages positions / working orders. Guarded pause keeps exits managed. Flatten or resolve the selected instrument, then Stop when flat."),"KINFX safety",MessageBoxButton.OK,MessageBoxImage.Warning);break;}};
        }
        public WorkspaceOptions WorkspaceOptions {get;set;}
        public void Restore(XDocument document,XElement element) {if(MainTabControl!=null)MainTabControl.RestoreFromXElement(element);}
        public void Save(XDocument document,XElement element) {if(MainTabControl!=null)MainTabControl.SaveToXElement(element);}
    }
    public sealed class RiskSettingsDialog : Window
    {
        public RiskSettings Result {get;private set;}
        internal ComboBox FirmChoice {get{return firm;}}
        internal ComboBox PlanChoice {get{return plan;}}
        internal ComboBox SizeChoice {get{return size;}}
        internal RiskSettings CurrentDraft {get{return draft.Settings;}}
        internal void LoadSelectedPreset() {var p=SelectedPreset();if(p==null)return;draft.Load(p.Apply(draft.Settings,root,optional.IsChecked==true));LoadFields();}
        readonly Dictionary<string,TextBox> boxes=new Dictionary<string,TextBox>();
        readonly CheckBox optional,trail;
        readonly ComboBox firm,plan,size;
        readonly TextBlock notes;
        readonly RiskSettingsDraft draft;
        readonly string root;
        public RiskSettingsDialog(RiskSettings initial,string instrumentRoot,int accountCount)
        {
            draft=new RiskSettingsDraft(initial);root=instrumentRoot;
            Title="Risk settings — "+accountCount+" account(s)";Width=670;Height=840;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=DashboardView.Canvas;Foreground=DashboardView.Ink;
            var dock=new DockPanel {Margin=new Thickness(20)};Content=dock;
            var buttons=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(buttons,Dock.Bottom);dock.Children.Add(buttons);
            var apply=new Button {Content="Apply to "+accountCount+" account(s)",Padding=new Thickness(15,8,15,8),Margin=new Thickness(0,0,8,0)};buttons.Children.Add(apply);apply.Click+=(s,e)=>Apply();var cancel=new Button {Content=Lang.T("Cancel"),Padding=new Thickness(15,8,15,8)};buttons.Children.Add(cancel);cancel.Click+=(s,e)=>Close();
            var panel=new StackPanel();dock.Children.Add(new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
            panel.Children.Add(new TextBlock {Text=Lang.T("Local risk controls"),FontSize=25,Margin=new Thickness(0,0,0,5)});
            panel.Children.Add(new TextBlock {Text=Lang.T("All values editable. Zero monetary limits = OFF. Values in account denomination. No compliance guarantee, no historical high inferred. Batch overwrites the shown settings on every selected account."),Foreground=DashboardView.Muted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,16)});
            var presets=new WrapPanel();firm=PresetCombo(160);plan=PresetCombo(230);size=PresetCombo(95);presets.Children.Add(firm);presets.Children.Add(plan);presets.Children.Add(size);panel.Children.Add(presets);
            firm.ItemsSource=new[]{"Custom"}.Concat(PresetCatalog.All.Select(p=>p.Firm).Distinct().OrderBy(s=>s));firm.SelectedIndex=0;
            firm.SelectionChanged+=(s,e)=>{plan.ItemsSource=PresetCatalog.All.Where(p=>p.Firm==(string)firm.SelectedItem).Select(p=>p.Plan).Distinct().ToArray();plan.SelectedIndex=0;};
            plan.SelectionChanged+=(s,e)=>{size.ItemsSource=PresetCatalog.All.Where(p=>p.Firm==(string)firm.SelectedItem&&p.Plan==(string)plan.SelectedItem).Select(p=>p.Size).ToArray();size.SelectedIndex=0;};
            notes=new TextBlock {Text=Lang.T("Custom: confirm your absolute floor and maximum instrument quantity manually."),Foreground=DashboardView.Muted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,10)};panel.Children.Add(notes);
            size.SelectionChanged+=(s,e)=>{var p=SelectedPreset();notes.Text=p==null?"Custom settings":p.Notes;};
            optional=new CheckBox {Template=DashboardView.PlainCheckTemplate(),Content=Lang.T("Explicitly apply optional / soft DLL if source has an amount"),Foreground=DashboardView.Muted,Margin=new Thickness(0,6,0,8)};panel.Children.Add(optional);
            var load=new Button {Content=Lang.T("Load selected template into editable fields"),Padding=new Thickness(10,6,10,6),HorizontalAlignment=HorizontalAlignment.Left};panel.Children.Add(load);load.Click+=(s,e)=>{var p=SelectedPreset();if(p==null)return;LoadSelectedPreset();if(!new[]{"MES","MNQ","M2K","MYM","ES","NQ","RTY","YM"}.Contains(root)||p.RawContracts=="—")MessageBox.Show(this,Lang.T("Source mini cap cannot be safely mapped. Set MaxContracts manually for your exact instrument / current scaling."),Lang.T("Manual quantity required"));};
            foreach(string field in new[]{"Alias","Group","Ratio","Target","Loss","MaxContracts","Floor","Baseline","TrailDistance","HighWater","BestDay","TotalProfit","Consistency"})
            {
                var row=new Grid {Margin=new Thickness(0,7,0,0)};row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(260)});row.ColumnDefinitions.Add(new ColumnDefinition());
                string label=field=="Target"?"Daily target (local observed P&L)":field=="Loss"?"Daily loss (positive amount)":field=="Floor"?"Confirmed absolute equity floor":field=="Baseline"?"P&L telemetry baseline (editable)":field=="HighWater"?"User / observed local high water":field=="MaxContracts"?"Max exact-instrument contracts":field;
                row.Children.Add(new TextBlock {Text=label,VerticalAlignment=VerticalAlignment.Center,Foreground=DashboardView.Muted});var b=new TextBox {Padding=new Thickness(7,4,7,4),Background=DashboardView.Panel,Foreground=DashboardView.Ink,BorderBrush=DashboardView.Line};Grid.SetColumn(b,1);row.Children.Add(b);boxes[field]=b;b.TextChanged+=(sender,args)=>{draft.Set(field,b.Text);b.BorderBrush=draft.IsInvalid(field)?DashboardView.Bad:DashboardView.Line;b.ToolTip=draft.IsInvalid(field)?Lang.T("Invalid value — retained until corrected; Apply is blocked."):null;};panel.Children.Add(row);
            }
            trail=new CheckBox {Template=DashboardView.PlainCheckTemplate(),Content=Lang.T("Enable LOCAL intraday high-water guard (not firm EOD calculation)"),Foreground=DashboardView.Muted,Margin=new Thickness(0,12,0,6),IsChecked=draft.Settings.LocalTrail};panel.Children.Add(trail);
            panel.Children.Add(new TextBlock {Text=Lang.T("BestDay / TotalProfit are supplied history, used for consistency advisory only. To unlock a persisted latch use the separate confirmed reset while disarmed. Baseline is never automatically reset on restart."),Foreground=DashboardView.Muted,TextWrapping=TextWrapping.Wrap});LoadFields();
        }
        static ComboBox PresetCombo(double width) {return DashboardView.Combo(width);}
        Preset SelectedPreset() {return PresetCatalog.All.FirstOrDefault(p=>p.Firm==(string)firm.SelectedItem&&p.Plan==(string)plan.SelectedItem&&p.Size==(string)size.SelectedItem);}
        void LoadFields() {foreach(var item in boxes){item.Value.Text=draft.Text(item.Key);}if(trail!=null)trail.IsChecked=draft.Settings.LocalTrail;}
        void Apply()
        {
            try
            {
                RiskSettings next=draft.Build(trail.IsChecked==true);
                Result=next;DialogResult=true;
            }
            catch(Exception ex){MessageBox.Show(this,ex.Message,Lang.T("Invalid settings"),MessageBoxButton.OK,MessageBoxImage.Warning);}
        }
    }
    // Reflect NT8 Position.AveragePrice and MasterInstrument.TickSize before any consistency read. Missing contract → Unavailable, never a zero.
    static class PlatformPositionContract
    {
        static bool probed,ready;
        static System.Reflection.PropertyInfo averagePrice,tickSize;
        public static bool Ready { get { return Probe(); } }
        public static bool Probe()
        {
            if(probed)return ready;
            probed=true;ready=false;
            try
            {
                var avg=typeof(Position).GetProperty("AveragePrice",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public);
                var tick=typeof(MasterInstrument).GetProperty("TickSize",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public);
                ready=PositionConsistency.ContractsReady(IsDoubleReader(avg),IsDoubleReader(tick));
                if(ready){averagePrice=avg;tickSize=tick;}
            }
            catch{ready=false;}
            return ready;
        }
        static bool IsDoubleReader(System.Reflection.PropertyInfo property)
        {
            return property!=null&&property.CanRead&&property.PropertyType==typeof(double)&&property.GetIndexParameters().Length==0;
        }
        public static bool TryTickSize(Instrument instrument,out double tick)
        {
            tick=0;
            if(!Probe()||instrument==null||instrument.MasterInstrument==null||tickSize==null)return false;
            object value;
            try{value=tickSize.GetValue(instrument.MasterInstrument,null);}catch{return false;}
            if(!(value is double))return false;
            double n=(double)value;
            if(!RiskPolicy.Finite(n)||n<=0)return false;
            tick=n;return true;
        }
        public static bool TryRead(Account account,Instrument instrument,out int signedQty,out double average,out bool hasAverage)
        {
            signedQty=0;average=0;hasAverage=false;
            if(!Probe()||averagePrice==null||account==null||instrument==null)return false;
            Position position;object value;
            lock(account.Positions)
            {
                position=account.Positions.FirstOrDefault(x=>x.Instrument!=null&&x.Instrument.FullName==instrument.FullName);
                if(position==null)return true;
                signedQty=position.Quantity*(position.MarketPosition==MarketPosition.Long?1:position.MarketPosition==MarketPosition.Short?-1:0);
                if(signedQty==0)return true;
                // Same lock as the quantity, so a fill between the two reads cannot pair a new average with an old quantity.
                try{value=averagePrice.GetValue(position,null);}catch{return true;}
            }
            if(value is double)
            {
                double n=(double)value;
                if(RiskPolicy.Finite(n)&&n>0){average=n;hasAverage=true;}
            }
            return true;
        }
    }
    public class KINFXTradeCopierTab : NTTabPage
    {
        DashboardView dashboard; // replaced when the language changes (see ChangeLanguage)
        readonly CopierEngine engine;
        readonly DispatcherTimer timer;
        readonly Queue<LogLine> logs=new Queue<LogLine>();
        const int LogCap=2000;
        bool logFileErrorLogged=false;
        // Plain-text form of the in-memory queue (translated when drawn, so history follows the picked language).
        string LogText(){var sb=new System.Text.StringBuilder();foreach(var l in logs)sb.Append(l.Time.ToString("HH:mm:ss",CultureInfo.InvariantCulture)).Append("  ").Append(Lang.Show(l.Text)).AppendLine();return sb.ToString();}
        internal void RedrawLog(){dashboard.RebuildLog(logs,LogText());}
        // Daily log lines are buffered and written once per timer tick (and on close), not one open/write/close per line on the UI thread.
        // Each line goes to the file of its own date, so a line logged at 23:59:59 and flushed after midnight stays in that day's file.
        readonly List<LogLine> dailyPending=new List<LogLine>();
        void AppendDailyFile(LogLine line)
        {
            if(logFileErrorLogged||line==null)return;
            dailyPending.Add(line);
            if(dailyPending.Count>=500)FlushDailyFile();
        }
        readonly object closeFileLock=new object();
        void CloseTimeFileLine(string message)
        {
            if(logFileErrorLogged)return;
            try
            {
                var line=LogClassifier.Classify(message??"",new string[0]);line.Time=DateTime.Now;
                string dir=Path.Combine(DirectoryPath,"logs");
                lock(closeFileLock){Directory.CreateDirectory(dir);foreach(var file in LogClassifier.DailyFiles(new[]{line}))File.AppendAllText(Path.Combine(dir,file.Key),file.Value,new System.Text.UTF8Encoding(false));}
            }
            catch{}
        }
        void FlushDailyFile()
        {
            lock(closeFileLock)FlushDailyFileLocked();
        }
        void FlushDailyFileLocked()
        {
            if(dailyPending.Count==0)return;
            var batch=dailyPending.ToArray();dailyPending.Clear();
            if(logFileErrorLogged)return;
            try
            {
                string dir=Path.Combine(DirectoryPath,"logs");Directory.CreateDirectory(dir);
                foreach(var file in LogClassifier.DailyFiles(batch))
                    File.AppendAllText(Path.Combine(dir,file.Key),file.Value,new System.Text.UTF8Encoding(false));
            }
            catch(Exception ex)
            {
                logFileErrorLogged=true;
                var err=LogClassifier.Classify("Log file write failed (daily log disabled for this session): "+ex.Message,dashboard.Rows.Select(r=>r.Name));err.Time=DateTime.Now;
                logs.Enqueue(err);while(logs.Count>LogCap)logs.Dequeue();dashboard.AddLog(err);
            }
        }
        void ExportLogFile()
        {
            try
            {
                string dir=Path.Combine(DirectoryPath,"logs");Directory.CreateDirectory(dir);
                string file=Path.Combine(dir,"export-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".txt");
                var names=dashboard.Rows.Select(r=>r.Name).ToArray();
                var sb=new System.Text.StringBuilder();
                foreach(var l in logs)sb.Append(l.Time.ToString("yyyy-MM-dd HH:mm:ss")).Append("  ").Append(l.Level).Append("  ").Append(LogExport.Mask(l.Text,names)).AppendLine();
                File.WriteAllText(file,sb.ToString(),new System.Text.UTF8Encoding(false));
                Log("Log exported to "+file+" (account names masked).");
            }
            catch(Exception ex){Log("Log export failed: "+ex.Message);}
        }
        // ---- Email alerts: notify.txt + NinjaTrader's default email share service (no password or SMTP server in KINFX). ----
        string NotifyPath {get{return Path.Combine(DirectoryPath,"notify.txt");}}
        NotifyPrefs.Settings notify=NotifyPrefs.Defaults();readonly NotifyPrefs.Limiter notifyLimiter=new NotifyPrefs.Limiter();
        static NotifyPrefs.Settings LoadNotify(string path){try{if(File.Exists(path))return NotifyPrefs.Parse(File.ReadAllLines(path));}catch{}return NotifyPrefs.Defaults();}
        void SaveNotify(NotifyPrefs.Settings s)
        {
            if(s==null)return;
            if(s.To.Length>0&&!NotifyPrefs.IsAddress(s.To)){SetNotifyStatus(Lang.T("Not saved: enter one plain email address like name@example.com"),true);return;}
            try{Directory.CreateDirectory(DirectoryPath);SettingsStore.AtomicWriteLines(NotifyPath,NotifyPrefs.Serialize(s));notify=s;Log(s.Enabled?"Email alerts on: "+string.Join(", ",NotifyPrefs.Kinds.Where(k=>s.On.Contains(k))):"Email alerts off.");SetNotifyStatus(Lang.T(s.Enabled?"Saved. Alerts are on.":"Saved. Alerts are off."),false);}
            catch(Exception ex){Log("Settings NOT saved: "+ex.Message);SetNotifyStatus(Lang.F("Settings NOT saved: {0}",ex.Message),true);}
        }
        void SetNotifyStatus(string text,bool bad){if(dashboard!=null&&dashboard.NotifyStatus!=null){dashboard.NotifyStatus.Text=text;dashboard.NotifyStatus.Foreground=bad?DashboardView.Bad:DashboardView.Muted;}}
        void TestNotify(NotifyPrefs.Settings s)
        {
            if(s==null||!NotifyPrefs.IsAddress(s.To)){SetNotifyStatus(Lang.T("Enter one plain email address like name@example.com first."),true);return;}
            SendNotifyMail(s.To,"KINFX Trade Copier: test","Test mail from KINFX Trade Copier through NinjaTrader's email share service. No order was sent.",true);
        }
        // Called for every recorded log line on the UI thread. Mail is handed to NinjaTrader off the UI thread; a failure is logged once
        // (as Warn, so it never mails itself) and never affects copying.
        void NotifyFor(LogLine line)
        {
            var mail=NotifyPrefs.Decide(notify,notifyLimiter,line,dashboard.Rows.Select(r=>r.Name),DateTime.Now);
            if(mail!=null)SendNotifyMail(notify.To,mail.Subject,mail.Body,false);
        }
        DateTime notifyFailLoggedAt=DateTime.MinValue;
        void SendNotifyMail(string to,string subject,string body,bool test)
        {
            System.Threading.Tasks.Task.Run(()=>
            {
                string err=null;
                try{var live=KINFXTradeCopier.Live;if(live==null)throw new InvalidOperationException("KINFX AddOn is not active in NinjaTrader");live.Mail(to,subject,body);}
                catch(Exception ex){err=ex.GetBaseException().Message;}
                Dispatch(()=>
                {
                    if(err==null){if(test){Log("Email test handed to NinjaTrader for "+LogExport.Mask(to,new string[0])+"; check the inbox and NinjaTrader's log.");SetNotifyStatus(Lang.T("Test handed to NinjaTrader. Check your inbox; if nothing arrives, check Tools > Options > General > Share services."),false);}}
                    else{if(test||DateTime.Now-notifyFailLoggedAt>=TimeSpan.FromMinutes(10)){if(!test)notifyFailLoggedAt=DateTime.Now;Log("Email alert not sent (blocked by NinjaTrader share service): "+err);}if(test)SetNotifyStatus(Lang.T("Email not sent: ")+err,true);}
                });
            });
        }
        readonly Dictionary<string,SavedAccount> saved=new Dictionary<string,SavedAccount>();
        string instanceId=Guid.NewGuid().ToString("N"),zone="Central Standard Time",lastDay="";
        int resetHour=17,persistTicks,avgWarnTicks=DisplayPrefs.DefaultAvgWarnTicks;
        bool cleaned,loading,settingsCorrupt;
        // Set while Cleanup runs: Log() then records synchronously (the dispatcher queue is not drained after close), so close-time lines reach the daily file.
        bool closing;
        string leaderName="";
        readonly HashSet<string> estimatedLogged=new HashSet<string>(StringComparer.Ordinal);
        static bool ntVersionLogged;
        public KINFXTradeCopierTab()
        {
            TabName="Dashboard";Lang.Set(LoadLanguage(LanguagePath));askSkipped=LoadAsk(AskPath);dashboard=new DashboardView();Content=dashboard;
            engine=new CopierEngine(Log,OnTelemetry,(attached,status)=>{int session=engine.SessionGeneration;Dispatch(()=>dashboard.TrySetSessionState(attached,status,session,engine.SessionGeneration,engine.Attached));},PersistLatch);
            avgWarnTicks=LoadAvgWarnTicks();dashboard.SetAvgWarnTicks(avgWarnTicks);
            WireDashboard();
            LoadDefault();Refresh();syncNames=CurrentAccountNames();
            timer=new DispatcherTimer(TimeSpan.FromSeconds(1),DispatcherPriority.Background,(s,e)=>{Tick();FlushDailyFile();},Dispatcher);timer.Start();
        }
        // Everything that attaches this tab to a DashboardView. Repeated for a rebuilt view after a language change.
        void WireDashboard()
        {
            dashboard.LanguageChanging=ChangeLanguage;dashboard.Command=Command;dashboard.BulkNotice=Log;dashboard.PauseRow=Pause;dashboard.RowSettings=r=>Edit(new[]{r});dashboard.FollowRow=Follow;dashboard.FlatRow=FlatOne;ShowAsk();dashboard.AskChanged=SetAsk;
            notify=LoadNotify(NotifyPath);dashboard.ShowNotify(notify);dashboard.NotifySave=SaveNotify;dashboard.NotifyTest=TestNotify;
            dashboard.ApplyColumns(LoadColumns(ColumnsPath));dashboard.ColumnsChanged=SaveColumns;
            dashboard.AvgWarnChanged=ticks=>{avgWarnTicks=ticks;try{DisplayPrefs.SaveAvgWarnTicks(DisplayPrefsPath,ticks);}catch(Exception ex){Log(string.Format(CultureInfo.InvariantCulture,PositionConsistency.NotSaved,ex.Message));}RefreshPositionConsistencySafe();};
            dashboard.LeaderBox.SelectionChanged+=(s,e)=>{if(engine.Attached)return;leaderName=dashboard.LeaderBox.SelectedItem as string ?? "";foreach(var row in dashboard.Rows){row.Role=row.Name==leaderName?"Leader":"Follower";if(row.Role=="Leader")row.Enabled=false;row.Changed();}dashboard.ResortLeaderFirst();};
            dashboard.AccountGrid.RowEditEnding+=(s,e)=>Dispatch(()=>{foreach(var row in dashboard.Rows){row.Changed();}dashboard.UpdateCards();});
            dashboard.InstrumentBox.LostKeyboardFocus+=(s,e)=>NormalizeInstrumentBox();
            dashboard.InstrumentBox.KeyDown+=(s,e)=>{if(e.Key==System.Windows.Input.Key.Enter){NormalizeInstrumentBox();e.Handled=true;}};
            WireWp1();WireWp2();WireWp3();
        }
        // Work-package wiring hooks: each package fills only its own method.
        void WireWp1()  // WP1 instrument picker / header
        {
            dashboard.InstrumentPicked+=()=>{if(engine.Attached)return;RecordRecent(dashboard.InstrumentBox.Text);NormalizeInstrumentBox();dashboard.UpdateCards();};
            dashboard.RecentRoots=LoadRecent;
            string actual="";
            try{var v=NinjaTrader.Core.Globals.ProductVersion;if(v!=null)actual=v.ToString();}catch{}
            dashboard.HeaderNotice=NtVersion.Notice(actual);
            if(!string.IsNullOrEmpty(dashboard.HeaderNotice)&&!ntVersionLogged){ntVersionLogged=true;Log(dashboard.HeaderNotice);}
        }
        //
        //
        //
        void WireWp2(){}  // WP2 monitor + settings pages (all wiring lives in the view constructor / build methods)
        //
        //
        //
        void WireWp3(){dashboard.ExportLog=ExportLogFile;dashboard.ClearQueue=()=>logs.Clear();}  // WP3 log page
        //
        //
        //
        // Switching language rebuilds the view (all labels are created once). Rows, ticks, drafts and the log are carried over;
        // nothing reaches the engine, and it is refused while a session is attached so a running copy is never disturbed.
        bool ChangeLanguage(string code)
        {
            if(!Lang.IsValid(code))return false;
            if(engine.Attached){Log("Language can only be changed while stopped (not attached to a session).");return false;}
            if(!dashboard.TryCommitSettings()){Log("Settings NOT saved: correct invalid per-account Ratio / row values first.");return false;}
            Persist();SaveLanguage(code);Lang.Set(code);
            string instrument=dashboard.InstrumentBox.Text;int tab=dashboard.Sections.SelectedIndex;
            dashboard=new DashboardView();Content=dashboard;WireDashboard();
            dashboard.InstrumentBox.Text=instrument;dashboard.SetAvgWarnTicks(avgWarnTicks);
            loading=false;Refresh();syncNames=CurrentAccountNames();
            dashboard.Sections.SelectedIndex=Math.Max(0,Math.Min(tab,dashboard.Sections.Items.Count-1));
            RedrawLog();dashboard.UpdateCards();
            Log(Lang.F("Language changed to {0}.",Lang.Names[Array.IndexOf(Lang.Codes,code)]));
            return true;
        }
        void Dispatch(Action action) {if(!cleaned)Dispatcher.BeginInvoke(action);}
        string DirectoryPath {get {return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"NinjaTrader 8","templates","KINFXTradeCopier");}}
        string LanguagePath {get{return Path.Combine(DirectoryPath,"language.txt");}}
        string DisplayPrefsPath {get{return Path.Combine(DirectoryPath,"display.json");}}
        int LoadAvgWarnTicks()
        {
            try
            {
                if(!File.Exists(DisplayPrefsPath))return DisplayPrefs.DefaultAvgWarnTicks;
                int n;
                if(DisplayPrefs.TryReadAvgWarnTicks(File.ReadAllText(DisplayPrefsPath),out n))return n;
                Log(PositionConsistency.PrefIgnored);
            }
            catch{Log(PositionConsistency.PrefIgnored);}
            return DisplayPrefs.DefaultAvgWarnTicks;
        }
        // Saved choice wins; otherwise follow the Windows UI language (zh-* / ja / ko, anything else English). A missing or bad file never blocks anything.
        static string LoadLanguage(string path){try{if(File.Exists(path)){string c=File.ReadAllText(path).Trim().ToLowerInvariant();if(Lang.IsValid(c))return c;}}catch{}return Lang.Detect(System.Globalization.CultureInfo.InstalledUICulture.Name,System.Globalization.CultureInfo.CurrentCulture.Name,System.Globalization.CultureInfo.CurrentUICulture.Name);}
        void SaveLanguage(string code){try{Directory.CreateDirectory(DirectoryPath);File.WriteAllText(LanguagePath,code);}catch(Exception ex){Log("Settings NOT saved: "+ex.Message);}}
        string SettingsPath {get {return Path.Combine(DirectoryPath,instanceId+".xml");}}
        string LatchPath {get{return Path.Combine(DirectoryPath,"risk-latches.xml");}}
        void PersistLatch(string account,string reason)
        {
            // Synchronous atomic journal BEFORE the engine continues. Restart/window changes cannot clear this latch.
            try {SettingsStore.SaveLatch(LatchPath,account,reason);}
            catch(Exception ex){engine.GuardedPause();Log("DURABLE LATCH WRITE FAILED: "+account+" / "+ex.Message+". Entries paused; do not restart until reviewed.");throw;}
        }
        void MergeLatches()
        {
            foreach(var pair in SettingsStore.ReadLatches(LatchPath))
            {SavedAccount item;if(!saved.TryGetValue(pair.Key,out item))saved[pair.Key]=item=new SavedAccount {Name=pair.Key};item.Latch.Locked=true;item.Latch.Reason=pair.Value;}
        }
        void LoadDefault()
        {
            string index=Path.Combine(DirectoryPath,"last-workspace.xml");
            try {if(File.Exists(index)){var x=XElement.Load(index);string id=(string)x.Attribute("id");Guid g;if(Guid.TryParseExact(id,"N",out g)){string file=Path.Combine(DirectoryPath,id+".xml");if(File.Exists(file))ApplyXml(XElement.Load(file));}}}
            catch(Exception ex){settingsCorrupt=true;Log("Settings index unreadable — Start blocked. "+ex.Message);}
        }
        void LoadDisk() {if(File.Exists(SettingsPath))ApplyXml(XElement.Load(SettingsPath));}
        void ApplyXml(XElement x)
        {
            var records=SettingsStore.FromXml(x);saved.Clear();foreach(var r in records)saved[r.Name]=r;
            dashboard.InstrumentBox.Text=(string)x.Attribute("instrument")??"MES 12-26";
            
            string z=(string)x.Attribute("zone")??"Central Standard Time";TimeZoneInfo.FindSystemTimeZoneById(z);zone=z;
            resetHour=(int?)x.Attribute("resetHour")??17;if(resetHour<0||resetHour>23)throw new InvalidDataException(Lang.T("Reset hour outside 0–23"));
            lastDay=(string)x.Attribute("lastDay")??"";boundaryMode=BoundaryMode.Parse((string)x.Attribute("boundary"));leaderName=(string)x.Attribute("leader")??"";
            // Enabled, pause/arm and live permissions are intentionally NOT persisted.
        }
        void Refresh()
        {
            if(engine.Attached){Log("Refresh blocked while managing a session.");return;}
            if(loading)return;loading=true;
            try
            {
                foreach(var r in dashboard.Rows)saved[r.Name]=Record(r);MergeLatches();
                Account[] accounts;lock(Account.All)accounts=Account.All.Where(a=>dashboard.ShowLive.IsChecked==true||CopierEngine.Listable(a)).OrderBy(a=>a.Name).ToArray();
                dashboard.Rows.Clear();
                foreach(var a in accounts)
                {
                    SavedAccount stored;saved.TryGetValue(a.Name,out stored);
                    dashboard.Rows.Add(new AccountRow {Account=a,Name=a.Name,Role=a.Name==leaderName?"Leader":"Follower",Currency=a.Denomination.ToString(),Connection=a.Connection==null?"Disconnected":a.Connection.Status.ToString(),Status=stored!=null&&stored.Latch.Locked?stored.Latch.Reason:"Disarmed",Settings=WithAlias(stored==null?NewSettingsFor(a):stored.Settings.Clone(),a.Name),Latch=stored==null?new RiskLatch():new RiskLatch {Locked=stored.Latch.Locked,Reason=stored.Latch.Reason}});
                }
                string keep=leaderName;dashboard.LeaderBox.ItemsSource=accounts.Where(CopierEngine.Listable).Select(a=>a.Name).ToArray();if(!string.IsNullOrEmpty(keep)&&accounts.Any(a=>a.Name==keep))dashboard.LeaderBox.SelectedItem=keep;else if(dashboard.LeaderBox.Items.Count>0)dashboard.LeaderBox.SelectedItem=DashboardChoices.DefaultLeader(dashboard.LeaderBox.Items.Cast<string>());
                RefreshPositionConsistencySafe();
                dashboard.UpdateCards();
            }
            catch(Exception ex){Log("Refresh error: "+ex.Message);}finally{loading=false;}
        }
        static RiskSettings NewSettingsFor(Account a){var r=new RiskSettings();r.Alias=CopierEngine.AliasFor(a.Name);return r;}
        // Every listed account shows an alias: a saved / sim-allow alias wins, otherwise the generated short one.
        static RiskSettings WithAlias(RiskSettings r,string name){if(string.IsNullOrWhiteSpace(r.Alias))r.Alias=ColumnPrefs.DefaultAlias(name);return r;}
        static SavedAccount Record(AccountRow r) {return new SavedAccount {Name=r.Name,Settings=r.Settings.Clone(),Latch=new RiskLatch {Locked=r.Latch.Locked,Reason=r.Latch.Reason}};}
        XElement Export()
        {
            foreach(var r in dashboard.Rows)saved[r.Name]=Record(r);
            var x=SettingsStore.ToXml(saved.Values,dashboard.InstrumentBox.Text.Trim(),zone,resetHour);x.SetAttributeValue("id",instanceId);x.SetAttributeValue("leader",leaderName);x.SetAttributeValue("lastDay",lastDay);x.SetAttributeValue("boundary",boundaryMode);return x;
        }
        void Persist()
        {
            if(!dashboard.TryCommitSettings()){Log("Settings NOT saved: correct invalid per-account Ratio / row values first.");return;}
            try {if(settingsCorrupt)throw new InvalidDataException(Lang.T("Previously unreadable settings; preserve original files, restore a verified backup before restarting."));SettingsStore.AtomicSave(SettingsPath,Export());SettingsStore.AtomicSave(Path.Combine(DirectoryPath,"last-workspace.xml"),new XElement("Last",new XAttribute("id",instanceId)));}
            catch(Exception ex){settingsCorrupt=true;engine.GuardedPause();Log("PERSISTENCE ERROR — entries paused, restart blocked until reviewed: "+ex.Message);}
        }
        void Log(string message)
        {
            Action record=()=>
            {
                var line=LogClassifier.Classify(message,dashboard.Rows.Select(r=>r.Name));line.Time=DateTime.Now;
                logs.Enqueue(line);while(logs.Count>LogCap)logs.Dequeue();
                dashboard.AddLog(line);AppendDailyFile(line);NotifyFor(line);
            };
            if(closing&&Dispatcher.CheckAccess()){record();return;}
            // Engine worker during close (engine.Dispose runs close() on its own thread): the dispatcher queue is not drained any more,
            // so append the line straight to the daily file. No UI or queue access off the UI thread.
            if(closing){CloseTimeFileLine(message);return;}
            Dispatch(record);
        }
        AccountRow[] Selected() {return dashboard.SelectedRows();}
        // Title stays English: automation finds this dialog by it and must not depend on the picked language. Body text is translated.
        bool Confirm(string text) {return MessageBox.Show(Window.GetWindow(this),text,"KINFX confirmation",MessageBoxButton.YesNo,MessageBoxImage.Warning)==MessageBoxResult.Yes;}
        // A skippable confirmation. When the user turned this kind off (Settings tab, "Ask before") the action goes ahead and the Logs tab says so.
        // riskWarning=true (an account in scope is not confirmed simulated) always shows the window, whatever was saved.
        bool Confirm(string kind,string text,bool riskWarning)
        {
            askSkipped=LoadAsk(AskPath); // another Dashboard tab may have changed it: the file is the only truth (unreadable file = ask everything)
            if(ConfirmPrefs.MustAsk(kind,askSkipped,riskWarning))return Confirm(text);
            Log("Confirmation skipped (turned off on the Settings tab): "+ConfirmPrefs.Label(kind));
#if KINFX_SIM_TEST
            if(kind==ConfirmPrefs.Start)Log(dashboard.DryRunBox!=null&&dashboard.DryRunBox.IsChecked==true?"Dry run is ON: nothing will be sent.":"Dry run is OFF: real simulation orders WILL be sent.");
#endif
            return true;
        }
        // Start / Flatten confirmation for a scope. All accounts confirmed simulated: the normal (skippable) window.
        // Any account NOT confirmed (a prop-firm account KINFX cannot tell from a funded / live one): a risk warning naming them, never skippable;
        // only on Yes are they written to sim-allow.txt (so it is asked once per account), and only then does the engine accept them.
        bool ConfirmScope(string kind,string text,Account[] scope)
        {
            var unconfirmed=scope.Where(a=>a!=null&&!CopierEngine.Sim(a)).Select(a=>a.Name).Distinct().ToArray();
            if(unconfirmed.Length==0)return Confirm(kind,text,false);
#if KINFX_SIM_TEST
            if(!Confirm(kind,Lang.F("RISK WARNING: not confirmed as simulated: {0}\nKINFX cannot tell a prop-firm evaluation account from a funded or live account. If any of these is funded or live, REAL MONEY is at risk.\nYes = these are simulated / evaluation accounts; remember them (sim-allow.txt) so this is not asked again. No = cancel.",string.Join(", ",unconfirmed))+"\n\n"+text,true)){Log("Cancelled at the risk warning: "+string.Join(", ",unconfirmed));return false;}
            string err=CopierEngine.RememberSimulated(unconfirmed);
            if(err!=null){Log("Account confirmation NOT saved, nothing sent: "+err);return false;}
            Log("Confirmed as simulated by you (saved in sim-allow.txt): "+string.Join(", ",unconfirmed));return true;
#else
            Log("LIVE BLOCKED in v0.2. Only Simulator / Playback; no live override is provided.");return false;
#endif
        }
        HashSet<string> askSkipped=new HashSet<string>(StringComparer.Ordinal);
        string AskPath {get{return Path.Combine(DirectoryPath,"confirm-prefs.txt");}}
        static HashSet<string> LoadAsk(string path){try{if(File.Exists(path))return ConfirmPrefs.Parse(File.ReadAllLines(path));}catch{}return new HashSet<string>(StringComparer.Ordinal);}
        void SetAsk(string kind,bool ask)
        {
            if(!ConfirmPrefs.Known(kind))return;
            askSkipped=LoadAsk(AskPath); // merge with what other tabs saved, never overwrite it with an older copy
            bool changed=ask?askSkipped.Remove(kind):askSkipped.Add(kind);if(!changed){ShowAsk();return;}
            try{Directory.CreateDirectory(DirectoryPath);SettingsStore.AtomicWriteLines(AskPath,ConfirmPrefs.Serialize(askSkipped));Log(ask?"Confirmation turned back on: "+ConfirmPrefs.Label(kind):"Confirmation turned off: "+ConfirmPrefs.Label(kind)+" (risk warnings and Unlock are still always asked)");}
            catch(Exception ex){if(ask)askSkipped.Add(kind);else askSkipped.Remove(kind);Log("Settings NOT saved: "+ex.Message);ShowAsk();}
        }
        string ColumnsPath {get{return Path.Combine(DirectoryPath,"columns.txt");}}
        static HashSet<string> LoadColumns(string path){return ColumnPrefs.Load(path);}
        // Display only: never touches settings, engine or orders. A failed save keeps the on-screen choice for this window and says so.
        void SaveColumns(HashSet<string> hidden){try{Directory.CreateDirectory(DirectoryPath);SettingsStore.AtomicWriteLines(ColumnsPath,ColumnPrefs.Serialize(hidden));}catch(Exception ex){Log("Settings NOT saved: "+ex.Message);}}
        void ShowAsk(){foreach(var k in ConfirmPrefs.Kinds){var cb=dashboard.AskBox(k);if(cb!=null)cb.IsChecked=!askSkipped.Contains(k);}}
        void Command(string c)
        {
            try
            {
                if(!DashboardSafety.CanExecute(c)){Log("REVIEW HOLD: trading and bulk edits disabled. Inspect Dashboard only; use native NT8 for positions.");return;}
                if(c=="refresh"){Refresh();return;}if(c=="save"){Persist();Log("Settings saved; startup stays disarmed.");return;}
                if(c=="pause"){engine.GuardedPause();return;}if(c=="resume"){Resume();return;}if(c=="refollow"){RefollowClosed(false);return;}if(c=="stop"){engine.StopFlat();return;}
                if(c=="start"){Start();return;}if(c=="flattenSelected"||c=="flattenAll"){Flatten(c=="flattenSelected"?Selected():dashboard.Rows.ToArray(),ConfirmPrefs.KindFor(c));return;}
                if(c=="pauseSelected"||c=="resumeSelected"){dashboard.SetSelectedPause(c=="pauseSelected");return;}
                if(engine.Attached){Log("Settings / enabled edits blocked during session. Pause per account or stop when flat first.");return;}
                if(c=="daySettings"){EditTradingDay();return;}
                if(c=="unlockAll"){UnlockAll();return;}
                if(c=="batch"){Edit(Selected());return;}
                if(c=="enable"||c=="disable"){foreach(var row in Selected()){row.Enabled=c=="enable"&&row.Role!="Leader"&&CopierEngine.Listable(row.Account);row.Changed();}dashboard.UpdateCards();}
            }
            catch(Exception ex){Log("UI action failed: "+ex.Message);}
        }
        void EditTradingDay()
        {
            var window=new Window {Title=Lang.T("Trading-day settings (local observation)"),Width=500,Height=340,Owner=Window.GetWindow(this),WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=DashboardView.Canvas,Foreground=DashboardView.Ink};
            var panel=new StackPanel {Margin=new Thickness(20)};window.Content=panel;
            panel.Children.Add(new TextBlock {Text=Lang.T("Windows timezone ID (DST aware)"),Margin=new Thickness(0,0,0,8)});var z=new TextBox {Text=zone,Padding=new Thickness(8)};panel.Children.Add(z);
            panel.Children.Add(new TextBlock {Text=Lang.T("Reset boundary hour 0–23 (no automatic latch / baseline reset)"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,8)});var h=new TextBox {Text=resetHour.ToString(),Padding=new Thickness(8)};panel.Children.Add(h);
            var pauseAt=new CheckBox{Content=Lang.T("Pause new entries at the boundary (off = notice only)"),IsChecked=boundaryMode==BoundaryMode.Pause,Foreground=DashboardView.Ink,Margin=new Thickness(0,12,0,0),Tag="boundaryPause"};panel.Children.Add(pauseAt);
            var apply=new Button {Content=Lang.T("Confirm local day boundary"),Padding=new Thickness(8),Margin=new Thickness(0,14,0,0)};panel.Children.Add(apply);
            apply.Click+=(s,e)=>{try{TimeZoneInfo.FindSystemTimeZoneById(z.Text.Trim());int hour;if(!int.TryParse(h.Text,out hour)||hour<0||hour>23)throw new ArgumentException(Lang.T("Hour must be 0–23"));zone=z.Text.Trim();resetHour=hour;boundaryMode=pauseAt.IsChecked==true?BoundaryMode.Pause:BoundaryMode.Notify;lastDay=RiskPolicy.TradingDay(DateTime.UtcNow,zone,resetHour);Persist();window.Close();}catch(Exception ex){MessageBox.Show(window,ex.Message,Lang.T("Invalid trading-day setting"));}};window.ShowDialog();
        }
        void Start()
        {
            if(!dashboard.AccountGrid.CommitEdit(DataGridEditingUnit.Cell,true)||!dashboard.AccountGrid.CommitEdit(DataGridEditingUnit.Row,true)){Log("Start refused: unresolved inline validation error.");return;}
            if(settingsCorrupt){Log("Start blocked: settings persistence error. Review / reset while disarmed.");return;}
            var leader=dashboard.Rows.FirstOrDefault(r=>r.Name==(string)dashboard.LeaderBox.SelectedItem);
            if(leader==null){Log("Choose a simulation leader.");return;}
            var inst=Instrument.GetInstrument(InstrumentText(),false);if(inst==null){Log("Unknown exact instrument.");return;}
            MergeLatches();foreach(var row in dashboard.Rows){SavedAccount stored;if(saved.TryGetValue(row.Name,out stored)&&stored.Latch.Locked)row.Latch=stored.Latch;}
            var selected=dashboard.Rows.Where(r=>r.Enabled&&r!=leader&&!r.Paused).ToArray();if(selected.Length==0){Log("Enable at least one unpaused follower.");return;}
            if(selected.Any(r=>!r.Settings.Valid()||r.Latch.Locked)){Log("Invalid finite settings or risk-latched follower. Review Settings / confirmed reset.");return;}
            if(!ConfirmScope(ConfirmPrefs.Start,Lang.F("Start simulation copying to exactly {0} follower account(s) on {1}?\n\n{2}\n\nLocal copier guards only. No adoption; all selected accounts must be flat / no working instrument orders.",selected.Length,inst.FullName,DashboardChoices.StartSummary(leader.Name,inst.FullName,2,selected.Select(r=>new StartSummaryRow{Name=string.IsNullOrEmpty(r.Settings.Alias)?r.Name:r.Settings.Alias+" ("+r.Name+")",Ratio=r.Settings.Ratio}).ToArray(),IsDryRun())),selected.Select(r=>r.Account).Concat(new[]{leader.Account}).ToArray()))return;
            // Explicit observed baseline initialization, persisted BEFORE subscriptions can arm.
            foreach(var r in selected)if(!r.Settings.BaselineSet){double raw=r.Account.Get(AccountItem.RealizedProfitLoss,r.Account.Denomination)+r.Account.Get(AccountItem.UnrealizedProfitLoss,r.Account.Denomination);if(!RiskPolicy.Finite(raw)){Log("Invalid baseline telemetry: "+r.Name);return;}r.Settings.Baseline=raw;r.Settings.BaselineSet=true;}
            Persist();if(settingsCorrupt)return;
#if KINFX_SIM_TEST
            try{engine.DryRun=dashboard.DryRunBox!=null&&dashboard.DryRunBox.IsChecked==true;}catch(InvalidOperationException ex){Log("Start refused: "+ex.Message);return;}
#endif
            engine.Start(leader.Account,inst,selected.Select(r=>Tuple.Create(r.Account,r.Settings.Clone(),new RiskLatch {Locked=r.Latch.Locked,Reason=r.Latch.Reason})));
        }
        void Follow(AccountRow row,bool following)
        {
            if(!row.CanFollow){row.SyncFollowing(false,false);return;}
            if(!engine.Attached)return; // Disarmed ticks select scope only, never arm.
            int session=engine.SessionGeneration;
            int request=engine.SetFollowing(row.Name,following);
            if(request<0){row.SyncFollowing(false,false);Log(row.Name+": Follow refused — account outside managed session; stop flat and select scope again.");return;}
            row.TrackFollowRequest(session,request,following);dashboard.UpdateCards();
        }
        void Pause(AccountRow row,bool paused)
        {if(row.Role=="Leader"){Log("Use global guarded pause for leader.");return;}row.Paused=paused;row.Changed();engine.PauseAccount(row.Name,paused);dashboard.UpdateCards();}
        void Edit(AccountRow[] rows)
        {
            if(rows.Length==0){Log("Select accounts first.");return;}if(engine.Attached){Log("Stop flat before risk edits. Active engine uses immutable settings snapshots.");return;}
            var dialog=new RiskSettingsDialog(rows[0].Settings,dashboard.InstrumentBox.Text.Split(' ')[0],rows.Length){Owner=Window.GetWindow(this)};
            // Latch is not silently cleared by risk edits; explicit confirmation follows for a locked row.
            if(dialog.ShowDialog()==true){foreach(var row in rows){double individualRatio=row.Ratio;row.Settings=dialog.Result.Clone();if(rows.Length>1)row.Settings.Ratio=individualRatio;row.Changed();}Persist();Log("Applied editable risk snapshot to "+rows.Length+" account(s); per-account ratios / latch unchanged for bulk edits.");}
            if(rows.Length==1&&rows[0].Latch.Locked&&Confirm(Lang.F("Account {0} remains LOCKED: {1}.\nRequest unlock? This does NOT arm copying; current risk conditions will be rechecked, baseline unchanged.",rows[0].Name,Lang.Show(rows[0].Latch.Reason))))Unlock(rows[0]);
        }
        // Unlock all: one confirmation for every locked follower. Each account is checked with the same rules as the single unlock (RiskPolicy.UnlockBlock);
        // only the ones that pass are unlocked, the rest stay locked and the Logs tab names why. Still disarmed afterwards; nothing is sent.
        // ---- S1: resume after a pause, trading-day boundary mode, re-follow closed followers ----
        string boundaryMode=BoundaryMode.Pause;
        void Resume()
        {
            if(!engine.CanResume){Log("Resume refused: entries were stopped by a safety block, not by a pause; Stop when flat, then Start.");return;}
            if(!ConfirmScope(ConfirmPrefs.Start,Lang.T("Resume NEW entries?\n\nEntries the leader opened while paused are NOT copied (no catch-up); only later new entries are. Locked or unticked followers stay as they are."),new Account[0]))
            {Log("Resume cancelled; still paused.");return;}
            engine.ResumeEntries();
        }
        // auto=true: Tick() pass for opted-in followers; no dialog, one log line per account.
        // Auto re-follow: one attempt per close, never spent while entries are off (see AutoRefollowGate).
        readonly AutoRefollowGate autoRefollow=new AutoRefollowGate();
        void RefollowClosed(bool auto)
        {
            if(!engine.Attached)return;
            var leader=dashboard.Rows.FirstOrDefault(r=>r.Role=="Leader");int leaderPos=leader==null?0:leader.Position;
            var followers=dashboard.Rows.Where(r=>r.Role!="Leader").ToArray();
            AccountRow[] rows;
            if(auto)
            {
                var eligible=followers.Where(r=>Refollow.Auto(r.AutoRefollow,r.UserFlattened,r.ExitClosed,r.Enabled||r.FollowPending,r.Latch.Locked,r.Paused,r.Connection=="Connected",r.Position,leaderPos)).Select(r=>r.Name);
                var take=new HashSet<string>(autoRefollow.Take(engine.SessionGeneration,engine.EntriesArmed,eligible,followers.Where(r=>r.ExitClosed).Select(r=>r.Name)),StringComparer.Ordinal);
                rows=followers.Where(r=>take.Contains(r.Name)).ToArray();
                if(rows.Length==0)return;
            }
            else
            {
                rows=followers.Where(r=>Refollow.Eligible(r.ExitClosed,r.Enabled||r.FollowPending,r.Latch.Locked,r.Paused,r.Connection=="Connected",r.Position)).ToArray();
                if(rows.Length==0){Log("Re-follow: no closed account is flat, connected and unlocked.");return;}
                if(!ConfirmScope(ConfirmPrefs.Start,Lang.F("Tick {0} closed account(s) again?\n{1}\n\nOnly LATER new entries are copied; nothing is caught up. Each account is checked again (flat, connected, unlocked).",rows.Length,string.Join("\n",rows.Select(r=>r.Alias.Length>0?r.Alias+" ("+r.Name+")":r.Name))),rows.Select(r=>r.Account).Where(a=>a!=null).ToArray()))
                {Log("Re-follow cancelled; nothing changed.");return;}
            }
            foreach(var r in rows){r.Enabled=true;Log((auto?"Auto re-follow: ":"Re-follow: ")+r.Name+" — only later new entries are copied.");}
            dashboard.UpdateCards();
        }
        void UnlockAll()
        {
            var locked=dashboard.Rows.Where(r=>r.Role!="Leader"&&r.Latch.Locked).ToArray();
            if(locked.Length==0){Log("Unlock all: no locked account.");return;}
            var inst=Instrument.GetInstrument(InstrumentText(),false);
            if(inst==null){Log("Unknown exact instrument.");return;}
            var ok=new List<AccountRow>();var keep=new List<string>();
            foreach(var row in locked)
            {
                string why;
                try
                {
                    var a=row.Account;
                    if(a==null)why="Disconnected";
                    else
                    {
                        bool connected=a.Connection!=null&&a.Connection.Status==ConnectionStatus.Connected;
                        double raw=a.Get(AccountItem.RealizedProfitLoss,a.Denomination)+a.Get(AccountItem.UnrealizedProfitLoss,a.Denomination),equity=a.Get(AccountItem.NetLiquidation,a.Denomination);
                        why=RiskPolicy.UnlockBlock(row.Settings,connected,connected&&CopierEngine.Clean(a,inst),raw-row.Settings.Baseline,equity);
                    }
                }
                catch(Exception ex){why=ex.Message;}
                if(why=="")ok.Add(row);else keep.Add(Lang.F("{0}: {1}",row.Alias.Length>0?row.Alias:row.Name,Lang.Show(why)));
            }
            if(ok.Count==0){Log(Lang.F("Unlock all: nothing can be unlocked yet. Still locked: {0}",string.Join("; ",keep)));return;}
            string text=Lang.F("Unlock {0} account(s)? Each one is flat, connected and no longer at a risk limit:\n{1}\nThis does NOT start copying; tick them again and press Start.",ok.Count,string.Join("\n",ok.Select(r=>r.Name+" — "+Lang.Show(r.Latch.Reason))));
            if(keep.Count>0)text+="\n\n"+Lang.F("Stay locked ({0}):\n{1}",keep.Count,string.Join("\n",keep));
            if(!Confirm(text)){Log("Unlock all cancelled; nothing changed.");return;}
            foreach(var row in ok)
            {
                try{SettingsStore.RemoveLatch(LatchPath,row.Name);row.Latch=new RiskLatch();row.Status="Disarmed";row.Changed();Log("Confirmed unlock: "+row.Name+"; still disarmed.");}
                catch(Exception ex){Log("Unlock refused: "+row.Name+": "+ex.Message);}
            }
            Persist();dashboard.UpdateCards();
            if(keep.Count>0)Log(Lang.F("Still locked: {0}",string.Join("; ",keep)));
        }
        void Unlock(AccountRow row)
        {
            try
            {
                var a=row.Account;var inst=Instrument.GetInstrument(InstrumentText(),false);
                if(inst==null||!CopierEngine.Clean(a,inst))throw new InvalidOperationException(Lang.T("Unlock requires selected instrument flat / no working orders."));
                double raw=a.Get(AccountItem.RealizedProfitLoss,a.Denomination)+a.Get(AccountItem.UnrealizedProfitLoss,a.Denomination),equity=a.Get(AccountItem.NetLiquidation,a.Denomination);
                if(!RiskPolicy.CanUnlock(row.Settings,raw-row.Settings.Baseline,equity,a.Connection!=null&&a.Connection.Status==ConnectionStatus.Connected))throw new InvalidOperationException(Lang.T("Current telemetry / floor / local high-water still blocks unlock."));
                SettingsStore.RemoveLatch(LatchPath,row.Name);row.Latch=new RiskLatch();row.Status="Disarmed";row.Changed();Persist();Log("Confirmed unlock: "+row.Name+"; still disarmed.");
            }
            catch(Exception ex){Log("Unlock refused: "+ex.Message);}
        }
        void FlatOne(AccountRow row)
        {
            if(row==null||row.Account==null){Log("Flat: no account.");return;}
            if(!DashboardSafety.CanExecute("flattenSelected")){Log("REVIEW HOLD: Flat disabled.");return;}
            if(row.Role=="Leader"){Log("Flat: the leader row is not a follower; use Flatten all or flatten it in NinjaTrader.");return;}
            Flatten(new[]{row},ConfirmPrefs.KindFor("flatOne"));
        }
        void Flatten(AccountRow[] rows,string kind)
        {
            if(rows.Length==0){Log("No accounts in flatten scope.");return;}var inst=Instrument.GetInstrument(InstrumentText(),false);if(inst==null){Log("Flatten: exact instrument unknown.");return;}
            var targets=rows.Where(r=>r.Account!=null).Select(r=>r.Account).Distinct().ToArray();
            if(!ConfirmScope(kind,Lang.F("{0}{1}FLATTEN exactly {2} account(s), only {3}.\n{4}\nNew copies will be blocked BEFORE flatten requests. This cancels working orders and closes positions on that instrument. Retry/error remains visible.",DryRunPrefix(),kind==ConfirmPrefs.FlatOne?Lang.T("FLAT ONE ACCOUNT. "):"",targets.Length,inst.FullName,string.Join("\n",targets.Select(a=>a.Name))),targets))return;
            foreach(var r in rows)if(r.Account!=null)r.UserFlattened=true;
            engine.Flatten(targets,inst);
        }
        void OnTelemetry(AccountTelemetry t)
        {
            Dispatch(()=>
            {
                var row=dashboard.Rows.FirstOrDefault(r=>r.Name==t.Name);if(row==null)return;
                bool wasLocked=row.Latch.Locked;if(!engine.Attached||!row.ApplyTelemetry(t,engine.SessionGeneration))return;dashboard.UpdateCards();if(!wasLocked&&row.Latch.Locked)Persist();
            });
        }
        internal static void PollCashBalances(IEnumerable<AccountRow> rows,Func<AccountRow,bool> simulation,Func<AccountRow,bool> connected,Func<AccountRow,double> readCash)
        {
            // Display-only polling, independent of the copier's follower telemetry/version fields.
            foreach(var row in rows)
            {
                double cash=double.NaN;
                try {if(simulation(row)&&connected(row)){double value=readCash(row);if(RiskPolicy.Finite(value))cash=value;}}
                catch { /* Provider read failed: hide old cash immediately, do not change risk/follow state. */ }
                row.CashBalance=cash;row.Changed();
            }
        }
        // Turns "mnq", "MNQZ6", "MNQ1226" into "MNQ 12-26". An explicit month is kept. Unknown text is left as typed and reported; Start still requires an exact instrument NinjaTrader knows.
        bool IsDryRun()
        {
#if KINFX_SIM_TEST
            // While attached the session's fixed mode is the truth; while stopped the box is what the NEXT Start will use (engine.DryRun still holds the last session's value).
            return engine.Attached?engine.DryRun:(dashboard.DryRunBox!=null&&dashboard.DryRunBox.IsChecked==true);
#else
            return false;
#endif
        }
        string DryRunPrefix()
        {
#if KINFX_SIM_TEST
            if(IsDryRun())return Lang.T("*** DRY RUN: nothing will be sent; decisions are only logged. ***")+"\n\n";
#endif
            return "";
        }
        string InstrumentText(){var exact=ContractRoll.Normalize(dashboard.InstrumentBox.Text,DateTime.Now);return exact??dashboard.InstrumentBox.Text.Trim();}
        void NormalizeInstrumentBox()
        {
            if(engine.Attached)return;
            string typed=dashboard.InstrumentBox.Text;
            string exact=ContractRoll.Normalize(typed,DateTime.Now);
            if(exact==null){if(!string.IsNullOrWhiteSpace(typed)&&Instrument.GetInstrument(typed.Trim(),false)==null)Log("Instrument \""+typed.Trim()+"\" not recognised. Examples: MNQ, ES 12-26, NQZ6.");return;}
            if(exact!=typed.Trim()){dashboard.InstrumentBox.Text=exact;}
            string root=exact.Split(' ')[0];
            if(InstrumentCatalog.IsEstimated(root,DateTime.Now)&&!estimatedLogged.Contains(root)){estimatedLogged.Add(root);Log(Lang.F("Contract roll date estimated for {0}; check NinjaTrader's Instrument Manager.",root));}
            if(Instrument.GetInstrument(exact,false)==null)Log("Instrument "+exact+" is not available in this NinjaTrader (check the contract month / Instrument Manager).");
        }
        // Recent instrument roots (last 5, most recent first) for the picker. Persisted inside DirectoryPath.
        string RecentPath{get{return Path.Combine(DirectoryPath,"instruments-recent.txt");}}
        string[] LoadRecent()
        {
            try{if(File.Exists(RecentPath))return File.ReadAllLines(RecentPath);}catch{}
            return new string[0];
        }
        void RecordRecent(string instrumentText)
        {
            var root=(instrumentText??"").Trim().Split(' ')[0].Trim().ToUpperInvariant();
            if(!InstrumentCatalog.IsKnown(root))return;
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                var list=new List<string>{root};
                foreach(var line in LoadRecent())
                {
                    var r=(line??"").Trim().ToUpperInvariant();
                    if(r.Length==0||!InstrumentCatalog.IsKnown(r)||r==root||list.Contains(r))continue;
                    list.Add(r);
                    if(list.Count>=5)break;
                }
                SettingsStore.AtomicWriteLines(RecentPath,list.ToArray());
            }
            catch(Exception ex){Log("Settings NOT saved: "+ex.Message);}
        }
        string[] syncNames=new string[0];string syncNoticeKey="";
        // Names of the accounts the dashboard WOULD list right now (same filter as Refresh). Read-only: touches no account state.
        string[] CurrentAccountNames()
        {
            Account[] accounts;lock(Account.All)accounts=Account.All.Where(a=>dashboard.ShowLive.IsChecked==true||CopierEngine.Listable(a)).ToArray();
            return accounts.Select(a=>a.Name).ToArray();
        }
        bool EditingNow()
        {
            if(dashboard.AccountGrid.IsKeyboardFocusWithin&&!dashboard.AccountGrid.IsReadOnly&&System.Windows.Input.Keyboard.FocusedElement is TextBox)return true;
            if(dashboard.RiskGrid.IsKeyboardFocusWithin&&!dashboard.RiskGrid.IsReadOnly&&System.Windows.Input.Keyboard.FocusedElement is TextBox)return true;
            return false;
        }
        void SyncAccounts()
        {
            var diff=AccountSync.Diff(syncNames,CurrentAccountNames());
            var action=AccountSync.Decide(diff,engine.Attached,EditingNow());
            if(action==AccountSyncAction.None){syncNoticeKey="";return;}
            string key=string.Join(",",diff.Added)+"|"+string.Join(",",diff.Removed);
            if(action==AccountSyncAction.Reload){Refresh();syncNames=CurrentAccountNames();syncNoticeKey="";Log("Account list updated: "+(diff.Added.Length>0?"+"+string.Join(", +",diff.Added)+" ":"")+(diff.Removed.Length>0?"-"+string.Join(", -",diff.Removed):"").Trim());return;}
            if(syncNoticeKey!=key){syncNoticeKey=key;Log(action==AccountSyncAction.Notify?"Account list changed ("+(diff.Added.Length>0?"+"+string.Join(", +",diff.Added)+" ":"")+(diff.Removed.Length>0?"-"+string.Join(", -",diff.Removed):"").Trim()+") — not applied while copying. Stop when flat, then the list refreshes.":"Account list changed — will refresh when you finish editing.");}
        }
        string lastDriftError="";
        void RefreshPositionConsistencySafe()
        {
            try{RefreshPositionConsistency();lastDriftError="";}
            catch(Exception ex){if(ex.Message!=lastDriftError){lastDriftError=ex.Message;Log(string.Format(CultureInfo.InvariantCulture,PositionConsistency.DisplayFailed,ex.Message));}}
        }
        void RefreshPositionConsistency()
        {
            DateTime now=DateTime.UtcNow;
            bool contracts=PlatformPositionContract.Ready;
            Instrument inst=null;
            try{inst=Instrument.GetInstrument(InstrumentText(),false);}catch{inst=null;}
            double tick=0;
            bool tickOk=contracts&&inst!=null&&PlatformPositionContract.TryTickSize(inst,out tick);
            string instrumentName=inst==null?"":inst.FullName??"";
            AccountRow leader=null;
            foreach(var row in dashboard.Rows)
            {
                if(row.Role=="Leader")leader=row;
                bool connected=row.Connection=="Connected"||row.Connection=="Synthetic";
                int qty=row.Position;double avg=0;bool hasAvg=false,readOk=false;
                if(contracts&&row.Account!=null&&inst!=null)
                {
                    int platformQty;
                    readOk=PlatformPositionContract.TryRead(row.Account,inst,out platformQty,out avg,out hasAvg);
                    if(readOk)qty=platformQty;
                }
                row.Observe(instrumentName,connected,contracts,readOk,qty,hasAvg,avg,now);
            }
            foreach(var row in dashboard.Rows)
            {
                string previous=row.ConsistencyStatus;
                PositionConsistencyResult result;
                if(row.Role=="Leader")result=PositionConsistency.LeaderBaseline();
                else if(leader==null||leader.ObservedSnap==null||row.ObservedSnap==null)result=PositionConsistency.NotComparable();
                else result=PositionConsistency.Evaluate(leader.ObservedSnap,row.ObservedSnap,tickOk?tick:0,avgWarnTicks,now,PositionConsistency.ReportGrace);
                row.ApplyConsistency(result);
                if(row.Role!="Leader"&&result.Status==PositionConsistencyStatus.PriceDiverge&&previous!=PositionConsistencyStatus.PriceDiverge.ToString())
                    Log(string.Format(CultureInfo.InvariantCulture,PositionConsistency.Notice,row.Name,result.Badge));
            }
        }
        void Tick()
        {
            try{SyncAccounts();}catch(Exception ex){Log("Account sync error: "+ex.Message);}
            if(engine.Attached)engine.Poll();
            PollCashBalances(dashboard.Rows,
                r=>r.Account!=null&&CopierEngine.Listable(r.Account),
                r=>r.Account.Connection!=null&&r.Account.Connection.Status==ConnectionStatus.Connected,
                r=>{var a=r.Account;r.Currency=a.Denomination.ToString();return a.Get(AccountItem.CashValue,a.Denomination);});
            {
                // Qty must show the real signed position for every row this loop owns (the leader, un-ticked accounts, and every account before Start); the engine only publishes ticked followers.
                Instrument qtyInst=null;try{qtyInst=Instrument.GetInstrument(InstrumentText(),false);}catch{}
                foreach(var r in dashboard.Rows.Where(r=>!engine.Attached || r.Role=="Leader" || !r.Enabled))
                {
                    try
                    {
                        var a=r.Account;r.Position=qtyInst==null?0:CopierEngine.Position(a,qtyInst);r.Realized=a.Get(AccountItem.RealizedProfitLoss,a.Denomination);r.Unrealized=a.Get(AccountItem.UnrealizedProfitLoss,a.Denomination);r.Equity=a.Get(AccountItem.NetLiquidation,a.Denomination);r.Sod=a.Get(AccountItem.SodLiquidatingValue,a.Denomination);r.Pnl=r.Realized+r.Unrealized-(r.Settings.BaselineSet?r.Settings.Baseline:0);r.Connection=a.Connection==null?"Disconnected":a.Connection.Status.ToString();r.Changed();
                    }
                    catch(Exception ex){r.Status="Telemetry error";r.Changed();Log(r.Name+": "+ex.Message);}
                }
                RefreshPositionConsistencySafe();
                dashboard.UpdateCards();
            }
            if(engine.Attached && ++persistTicks>=30){persistTicks=0;Persist();}
            dashboard.ShowResume(engine.CanResume);
            if(engine.Attached){try{RefollowClosed(true);}catch(Exception ex){Log("Auto re-follow error: "+ex.Message);}}
            {var lr=dashboard.Rows.FirstOrDefault(r=>r.Role=="Leader");int lp=lr==null?0:lr.Position;dashboard.ShowRefollow(engine.Attached?dashboard.Rows.Count(r=>r.Role!="Leader"&&Refollow.Eligible(r.ExitClosed,r.Enabled||r.FollowPending,r.Latch.Locked,r.Paused,r.Connection=="Connected",r.Position)):0);}
            string day=RiskPolicy.TradingDay(DateTime.UtcNow,zone,resetHour);
            if(lastDay.Length==0){lastDay=day;return;}if(day!=lastDay){lastDay=day;if(boundaryMode==BoundaryMode.Notify)Log("Trading-day boundary "+zone+" "+resetHour+":00 (DST aware). Notice only: entries NOT paused. Baseline / latch NOT auto-reset.");else{engine.GuardedPause();Log("Trading-day boundary "+zone+" "+resetHour+":00 (DST aware). New entries paused. Baseline / latch NOT auto-reset. Confirm new baseline and unlock while flat.");}Persist();}
        }
        public bool CanClose() {try{return engine.CanClose();}catch{return false;}}
        protected override string GetHeaderPart(string variable) {return "Dashboard";}
        protected override void Save(XElement element) {element.Add(Export());Persist();}
        protected override void Restore(XElement element)
        {
            try{var x=element.Element("KINFX");if(x==null)return;string id=(string)x.Attribute("id");Guid g;if(Guid.TryParseExact(id,"N",out g))instanceId=id;ApplyXml(x);
                // Disk latch is newer than workspace in many restart scenarios. Merge locks conservatively.
                if(File.Exists(SettingsPath))foreach(var disk in SettingsStore.FromXml(XElement.Load(SettingsPath)))if(disk.Latch.Locked){SavedAccount item;if(saved.TryGetValue(disk.Name,out item))item.Latch=disk.Latch;else saved[disk.Name]=disk;}
                dashboard.Rows.Clear();Refresh();}
            catch(Exception ex){settingsCorrupt=true;Log("Restore failed — Start blocked: "+ex.Message);}
        }
        public override void Cleanup() {if(cleaned)return;timer.Stop();closing=true;Persist();engine.Dispose();FlushDailyFile();cleaned=true;base.Cleanup();}
    }
    public class KINFXTradeCopierWindowFactory : INTTabFactory
    {
        public NTWindow CreateParentWindow() {return new KINFXTradeCopierWindow();}
        public NTTabPage CreateTabPage(string typeName,bool isNew) {return new KINFXTradeCopierTab();}
    }
}
