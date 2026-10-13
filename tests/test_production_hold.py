"""Public alpha: trading DISABLED for ALL providers, including Sim/Playback.

Portable stdlib syntax checks are NOT C# AST or runtime proof and NOT a
certified firewall. Optional Windows Framework tests execute only freshly
extracted method entries with audited, harmless stubs; never NT assemblies.
All runtime artifacts require an explicitly supplied test scratch directory.
No environment variable here changes the production hold.
"""

import hashlib
import os
from pathlib import Path
import re
import subprocess
import unittest
import uuid


REPO = Path(__file__).resolve().parents[1]
SOURCE = Path(os.environ.get("KINFX_HOLD_SOURCE", str(REPO / "src/AddOns/KINFXTradeCopier.cs")))
GUARD = "if(typeof(Account).Assembly!=typeof(CopierEngine).Assembly)"


def mask_noncode(text):
    """Length-preserving C# comment/string mask for limited lexical checks."""
    pattern = r'//[^\n]*|/\*[\s\S]*?\*/|@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\''
    return re.sub(pattern, lambda m: "".join("\n" if c == "\n" else " " for c in m[0]), text)


SIM_DIRECTIVE = re.compile(r"^[ \t]*#(if !KINFX_SIM_TEST|if KINFX_SIM_TEST|else|endif)[^\n]*\n?", re.M)


def default_build_view(text):
    """Source as the DEFAULT build sees it: KINFX_SIM_TEST undefined.

    Only `#if KINFX_SIM_TEST` / `#if !KINFX_SIM_TEST` blocks are resolved; every other directive (notably
    `#if !KINFX_OFFLINE_FAKE`) is left in place so the existing hold checks still see the real structure.
    A `#if KINFX_SIM_TEST ... #else ... #endif` keeps its #else branch; a plain `#if KINFX_SIM_TEST` block is dropped.
    """
    out, stack, keep = [], [], True
    for line in text.splitlines(True):
        stripped = line.strip()
        if stripped == "#if !KINFX_OFFLINE_FAKE && !KINFX_SIM_TEST":
            # default build: KINFX_SIM_TEST undefined, so this is exactly `#if !KINFX_OFFLINE_FAKE`
            line = line.replace("#if !KINFX_OFFLINE_FAKE && !KINFX_SIM_TEST", "#if !KINFX_OFFLINE_FAKE")
            stripped = "#if !KINFX_OFFLINE_FAKE"
        if stripped.startswith("#if KINFX_SIM_TEST") or stripped.startswith("#if !KINFX_SIM_TEST"):
            stack.append(("sim", stripped.startswith("#if !")))
            keep = keep and (stripped.startswith("#if !"))
            continue
        if stack and stripped == "#else" and stack[-1][0] == "sim":
            keep = not stack[-1][1]
            continue
        if stack and stripped.startswith("#endif") and stack[-1][0] == "sim":
            stack.pop()
            keep = True
            continue
        if stripped.startswith("#if"):
            stack.append(("other", False))
        elif stripped.startswith("#endif") and stack and stack[-1][0] == "other":
            stack.pop()
        if keep:
            out.append(line)
    return "".join(out)


def method(text, signature):
    code = mask_noncode(text)
    matches = list(re.finditer(re.escape(signature), code))
    if len(matches) != 1:
        raise AssertionError("expected one declaration: " + signature)
    start = matches[0].start()
    opening = code.index("{", matches[0].end())
    depth = 1
    for end in range(opening + 1, len(code)):
        depth += (code[end] == "{") - (code[end] == "}")
        if not depth:
            return text[start:end + 1]
    raise AssertionError("unclosed method: " + signature)


SIGNATURES = (
    "public void Start(Account selected,Instrument inst,IEnumerable<Tuple<Account,RiskSettings,RiskLatch>> selectedFollowers,bool copyAtm=true)",
    "public void Flatten(Account[] targets,Instrument inst)",
)


