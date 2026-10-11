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
#if KINFX_SIM_TEST
        // SIM TEST BUILD ONLY: the dashboard commands are reachable, but the engine still refuses any non-Simulator/Playback account.
        public static bool CanExecute(string command) { return true; }
#else
        public static bool CanExecute(string command)
        { return command!="start" && command!="flattenSelected" && command!="flattenAll" && command!="batch"; }
#endif
    }
    // Advisory text for settings that are legal but easy to misread. Pure: no account, engine or dispatch access.
    // Returns "" when there is nothing to say. Never blocks anything; it only informs.
    public static class SettingsAdvice
    {
        public static string For(RiskSettings s,string role,int referenceLots)
        {
            if(s==null||role=="Leader")return "";
            if(!RiskPolicy.Finite(s.Ratio)||s.Ratio<=0)return "Ratio is not a positive number";
            var notes=new System.Collections.Generic.List<string>();
            if(s.OnDivergence=="FlattenOnce")notes.Add("On divergence = FlattenOnce is saved but NOT active in this alpha: the engine only locks and notifies, it never flattens by itself");
            if(s.Target<=0&&s.Loss<=0)notes.Add("no daily target and no daily loss limit (0 = off)");
            else if(s.Loss<=0)notes.Add("no daily loss limit (0 = off)");
            else if(s.Target<=0)notes.Add("no daily target (0 = off)");
            if(referenceLots>0)
            {
                if(RiskPolicy.Quantity(referenceLots,s.Ratio)==0)notes.Add("Ratio gives 0 lots for a "+referenceLots+"-lot leader order");
                else if(s.MaxContracts>0&&RiskPolicy.Quantity(referenceLots,s.Ratio)>s.MaxContracts)notes.Add("size capped by Max "+s.MaxContracts);
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
        public static string ConsistencyAdvice(RiskSettings s)
        {
            if(s.Consistency<=0)return "Off";if(s.TotalProfit<=0||s.BestDay<=0)return "History required";
            double pct=s.BestDay/s.TotalProfit*100;return (pct>s.Consistency?"Over advisory limit: ":"Within advisory limit: ")+pct.ToString("F2",CultureInfo.InvariantCulture)+"%";
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
            var field=typeof(RiskSettings).GetField(name);if(field==null)throw new ArgumentException("Unknown risk field");
            try
            {
                var value=SettingsStore.FieldValue(text,field.FieldType);
                var probe=new RiskSettings();field.SetValue(probe,value);
                if(!probe.Valid())throw new ArgumentException("Invalid risk value");
                var next=current.Clone();field.SetValue(next,value);
                current=next;invalid.Remove(name);return true;
            }
            catch(FormatException){}catch(OverflowException){}catch(ArgumentException){}
            invalid[name]=text??"";return false;
        }
        public void Load(RiskSettings preset){current=preset.Clone();} // Explicit template load keeps unresolved typed errors visible.
        public RiskSettings Build(bool localTrail)
        {
            if(HasErrors)throw new ArgumentException("Correct the highlighted risk values before Apply.");
            var next=current.Clone();next.LocalTrail=localTrail;next.BaselineSet=true;
            if(!next.Valid())throw new ArgumentException("Every number must be finite; ratio / max quantity positive; monetary guards nonnegative.");
            if(next.LocalTrail&&(next.HighWater<=0||next.TrailDistance<=0))throw new ArgumentException("Supply positive confirmed high-water and trail distance first.");
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
            if (xml == null || xml.Name != "KINFX" || (int?)xml.Attribute("version") != 2) throw new InvalidDataException("Unsupported settings version");
            var result = new List<SavedAccount>();
            foreach (XElement e in xml.Elements("Account"))
            {
                var r = new SavedAccount { Name=(string)e.Attribute("name") ?? "" };
                foreach (var f in typeof(RiskSettings).GetFields())
                {
                    XAttribute a=e.Attribute(f.Name); if (a != null) f.SetValue(r.Settings, FieldValue(a.Value,f.FieldType));
                }
                r.Latch.Locked=(bool?)e.Attribute("locked") ?? false; r.Latch.Reason=(string)e.Attribute("reason") ?? "Persisted lock";
                if (!r.Settings.Valid()) throw new InvalidDataException("Invalid finite settings: " + r.Name);
                result.Add(r);
            }
            if (result.Select(r=>r.Name).Distinct().Count()!=result.Count) throw new InvalidDataException("Duplicate account settings");
            return result;
        }
        public static Dictionary<string,string> ReadLatches(string path)
        {
            lock(IoGate)
            {
                if(!File.Exists(path))return new Dictionary<string,string>();
                XElement root=XElement.Load(path);if(root.Name!="Latches")throw new InvalidDataException("Invalid latch journal");
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
            var start=new DateTime(now.Year,now.Month,1);
            for(int add=0;add<=24;add++){var d=start.AddMonths(add);if(spec.Months.Contains(d.Month))return Format(r,d.Month,d.Year);}
            return null;
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
// END KINFX PURE
    internal sealed class AccountTelemetry
    {
        public string Name, Currency, Connection, State, Reason;
        public double Baseline;
        public double Pnl, Equity, Realized, Unrealized, Sod, HighWater;
        public int Position, FollowGeneration, ProcessedFollowGeneration, SessionGeneration;
        public bool Locked, Paused, Following, DisablePending;
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
        public bool DisablePending, ExitCompleted, Cancelling, EvidenceUnresolved;
        // Issue #4: provider calls for THIS account run on its own serial thread, so one stalled provider call cannot delay other accounts.
        // Engine state is still mutated only on the engine worker; completions are posted back to it.
        public SerialWorker Dispatcher;
        // Set on the ACCOUNT thread the moment any provider call for this account throws. Entry commands still queued behind it are not sent
        // (the engine-thread Lock arrives later), which preserves the old "after a failure no further entry is attempted" behaviour.
        public volatile bool ProviderFaulted;
        public string EvidenceReason;
        public readonly HashSet<string> Ignored=new HashSet<string>();
        public double HighWater;
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
        public bool Exit { get { return Action==OrderAction.Sell || Action==OrderAction.BuyToCover; } }
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
        public bool OcoRetired;
        public DateTime WaitingSince=DateTime.UtcNow;
        public string Oco, ProviderId;
        public int Desired, ObservedFilled, RequestedQuantity, ReservedQuantity;
        public OrderAction SubmittedAction;
        public OrderState ObservedState;
        public bool CancellationRequested, SubmissionInFlight, AcceptanceConfirmed;
        // Change commands dispatched but not yet returned from the provider, and the values the LAST one requested. While any is in flight the
        // "nothing to change" comparison must use these, not the order's applied values, or a revert to the old price is silently dropped.
        public int ChangesInFlight;
        public readonly List<int> PendingChangeQuantities=new List<int>();
        public double SentLimit, SentStop; public int SentQty;
        public readonly HashSet<Order> Wrappers=new HashSet<Order>();
        public readonly List<FollowerSnapshot> ExecutionEvidence=new List<FollowerSnapshot>();
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
        int pollPending, controlGeneration, sessionGeneration;
        int pendingFollowerCallbacks;
        int providerCallsInFlight;
        readonly List<SerialWorker> dispatchers=new List<SerialWorker>();
        internal int ProviderCallsInFlight { get { return Volatile.Read(ref providerCallsInFlight); } }
        readonly object controlGate=new object();
        readonly object admissionGate=new object();
        bool mirrorAtm=true;
        public bool Attached { get { return attached; } }
        public bool EntriesArmed { get { return entries; } }
        public int SessionGeneration {get{return Volatile.Read(ref sessionGeneration);}}
        public CopierEngine(Action<string> logger,Action<AccountTelemetry> onTelemetry,Action<bool,string> onState,Action<string,string> persistLatch)
        {
            log=logger; telemetry=onTelemetry; stateChanged=onState;durableLock=persistLatch;
            worker=new SerialWorker(ex=>{ entries=false; log("ENGINE ERROR: "+ex.Message+" — entries blocked; coverage unverified, manual recovery required"); stateChanged(attached,"Error — guarded pause; verify exposure manually"); });
        }
        public static bool Sim(Account a) { return a.Provider==Provider.Simulator || a.Provider==Provider.Playback; }
        // Scope check for the KINFX_SIM_TEST build. A scope is allowed only when it is non-empty, has no null member and EVERY account is
        // Simulator / Playback. One Live (or any other provider) account refuses the whole scope. Pure: reads Provider only.
        public static bool SimScope(IEnumerable<Account> scope)
        {
            if(scope==null)return false;
            int n=0;
            foreach(var a in scope){ if(a==null||!Sim(a))return false; n++; }
            return n>0;
        }
        static bool Connected(Account a) { return a.Connection!=null && a.Connection.Status==ConnectionStatus.Connected; }
        public static int Position(Account a,Instrument inst)
        {
            lock(a.Positions) { var p=a.Positions.FirstOrDefault(x=>x.Instrument.FullName==inst.FullName); return p==null ? 0 : p.Quantity*(p.MarketPosition==MarketPosition.Long?1:p.MarketPosition==MarketPosition.Short?-1:0); }
        }
        public static bool Clean(Account a,Instrument inst)
        {
            if(Position(a,inst)!=0)return false;
            lock(a.Orders) return !a.Orders.Any(o=>o.Instrument!=null && o.Instrument.FullName==inst.FullName && !Order.IsTerminalState(o.OrderState));
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
                    if(accounts.Any(a=>!Clean(a,inst))) throw new InvalidOperationException("Selected instrument already has a position / working order. No adoption or catch-up.");
                    Interlocked.Increment(ref sessionGeneration);
                    leader=selected; instrument=inst;mirrorAtm=copyAtm;sessionAccounts=accounts; DisposeDispatchers(); followers.Clear(); links.Clear(); tombstones.Clear();
                    lock(identityGate) { identities.Clear(); providerIdentities.Clear(); wrapperProviderIds.Clear(); }
                    foreach(var item in list) followers.Add(new EngineAccount { Dispatcher=NewDispatcher(),Account=item.Item1,Settings=item.Item2.Clone(),Latch=new RiskLatch {Locked=item.Item3.Locked,Reason=item.Item3.Reason},HighWater=item.Item2.HighWater });
                    foreach(var f in followers.Where(x=>!x.Settings.CopyAtm||!copyAtm))log(f.Account.Name+": ATM exit mirroring OFF — nonmarket exits skipped; market closes only. No native ATM clone / autonomous protection.");
                    followerView=followers.ToArray();
                    attached=true; entries=false;suppressNew=false;
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
                    if(wrapperProviderIds.TryGetValue(order,out prior)&&!string.IsNullOrEmpty(prior)&&provider!=prior)return null;
                    if(!string.IsNullOrEmpty(provider))
                    {
                        string mapped; if(accountIds.TryGetValue(provider,out mapped)&&mapped!=id)return null;
                        accountIds[provider]=id; wrapperProviderIds[order]=provider;
                    }
                    return id;
                }
                if(!string.IsNullOrEmpty(provider)&&accountIds.TryGetValue(provider,out id)) { identities[order]=id; wrapperProviderIds[order]=provider;return id; }
                // Blank IDs can only start at the initial state; a later unknown wrapper is ambiguous.
                if(string.IsNullOrEmpty(provider)&&state!=OrderState.Submitted)return null;
                id=Guid.NewGuid().ToString("N"); identities[order]=id;
                if(!string.IsNullOrEmpty(provider)){accountIds[provider]=id;wrapperProviderIds[order]=provider;}
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
            worker.Post(()=>Process(s));
        }
        static bool Alive(OrderState s) { return s==OrderState.Submitted || s==OrderState.Accepted || s==OrderState.Working || s==OrderState.TriggerPending || s==OrderState.PartFilled; }
        void Process(OrderSnapshot s)
        {
            if(!attached||s.Origin!=leader||s.Source.Account!=leader||s.Session!=Volatile.Read(ref sessionGeneration)||s.InstrumentName!=instrument.FullName)return;
            if(s.Id==null) { BlockEntries();foreach(var f in followers)Lock(f,"Ambiguous provider order identity / transition — no catch-up, manual recovery required");return; }
            if(!s.Exit&&s.Execution!=Volatile.Read(ref controlGeneration)) { tombstones.Add(s.Id); return; }
            // Session-frozen mode. Market closes still mirror when bracket copying is off.
            if(s.Exit&&s.Type!=OrderType.Market&&!mirrorAtm)return;
            if(!RiskPolicy.Finite(s.Limit)||!RiskPolicy.Finite(s.Stop)||s.Limit<0||s.Stop<0||s.Quantity<=0||s.Quantity>1000000 ||
                !(s.Action==OrderAction.Buy||s.Action==OrderAction.SellShort||s.Action==OrderAction.Sell||s.Action==OrderAction.BuyToCover) ||
                !(s.Type==OrderType.Market||s.Type==OrderType.Limit||s.Type==OrderType.StopMarket||s.Type==OrderType.StopLimit||s.Type==OrderType.MIT))
            { BlockEntries(); foreach(var f in followers)Lock(f,"Invalid / unsupported leader order — entries blocked"); return; }
            if(!Connected(leader)) { entries=false; foreach(var f in followers) Lock(f,"Leader disconnected — no catch-up"); return; }
            List<CopyLink> copies;
            if(!links.TryGetValue(s.Id,out copies))
            {
                if(tombstones.Contains(s.Id) || !Alive(s.State)) return;
                if(suppressNew){tombstones.Add(s.Id);return;}
                if(links.Count+tombstones.Count>=10000) { entries=false; log("Session history bound reached — guarded pause; stop flat and restart."); return; }
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
                        if(desired<=0)continue;
                        var link=new CopyLink {Follower=f,Latest=s,Exit=s.Exit,Desired=desired,Oco=oco.Get(f.Account.Name,s.Oco),Waiting=true};
                        copies.Add(link);
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
                    { Lock(link.Follower,"Unsupported leader action / type / OCO / time-in-force transition — submitted order unchanged; manual recovery required");continue; }
                    link.Latest=s;
                    if(s.State==OrderState.Cancelled || s.State==OrderState.Rejected)
                    {
                        link.Cancelled=true; link.Waiting=false;
                        if(link.Order!=null && !Order.IsTerminalState(link.Order.OrderState)) { var cf=link.Follower; var co=link.Order; Dispatch(cf,()=>cf.Account.Cancel(new[]{co}),err=>{ if(err!=null)Lock(cf,"Manage error: "+err.Message); }); }
                        continue;
                    }
                    if(Alive(s.State))
                    {
                        link.Desired=RiskPolicy.Quantity(s.Quantity,link.Follower.Settings.Ratio);
                        if(link.Waiting)Submit(link); else Change(link);
                    }
                }
                catch(Exception ex) { Lock(link.Follower,"Manage error: "+ex.Message); }
            }
            // Filled links retained: follower fills may lag, and protective exits can arrive later.
        }
        // Lots this copier owns that no non-terminal, non-cancelled exit order of this copier is covering. Never optimistic:
        // an exit still shown Working counts only up to its remaining quantity, so the result can overstate but never understate the gap.
        int UnprotectedLots(EngineAccount f)
        {
            int owned=Math.Abs(OwnedPosition(f));
            int covered=links.Values.SelectMany(v=>v).Where(x=>x.Follower==f&&x.Exit&&x.Order!=null&&!x.Cancelled&&!Order.IsTerminalState(x.ObservedState)).Sum(x=>CapturedRemaining(x));
            return Math.Max(0,owned-covered);
        }
        string UnprotectedText(EngineAccount f)
        {
            int n=UnprotectedLots(f);
            return n>0?n+(n==1?" lot":" lots")+" UNPROTECTED (no protective exit working) — check NinjaTrader now and place or flatten by hand; this copier will not create it":"";
        }
        int OwnedPosition(EngineAccount f)
        {
            long net=0;
            foreach(var x in links.Values.SelectMany(v=>v).Where(x=>x.Follower==f&&x.Order!=null))
            {
                net+=(x.SubmittedAction==OrderAction.Buy||x.SubmittedAction==OrderAction.BuyToCover?1L:-1L)*x.ObservedFilled;
            }
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
            var reservations=links.Values.SelectMany(v=>v).Where(x=>x.Follower==f&&x!=current&&x.Exit&&x.Order!=null&&x.SubmittedAction==current.Latest.Action)
                // Issue #3: a sibling the engine retired because its OCO group completed no longer reserves capacity for ANOTHER group.
                // Its stale captured Working state would otherwise make a still-open group look like it has no closable exposure and
                // get its sole protection cancelled. Leader-cancelled links (Cancelled but not OcoRetired) still reserve until acknowledged.
                // Same-OCO members are excluded again below by OCO id, so no same-group exception is needed here.
                .Where(x=>!x.OcoRetired)
                .Select(x=>Tuple.Create(x.Oco,CapturedRemaining(x))).ToList();
            // Unknown manual work consumes capacity but is never adopted. Owned wrappers use captured evidence only.
            Order[] actual; lock(f.Account.Orders)actual=f.Account.Orders.ToArray();
            reservations.AddRange(actual.Where(o=>o.Instrument!=null&&o.Instrument.FullName==instrument.FullName&&o.OrderAction==current.Latest.Action&&!Order.IsTerminalState(o.OrderState)&&FindFollowerLink(f,o,o.OrderId)==null)
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
                return CopyLedger.ExitBudget(RiskPolicy.ExitRemaining(link.Desired,link.ObservedFilled,closable),closable,ExitReserved(link));
            }
            bool opposite;
            opposite=links.Values.SelectMany(v=>v).Any(x=>x.Follower==link.Follower&&x!=link&&!x.Exit&&x.Order!=null&&CapturedRemaining(x)>0&&x.SubmittedAction!=link.Latest.Action);
            if(opposite||(link.Latest.Action==OrderAction.Buy && p<0)||(link.Latest.Action==OrderAction.SellShort && p>0)) { Lock(link.Follower,"Ambiguous reversal blocked"); return 0; }
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
        void Dispatch(EngineAccount f,Action call,Action<Exception> done)
        {
#if KINFX_SIM_TEST
            var leaderAtQueue=leader;
#endif
            Interlocked.Increment(ref providerCallsInFlight);
            Action<Exception> finish=err=>
            {
                Action fin=()=>{ try { done(err); } catch(Exception ex) { log("PROVIDER COMPLETION ERROR: "+ex.Message); } finally { Interlocked.Decrement(ref providerCallsInFlight); } };
                if(!worker.Post(fin)) { Interlocked.Decrement(ref providerCallsInFlight); if(err!=null)log(f.Account.Name+": provider call failed while engine stopping: "+err.Message); }
            };
            if(!f.Dispatcher.Post(()=>{ Exception err=null; try {
#if KINFX_SIM_TEST
                // SIM TEST BUILD: provider is re-read when the call is actually sent, not only at Start. Fail closed: the call is NOT sent and the account is locked by the normal error path.
                if(f.Account==null||!Sim(f.Account)||leaderAtQueue==null||!Sim(leaderAtQueue))throw new InvalidOperationException("SIM TEST: account is not Simulator / Playback at send time; call was NOT sent");
#endif
                call(); } catch(Exception ex) { err=ex; f.ProviderFaulted=true; } finish(err); }))
                finish(new InvalidOperationException("provider dispatcher stopped; call was NOT sent"));
        }
        void Submit(CopyLink link,string errPrefix="Order error: ")
        {
            var f=link.Follower;
            if(!Connected(f.Account)) { link.Waiting=false; Lock(f,"Disconnected — no catch-up"); return; }
            if(!link.Exit && (!entries||!f.Following||f.Paused||f.Latch.Locked||link.Cancelled)) { link.Waiting=false; return; }
            int qty=AllowedRemaining(link);
            if(qty<=0)
            {
                // Only retain delayed protection if THIS copier has an entry link; never adopt an unrelated later position.
                if(link.Exit && !f.ExitCompleted && links.Values.SelectMany(x=>x).Any(x=>x.Follower==f&&!x.Exit&&x.Order!=null&&(!x.Cancelled||x.ObservedFilled>0)))
                { if(DateTime.UtcNow-link.WaitingSince>TimeSpan.FromSeconds(10)){link.Waiting=false;Lock(f,"Exit could not obtain closable exposure within 10s — review protection");}else link.Waiting=true;return; }
                link.Waiting=false; if(!link.Exit) log(f.Account.Name+": entry skipped, projected cap / floor quantity"); return;
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
            Dispatch(f,()=>{ if(!link.Exit&&f.ProviderFaulted)throw new InvalidOperationException("entry not sent: an earlier provider call for this account failed"); f.Account.Submit(new[]{order}); },err=>
            {
                try { if(err==null)log("COPY "+f.Account.Name+" "+s.Action+" "+qty+" "+s.Type); else Lock(f,errPrefix+err.Message); }
                finally { lock(controlGate)link.SubmissionInFlight=false; RecomputeDisablePending(f); }
            });
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
            int qty=RiskPolicy.ChangeTotal(link.ReservedQuantity,filled,link.Desired,link.Exit,closable,link.Exit?ExitReserved(link):PendingEntries(f,order),f.Settings.MaxContracts,entries&&f.Following&&!f.Paused&&!f.Latch.Locked);
            // Effective quantity the provider holds or is about to hold: a Change still in flight counts, not the stale applied value.
            bool changePending=Volatile.Read(ref link.ChangesInFlight)>0;
            int effectiveQty=changePending?link.SentQty:order.Quantity;
            if(positionBehindOwned&&link.Exit&&qty<effectiveQty)qty=effectiveQty; // freeze quantity, keep price updates
            if(qty<=filled)
            {
                link.Cancelled=true; link.Waiting=false; Dispatch(f,()=>f.Account.Cancel(new[]{order}),err=>{ if(err!=null)Lock(f,errPrefix+err.Message); });
                if(link.Exit&&p==0&&OwnedPosition(f)!=0)Lock(f,"Flat Position conflicts with owned executions — exits cancelled to prevent reversal; review protection");
                return;
            }
            int curQty=effectiveQty; double curLimit=changePending?link.SentLimit:order.LimitPrice, curStop=changePending?link.SentStop:order.StopPrice;
            if(curQty==qty && Math.Abs(curLimit-s.Limit)<1e-9 && Math.Abs(curStop-s.Stop)<1e-9)return;
            link.RequestedQuantity=qty; link.ReservedQuantity=Math.Max(link.ReservedQuantity,qty);
            // The requested values are captured NOW, in a command applied on the account's own thread. Writing them to the shared
            // Order object here and sending later would let a newer Change overwrite an older, still-queued one.
            double wantLimit=s.Limit,wantStop=s.Stop; int wantQty=qty;
            link.SentQty=wantQty; link.SentLimit=wantLimit; link.SentStop=wantStop; Interlocked.Increment(ref link.ChangesInFlight);link.PendingChangeQuantities.Add(wantQty);
            Dispatch(f,()=>{ try { order.QuantityChanged=wantQty; order.LimitPriceChanged=wantLimit; order.StopPriceChanged=wantStop; f.Account.Change(new[]{order}); } finally { Interlocked.Decrement(ref link.ChangesInFlight); } },err=>{ link.PendingChangeQuantities.Remove(wantQty);if(err!=null)Lock(f,errPrefix+err.Message); });
        }
        void FollowerUpdate(object sender,OrderEventArgs e,int generation,string subscribedInstrument)
        {
            if(generation!=Volatile.Read(ref sessionGeneration))return;
            Account account=sender as Account;
            if(e.Order==null||account==null||e.Order.Account!=account||e.Order.Instrument==null||e.Order.Instrument.FullName!=subscribedInstrument)return;
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
                if(link==null&&!f.Flattening)Lock(f,"Unknown follower order activity — no adoption / manual review");
                if(link!=null)
                {
                    if(!string.IsNullOrEmpty(link.ProviderId)&&snapshot.ProviderId!=link.ProviderId) { Lock(f,"Follower provider identity transition ambiguous — captured fill evidence retained, manual recovery required"); }
                    else if(!string.IsNullOrEmpty(snapshot.ProviderId))link.ProviderId=snapshot.ProviderId;
                    link.Wrappers.Add(order);link.Order=order;
                    if(eventState==OrderState.Accepted||eventState==OrderState.Working||eventState==OrderState.TriggerPending||eventState==OrderState.PartFilled||snapshot.Filled>0)link.AcceptanceConfirmed=true;
                    int prior=link.ObservedFilled;
                    if(snapshot.Filled!=prior||Order.IsTerminalState(snapshot.State))link.ExecutionEvidence.Add(snapshot);
                    if(snapshot.Filled<prior||snapshot.Action!=link.SubmittedAction||snapshot.Type!=link.Latest.Type)Lock(f,"Follower execution metadata divergence — manual recovery required");
                    bool terminalRegression=(link.ObservedState==OrderState.Filled&&eventState!=OrderState.Filled)||(Order.IsTerminalState(link.ObservedState)&&!Order.IsTerminalState(eventState));
                    if(terminalRegression)Lock(f,"Follower terminal-state evidence divergence — older active state cannot revoke terminal acknowledgement; manual recovery required");
                    link.ObservedFilled=Math.Max(prior,snapshot.Filled);
                    if(!terminalRegression)
                    {
                        link.ObservedState=eventState;
                        // An old ACK can equal the newest quantity after a 2 -> 4 -> 2 sequence.
                        // Never release a larger reservation when the captured wrapper/dispatch intent
                        // still contradicts that reduction. These hints can refuse release, not certify acceptance.
                        bool reductionCoherent=snapshot.Quantity>=snapshot.WrapperQuantity&&snapshot.Quantity>=snapshot.WrapperQuantityChanged;
                        // Mutable wrapper quantities cannot reveal a larger Change still queued on the dispatcher.
                        // Engine-owned pending command intent can veto release, never certify acceptance.
                        bool pendingLarger=link.PendingChangeQuantities.Any(q=>q>snapshot.Quantity);
                        link.ReservedQuantity=snapshot.Quantity==link.RequestedQuantity&&reductionCoherent&&!pendingLarger?snapshot.Quantity:Math.Max(link.ReservedQuantity,snapshot.Quantity);
                    }
                    if(eventState==OrderState.Filled&&snapshot.Filled!=link.RequestedQuantity)
                        Lock(f,"Terminal fill quantity divergence — expected="+link.RequestedQuantity+", observed="+snapshot.Filled+"; no makeup / chase, manual reconciliation required");
                    if(link.Exit&&snapshot.Filled>prior&&!string.IsNullOrEmpty(link.Oco)&&links.Values.SelectMany(v=>v).Any(x=>x!=link&&x.Follower==f&&x.Exit&&x.Oco==link.Oco&&x.ObservedFilled>0))
                        Lock(f,"OCO multiple exits filled — durable divergence lock; provider atomicity not guaranteed, manual recovery required");
                    if(link.Exit&&eventState==OrderState.Filled)
                    {
                        f.ExitCompleted=true; f.Following=false; link.Cancelled=true; link.Waiting=false;
                        CancelEntries(f);
                        foreach(var waiting in links.Values.SelectMany(v=>v).Where(x=>x.Follower==f&&x.Exit&&x.Waiting&&x.Order==null)) { waiting.Cancelled=true; waiting.Waiting=false; }
                        foreach(var sibling in links.Values.SelectMany(v=>v).Where(x=>x.Follower==f&&x.Exit&&x!=link&&!string.IsNullOrEmpty(link.Oco)&&x.Oco==link.Oco).ToArray())
                        {
                            sibling.Cancelled=true; sibling.Waiting=false; sibling.OcoRetired=true;
                            if(sibling.Order!=null&&!Order.IsTerminalState(sibling.Order.OrderState)) { var so=sibling.Order; Dispatch(f,()=>f.Account.Cancel(new[]{so}),err=>{ if(err!=null)Lock(f,"OCO cancellation unresolved: "+err.Message); }); }
                        }
                    }
                    if(link.Exit&&(eventState==OrderState.Cancelled||eventState==OrderState.Rejected))
                    {
                        f.ExitCompleted=true; f.Following=false; link.Cancelled=true; link.Waiting=false;
                        CancelEntries(f);
                        foreach(var waiting in links.Values.SelectMany(v=>v).Where(x=>x.Follower==f&&x.Exit&&x.Waiting&&x.Order==null)) { waiting.Cancelled=true;waiting.Waiting=false; }
                        bool protectedElsewhere=links.Values.SelectMany(v=>v).Any(x=>x.Follower==f&&x.Exit&&x!=link&&x.Order!=null&&!Order.IsTerminalState(x.Order.OrderState)&&!x.Cancelled);
                        if(OwnedPosition(f)!=0&&!protectedElsewhere)Lock(f,"Protective exit terminal with uncovered exposure — manual review required"+(UnprotectedLots(f)>0?" — "+UnprotectedText(f):""));
                    }
                    if(!link.Exit&&snapshot.Filled>prior&&f.ExitCompleted)Lock(f,"Late uncovered entry fill after exit completion — manual recovery required; coverage not established"+(UnprotectedLots(f)>0?" — "+UnprotectedText(f):""));
                }
                RecomputeDisablePending(f);
                if(eventState==OrderState.Rejected) Lock(f,"Follower order rejected — review protection");
                CheckRisk(f);
                MaintainProtection(f);
                }
                finally { Interlocked.Decrement(ref pendingFollowerCallbacks); ReconcileCloseEvidence(); }
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
            if(suppressNew||f.Flattening)return;
            foreach(var link in links.Values.SelectMany(x=>x).Where(x=>x.Follower==f && x.Waiting && !x.Cancelled).ToArray())
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
        void CheckRisk(EngineAccount f)
        {
            if(f.Flattening)
            {
                if(Clean(f.Account,instrument)&&OwnedPosition(f)==0&&!HasPendingEntryWork(f))flattenConfirmations[f]="Confirmed flat";
                else { flattenConfirmations[f]="Awaiting provider flat / order confirmation"; Lock(f,"Flatten unconfirmed — no concurrent recovery exits authorized; verify provider state manually before explicit abandonment"); }
            }
            if(!f.Flattening)
            {
                Order[] actual; lock(f.Account.Orders)actual=f.Account.Orders.ToArray();
                if(actual.Any(o=>o.Instrument!=null&&o.Instrument.FullName==instrument.FullName&&(!Order.IsTerminalState(o.OrderState)||o.Filled>0)&&FindFollowerLink(f,o,o.OrderId)==null))Lock(f,"Unknown follower order / terminal fill evidence — no adoption; manual recovery required");
                if(Volatile.Read(ref pendingFollowerCallbacks)==0&&links.Values.SelectMany(v=>v).Any(x=>x.Follower==f&&x.Order!=null&&(x.Order.Filled!=x.ObservedFilled||(Order.IsTerminalState(x.Order.OrderState)&&x.Order.OrderState!=x.ObservedState))))
                    Lock(f,"Provider wrapper diverges from captured order / fill evidence — reservations retained; manual recovery required");
                int position=Position(f.Account,instrument), execution=OwnedPosition(f);
                if(Volatile.Read(ref pendingFollowerCallbacks)<=1&&position!=0&&(execution==0||Math.Sign(position)!=Math.Sign(execution)||Math.Abs((long)position)>Math.Abs((long)execution)))Lock(f,"Position diverges from owned execution ledger — manual recovery required");
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
            Publish(f);
        }
        void Publish(EngineAccount f)
        {
            int generation,processed,session; bool following,pending,locked,paused,armed; string reason;
            lock(controlGate) { generation=f.FollowGeneration; processed=f.ProcessedFollowGeneration; session=sessionGeneration; following=f.Following; pending=f.DisablePending; locked=f.Latch.Locked; reason=f.Latch.Reason; paused=f.Paused; armed=entries; }
            if(!string.IsNullOrEmpty(f.EvidenceReason)&&reason!=f.EvidenceReason)reason=(string.IsNullOrEmpty(reason)?"":reason+" | ")+f.EvidenceReason;
            bool coverageUnknown=links.Values.SelectMany(v=>v).Any(x=>x.Follower==f&&x.Exit&&x.Order!=null&&!Order.IsTerminalState(x.ObservedState)&&!x.AcceptanceConfirmed);
            if(coverageUnknown)reason=(string.IsNullOrEmpty(reason)?"":reason+" | ")+"Protective coverage unknown — requested, provider acceptance unconfirmed; no retry / chase";
            double realized=f.Account.Get(AccountItem.RealizedProfitLoss,f.Account.Denomination), unrealized=f.Account.Get(AccountItem.UnrealizedProfitLoss,f.Account.Denomination);
            telemetry(new AccountTelemetry {Name=f.Account.Name,Baseline=f.Settings.Baseline,Currency=f.Account.Denomination.ToString(),Connection=Connected(f.Account)?"Connected":"Disconnected",Pnl=realized+unrealized-f.Settings.Baseline,Equity=f.Account.Get(AccountItem.NetLiquidation,f.Account.Denomination),Realized=realized,Unrealized=unrealized,Sod=f.Account.Get(AccountItem.SodLiquidatingValue,f.Account.Denomination),HighWater=f.HighWater,Position=Position(f.Account,instrument),Direction=Position(f.Account,instrument)>0?"Long":Position(f.Account,instrument)<0?"Short":"Flat",Following=following,FollowGeneration=generation,ProcessedFollowGeneration=processed,SessionGeneration=session,DisablePending=pending,Locked=locked,Reason=reason,Paused=paused,State=locked||coverageUnknown?reason:pending?"OFF — cancellation pending":!following?"OFF / exits managed":paused||!armed?"Paused / exits managed":"Active"});
        }
        public void Poll()
        {
            if(Interlocked.Exchange(ref pollPending,1)!=0)return;
            if(!worker.Post(()=>{ try { if(attached) { if(!Connected(leader)){entries=false;foreach(var f in followers)Lock(f,"Leader disconnected — no catch-up");} foreach(var f in followers)try {CheckRisk(f);MaintainProtection(f);}catch(Exception ex){Lock(f,"Telemetry error: "+ex.Message);} } } finally {Interlocked.Exchange(ref pollPending,0);} }))Interlocked.Exchange(ref pollPending,0); // refused by a stopped mailbox: do not leave the flag stuck
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
                    { link.CancellationRequested=true; var eo=link.Order; Dispatch(f,()=>f.Account.Cancel(new[]{eo}),err=>{ if(err!=null) { link.CancellationRequested=false; f.DisablePending=true; Lock(f,"Entry cancellation unresolved: "+err.Message); } }); }
            }
            RecomputeDisablePending(f);
            } finally { f.Cancelling=false; }
        }
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
                bool clean=Clean(f.Account,instrument)&&OwnedPosition(f)==0&&!ExposureOrders(f).Any(o=>o.Instrument!=null&&o.Instrument.FullName==instrument.FullName&&!Order.IsTerminalState(o.OrderState))&&!f.DisablePending&&!f.Latch.Locked&&Connected(f.Account)&&entries&&!f.Flattening;
                lock(controlGate)
                {
                    if(request!=f.FollowGeneration)return;
                    if(clean) { lock(identityGate)foreach(var id in identities.Values)f.Ignored.Add(id); f.ExitCompleted=false; f.Following=true; }
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
        void BlockEntries() { lock(controlGate) { ++controlGeneration; entries=false; } }
        public void GuardedPause()
        { BlockEntries(); worker.Post(()=>{stateChanged(attached,"GUARDED PAUSE — no new entries; existing lifecycle + new exits still managed");log("Paused NEW entries. Working entries may still fill; new exits and changes/cancels remain mirrored. Flatten explicitly to remove exposure.");}); }
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
            BlockEntries();suppressNew=true; // block ALL new copies during the flatten request.
            bool flattenAccepted=worker.Post(()=>
            {
                if(!SessionOwnership.Acquire(targets.Select(a=>"account:"+a.Name),this)){suppressNew=false;log("Flatten blocked: another copier owns an account in this scope.");return;}
                if(attached && targets.Contains(leader) && followers.Any(f=>!targets.Contains(f.Account) && !Clean(f.Account,instrument)))
                {suppressNew=false;log("Flatten leader blocked: other managed followers still have exposure/working orders. Include all managed followers or flatten followers first.");return;}
                foreach(var f in followers.Where(f=>targets.Contains(f.Account))) { f.Flattening=true;flattenConfirmations[f]="Requested"; }
                stateChanged(attached,"FLATTEN requested — new entries blocked; verify flat status");
                foreach(var account in targets)
                {
                    if(!Sim(account)) {log("LIVE FLATTEN BLOCKED "+account.Name);continue;}
                    var target=account; var tf=followers.FirstOrDefault(x=>x.Account==target);
                    Action<Exception> flattenDone=ex=>
                    {
                        if(ex==null){log("Flatten request: "+target.Name+" / "+inst.FullName+" — verify flat, retry manually if error");return;}
                        if(tf!=null) { flattenConfirmations[tf]="Request threw — provider acceptance unknown"; Lock(tf,"Flatten acceptance uncertain — existing protection retained; duplicate reductions blocked until explicit provider reconciliation: "+ex.Message); }
                        log("FLATTEN FAILED "+target.Name+": "+ex.Message);
                    };
                    // A follower's Flatten goes through ITS dispatcher so it can never overtake a Submit/Change/Cancel already queued for the same account.
                    if(tf!=null)Dispatch(tf,()=>target.Flatten(new[]{inst}),flattenDone);
                    else { try { target.Flatten(new[]{inst}); flattenDone(null); } catch(Exception ex) { flattenDone(ex); } }
                }
                if(attached)suppressNew=false; // selected followers excluded; unflattened followers still need new protective exits.
                if(!attached)SessionOwnership.Release(this);
            });
            // Refused by a stopped mailbox: nothing will run to clear the flag, and nothing reached an account.
            if(!flattenAccepted){suppressNew=false;log("Flatten refused: copier is stopped; no request was sent.");}
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
                if(!Connected(f.Account)||HasPendingEntryWork(f)||ExposureOrders(f).Any(o=>o.Instrument!=null&&o.Instrument.FullName==instrument.FullName&&!Order.IsTerminalState(o.OrderState)&&!known.Contains(o)))
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
        public string Currency { get; set; }
        public string Connection { get; set; }
        public string Status { get; set; }
        public double Pnl { get; set; }
        public double Equity { get; set; }
        public double CashBalance {get;set;}=double.NaN;
        public string CurrencyCode
        {
            get
            {
                string[] names={"AustralianDollar","BritishPound","CanadianDollar","Euro","HongKongDollar","JapaneseYen","SwissFranc","UsDollar","KoreanWon","IndianRupee","SwedishKrona","MexicanPeso","BrazilianReal","SingaporeDollar","ChinaYuan","MalaysiaRinggit","ThailandBaht","TaiwanNewDollar","NewZealandDollar","SouthAfricanRand","CzechRepublicKoruna","NorwayKrone","DenmarkKrone","HungaryForint","PolandZloty","TurkeyLira","IsraeliShekel","RussiaRuble","SaudiRiyal"};
                string[] codes={"AUD","GBP","CAD","EUR","HKD","JPY","CHF","USD","KRW","INR","SEK","MXN","BRL","SGD","CNY","MYR","THB","TWD","NZD","ZAR","CZK","NOK","DKK","HUF","PLN","TRY","ILS","RUB","SAR"};
                int index=Array.IndexOf(names,Currency);return index>=0?codes[index]:string.IsNullOrWhiteSpace(Currency)?"CCY?":Currency;
            }
        }
        public string BalanceText {get{return (Connection=="Connected"||Connection=="Synthetic")&&RiskPolicy.Finite(CashBalance)?CashBalance.ToString("N2",CultureInfo.InvariantCulture)+" "+CurrencyCode:"— "+CurrencyCode;}}
        public string BalanceDetail {get{return "CashValue · "+Name+" · "+BalanceText+"\nNetLiquidation (net equity, NOT cash balance): "+((Connection=="Connected"||Connection=="Synthetic")&&RiskPolicy.Finite(Equity)?Equity.ToString("N2",CultureInfo.InvariantCulture):"—")+" "+CurrencyCode+"\nAccount.Get reports provider values; currency argument does not convert. Dash = unavailable/disconnected. Synthetic rows are not broker data.";}}
        public double Realized { get; set; }
        public double Unrealized { get; set; }
        public double Sod { get; set; }
        public int Position { get; set; }
        public bool Paused { get; set; }
        bool enabled;
        public bool DisablePending {get;set;}
        public event Action<AccountRow,bool> FollowRequested;
        public bool Enabled {get{return enabled;}set{if(value&&(!CanFollow||Latch.Locked||DisablePending))return;if(enabled==value&&(value||!FollowPending))return;enabled=value;Changed();var h=FollowRequested;if(h!=null)h(this,value);}}
        int latestFollowSession=-1,latestFollowGeneration;
        public bool FollowPending {get;private set;}
        internal void SyncFollowing(bool following,bool pending){enabled=following&&!Latch.Locked;DisablePending=pending;FollowPending=false;Changed();}
        internal void InvalidateSessionActivity(){FollowPending=false;DisablePending=false;Paused=false;Status="Disarmed";latestFollowSession=-1;latestFollowGeneration=0;Changed();}
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
            Pnl=t.Pnl;Equity=t.Equity;Realized=t.Realized;Unrealized=t.Unrealized;Sod=t.Sod;Position=t.Position;Connection=t.Connection;Status=t.State;Paused=t.Paused;
            if(t.Locked&&!Latch.Locked){Latch.Locked=true;Latch.Reason=t.Reason;}
            Settings.HighWater=t.HighWater;Settings.Baseline=t.Baseline;Settings.BaselineSet=true;
            if(t.ProcessedFollowGeneration==latestFollowGeneration)SyncFollowing(t.Following,t.DisablePending);else Changed();return true;
        }
        public bool CopyAtm {get{return Settings.CopyAtm;}set{Settings.CopyAtm=value;Changed();}}
        public string OnDivergence {get{return Settings.OnDivergence;}set{if(!DivergenceChoice.IsValid(value))throw new ArgumentException("On divergence must be Notify or FlattenOnce");Settings.OnDivergence=value;Changed();}}
        public bool CanFollow {get{return Role!="Leader"&&(Account!=null?CopierEngine.Sim(Account):Connection=="Synthetic");}}
        public Brush ConnLight {get{return Connection=="Connected"?DashboardView.Good:Connection=="Disconnected"?DashboardView.Bad:Connection=="Synthetic"||Connection=="Connecting"?DashboardView.Warn:DashboardView.Off;}}
        public Brush StateLight {get{return Latch.Locked||Status=="Telemetry error"?DashboardView.Bad:Paused||Status=="Target hit"||(Status??"").IndexOf("pause",StringComparison.OrdinalIgnoreCase)>=0?DashboardView.Warn:Status=="Active"?DashboardView.Good:DashboardView.Off;}}
        public string AccountPrefix {get{string n=Name??"";return n.Length>12?n.Substring(0,n.Length-4):n;}}
        public string AccountSuffix {get{string n=Name??"";return n.Length>12?n.Substring(n.Length-4):"";}}
        public string StateLabel {get{return DisablePending?"Cancelling":FollowPending?"Rechecking":Latch.Locked?"Locked":Status=="Telemetry error"?"Error":Paused?"Paused":Role=="Leader"?"Leader":Status=="Active"&&Enabled?"Copying":Connection=="Disconnected"?"Offline":Status=="Demo only"?"Demo":Enabled?"Selected":"Off";}}
        public Brush StateInk {get{return DisablePending?DashboardView.Warn:Latch.Locked||Status=="Telemetry error"?DashboardView.Bad:StateLabel=="Copying"?DashboardView.Good:Paused?DashboardView.Warn:DashboardView.Muted;}}
        // Advisory only (never blocks). Reference lot size is the dashboard's current reference; see SettingsAdvice.
        internal static int AdviceReferenceLots=2;
        public string Advice {get{return SettingsAdvice.For(Settings,Role,AdviceReferenceLots);}}
        public string StateDetail {get{return Name+" · "+Role+" · "+Status+(DisablePending?" · cancellation not confirmed":"")+(Latch.Locked?" · "+Latch.Reason:"")+(Advice.Length>0?" · NOTE: "+Advice:"")+" · "+Currency;}}
        public string Alias { get {return Settings.Alias;} set {Settings.Alias=value ?? "";} }
        public string Group { get {return Settings.Group;} set {Settings.Group=value ?? "";} }
        public double Ratio { get {return Settings.Ratio;} set {if(!RiskPolicy.Finite(value)||value<=0)throw new ArgumentException("This account Ratio must be finite and > 0");ratioDraft=null;Settings.Ratio=value;Changed();} }
        public double Target { get {return Settings.Target;} set {if(!RiskPolicy.Finite(value)||value<0)throw new ArgumentException("Daily target must be a finite amount >= 0 (0 = off)");targetDraft=null;Settings.Target=value;} }
        public double Loss { get {return Settings.Loss;} set {if(!RiskPolicy.Finite(value)||value<0)throw new ArgumentException("Daily loss must be a finite amount >= 0 (0 = off)");lossDraft=null;Settings.Loss=value;} }
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
            throw new ArgumentException((target?"Daily target":"Daily loss")+" must be a plain amount like 1000 (0 = off): no commas, signs or text");
        }
        public int MaxContracts { get {return Settings.MaxContracts;} set {if(value<1)throw new ArgumentException("Max contracts must be a whole number >= 1");maxDraft=null;Settings.MaxContracts=value;} }
        public string RatioText {get{return ratioDraft!=null?ratioDraft:Ratio.ToString("0.################",CultureInfo.InvariantCulture);}set{SetRatioText(value);}}
        public string MaxText {get{return maxDraft!=null?maxDraft:MaxContracts.ToString(CultureInfo.InvariantCulture);}set{SetMaxText(value);}}
        void SetRatioText(string text)
        {
            double v; string t=(text??"").Trim();
            if(t.Length>0&&double.TryParse(t,NumberStyles.AllowDecimalPoint|NumberStyles.AllowExponent,CultureInfo.InvariantCulture,out v)&&RiskPolicy.Finite(v)&&v>0){Ratio=v;return;}
            ratioDraft=text??"";
            throw new ArgumentException("Ratio must be a plain positive number like 1 or 0.5: no commas, signs or text");
        }
        void SetMaxText(string text)
        {
            int v; string t=(text??"").Trim();
            if(t.Length>0&&int.TryParse(t,NumberStyles.None,CultureInfo.InvariantCulture,out v)&&v>=1){MaxContracts=v;return;}
            maxDraft=text??"";
            throw new ArgumentException("Max must be a whole number >= 1");
        }
        public string TargetRemaining { get {return Target>0?Math.Max(0,Target-Pnl).ToString("N2"):"Off";} }
        public string LossRemaining { get {return Loss>0?Math.Max(0,Loss+Pnl).ToString("N2"):"Off";} }
        public string Preset { get {return Settings.Preset;} }
        public string ConsistencyAdvice {get{return RiskPolicy.ConsistencyAdvice(Settings);}}
        public event PropertyChangedEventHandler PropertyChanged;
        public void Changed() {var h=PropertyChanged;if(h!=null)h(this,new PropertyChangedEventArgs(""));}
    }
    public sealed class DashboardView : Grid
    {
        public readonly ObservableCollection<AccountRow> Rows=new ObservableCollection<AccountRow>();
        public readonly DataGrid AccountGrid, RiskGrid;
        public readonly TabControl Sections;
        internal readonly ComboBox LeaderBox;
        internal readonly TextBox InstrumentBox, LogBox;
        internal readonly Button StartButton;
        internal readonly TextBlock StatusText,SessionText,FollowCountText;
        internal readonly CheckBox ShowLive;
        internal Action<string> Command;
        internal Action<AccountRow,bool> PauseRow;
        internal Action<AccountRow> RowSettings;
        internal Action<AccountRow,bool> FollowRow;
        readonly ICollectionView view;
        readonly TextBox search;
        readonly ComboBox filter;
        readonly TextBlock summary,selection;TextBlock adviceLine,lockedLine;
        readonly bool demo;
        readonly List<Button> editButtons=new List<Button>(); StackPanel bulkBar;
        bool editable=true;
        public string SearchText {get{return search.Text;}set{search.Text=value;}}
        public int VisibleCount {get{return view.Cast<AccountRow>().Count();}}
        public int LockedCount {get{return Rows.Count(r=>r.Role!="Leader"&&r.Latch.Locked);}}
        public string LockedText {get{int n=LockedCount;return n==0?"":"● "+n+" LOCKED · check NinjaTrader, then review each reason";}}
        public int AdviceCount {get{return Rows.Count(r=>r.Role!="Leader"&&r.Advice.Length>0);}}
        public string AdviceText {get{int n=AdviceCount;return n==0?"":"⚠ "+n+" account(s) with a settings note · hover State";}}
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
        static TextBox Input(string text,double width){return new TextBox{Style=new Style(typeof(TextBox)),MinWidth=0,MinHeight=0,FontSize=13,Text=text,Width=width,Padding=new Thickness(8,5,8,5),Margin=new Thickness(0,0,8,0),Background=Panel,Foreground=Ink,BorderBrush=Line,VerticalContentAlignment=VerticalAlignment.Center};}
        internal static ComboBox Combo(double width)
        {
            var c=new ComboBox{Style=new Style(typeof(ComboBox)),MinWidth=0,MinHeight=0,FontSize=13,Width=width,Margin=new Thickness(0,0,8,0),Padding=new Thickness(8,5,8,5),Background=Panel,Foreground=Ink,BorderBrush=Line};
            var t=new ControlTemplate(typeof(ComboBox));var g=new FrameworkElementFactory(typeof(Grid));
            var toggle=new FrameworkElementFactory(typeof(ToggleButton));toggle.SetValue(Control.BackgroundProperty,Panel);toggle.SetValue(Control.BorderBrushProperty,Line);toggle.SetValue(Control.ForegroundProperty,Ink);toggle.SetValue(ContentControl.ContentProperty,"▾");toggle.SetValue(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Right);toggle.SetValue(Control.PaddingProperty,new Thickness(8,5,8,5));toggle.SetBinding(ToggleButton.IsCheckedProperty,new Binding("IsDropDownOpen"){RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent),Mode=BindingMode.TwoWay});g.AppendChild(toggle);
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
        static void Header(DataGrid g,DataGridColumn c,string tip){var h=new Style(typeof(DataGridColumnHeader),g.ColumnHeaderStyle);h.Setters.Add(new Setter(FrameworkElement.ToolTipProperty,tip));c.HeaderStyle=h;}
        static void Column(DataGrid g,string title,string path,double width,bool ro,string tip,string format=null,bool number=false)
        {
            var c=new DataGridTextColumn{Header=title,SortMemberPath=path.EndsWith("Text")?path.Substring(0,path.Length-4):path,Binding=new Binding(path){Mode=ro?BindingMode.OneWay:BindingMode.TwoWay,StringFormat=format,UpdateSourceTrigger=UpdateSourceTrigger.LostFocus,ValidatesOnExceptions=!ro,ConverterCulture=CultureInfo.InvariantCulture},Width=width,IsReadOnly=ro};
            var s=new Style(typeof(TextBlock));s.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center));s.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(9,0,9,0)));if(number){var hs=new Style(typeof(DataGridColumnHeader),g.ColumnHeaderStyle);hs.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Right));g.Resources["NumericHeader"]=hs;s.Setters.Add(new Setter(TextBlock.TextAlignmentProperty,TextAlignment.Right));s.Setters.Add(new Setter(TextBlock.FontFamilyProperty,new FontFamily("Consolas")));}c.ElementStyle=s;Header(g,c,tip);if(number){var hs=new Style(typeof(DataGridColumnHeader),c.HeaderStyle);hs.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Right));c.HeaderStyle=hs;}g.Columns.Add(c);
        }
        static void RatioColumn(DataGrid g)
        {
            var input=new FrameworkElementFactory(typeof(TextBox));input.SetValue(FrameworkElement.TagProperty,"rowRatio");input.SetValue(Control.BackgroundProperty,Panel);input.SetValue(Control.ForegroundProperty,Ink);input.SetValue(Control.BorderBrushProperty,Line);input.SetValue(Control.BorderThicknessProperty,new Thickness(1));input.SetValue(Control.PaddingProperty,new Thickness(4,1,4,1));input.SetValue(FrameworkElement.MarginProperty,new Thickness(4,1,4,1));input.SetValue(FrameworkElement.MinWidthProperty,0.0);input.SetValue(Control.FontSizeProperty,13.0);input.SetValue(TextBox.TextAlignmentProperty,TextAlignment.Right);input.SetValue(Control.VerticalContentAlignmentProperty,VerticalAlignment.Center);
            const string tip="Ratio for THIS account only · finite > 0 · follower qty = floor(leader qty × ratio) · locked while attached";
            input.SetValue(FrameworkElement.ToolTipProperty,tip);input.SetValue(System.Windows.Automation.AutomationProperties.NameProperty,tip);
            input.SetBinding(TextBox.TextProperty,new Binding("RatioText"){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.LostFocus,ValidatesOnExceptions=true});
            var style=new Style(typeof(TextBox));var locked=new DataTrigger{Binding=new Binding("IsReadOnly"){RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(DataGrid),1)},Value=true};locked.Setters.Add(new Setter(UIElement.IsEnabledProperty,false));style.Triggers.Add(locked);input.SetValue(FrameworkElement.StyleProperty,style);
            var c=new DataGridTemplateColumn{Header="Ratio",SortMemberPath="Ratio",Width=65,CellTemplate=new DataTemplate{VisualTree=input}};Header(g,c,tip);g.Columns.Add(c);
        }
        static ControlTemplate FollowTemplate()
        {
            var t=new ControlTemplate(typeof(CheckBox));var hit=new FrameworkElementFactory(typeof(Grid));hit.SetValue(FrameworkElement.WidthProperty,32.0);hit.SetValue(FrameworkElement.HeightProperty,26.0);hit.SetValue(System.Windows.Controls.Panel.BackgroundProperty,Brush("#00000000"));var box=new FrameworkElementFactory(typeof(Border),"TickBox");box.SetValue(FrameworkElement.WidthProperty,17.0);box.SetValue(FrameworkElement.HeightProperty,17.0);box.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);box.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);box.SetValue(Border.CornerRadiusProperty,new CornerRadius(4));box.SetValue(Border.BackgroundProperty,Canvas);box.SetValue(Border.BorderBrushProperty,Muted);box.SetValue(Border.BorderThicknessProperty,new Thickness(1));var mark=new FrameworkElementFactory(typeof(TextBlock),"Mark");mark.SetValue(TextBlock.TextProperty,"✓");mark.SetValue(TextBlock.FontSizeProperty,12.0);mark.SetValue(TextBlock.ForegroundProperty,Canvas);mark.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);mark.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);mark.SetValue(UIElement.VisibilityProperty,Visibility.Hidden);box.AppendChild(mark);hit.AppendChild(box);t.VisualTree=hit;var on=new Trigger{Property=ToggleButton.IsCheckedProperty,Value=true};on.Setters.Add(new Setter(Border.BackgroundProperty,Accent,"TickBox"));on.Setters.Add(new Setter(Border.BorderBrushProperty,Accent,"TickBox"));on.Setters.Add(new Setter(UIElement.VisibilityProperty,Visibility.Visible,"Mark"));t.Triggers.Add(on);var disabled=new Trigger{Property=UIElement.IsEnabledProperty,Value=false};disabled.Setters.Add(new Setter(UIElement.OpacityProperty,0.45,"TickBox"));t.Triggers.Add(disabled);var focus=new Trigger{Property=UIElement.IsKeyboardFocusedProperty,Value=true};focus.Setters.Add(new Setter(Border.BorderBrushProperty,Ink,"TickBox"));focus.Setters.Add(new Setter(Border.BorderThicknessProperty,new Thickness(2),"TickBox"));t.Triggers.Add(focus);return t;
        }
        static void TickColumn(DataGrid g)
        {
            var tick=new FrameworkElementFactory(typeof(CheckBox));tick.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);tick.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);tick.SetValue(FrameworkElement.TagProperty,"follow");tick.SetValue(Control.TemplateProperty,FollowTemplate());tick.SetValue(System.Windows.Automation.AutomationProperties.NameProperty,"Follow account; untick stops new entries");tick.SetValue(FrameworkElement.ToolTipProperty,"Explicit follow / OFF · pending cancels and latch may block re-enable");tick.SetBinding(ToggleButton.IsCheckedProperty,new Binding("Enabled"){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});var style=new Style(typeof(CheckBox));style.Setters.Add(new Setter(UIElement.IsEnabledProperty,new Binding("CanFollow")));tick.SetValue(FrameworkElement.StyleProperty,style);
            var c=new DataGridTemplateColumn{Header="On",Width=40,CellTemplate=new DataTemplate{VisualTree=tick},SortMemberPath="Enabled"};Header(g,c,"Follower enabled · one click");g.Columns.Add(c);
        }
        static void AccountColumn(DataGrid g)
        {
            var line=new FrameworkElementFactory(typeof(DockPanel));line.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);line.SetValue(FrameworkElement.MarginProperty,new Thickness(9,0,9,0));line.SetBinding(FrameworkElement.ToolTipProperty,new Binding("Name"));line.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty,new Binding("Name"));var suffix=new FrameworkElementFactory(typeof(TextBlock));suffix.SetValue(DockPanel.DockProperty,Dock.Right);suffix.SetBinding(TextBlock.TextProperty,new Binding("AccountSuffix"){Mode=BindingMode.OneWay});suffix.SetValue(TextBlock.ForegroundProperty,Ink);suffix.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);line.AppendChild(suffix);var prefix=new FrameworkElementFactory(typeof(TextBlock));prefix.SetBinding(TextBlock.TextProperty,new Binding("AccountPrefix"){Mode=BindingMode.OneWay});prefix.SetValue(TextBlock.ForegroundProperty,Muted);prefix.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);line.AppendChild(prefix);var c=new DataGridTemplateColumn{Header="Acct",Width=new DataGridLength(1,DataGridLengthUnitType.Star),MinWidth=175,SortMemberPath="Name",IsReadOnly=true,CellTemplate=new DataTemplate{VisualTree=line}};Header(g,c,"Exact broker identity · full name on hover · differing suffix emphasized");g.Columns.Add(c);
        }
        static void StateColumn(DataGrid g)
        {
            var border=new FrameworkElementFactory(typeof(Border));border.SetValue(FrameworkElement.MarginProperty,new Thickness(5,4,5,4));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(4));border.SetValue(Border.BackgroundProperty,Panel);border.SetBinding(FrameworkElement.ToolTipProperty,new Binding("StateDetail"));var text=new FrameworkElementFactory(typeof(TextBlock));text.SetBinding(TextBlock.TextProperty,new Binding("StateLabel"));text.SetBinding(TextBlock.ForegroundProperty,new Binding("StateInk"));text.SetValue(TextBlock.FontSizeProperty,11.0);text.SetValue(TextBlock.FontWeightProperty,FontWeights.Medium);text.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);text.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);border.AppendChild(text);var c=new DataGridTemplateColumn{Header="State",Width=96,IsReadOnly=true,SortMemberPath="StateLabel",CellTemplate=new DataTemplate{VisualTree=border}};Header(g,c,"Observed copy state, NOT connection or follower selection");g.Columns.Add(c);
        }
        static ControlTemplate AtmTemplate()
        {
            var t=new ControlTemplate(typeof(CheckBox));var border=new FrameworkElementFactory(typeof(Border),"Switch");border.SetValue(Border.CornerRadiusProperty,new CornerRadius(4));border.SetValue(Border.BackgroundProperty,Canvas);border.SetValue(Border.BorderBrushProperty,Line);border.SetValue(Border.BorderThicknessProperty,new Thickness(1));border.SetValue(FrameworkElement.WidthProperty,38.0);border.SetValue(FrameworkElement.HeightProperty,20.0);var text=new FrameworkElementFactory(typeof(TextBlock),"Label");text.SetValue(TextBlock.TextProperty,"Off");text.SetValue(TextBlock.FontSizeProperty,11.0);text.SetValue(TextBlock.ForegroundProperty,Warn);text.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);text.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);border.AppendChild(text);t.VisualTree=border;var on=new Trigger{Property=ToggleButton.IsCheckedProperty,Value=true};on.Setters.Add(new Setter(TextBlock.TextProperty,"On","Label"));on.Setters.Add(new Setter(TextBlock.ForegroundProperty,Ink,"Label"));on.Setters.Add(new Setter(Border.BackgroundProperty,Panel,"Switch"));t.Triggers.Add(on);var focus=new Trigger{Property=UIElement.IsKeyboardFocusedProperty,Value=true};focus.Setters.Add(new Setter(Border.BorderBrushProperty,Accent,"Switch"));t.Triggers.Add(focus);var disabled=new Trigger{Property=UIElement.IsEnabledProperty,Value=false};disabled.Setters.Add(new Setter(UIElement.OpacityProperty,0.45,"Switch"));t.Triggers.Add(disabled);return t;
        }
        static void AtmColumn(DataGrid g)
        {
            var tick=new FrameworkElementFactory(typeof(CheckBox));tick.SetValue(FrameworkElement.TagProperty,"atm");tick.SetValue(Control.TemplateProperty,AtmTemplate());tick.SetValue(System.Windows.Automation.AutomationProperties.NameProperty,"Per-account ATM exit mirroring");tick.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);tick.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);tick.SetValue(FrameworkElement.ToolTipProperty,"Per-account ATM exit mirroring, NOT a native ATM clone. OFF ignores non-market SL / TP: follower can be unprotected. Market closes still copy. Frozen while attached.");tick.SetBinding(ToggleButton.IsCheckedProperty,new Binding("CopyAtm"){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});var style=new Style(typeof(CheckBox));style.Setters.Add(new Setter(UIElement.IsEnabledProperty,new Binding("CanFollow")));var locked=new DataTrigger{Binding=new Binding("IsReadOnly"){RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(DataGrid),1)},Value=true};locked.Setters.Add(new Setter(UIElement.IsEnabledProperty,false));style.Triggers.Add(locked);tick.SetValue(FrameworkElement.StyleProperty,style);var c=new DataGridTemplateColumn{Header="ATM",Width=56,SortMemberPath="CopyAtm",CellTemplate=new DataTemplate{VisualTree=tick}};Header(g,c,"Independent ATM exit mirror · OFF can leave exposure unprotected");g.Columns.Add(c);
        }
        static ControlTemplate OffTemplate()
        {
            var t=new ControlTemplate(typeof(Button));var border=new FrameworkElementFactory(typeof(Border),"Power");border.SetValue(Border.CornerRadiusProperty,new CornerRadius(4));border.SetValue(Border.BackgroundProperty,Panel);border.SetValue(Border.BorderThicknessProperty,new Thickness(1));border.SetValue(Border.BorderBrushProperty,Line);border.SetValue(FrameworkElement.WidthProperty,28.0);border.SetValue(FrameworkElement.HeightProperty,22.0);var p=new FrameworkElementFactory(typeof(ContentPresenter));p.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);p.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);border.AppendChild(p);t.VisualTree=border;var disabled=new Trigger{Property=UIElement.IsEnabledProperty,Value=false};disabled.Setters.Add(new Setter(UIElement.OpacityProperty,0.35,"Power"));t.Triggers.Add(disabled);var hover=new Trigger{Property=UIElement.IsMouseOverProperty,Value=true};hover.Setters.Add(new Setter(Border.BorderBrushProperty,Bad,"Power"));t.Triggers.Add(hover);var focus=new Trigger{Property=UIElement.IsKeyboardFocusedProperty,Value=true};focus.Setters.Add(new Setter(Border.BorderBrushProperty,Accent,"Power"));t.Triggers.Add(focus);return t;
        }
        static void OffColumn(DataGrid g)
        {
            var button=new FrameworkElementFactory(typeof(Button));CompactCellButton(button,32);button.SetValue(ContentControl.ContentProperty,"\uE7E8");button.SetValue(FrameworkElement.TagProperty,"off");button.SetValue(Control.FontFamilyProperty,new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"));button.SetValue(Control.BackgroundProperty,Panel);button.SetValue(Control.ForegroundProperty,Bad);button.SetValue(Control.BorderBrushProperty,Line);button.SetValue(Control.PaddingProperty,new Thickness(4,1,4,1));button.SetValue(Control.TemplateProperty,OffTemplate());button.SetValue(FrameworkElement.ToolTipProperty,"OFF: block new entries / request owned entry cancellation; retain exits. Pending fills may still occur. Explicit follow required again; latch remains.");button.SetValue(System.Windows.Automation.AutomationProperties.NameProperty,"Stop following this account");button.SetBinding(UIElement.IsEnabledProperty,new Binding("CanFollow"));button.AddHandler(ButtonBase.ClickEvent,new RoutedEventHandler((s,e)=>{var r=(s as FrameworkElement).DataContext as AccountRow;if(r!=null)r.Enabled=false;e.Handled=true;}));var c=new DataGridTemplateColumn{Header="Off",Width=42,IsReadOnly=true,CellTemplate=new DataTemplate{VisualTree=button}};Header(g,c,"One-click OFF is not Flatten; current positions / SL / TP remain");g.Columns.Add(c);
        }
        static void LightColumn(DataGrid g,string title,string path,string detail)
        {
            var light=new FrameworkElementFactory(typeof(System.Windows.Shapes.Ellipse));light.SetValue(FrameworkElement.WidthProperty,8.0);light.SetValue(FrameworkElement.HeightProperty,8.0);light.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);light.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);light.SetBinding(System.Windows.Shapes.Shape.FillProperty,new Binding(path));light.SetBinding(FrameworkElement.ToolTipProperty,new Binding(detail));var c=new DataGridTemplateColumn{Header=title,Width=54,IsReadOnly=true,SortMemberPath=detail,CellTemplate=new DataTemplate{VisualTree=light}};Header(g,c,detail+" · green / amber / red / off");g.Columns.Add(c);
        }
        static void ProportionalCopyColumns(DataGrid g)
        {
            // Star allocation across ALL data columns; fixed toggles/status lamps never stretch.
            var minimums=new Dictionary<string,double>{{"Acct",170},{"Alias",95},{"Ratio",60},{"Tgt",75},{"DLL",75},{"Qty",50},{"P/L",95},{"Bal",130},{"State",96}};
            foreach(var c in g.Columns)
            {
                double minimum;
                if(minimums.TryGetValue((string)c.Header,out minimum)){c.MinWidth=minimum;c.Width=new DataGridLength(minimum,DataGridLengthUnitType.Star);}
                else {double width=c.Width.Value;c.MinWidth=width;c.MaxWidth=width;}
            }
            g.HorizontalScrollBarVisibility=ScrollBarVisibility.Auto;
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
        public DashboardView(bool demoMode=false)
        {
            demo=demoMode;Background=Canvas;Margin=new Thickness(16);TextElement.SetFontFamily(this,new FontFamily("Segoe UI Variable Text, Segoe UI"));
            // Do not inherit NT8 application implicit dimensions/fonts for compact dashboard controls.
            foreach(Type type in new[]{typeof(Button),typeof(ToggleButton),typeof(CheckBox),typeof(TextBox),typeof(ComboBox),typeof(TabControl),typeof(DataGrid)})
            {var scope=new Style(type);scope.Setters.Add(new Setter(FrameworkElement.MinWidthProperty,0.0));scope.Setters.Add(new Setter(FrameworkElement.MinHeightProperty,0.0));scope.Setters.Add(new Setter(Control.FontSizeProperty,13.0));scope.Setters.Add(new Setter(Control.ForegroundProperty,Ink));Resources[type]=scope;}
            RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var header=new DockPanel{Margin=new Thickness(0,0,0,14)};StatusText=Text(demo?"DEMO · NO TRADES":"REVIEW HOLD · TRADING DISABLED",11,Warn);DockPanel.SetDock(StatusText,Dock.Right);header.Children.Add(StatusText);var brand=new StackPanel{Orientation=Orientation.Horizontal};brand.Children.Add(Text("KINFX",20,Accent));brand.Children.Add(new TextBlock{Text="  /  Copy desk",FontSize=17,Foreground=Ink,VerticalAlignment=VerticalAlignment.Center});header.Children.Add(brand);AddRow(this,header,0);
            var session=new WrapPanel{Margin=new Thickness(0,0,0,6)};LeaderBox=Combo(232);LeaderBox.ToolTip="Simulation leader account · no live opt-in";session.Children.Add(Labeled("Lead",LeaderBox));InstrumentBox=Input("MES 12-26",126);InstrumentBox.ToolTip="Exact instrument";session.Children.Add(Labeled("Inst",InstrumentBox));
            FollowCountText=Text("→  0 followers  ",12,Muted);session.Children.Add(FollowCountText);
            StartButton=Button("▶  Start",()=>Send("start"),true);StartButton.ToolTip="REVIEW HOLD: copying unavailable until engine safety issues are fixed";ToolTipService.SetShowOnDisabled(StartButton,true);StartButton.IsEnabled=false;session.Children.Add(StartButton);session.Children.Add(Icon("\uE769","Guarded pause · new entries only","pause",()=>Send("pause")));session.Children.Add(Icon("\uE71A","Stop when flat","stop",()=>Send("stop")));session.Children.Add(Icon("\uE72C","Refresh accounts","refresh",()=>Send("refresh")));AddRow(this,new Border{Background=Panel,BorderBrush=Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(7),Padding=new Thickness(12,8,12,0),Margin=new Thickness(0,0,0,10),Child=session},1);
            Sections=new TabControl{Background=Canvas,Foreground=Ink,BorderBrush=Line,Padding=new Thickness(0,10,0,0),ItemContainerStyle=TabStyle()};AddRow(this,Sections,2);
            var copy=new Grid();copy.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});copy.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
            var toolbar=new WrapPanel{Margin=new Thickness(0,0,0,9)};
            search=Input("",185);search.ToolTip="Search account / alias / group / preset · Ctrl+F · Escape clears";toolbar.Children.Add(Labeled("⌕",search,17));filter=Combo(120);filter.ToolTip="Filter displayed account states";filter.ItemsSource=new[]{"All","On","Paused","Target"};filter.SelectedIndex=0;toolbar.Children.Add(filter);var ticks=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,6)};ticks.Children.Add(Icon("\uE73A","Tick all visible followers","tickAll",()=>TickVisible(true),true));ticks.Children.Add(Icon("\uE711","Untick all visible followers","tickNone",()=>TickVisible(false),true));toolbar.Children.Add(ticks);toolbar.Children.Add(new TextBlock{Text="Ratio: edit each account row",Foreground=Muted,FontSize=12,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,0,8,6)});var bulk=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,6)};bulk.Tag="bulkbar";bulk.Children.Add(Text("Bulk  ",11,Muted));var bulkValue=Input("",78);bulkValue.ToolTip="Amount for the highlighted accounts · account currency · 0 = off";System.Windows.Automation.AutomationProperties.SetName(bulkValue,"Bulk amount");bulk.Children.Add(bulkValue);bulk.Children.Add(TextTool("Tgt","Set Target on highlighted accounts","bulkTgt",()=>BulkClick("Tgt",bulkValue.Text)));bulk.Children.Add(TextTool("DLL","Set Daily loss on highlighted accounts","bulkDll",()=>BulkClick("DLL",bulkValue.Text)));bulk.Children.Add(TextTool("ATM on","ATM exit-mirroring ON for highlighted accounts","bulkAtmOn",()=>BulkClick("ATM","on")));bulk.Children.Add(TextTool("ATM off","ATM exit-mirroring OFF for highlighted accounts","bulkAtmOff",()=>BulkClick("ATM","off")));bulk.Children.Add(TextTool("Div: notify","On divergence: only lock, show red and notify (default) for highlighted accounts","bulkDivNotify",()=>BulkClick("Diverge","Notify")));bulk.Children.Add(TextTool("Div: flatten 1x","On divergence: request ONE flatten, never retried (saved only; NOT active in this alpha) for highlighted accounts","bulkDivFlat",()=>BulkClick("Diverge","FlattenOnce")));bulk.Children.Insert(1,TextTool("From Leader","Copy the leader row's Tgt / DLL / ATM to highlighted followers (all visible if none highlighted). One-time copy; edit any row afterwards. Ratio and ticks are not copied.","bulkLeader",()=>BulkLeaderClick()));bulkBar=bulk;toolbar.Children.Add(bulk);AddRow(copy,toolbar,0);
            AccountGrid=GridTable();TickColumn(AccountGrid);AccountColumn(AccountGrid);Column(AccountGrid,"Alias","Alias",100,false,"Optional display alias");RatioColumn(AccountGrid);Column(AccountGrid,"Tgt","TargetText",78,false,"Independent daily local profit target · account CCY / 0 = off",null,true);Column(AccountGrid,"DLL","LossText",78,false,"Independent daily loss limit · positive amount / 0 = off",null,true);AtmColumn(AccountGrid);Column(AccountGrid,"Qty","Position",55,true,"Signed position · + long / − short",null,true);Column(AccountGrid,"P/L","Pnl",95,true,"Observed session P/L in account currency · baseline is not historical daily accounting","N2",true);Column(AccountGrid,"Bal","BalanceText",130,true,"CashValue in account currency; not NetLiquidation / net equity",null,true);((DataGridTextColumn)AccountGrid.Columns.Last()).ElementStyle.Setters.Add(new Setter(FrameworkElement.ToolTipProperty,new Binding("BalanceDetail")));((DataGridTextColumn)AccountGrid.Columns.Last()).ElementStyle.Setters.Add(new Setter(System.Windows.Automation.AutomationProperties.NameProperty,new Binding("BalanceDetail")));LightColumn(AccountGrid,"Conn","ConnLight","Connection");StateColumn(AccountGrid);OffColumn(AccountGrid);AccountGrid.FrozenColumnCount=2;ProportionalCopyColumns(AccountGrid);AddRow(copy,AccountGrid,1);Sections.Items.Add(new TabItem{Header="Copy",Content=copy});
            var risk=new Grid();risk.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});risk.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});risk.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});var tools=new WrapPanel{Margin=new Thickness(0,0,0,10)};
            var batch=Icon("\uE713","Batch risk / presets · review hold","batch",()=>Send("batch"));batch.IsEnabled=false;tools.Children.Add(batch);tools.Children.Add(Icon("\uE74E","Save settings","save",()=>Send("save")));tools.Children.Add(Icon("\uE787","Trading-day boundary / timezone","daySettings",()=>Send("daySettings")));tools.Children.Add(Icon("\uE769","Pause highlighted accounts","pauseSelected",()=>SetSelectedPause(true)));tools.Children.Add(Icon("\uE768","Resume highlighted accounts · latch unchanged / no auto-arm","resumeSelected",()=>SetSelectedPause(false)));var fs=Icon("\uE74D","Flatten highlighted accounts · review hold","flattenSelected",()=>Send("flattenSelected"));fs.IsEnabled=false;tools.Children.Add(fs);var fa=Icon("\uE7BA","Flatten all displayed accounts · review hold","flattenAll",()=>Send("flattenAll"));fa.IsEnabled=false;tools.Children.Add(fa);
            ShowLive=new CheckBox{Content="Live (RO)",Foreground=Muted,Margin=new Thickness(12,6,16,0),ToolTip="Display live accounts read-only · live copying blocked"};tools.Children.Add(ShowLive);var group=new CheckBox{Content="Grp",Foreground=Muted,Margin=new Thickness(0,6,0,0),ToolTip="Group account rows"};tools.Children.Add(group);AddRow(risk,tools,0);
            RiskGrid=GridTable();Column(RiskGrid,"Acct","Name",155,true,"Broker account");Column(RiskGrid,"Alias","Alias",115,false,"Display alias");Column(RiskGrid,"Grp","Group",90,false,"Account group");Column(RiskGrid,"Tgt","TargetText",85,false,"Daily local profit target · 0 = off",null,true);Column(RiskGrid,"DLL","LossText",85,false,"Daily loss limit · positive value / 0 = off",null,true);Column(RiskGrid,"Max","MaxText",60,false,"Maximum exact-instrument contracts",null,true);RatioColumn(RiskGrid);Column(RiskGrid,"P/L","Pnl",95,true,"Observed session P/L","N2",true);Column(RiskGrid,"CCY","Currency",55,true,"Account currency");Column(RiskGrid,"Tgt Δ","TargetRemaining",80,true,"Amount remaining to daily target",null,true);Column(RiskGrid,"DLL Δ","LossRemaining",80,true,"Loss headroom",null,true);Column(RiskGrid,"Qty","Position",50,true,"Signed position",null,true);Column(RiskGrid,"Conn","Connection",100,true,"Connection status");Column(RiskGrid,"St","Status",160,true,"Copier / risk state");Column(RiskGrid,"Preset","Preset",200,true,"Editable firm template");Column(RiskGrid,"Cons","ConsistencyAdvice",160,true,"Consistency advisory · not a broker guarantee");
            var action=new FrameworkElementFactory(typeof(Button));CompactCellButton(action,28);action.SetValue(Control.TemplateProperty,OffTemplate());action.SetValue(ContentControl.ContentProperty,"\uE713");action.SetValue(Control.FontFamilyProperty,new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"));action.SetValue(Control.BackgroundProperty,Panel);action.SetValue(Control.ForegroundProperty,Ink);action.SetValue(Control.BorderBrushProperty,Line);action.SetValue(Control.PaddingProperty,new Thickness(4,1,4,1));action.SetValue(FrameworkElement.ToolTipProperty,"Account risk / template / history");var gearStyle=new Style(typeof(Button));var gearLocked=new DataTrigger{Binding=new Binding("IsReadOnly"){RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(DataGrid),1)},Value=true};gearLocked.Setters.Add(new Setter(UIElement.IsEnabledProperty,false));gearStyle.Triggers.Add(gearLocked);action.SetValue(FrameworkElement.StyleProperty,gearStyle);action.AddHandler(ButtonBase.ClickEvent,new RoutedEventHandler((s,e)=>{if(!editable){e.Handled=true;return;}var r=(s as FrameworkElement).DataContext as AccountRow;if(r!=null&&RowSettings!=null)RowSettings(r);}));RiskGrid.Columns.Add(new DataGridTemplateColumn{Header="⚙",Width=40,CellTemplate=new DataTemplate{VisualTree=action}});AddRow(risk,RiskGrid,1);
            AddRow(risk,new TextBlock{Text="OFF cancels owned entries, retains exits · Re-follow explicit / only when clean · ATM OFF may be unprotected · Local guards only",Foreground=Muted,FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0)},2);Sections.Items.Add(new TabItem{Header="Risk",Content=risk});
            var logPanel=new Grid();logPanel.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});logPanel.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});logPanel.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});AddRow(logPanel,new TextBlock{Text="REVIEW HOLD: Start / Flatten / bulk risk disabled. ATM means order mirroring, not a follower native ATM. No real or simulation orders were submitted by this build verification.",Foreground=Warn,FontSize=12,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)},0);LogBox=Input("",double.NaN);LogBox.IsReadOnly=true;LogBox.FontFamily=new FontFamily("Consolas");LogBox.FontSize=12;LogBox.Foreground=Brush("#E4E1EC");LogBox.VerticalContentAlignment=VerticalAlignment.Top;LogBox.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;LogBox.HorizontalScrollBarVisibility=ScrollBarVisibility.Auto;var logTools=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,0,0,6)};logTools.Children.Add(TextTool("Clear log","Clear this log view (does not affect settings, orders or saved latches)","logClear",()=>ClearLog()));AddRow(logPanel,logTools,1);AddRow(logPanel,LogBox,2);Sections.Items.Add(new TabItem{Header="Logs",Content=logPanel});Sections.SelectedIndex=0;
            var footer=new DockPanel{Margin=new Thickness(0,10,0,0)};lockedLine=Text("",12,Bad);lockedLine.FontWeight=FontWeights.SemiBold;lockedLine.Margin=new Thickness(12,0,12,0);DockPanel.SetDock(lockedLine,Dock.Right);footer.Children.Add(lockedLine);adviceLine=Text("",12,Warn);adviceLine.Margin=new Thickness(12,0,12,0);DockPanel.SetDock(adviceLine,Dock.Right);footer.Children.Add(adviceLine);selection=Text("",12,Muted);DockPanel.SetDock(selection,Dock.Right);footer.Children.Add(selection);SessionText=Text("Disarmed",11,Muted);SessionText.Margin=new Thickness(12,0,12,0);DockPanel.SetDock(SessionText,Dock.Right);footer.Children.Add(SessionText);summary=Text("",12,Muted);footer.Children.Add(summary);AddRow(this,footer,3);
            Rows.CollectionChanged+=(s,e)=>{if(e.OldItems!=null)foreach(AccountRow r in e.OldItems)r.FollowRequested-=OnFollowRequested;if(e.NewItems!=null)foreach(AccountRow r in e.NewItems)r.FollowRequested+=OnFollowRequested;};view=CollectionViewSource.GetDefaultView(Rows);view.Filter=Matches;AccountGrid.ItemsSource=view;RiskGrid.ItemsSource=view;
            PreviewKeyDown+=(s,e)=>{if(e.Key==System.Windows.Input.Key.F&&System.Windows.Input.Keyboard.Modifiers==System.Windows.Input.ModifierKeys.Control){FocusSearch();e.Handled=true;}else if(e.Key==System.Windows.Input.Key.Escape&&search.IsKeyboardFocusWithin){SearchText="";e.Handled=true;}};search.TextChanged+=(s,e)=>{view.Refresh();UpdateCards();};filter.SelectionChanged+=(s,e)=>{view.Refresh();UpdateCards();};group.Checked+=(s,e)=>GroupByAccount=true;group.Unchecked+=(s,e)=>GroupByAccount=false;ShowLive.Checked+=(s,e)=>Send("refresh");ShowLive.Unchecked+=(s,e)=>Send("refresh");AccountGrid.SelectionChanged+=(s,e)=>UpdateCards();RiskGrid.SelectionChanged+=(s,e)=>UpdateCards();Sections.SelectionChanged+=(s,e)=>{if(summary!=null)UpdateCards();};
            if(demo){for(int i=1;i<=30;i++)Rows.Add(new AccountRow{Name="Demo "+i.ToString("00"),Alias="Eval "+i,Group=i<=10?"Tradeify":i<=20?"Lucid":"Topstep",Role=i==1?"Leader":"Follower",Connection="Synthetic",Enabled=i>1,Currency="USD",Status=i%7==0?"Target hit":i%4==0?"Paused":"Demo only",Paused=i%4==0,Pnl=i%4==0?-120:i*24.5,Settings=new RiskSettings{Alias="Eval "+i,Group=i<=10?"Tradeify":i<=20?"Lucid":"Topstep",Target=1000,Loss=500,Ratio=1,MaxContracts=4,Preset="Demo / not broker data"}});LeaderBox.ItemsSource=new[]{"Demo 01"};LeaderBox.SelectedIndex=0;ShowLive.IsEnabled=false;}UpdateCards();
        }
        void OnFollowRequested(AccountRow row,bool enabled){if(!demo&&FollowRow!=null)FollowRow(row,enabled);UpdateCards();}
        bool Matches(object value){var r=value as AccountRow;if(r==null)return false;string q=search.Text.Trim();if(q.Length>0&&!(r.Name+" "+r.Alias+" "+r.Group+" "+r.Preset).ToLowerInvariant().Contains(q.ToLowerInvariant()))return false;string f=filter.SelectedItem as string;return f=="On"?r.Enabled:f=="Paused"?r.Paused||r.Latch.Locked:f=="Target"?r.Status=="Target hit":true;}
        void Send(string c){if(demo||!DashboardSafety.CanExecute(c))return;var cmd=Command;if(cmd!=null)cmd(c);}
        public bool GroupByAccount{get{return view.GroupDescriptions.Count>0;}set{view.GroupDescriptions.Clear();if(value)view.GroupDescriptions.Add(new PropertyGroupDescription("Group"));}}
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
            double amount=0;bool atm=false;
            if(field=="Tgt"||field=="DLL"){if(!BulkEdit.TryParseAmount(text,out amount))return "Enter a plain amount like 1000 (0 = off); no commas, signs or text";}
            else if(field=="ATM"){if(text=="on")atm=true;else if(text=="off")atm=false;else return "ATM value must be on or off";}
            else if(field=="Diverge"){if(!DivergenceChoice.IsValid(text))return "On divergence must be Notify or FlattenOnce";}
            else return "Unknown bulk field";
            foreach(var r in targets)
            {
                if(field=="Tgt")r.Target=amount;else if(field=="DLL")r.Loss=amount;else if(field=="Diverge")r.OnDivergence=text;else r.CopyAtm=atm;
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
            string div=leader.OnDivergence;foreach(var r in targets){r.Target=tgt;r.Loss=dll;r.CopyAtm=atm;r.OnDivergence=div;r.Changed();}
            UpdateCards();return null;
        }
        public void ClearLog(){LogBox.Text="";}
        public bool BulkBarPresent{get{return bulkBar!=null&&AccountGrid.Parent!=null;}}
        internal Action<string> BulkNotice;
        void BulkLeaderClick(){var reason=BulkFromLeader();var n=BulkNotice;if(n!=null)n(reason==null?"Copied leader Tgt / DLL / ATM to followers (one-time); Ratio and ticks unchanged. Edit any row to fine-tune.":"From Leader refused: "+reason);}
        void BulkClick(string field,string text){var reason=BulkApply(field,text);var n=BulkNotice;if(n!=null)n(reason==null?"Bulk "+field+" applied to highlighted followers; Ratio and ticks unchanged.":"Bulk "+field+" refused: "+reason);}
        public void SetSelectedPause(bool paused){foreach(var r in SelectedRows().Where(r=>r.CanFollow)){if(!demo&&PauseRow!=null)PauseRow(r,paused);else{r.Paused=paused;r.Changed();}}UpdateCards();}
        public AccountRow[] SelectedRows(){return (Sections.SelectedIndex==1?RiskGrid:AccountGrid).SelectedItems.Cast<AccountRow>().ToArray();}
        public void UpdateCards(){if(summary==null)return;FollowCountText.Text="→  "+Rows.Count(r=>r.Enabled&&r.Role!="Leader")+" followers  ";var ccy=Rows.Select(r=>r.Currency).Where(s=>!string.IsNullOrEmpty(s)).Distinct().ToArray();summary.Text=Rows.Count(r=>r.Enabled&&r.Role!="Leader")+" TICKED  /  "+Rows.Count+" ACCTS     ·     P/L  "+(ccy.Length<=1?Rows.Sum(r=>RiskPolicy.Finite(r.Pnl)?r.Pnl:0).ToString("N2")+" "+ccy.FirstOrDefault():"Mixed CCY")+"";selection.Text=SelectedRows().Length+" sel / "+VisibleCount+" rows";if(adviceLine!=null)adviceLine.Text=AdviceText;if(lockedLine!=null)lockedLine.Text=LockedText;}
        internal bool TrySetSessionState(bool attached,string status,int callbackSession,int currentSession,bool currentAttached){if(callbackSession!=currentSession||attached!=currentAttached)return false;SetSessionState(attached,status);return true;}
        internal void SetSessionState(bool attached,string status){if(!attached)foreach(var row in Rows)row.InvalidateSessionActivity();SessionText.Text=DashboardSafety.CanExecute("start")?status:(attached?"REVIEW HOLD · unexpected attachment":"Disarmed");SessionText.ToolTip=status;Editable(!attached);UpdateCards();}
        internal void Editable(bool canEdit){editable=canEdit;LeaderBox.IsEnabled=canEdit;InstrumentBox.IsEnabled=canEdit;ShowLive.IsEnabled=canEdit&&!demo;AccountGrid.IsReadOnly=!canEdit;RiskGrid.IsReadOnly=!canEdit;foreach(var b in editButtons)b.IsEnabled=canEdit;StartButton.IsEnabled=canEdit&&!demo&&DashboardSafety.CanExecute("start");}
    }
    public class KINFXTradeCopier : AddOnBase
    {
        readonly Dictionary<Window,Tuple<NTMenuItem,NTMenuItem>> menus=new Dictionary<Window,Tuple<NTMenuItem,NTMenuItem>>();
        protected override void OnStateChange() {if(State==State.SetDefaults){Name="KINFXTradeCopier";Description="KINFX multi-account simulation copier with local risk Dashboard";}}
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
            Closing+=(s,e)=>{foreach(var tab in tabs.Items.OfType<KINFXTradeCopierTab>())if(!tab.CanClose()){e.Cancel=true;MessageBox.Show(this,"Close blocked: copier still manages positions / working orders. Guarded pause keeps exits managed. Flatten or resolve the selected instrument, then Stop when flat.","KINFX safety",MessageBoxButton.OK,MessageBoxImage.Warning);break;}};
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
            var apply=new Button {Content="Apply to "+accountCount+" account(s)",Padding=new Thickness(15,8,15,8),Margin=new Thickness(0,0,8,0)};buttons.Children.Add(apply);apply.Click+=(s,e)=>Apply();var cancel=new Button {Content="Cancel",Padding=new Thickness(15,8,15,8)};buttons.Children.Add(cancel);cancel.Click+=(s,e)=>Close();
            var panel=new StackPanel();dock.Children.Add(new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
            panel.Children.Add(new TextBlock {Text="Local risk controls",FontSize=25,Margin=new Thickness(0,0,0,5)});
            panel.Children.Add(new TextBlock {Text="All values editable. Zero monetary limits = OFF. Values in account denomination. No compliance guarantee, no historical high inferred. Batch overwrites the shown settings on every selected account.",Foreground=DashboardView.Muted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,16)});
            var presets=new WrapPanel();firm=PresetCombo(160);plan=PresetCombo(230);size=PresetCombo(95);presets.Children.Add(firm);presets.Children.Add(plan);presets.Children.Add(size);panel.Children.Add(presets);
            firm.ItemsSource=new[]{"Custom"}.Concat(PresetCatalog.All.Select(p=>p.Firm).Distinct().OrderBy(s=>s));firm.SelectedIndex=0;
            firm.SelectionChanged+=(s,e)=>{plan.ItemsSource=PresetCatalog.All.Where(p=>p.Firm==(string)firm.SelectedItem).Select(p=>p.Plan).Distinct().ToArray();plan.SelectedIndex=0;};
            plan.SelectionChanged+=(s,e)=>{size.ItemsSource=PresetCatalog.All.Where(p=>p.Firm==(string)firm.SelectedItem&&p.Plan==(string)plan.SelectedItem).Select(p=>p.Size).ToArray();size.SelectedIndex=0;};
            notes=new TextBlock {Text="Custom: confirm your absolute floor and maximum instrument quantity manually.",Foreground=DashboardView.Muted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,10)};panel.Children.Add(notes);
            size.SelectionChanged+=(s,e)=>{var p=SelectedPreset();notes.Text=p==null?"Custom settings":p.Notes;};
            optional=new CheckBox {Content="Explicitly apply optional / soft DLL if source has an amount",Foreground=DashboardView.Muted,Margin=new Thickness(0,6,0,8)};panel.Children.Add(optional);
            var load=new Button {Content="Load selected template into editable fields",Padding=new Thickness(10,6,10,6),HorizontalAlignment=HorizontalAlignment.Left};panel.Children.Add(load);load.Click+=(s,e)=>{var p=SelectedPreset();if(p==null)return;LoadSelectedPreset();if(!new[]{"MES","MNQ","M2K","MYM","ES","NQ","RTY","YM"}.Contains(root)||p.RawContracts=="—")MessageBox.Show(this,"Source mini cap cannot be safely mapped. Set MaxContracts manually for your exact instrument / current scaling.","Manual quantity required");};
            foreach(string field in new[]{"Alias","Group","Ratio","Target","Loss","MaxContracts","Floor","Baseline","TrailDistance","HighWater","BestDay","TotalProfit","Consistency"})
            {
                var row=new Grid {Margin=new Thickness(0,7,0,0)};row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(260)});row.ColumnDefinitions.Add(new ColumnDefinition());
                string label=field=="Target"?"Daily target (local observed P&L)":field=="Loss"?"Daily loss (positive amount)":field=="Floor"?"Confirmed absolute equity floor":field=="Baseline"?"P&L telemetry baseline (editable)":field=="HighWater"?"User / observed local high water":field=="MaxContracts"?"Max exact-instrument contracts":field;
                row.Children.Add(new TextBlock {Text=label,VerticalAlignment=VerticalAlignment.Center,Foreground=DashboardView.Muted});var b=new TextBox {Padding=new Thickness(7,4,7,4),Background=DashboardView.Panel,Foreground=DashboardView.Ink,BorderBrush=DashboardView.Line};Grid.SetColumn(b,1);row.Children.Add(b);boxes[field]=b;b.TextChanged+=(sender,args)=>{draft.Set(field,b.Text);b.BorderBrush=draft.IsInvalid(field)?DashboardView.Bad:DashboardView.Line;b.ToolTip=draft.IsInvalid(field)?"Invalid value — retained until corrected; Apply is blocked.":null;};panel.Children.Add(row);
            }
            trail=new CheckBox {Content="Enable LOCAL intraday high-water guard (not firm EOD calculation)",Foreground=DashboardView.Muted,Margin=new Thickness(0,12,0,6),IsChecked=draft.Settings.LocalTrail};panel.Children.Add(trail);
            panel.Children.Add(new TextBlock {Text="BestDay / TotalProfit are supplied history, used for consistency advisory only. To unlock a persisted latch use the separate confirmed reset while disarmed. Baseline is never automatically reset on restart.",Foreground=DashboardView.Muted,TextWrapping=TextWrapping.Wrap});LoadFields();
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
            catch(Exception ex){MessageBox.Show(this,ex.Message,"Invalid settings",MessageBoxButton.OK,MessageBoxImage.Warning);}
        }
    }
    public class KINFXTradeCopierTab : NTTabPage
    {
        readonly DashboardView dashboard;
        readonly CopierEngine engine;
        readonly DispatcherTimer timer;
        readonly Queue<string> logs=new Queue<string>();
        readonly Dictionary<string,SavedAccount> saved=new Dictionary<string,SavedAccount>();
        string instanceId=Guid.NewGuid().ToString("N"),zone="Central Standard Time",lastDay="";
        int resetHour=17,persistTicks;
        bool cleaned,loading,settingsCorrupt;
        string leaderName="";
        public KINFXTradeCopierTab()
        {
            TabName="Dashboard";dashboard=new DashboardView();Content=dashboard;
            engine=new CopierEngine(Log,OnTelemetry,(attached,status)=>{int session=engine.SessionGeneration;Dispatch(()=>dashboard.TrySetSessionState(attached,status,session,engine.SessionGeneration,engine.Attached));},PersistLatch);
            dashboard.Command=Command;dashboard.BulkNotice=Log;dashboard.PauseRow=Pause;dashboard.RowSettings=r=>Edit(new[]{r});dashboard.FollowRow=Follow;
            dashboard.LeaderBox.SelectionChanged+=(s,e)=>{if(engine.Attached)return;leaderName=dashboard.LeaderBox.SelectedItem as string ?? "";foreach(var row in dashboard.Rows){row.Role=row.Name==leaderName?"Leader":"Follower";if(row.Role=="Leader")row.Enabled=false;row.Changed();}};
            dashboard.AccountGrid.RowEditEnding+=(s,e)=>Dispatch(()=>{foreach(var row in dashboard.Rows){row.Changed();}dashboard.UpdateCards();});
            dashboard.InstrumentBox.LostKeyboardFocus+=(s,e)=>NormalizeInstrumentBox();
            dashboard.InstrumentBox.KeyDown+=(s,e)=>{if(e.Key==System.Windows.Input.Key.Enter){NormalizeInstrumentBox();e.Handled=true;}};
            LoadDefault();Refresh();syncNames=CurrentAccountNames();
            timer=new DispatcherTimer(TimeSpan.FromSeconds(1),DispatcherPriority.Background,(s,e)=>Tick(),Dispatcher);timer.Start();
        }
        void Dispatch(Action action) {if(!cleaned)Dispatcher.BeginInvoke(action);}
        string DirectoryPath {get {return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"NinjaTrader 8","templates","KINFXTradeCopier");}}
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
            resetHour=(int?)x.Attribute("resetHour")??17;if(resetHour<0||resetHour>23)throw new InvalidDataException("Reset hour outside 0–23");
            lastDay=(string)x.Attribute("lastDay")??"";leaderName=(string)x.Attribute("leader")??"";
            // Enabled, pause/arm and live permissions are intentionally NOT persisted.
        }
        void Refresh()
        {
            if(engine.Attached){Log("Refresh blocked while managing a session.");return;}
            if(loading)return;loading=true;
            try
            {
                foreach(var r in dashboard.Rows)saved[r.Name]=Record(r);MergeLatches();
                Account[] accounts;lock(Account.All)accounts=Account.All.Where(a=>dashboard.ShowLive.IsChecked==true||CopierEngine.Sim(a)).OrderBy(a=>a.Name).ToArray();
                dashboard.Rows.Clear();
                foreach(var a in accounts)
                {
                    SavedAccount stored;saved.TryGetValue(a.Name,out stored);
                    dashboard.Rows.Add(new AccountRow {Account=a,Name=a.Name,Role=a.Name==leaderName?"Leader":"Follower",Currency=a.Denomination.ToString(),Connection=a.Connection==null?"Disconnected":a.Connection.Status.ToString(),Status=stored!=null&&stored.Latch.Locked?stored.Latch.Reason:"Disarmed",Settings=stored==null?new RiskSettings():stored.Settings.Clone(),Latch=stored==null?new RiskLatch():new RiskLatch {Locked=stored.Latch.Locked,Reason=stored.Latch.Reason}});
                }
                string keep=leaderName;dashboard.LeaderBox.ItemsSource=accounts.Where(CopierEngine.Sim).Select(a=>a.Name).ToArray();if(!string.IsNullOrEmpty(keep)&&accounts.Any(a=>a.Name==keep))dashboard.LeaderBox.SelectedItem=keep;else if(dashboard.LeaderBox.Items.Count>0)dashboard.LeaderBox.SelectedIndex=0;
                dashboard.UpdateCards();
            }
            catch(Exception ex){Log("Refresh error: "+ex.Message);}finally{loading=false;}
        }
        static SavedAccount Record(AccountRow r) {return new SavedAccount {Name=r.Name,Settings=r.Settings.Clone(),Latch=new RiskLatch {Locked=r.Latch.Locked,Reason=r.Latch.Reason}};}
        XElement Export()
        {
            foreach(var r in dashboard.Rows)saved[r.Name]=Record(r);
            var x=SettingsStore.ToXml(saved.Values,dashboard.InstrumentBox.Text.Trim(),zone,resetHour);x.SetAttributeValue("id",instanceId);x.SetAttributeValue("leader",leaderName);x.SetAttributeValue("lastDay",lastDay);return x;
        }
        void Persist()
        {
            if(!dashboard.TryCommitSettings()){Log("Settings NOT saved: correct invalid per-account Ratio / row values first.");return;}
            try {if(settingsCorrupt)throw new InvalidDataException("Previously unreadable settings; preserve original files, restore a verified backup before restarting.");SettingsStore.AtomicSave(SettingsPath,Export());SettingsStore.AtomicSave(Path.Combine(DirectoryPath,"last-workspace.xml"),new XElement("Last",new XAttribute("id",instanceId)));}
            catch(Exception ex){settingsCorrupt=true;engine.GuardedPause();Log("PERSISTENCE ERROR — entries paused, restart blocked until reviewed: "+ex.Message);}
        }
        void Log(string message)
        {
            Dispatch(()=>{logs.Enqueue(DateTime.Now.ToString("HH:mm:ss")+"  "+message);while(logs.Count>300)logs.Dequeue();dashboard.LogBox.Text=string.Join(Environment.NewLine,logs);dashboard.LogBox.ScrollToEnd();});
        }
        AccountRow[] Selected() {return dashboard.SelectedRows();}
        bool Confirm(string text) {return MessageBox.Show(Window.GetWindow(this),text,"KINFX confirmation",MessageBoxButton.YesNo,MessageBoxImage.Warning)==MessageBoxResult.Yes;}
        void Command(string c)
        {
            try
            {
                if(!DashboardSafety.CanExecute(c)){Log("REVIEW HOLD: trading and bulk edits disabled. Inspect Dashboard only; use native NT8 for positions.");return;}
                if(c=="refresh"){Refresh();return;}if(c=="save"){Persist();Log("Settings saved; startup stays disarmed.");return;}
                if(c=="pause"){engine.GuardedPause();return;}if(c=="stop"){engine.StopFlat();return;}
                if(c=="start"){Start();return;}if(c=="flattenSelected"||c=="flattenAll"){Flatten(c=="flattenSelected"?Selected():dashboard.Rows.ToArray());return;}
                if(c=="pauseSelected"||c=="resumeSelected"){dashboard.SetSelectedPause(c=="pauseSelected");return;}
                if(engine.Attached){Log("Settings / enabled edits blocked during session. Pause per account or stop when flat first.");return;}
                if(c=="daySettings"){EditTradingDay();return;}
                if(c=="batch"){Edit(Selected());return;}
                if(c=="enable"||c=="disable"){foreach(var row in Selected()){row.Enabled=c=="enable"&&row.Role!="Leader"&&CopierEngine.Sim(row.Account);row.Changed();}dashboard.UpdateCards();}
            }
            catch(Exception ex){Log("UI action failed: "+ex.Message);}
        }
        void EditTradingDay()
        {
            var window=new Window {Title="Trading-day settings (local observation)",Width=500,Height=280,Owner=Window.GetWindow(this),WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=DashboardView.Canvas,Foreground=DashboardView.Ink};
            var panel=new StackPanel {Margin=new Thickness(20)};window.Content=panel;
            panel.Children.Add(new TextBlock {Text="Windows timezone ID (DST aware)",Margin=new Thickness(0,0,0,8)});var z=new TextBox {Text=zone,Padding=new Thickness(8)};panel.Children.Add(z);
            panel.Children.Add(new TextBlock {Text="Reset boundary hour 0–23 (no automatic latch / baseline reset)",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,8)});var h=new TextBox {Text=resetHour.ToString(),Padding=new Thickness(8)};panel.Children.Add(h);
            var apply=new Button {Content="Confirm local day boundary",Padding=new Thickness(8),Margin=new Thickness(0,14,0,0)};panel.Children.Add(apply);
            apply.Click+=(s,e)=>{try{TimeZoneInfo.FindSystemTimeZoneById(z.Text.Trim());int hour;if(!int.TryParse(h.Text,out hour)||hour<0||hour>23)throw new ArgumentException("Hour must be 0–23");zone=z.Text.Trim();resetHour=hour;lastDay=RiskPolicy.TradingDay(DateTime.UtcNow,zone,resetHour);Persist();window.Close();}catch(Exception ex){MessageBox.Show(window,ex.Message,"Invalid trading-day setting");}};window.ShowDialog();
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
            if(!Confirm("Start simulation copying to exactly "+selected.Length+" follower account(s) on "+inst.FullName+"?\n"+string.Join("\n",selected.Select(r=>r.Name))+"\nLocal copier guards only. No adoption; all selected accounts must be flat / no working instrument orders."))return;
            // Explicit observed baseline initialization, persisted BEFORE subscriptions can arm.
            foreach(var r in selected)if(!r.Settings.BaselineSet){double raw=r.Account.Get(AccountItem.RealizedProfitLoss,r.Account.Denomination)+r.Account.Get(AccountItem.UnrealizedProfitLoss,r.Account.Denomination);if(!RiskPolicy.Finite(raw)){Log("Invalid baseline telemetry: "+r.Name);return;}r.Settings.Baseline=raw;r.Settings.BaselineSet=true;}
            Persist();if(settingsCorrupt)return;
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
            if(rows.Length==1&&rows[0].Latch.Locked&&Confirm("Account "+rows[0].Name+" remains LOCKED: "+rows[0].Latch.Reason+".\nRequest unlock? This does NOT arm copying; current risk conditions will be rechecked, baseline unchanged."))Unlock(rows[0]);
        }
        void Unlock(AccountRow row)
        {
            try
            {
                var a=row.Account;var inst=Instrument.GetInstrument(InstrumentText(),false);
                if(inst==null||!CopierEngine.Clean(a,inst))throw new InvalidOperationException("Unlock requires selected instrument flat / no working orders.");
                double raw=a.Get(AccountItem.RealizedProfitLoss,a.Denomination)+a.Get(AccountItem.UnrealizedProfitLoss,a.Denomination),equity=a.Get(AccountItem.NetLiquidation,a.Denomination);
                if(!RiskPolicy.CanUnlock(row.Settings,raw-row.Settings.Baseline,equity,a.Connection!=null&&a.Connection.Status==ConnectionStatus.Connected))throw new InvalidOperationException("Current telemetry / floor / local high-water still blocks unlock.");
                SettingsStore.RemoveLatch(LatchPath,row.Name);row.Latch=new RiskLatch();row.Status="Disarmed";row.Changed();Persist();Log("Confirmed unlock: "+row.Name+"; still disarmed.");
            }
            catch(Exception ex){Log("Unlock refused: "+ex.Message);}
        }
        void Flatten(AccountRow[] rows)
        {
            if(rows.Length==0){Log("No accounts in flatten scope.");return;}var inst=Instrument.GetInstrument(InstrumentText(),false);if(inst==null){Log("Flatten: exact instrument unknown.");return;}
            var targets=rows.Where(r=>r.Account!=null).Select(r=>r.Account).Distinct().ToArray();
            if(targets.Any(a=>!CopierEngine.Sim(a))){Log("Flatten refused: live account in scope; v0.2 simulation only.");return;}
            if(!Confirm("FLATTEN exactly "+targets.Length+" account(s), only "+inst.FullName+".\n"+string.Join("\n",targets.Select(a=>a.Name))+"\nNew copies will be blocked BEFORE flatten requests. This cancels working orders and closes positions on that instrument. Retry/error remains visible."))return;
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
        string InstrumentText(){var exact=ContractRoll.Normalize(dashboard.InstrumentBox.Text,DateTime.Now);return exact??dashboard.InstrumentBox.Text.Trim();}
        void NormalizeInstrumentBox()
        {
            if(engine.Attached)return;
            string typed=dashboard.InstrumentBox.Text;
            string exact=ContractRoll.Normalize(typed,DateTime.Now);
            if(exact==null){if(!string.IsNullOrWhiteSpace(typed)&&Instrument.GetInstrument(typed.Trim(),false)==null)Log("Instrument \""+typed.Trim()+"\" not recognised. Examples: MNQ, ES 12-26, NQZ6.");return;}
            if(exact!=typed.Trim()){dashboard.InstrumentBox.Text=exact;}
            if(Instrument.GetInstrument(exact,false)==null)Log("Instrument "+exact+" is not available in this NinjaTrader (check the contract month / Instrument Manager).");
        }
        string[] syncNames=new string[0];string syncNoticeKey="";
        // Names of the accounts the dashboard WOULD list right now (same filter as Refresh). Read-only: touches no account state.
        string[] CurrentAccountNames()
        {
            Account[] accounts;lock(Account.All)accounts=Account.All.Where(a=>dashboard.ShowLive.IsChecked==true||CopierEngine.Sim(a)).ToArray();
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
        void Tick()
        {
            try{SyncAccounts();}catch(Exception ex){Log("Account sync error: "+ex.Message);}
            if(engine.Attached)engine.Poll();
            PollCashBalances(dashboard.Rows,
                r=>r.Account!=null&&CopierEngine.Sim(r.Account),
                r=>r.Account.Connection!=null&&r.Account.Connection.Status==ConnectionStatus.Connected,
                r=>{var a=r.Account;r.Currency=a.Denomination.ToString();return a.Get(AccountItem.CashValue,a.Denomination);});
            {
                foreach(var r in dashboard.Rows.Where(r=>!engine.Attached || r.Role=="Leader" || !r.Enabled))
                {
                    try
                    {
                        var a=r.Account;r.Realized=a.Get(AccountItem.RealizedProfitLoss,a.Denomination);r.Unrealized=a.Get(AccountItem.UnrealizedProfitLoss,a.Denomination);r.Equity=a.Get(AccountItem.NetLiquidation,a.Denomination);r.Sod=a.Get(AccountItem.SodLiquidatingValue,a.Denomination);r.Pnl=r.Realized+r.Unrealized-(r.Settings.BaselineSet?r.Settings.Baseline:0);r.Connection=a.Connection==null?"Disconnected":a.Connection.Status.ToString();r.Changed();
                    }
                    catch(Exception ex){r.Status="Telemetry error";Log(r.Name+": "+ex.Message);}
                }
                dashboard.UpdateCards();
            }
            if(engine.Attached && ++persistTicks>=30){persistTicks=0;Persist();}
            string day=RiskPolicy.TradingDay(DateTime.UtcNow,zone,resetHour);
            if(lastDay.Length==0){lastDay=day;return;}if(day!=lastDay){lastDay=day;engine.GuardedPause();Log("Trading-day boundary "+zone+" "+resetHour+":00 (DST aware). New entries paused. Baseline / latch NOT auto-reset. Confirm new baseline and unlock while flat.");Persist();}
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
        public override void Cleanup() {if(cleaned)return;timer.Stop();Persist();engine.Dispose();cleaned=true;base.Cleanup();}
    }
    public class KINFXTradeCopierWindowFactory : INTTabFactory
    {
        public NTWindow CreateParentWindow() {return new KINFXTradeCopierWindow();}
        public NTTabPage CreateTabPage(string typeName,bool isNew) {return new KINFXTradeCopierTab();}
    }
}