def representative_entry(text, signature):
    """Exact entry/hold/conditional prefix; fake engine tail becomes a spy."""
    body = method(text, signature)
    if body.count("#else") != 1 or body.count("#endif") != 1:
        raise AssertionError("unsupported preprocessor layout")
    return body.split("#else", 1)[0] + "#else\n            reached++;\n#endif\n        }"


class SourceSyntaxHoldTests(unittest.TestCase):
    """Limited lexical/syntax regression checks, not a C# AST audit."""

    @classmethod
    def setUpClass(cls):
        cls.raw = SOURCE.read_text(encoding="utf-8")
        cls.text = default_build_view(cls.raw)

    def test_sim_test_symbol_is_confined_to_known_blocks(self):
        # The only place KINFX_SIM_TEST may appear: the identity guard wrapper, the scope checks in Start/Flatten
        # and DashboardSafety.CanExecute. Anything else is a new bypass and must be reviewed explicitly.
        code = mask_noncode(self.raw)
        # 21 earlier blocks + 1 for the generated language table: the SIM-only sentences (SIM TEST / DRY RUN texts) are compiled only in the SIM build,
        # so the default build never contains them (see test_default_view_drops_every_sim_block).
        self.assertEqual(code.count("KINFX_SIM_TEST"), 22, "unexpected KINFX_SIM_TEST usage")
        for signature in SIGNATURES:
            body = method(self.raw, signature)
            self.assertIn("#if !KINFX_SIM_TEST", body)
            self.assertIn("SimScope(", body, "Sim scope check missing from " + signature[:30])
        self.assertIn("SimScope", method(self.raw, "public static bool SimScope(IEnumerable<Account> scope)"))
        sim = method(self.raw, "public static bool SimScope(IEnumerable<Account> scope)")
        self.assertIn("Sim(a)", sim)
        self.assertNotIn("Live", sim)
        # No active #define of either symbol in the shipped source.
        self.assertNotRegex(code, r"(?m)^\s*#define\s+KINFX_(SIM_TEST|OFFLINE_FAKE)")

    def test_sim_directives_use_only_the_two_allowed_forms(self):
        # Compound conditions (`|| true`, `&& false`, `#elif`) would silently keep or drop code in the default build
        # while default_build_view still resolves them as plain blocks. Only these two exact forms are allowed.
        allowed = {"#if KINFX_SIM_TEST", "#if !KINFX_SIM_TEST", "#if !KINFX_OFFLINE_FAKE && !KINFX_SIM_TEST"}
        for number, line in enumerate(self.raw.splitlines(), 1):
            stripped = line.strip()
            if stripped.startswith("#") and ("SIM_TEST" in stripped or "KINFX_SIM" in stripped):
                self.assertIn(re.sub(r"\s+", " ", stripped), allowed, "line %d: directive form not allowed: %s" % (number, stripped))

    def test_no_define_undef_or_escaped_spelling_of_either_symbol(self):
        for number, line in enumerate(self.raw.splitlines(), 1):
            stripped = line.strip()
            if stripped.startswith("#"):
                self.assertNotRegex(stripped, r"^#\s*(define|undef)\b", "line %d: symbol must not be defined in source" % number)
        # Unicode-escaped identifiers are decoded by Roslyn: `\u0054EST` is `TEST`. Forbidden in any directive.
        for number, line in enumerate(self.raw.splitlines(), 1):
            if line.strip().startswith("#"):
                self.assertNotIn("\\u", line, "line %d: unicode escape in a directive" % number)

    def test_sim_symbol_alone_reaches_the_engine_branch(self):
        # BLOCKER found by independent review: the engine body sits under `#if !KINFX_OFFLINE_FAKE ... #else`. A real NinjaTrader
        # install only ever gets KINFX_SIM_TEST, so the all-account hold must ALSO be skipped by that one symbol.
        for signature in SIGNATURES:
            with self.subTest(signature=signature[:32]):
                body = method(self.raw, signature)
                self.assertIn("#if !KINFX_OFFLINE_FAKE && !KINFX_SIM_TEST", body,
                              "the all-account hold must not apply when KINFX_SIM_TEST is defined")
                self.assertEqual(body.count("#if !KINFX_OFFLINE_FAKE"), 1)

    def test_send_time_provider_recheck_lives_in_dispatch(self):
        body = method(self.raw, "void Dispatch(EngineAccount f,Action call,Action<Exception> done,string what=null)")
        self.assertIn("#if KINFX_SIM_TEST", body)
        self.assertIn("!Sim(f.Account)", body)
        self.assertIn("!Sim(leaderAtQueue)", body)
        self.assertLess(body.index("!Sim(f.Account)"), body.index("call();"), "provider re-check must run before the provider call")

    def test_default_build_has_no_simscope_member(self):
        # The default build must be code-identical to main here: SimScope exists only when KINFX_SIM_TEST is defined.
        self.assertNotIn("SimScope", mask_noncode(self.text))

    def test_default_view_drops_every_sim_block(self):
        # Compare CODE only (comments and string literals masked): the default build must not see the symbol at all.
        code = mask_noncode(self.text)
        self.assertNotIn("KINFX_SIM_TEST", code)
        self.assertNotIn("SIM TEST:", self.text, "a Sim-test log line leaked into the default build")
        self.assertNotIn("SimScope(selectedFollowers", code)
        self.assertIn("command!=\"start\"", self.text)

    def test_external_identity_guard_is_first_and_outside_conditional(self):
        for signature in SIGNATURES:
            with self.subTest(signature=signature):
                body = method(self.text, signature)
                prefix = body[body.index("{") + 1:body.index("#if")]
                code = re.sub(r"\s+", "", mask_noncode(prefix))
                self.assertTrue(code.startswith(GUARD + "{BlockEntries();log("),
                                "external Account hold missing before fake-symbol branch")
                self.assertRegex(code, r"return;\}$")
                self.assertNotIn("#", prefix)

    def test_no_define_branch_keeps_all_account_hold(self):
        for signature in SIGNATURES:
            with self.subTest(signature=signature):
                body = method(self.text, signature)
                self.assertIn("#if !KINFX_OFFLINE_FAKE", body)
                hold = body.split("#if !KINFX_OFFLINE_FAKE", 1)[1].split("#else", 1)[0]
                hold = hold.replace(" && !KINFX_SIM_TEST", "", 1)  # default build: this symbol is undefined
                code = re.sub(r"\s+", "", mask_noncode(hold))
                self.assertTrue(code.startswith("BlockEntries();log("))
                self.assertTrue(code.endswith("return;"))

    def test_ui_hold_and_no_automatic_activation(self):
        safety = method(self.text, "public static bool CanExecute(string command)")
        for command in ("start", "flattenSelected", "flattenAll", "batch"):
            self.assertIn('command!="' + command + '"', safety)
        self.assertIn("if(demo||!DashboardSafety.CanExecute(c))return;", self.text)
        self.assertIn("if(!DashboardSafety.CanExecute(c))", method(self.text, "void Command(string c)"))
        code = mask_noncode(self.text)
        # All current actual automatic arming assignments belong to held Start.
        self.assertEqual(len(re.findall(r"\bentries\s*=\s*true\b", code)), 1)
        self.assertEqual(len(re.findall(r"\battached\s*=\s*true\b", code)), 1)
        start = mask_noncode(method(self.text, SIGNATURES[0]))
        self.assertRegex(start, r"\bentries\s*=\s*true\b")
        self.assertRegex(start, r"\battached\s*=\s*true\b")
        self.assertEqual(len(re.findall(r"\bengine\.Start\s*\(", code)), 1)
        self.assertEqual(len(re.findall(r"\bengine\.Flatten\s*\(", code)), 1)
        self.assertIn("engine.Start(", method(self.text, "void Start()"))
        self.assertIn("engine.Flatten(", method(self.text, "void Flatten(AccountRow[] rows)"))
        for signature in ("void ApplyXml(XElement x)", "void Tick()",
                          "public KINFXTradeCopierTab()", "protected override void Restore(XElement element)"):
            automatic = mask_noncode(method(self.text, signature))
            self.assertNotRegex(automatic, r"\b(?:engine\.(?:Start|Flatten)|(?<![.\w])(?:Start|Flatten))\s*\(")
            self.assertNotRegex(automatic, r"\b(?:entries|attached)\s*=\s*true\b")


STUB = "namespace NinjaTrader.Cbi { public sealed class Account {} }\n"
# Audit runs before the representative target or stub is executed. It uses
# ReflectionOnlyLoadFrom, never loads an NT/native DLL, checks references,
# bases and every resolved IL operand, and rejects P/Invoke/native methods.
AUDITOR = r'''
using System;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
public static class Audit {
    static Dictionary<string,bool> allowed = new Dictionary<string,bool>(StringComparer.OrdinalIgnoreCase);
    static Dictionary<short,OpCode> ops = new Dictionary<short,OpCode>();
    static void CheckAssembly(Assembly a) {
        if(!allowed.ContainsKey(a.FullName)) throw new Exception("unapproved assembly: "+a.FullName);
    }
    static void CheckType(Type t) {
        if(t==null) return;
        CheckAssembly(t.Assembly);
        if(t.FullName=="NinjaTrader.Gui.Tools.NTWindow" || t.FullName=="NinjaTrader.Gui.Tools.NTTabPage" ||
           t.FullName=="System.Windows.Window") throw new Exception("native/UI base");
    }
    static void CheckMember(MemberInfo m) {
        CheckType(m as Type); CheckType(m.DeclaringType);
        if(m.DeclaringType!=null && m.DeclaringType.FullName=="NinjaTrader.Cbi.Account")
            throw new Exception("Account member operand forbidden");
    }
    static void CheckMethod(MethodBase m) {
        if((m.Attributes & MethodAttributes.PinvokeImpl)!=0 ||
           (m.GetMethodImplementationFlags() & (MethodImplAttributes.InternalCall | MethodImplAttributes.Native | MethodImplAttributes.Unmanaged))!=0)
            throw new Exception("native method forbidden");
        MethodBody body=m.GetMethodBody(); if(body==null)return;
        foreach(LocalVariableInfo v in body.LocalVariables)CheckType(v.LocalType);
        foreach(ExceptionHandlingClause e in body.ExceptionHandlingClauses)
            if(e.Flags==ExceptionHandlingClauseOptions.Clause)CheckType(e.CatchType);
        byte[] il=body.GetILAsByteArray(); int p=0;
        while(p<il.Length) {
            short id=il[p++]; if(id==0xfe)id=(short)(0xfe00|il[p++]);
            OpCode op=ops[id]; int n=0;
            switch(op.OperandType) {
            case OperandType.InlineNone: break;
            case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar: n=1;break;
            case OperandType.InlineVar:n=2;break;
            case OperandType.InlineI8:case OperandType.InlineR:n=8;break;
            case OperandType.InlineSwitch:n=4+4*BitConverter.ToInt32(il,p);break;
            case OperandType.InlineMethod:case OperandType.InlineField:case OperandType.InlineType:case OperandType.InlineTok:
                CheckMember(m.Module.ResolveMember(BitConverter.ToInt32(il,p)));n=4;break;
            case OperandType.InlineSig:throw new Exception("indirect signature forbidden");
            default:n=4;break;
            }
            p+=n; if(p>il.Length)throw new Exception("invalid IL");
        }
    }
    public static int Main(string[] args) {
        try {
            allowed[typeof(object).Assembly.FullName]=true;
            foreach(FieldInfo f in typeof(OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static)) {
                OpCode op=(OpCode)f.GetValue(null);ops[op.Value]=op;
            }
            var artifacts=new List<Assembly>();
            foreach(string path in args) {
                Assembly a=Assembly.ReflectionOnlyLoadFrom(Path.GetFullPath(path));
                allowed[a.FullName]=true;artifacts.Add(a);
            }
            AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve+=(s,e)=> {
                foreach(Assembly a in artifacts)if(a.FullName==e.Name)return a;
                if(e.Name==typeof(object).Assembly.FullName)return Assembly.ReflectionOnlyLoad(e.Name);
                throw new Exception("unapproved resolve: "+e.Name);
            };
            foreach(Assembly a in artifacts) {
                foreach(AssemblyName r in a.GetReferencedAssemblies())
                    if(!allowed.ContainsKey(r.FullName))throw new Exception("unapproved reference: "+r.FullName);
                foreach(Type t in a.GetTypes()) {
                    CheckType(t.BaseType);
                    if(t.FullName=="NinjaTrader.Cbi.Account") {
                        if(t.GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly).Length!=0 ||
                           t.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly).Length!=0)
                            throw new Exception("Account stub not empty");
                    }
                    foreach(FieldInfo f in t.GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly))CheckType(f.FieldType);
                    foreach(MethodInfo m in t.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly))CheckMethod(m);
                    foreach(ConstructorInfo c in t.GetConstructors(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly))CheckMethod(c);
                    if(t.TypeInitializer!=null)CheckMethod(t.TypeInitializer);
                }
                Console.WriteLine("AUDIT PASS "+a.GetName().Name);
            }
            return 0;
        } catch(Exception e) {Console.Error.WriteLine("AUDIT FAIL "+e);return 1;}
    }
}
'''


def harness(text, define, external, method_index, throwing_log=False):
    prefix = "#define KINFX_OFFLINE_FAKE\n" if define else ""
    declaration = "" if external else STUB
    exact = representative_entry(text, SIGNATURES[method_index])
    block = method(text, "void BlockEntries()")
    should_reach = define and not external
    return prefix + r'''
using System;
using System.Collections.Generic;
using NinjaTrader.Cbi;
''' + declaration + r'''
public sealed class Instrument {}
public sealed class RiskSettings {}
public sealed class RiskLatch {}
internal sealed class CopierEngine {
    readonly object controlGate=new object();
    int controlGeneration;
    bool entries=true;
    int reached=0, logged=0;
    readonly Action<string> log;
    public CopierEngine(bool throwing) { log=s=>{logged++;if(throwing)throw new InvalidOperationException("harmless logger failure");}; }
''' + block + "\n" + exact + r'''
    public static int Main() {
        var engine=new CopierEngine(''' + str(throwing_log).lower() + r''');
        bool caught=false;
        try {
''' + ("engine.Start(null,null,null);" if method_index == 0 else "engine.Flatten(null,null);") + r'''
        } catch(InvalidOperationException) {caught=true;}
        bool reachedExpected=''' + str(should_reach).lower() + r''';
        bool throwing=''' + str(throwing_log).lower() + r''';
        bool identity=typeof(Account).Assembly==typeof(CopierEngine).Assembly;
        bool pass=identity==''' + str(not external).lower() + r''' &&
            engine.reached==(reachedExpected?1:0) && engine.entries==reachedExpected &&
            engine.logged==(reachedExpected?0:1) && engine.controlGeneration==(reachedExpected?0:1) &&
            caught==(throwing&&!reachedExpected);
        Console.WriteLine((pass?"PASS ":"FAIL ")+"method=''' + ("Start" if method_index == 0 else "Flatten") + r''' define=''' + str(define).lower() + r''' external=''' + str(external).lower() + r''' throwing=''' + str(throwing_log).lower() + r''' reached="+engine.reached+" entries="+engine.entries+" logged="+engine.logged+" generation="+engine.controlGeneration);
        return pass?0:1;
    }
}
'''


@unittest.skipUnless(os.name == "nt" and os.environ.get("KINFX_HOLD_CSC") and os.environ.get("KINFX_HOLD_SCRATCH"),
                     "optional Windows Framework behavioral proof; portable syntax tests still run")
class FrameworkBehaviorHoldTests(unittest.TestCase):
    """Actual compiled entry behavior, not full engine/provider acceptance."""

    @classmethod
    def setUpClass(cls):
        cls.compiler = Path(os.environ["KINFX_HOLD_CSC"]).resolve(strict=True)
        # Restrict this optional host to the Framework compiler and its mscorlib.
        if cls.compiler.name.lower() != "csc.exe" or cls.compiler.parent.name != "v4.0.30319":
            raise AssertionError("only installed Framework csc is supported")
        cls.core = cls.compiler.parent / "mscorlib.dll"
        cls.root = Path(os.environ["KINFX_HOLD_SCRATCH"]).resolve(strict=True) / ("hold-" + uuid.uuid4().hex)
        cls.root.mkdir()
        cls.text = SOURCE.read_text(encoding="utf-8")
        (cls.root / "source-snapshot.cs").write_bytes(SOURCE.read_bytes())
        cls.env = dict(os.environ, TMP=str(cls.root), TEMP=str(cls.root), TMPDIR=str(cls.root))
        cls.auditor = cls.root / "Audit.exe"
        cls.compile("Audit.cs", AUDITOR, cls.auditor)
        cls.stub = cls.root / "HarmlessAccountStub.dll"
        cls.compile("HarmlessAccountStub.cs", STUB, cls.stub, library=True)
        cls.run_command([str(cls.auditor), str(cls.stub)], "stub-preflight", check=True)

    @classmethod
    def run_command(cls, command, label, check=False):
        run = subprocess.run(command, cwd=cls.root, env=cls.env, capture_output=True,
                             text=True, encoding="utf-8", errors="replace", timeout=60)
        output = run.stdout + run.stderr
        (cls.root / (label + ".log")).write_text(output, encoding="utf-8")
        print(label + ": exit=" + str(run.returncode) + "\n" + output.strip(), flush=True)
        if check and run.returncode:
            raise AssertionError(label + ": " + output)
        return run

    @classmethod
    def compile(cls, name, text, out, library=False, external=False):
        path = cls.root / name
        path.write_text(text, encoding="utf-8")
        args = [str(cls.compiler), "/nologo", "/utf8output", "/noconfig", "/nostdlib+", "/r:" + str(cls.core),
                "/target:" + ("library" if library else "exe"), "/out:" + str(out)]
        if external:
            args.append("/r:" + str(cls.stub))
        args.append(str(path))
        cls.run_command(args, name + "-compile", check=True)
        print("ARTIFACT sha256=" + hashlib.sha256(out.read_bytes()).hexdigest() + " " + out.name, flush=True)

    def test_actual_entries_define_identity_matrix(self):
        for index, signature in enumerate(SIGNATURES):
            for define in (False, True):
                for external in (False, True):
                    for throwing in (False, True):
                        label = "entry-%s-define%s-external%s-throw%s" % (index, int(define), int(external), int(throwing))
                        with self.subTest(label=label):
                            exe = self.root / (label + ".exe")
                            self.compile(label + ".cs", harness(self.text, define, external, index, throwing), exe, external=external)
                            # Only this empty, freshly built, preflighted external stub may be allowed.
                            audit_args = [str(self.auditor), str(exe)] + ([str(self.stub)] if external else [])
                            self.run_command(audit_args, label + "-audit", check=True)
                            actual = self.run_command([str(exe)], label + "-runtime")
                            self.assertEqual(actual.returncode, 0, actual.stdout + actual.stderr)


if __name__ == "__main__":
    unittest.main(verbosity=2)
