"""Safe offline verification. Uses Python's standard library only."""
from pathlib import Path
import re
import sys
import argparse
import hashlib
import importlib.util
import json
import os
import subprocess
import uuid

sys.dont_write_bytecode = True
# Works via direct script execution and importlib-based portable tests.
_spec = importlib.util.spec_from_file_location('pe_audit', Path(__file__).with_name('pe_audit.py'))
_pe = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_pe)
metadata = _pe.metadata
FRAMEWORK = ('mscorlib', 'System', 'System.Core', 'System.Xml', 'System.Xml.Linq')
TOOLING = ('Microsoft.CodeAnalysis', 'Microsoft.CodeAnalysis.CSharp', 'System.Collections.Immutable',
           'System.Reflection.Metadata', 'System.Memory', 'System.Runtime.CompilerServices.Unsafe',
           'System.Buffers', 'System.Threading.Tasks.Extensions', 'System.Numerics.Vectors',
           'System.Text.Encoding.CodePages', 'System.ValueTuple')


def require_windows(platform=None):
    if (sys.platform if platform is None else platform) != 'win32':
        raise ValueError('Windows .NET Framework is required; use verify on other platforms')


def scan_public(files):
    """Heuristic public-tree guard; never return matched content."""
    findings = []
    for name, data in sorted(files.items()):
        if name.split('/')[0] in ('.git', '.artifacts', '__pycache__'):
            continue
        reason = None
        if Path(name).suffix.lower() in ('.dll', '.exe', '.pdb', '.pfx', '.key', '.pem', '.zip') or data.startswith((b'MZ', b'\x7fELF')):
            reason = 'binary-or-private-key'
        else:
            text = data.decode('utf-8', errors='replace')
            if re.search(r'(?i)(?:[a-z]:[\\/]+Users[\\/]+|/(?:home|Users)/)[a-z0-9_.-]+', text):
                reason = 'absolute-user-path'
            elif re.search(r'(?i)(?:api[_-]?key|password|access[_-]?token|client[_-]?secret)\s*[=:]\s*[\"\']?[a-z0-9_+/-]{16,}', text) or re.search(r'(?:gh[pousr]_[a-zA-Z0-9]{20,}|-----BEGIN [A-Z ]*PRIVATE KEY-----)', text):
                reason = 'possible-credential'
        if reason:
            findings.append({'path': name, 'reason': reason})
    return findings

ROOT = Path(__file__).resolve().parents[1]
SOURCE = Path('src/AddOns/KINFXTradeCopier.cs')
PURE_BEGIN = '// BEGIN KINFX PURE\n'
PURE_END = '// END KINFX PURE\n'
ENGINE_END = '    public sealed class AccountRow : INotifyPropertyChanged'


def extract(text, mode):
    """Keep exact marked code, refusing ambiguous or missing boundaries."""
    for marker in (PURE_BEGIN, PURE_END):
        if text.count(marker.rstrip('\n')) != 1 or marker not in text:
            raise ValueError('extraction boundary missing or duplicated')
    start = text.index(PURE_BEGIN) + len(PURE_BEGIN)
    end = text.index(PURE_END)
    if mode == 'engine':
        if text.count(ENGINE_END) != 1:
            raise ValueError('engine boundary missing or duplicated')
        end = text.index(ENGINE_END)
    if end <= start or mode not in ('pure', 'engine'):
        raise ValueError('invalid extraction mode or boundary order')
    return text[start:end]


def digest(data):
    return hashlib.sha256(data).hexdigest()


def case_names(text):
    return re.findall(r'\bstatic\s+void\s+([A-Za-z_]\w*)\s*\(\s*\)\s*\{', text)


def case_directory(folder, index):
    return folder / ('c' + str(index).zfill(3))


def parser():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('mode', choices=('verify', 'pure', 'engine', 'independent', 'compile'))
    p.add_argument('--nt-bin', type=Path, help='read-only installed NinjaTrader bin directory')
    p.add_argument('--artifacts-root', type=Path, default=ROOT, help='parent of unique .artifacts/ outputs')
    return p


def new_run(root, mode):
    parent = root.resolve() / '.artifacts'
    if parent.is_symlink():
        raise ValueError('artifact symlink rejected')
    parent.mkdir(parents=True, exist_ok=True)
    run = parent / (mode + '-' + uuid.uuid4().hex)
    run.mkdir()
    return run


def command(cmd, folder, label, timeout=120):
    env = dict(os.environ)
    # Every fixture's persistence and CLR temp path remains inside this run.
    for key in ('TMPDIR', 'TMP', 'TEMP'):
        env[key] = str(folder.resolve())
    try:
        result = subprocess.run([str(x) for x in cmd], cwd=folder, env=env, capture_output=True,
                                timeout=timeout, text=True, errors='replace')
        code, out, err = result.returncode, result.stdout, result.stderr
    except subprocess.TimeoutExpired as ex:
        code, out, err = 124, 'HARNESS_TIMEOUT', str(type(ex).__name__)
    # Raw output stays private, never printed or included in public manifests.
    (folder / (label + '.stdout.txt')).write_text(out, encoding='utf-8')
    (folder / (label + '.stderr.txt')).write_text(err, encoding='utf-8')
    return code


def build_tools(nt_bin, run):
    require_windows()
    nt_bin = nt_bin.resolve(strict=True)
    fw = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319'
    refs = [fw / (n + '.dll') for n in FRAMEWORK]
    compiler_refs = refs + [fw / 'netstandard.dll'] + [nt_bin / (n + '.dll') for n in TOOLING[:3]]
    # Complete tooling reference closure is inspected statically BEFORE any host execution.
    inventory = {}
    todo = list(TOOLING[:3])
    framework_allowed = set(FRAMEWORK) | {'netstandard', 'System.Numerics', 'System.ValueTuple'}
    while todo:
        name = todo.pop()
        if name in inventory:
            continue
        path = nt_bin / (name + '.dll')
        if not path.exists() and name in framework_allowed:
            continue
        info = metadata(path.read_bytes())
        if info['name'] != name:
            raise ValueError('compiler tooling identity mismatch')
        inventory[name] = {'sha256': digest(path.read_bytes()), 'references': info['references'], 'pinvoke_declared': info['pinvoke']}
        for ref in info['references']:
            if ref not in framework_allowed and ref not in TOOLING:
                raise ValueError('unapproved compiler dependency')
            if ref in TOOLING:
                todo.append(ref)
    (run / 'compiler-reference-audit.json').write_text(json.dumps(inventory, indent=2), encoding='utf-8')
    tools = {'nt_bin': nt_bin, 'framework_refs': refs, 'framework': fw, 'run': run}
    for stem, extra in [('CompilerHost', compiler_refs), ('RuntimeAudit', refs)]:
        source = ROOT / 'tests' / (stem + '.cs')
        exe = run / (stem + '.exe')
        cmd = [fw / 'csc.exe', '/nologo', '/noconfig', '/nostdlib+', '/target:exe', '/out:' + str(exe)]
        cmd += ['/r:' + str(p) for p in extra] + [source]
        if command(cmd, run, 'bootstrap-' + stem) != 0:
            raise ValueError('tool bootstrap failed: ' + stem)
        info = metadata(exe.read_bytes())
        allowed = set(FRAMEWORK) if stem == 'RuntimeAudit' else set(FRAMEWORK) | set(TOOLING) | {'netstandard'}
        if not set(info['references']) <= allowed or info['pinvoke']:
            raise ValueError('tool bootstrap metadata rejected')
        tools[stem] = exe
    return tools


def compile_artifact(tools, folder, sources, mode, references=None):
    sources_file, refs_file = folder / 'sources.txt', folder / 'references.txt'
    sources_file.write_text('\n'.join(str(p.resolve()) for p in sources), encoding='utf-8')
    refs_file.write_text('\n'.join(str(p.resolve()) for p in (references or tools['framework_refs'])), encoding='utf-8')
    output = folder / ('artifact.dll' if mode == 'full' else 'artifact.exe')
    cmd = [tools['CompilerHost'], tools['nt_bin'], mode, sources_file, refs_file, output]
    if command(cmd, folder, 'compile') != 0:
        raise ValueError('artifact compilation failed; private diagnostics retained')
    return output


def audit_artifact(tools, folder, exe):
    info = metadata(exe.read_bytes())
    if not set(info['references']) <= set(FRAMEWORK) or info['pinvoke']:
        return False
    return command([tools['RuntimeAudit'], exe], folder, 'audit') == 0


def runtime_status(code):
    return {0: 'PASS', 1: 'FAIL', 124: 'HARNESS_TIMEOUT'}.get(code, 'HARNESS_CRASH')


def execute_case(tools, folder, exe, name):
    # Audit AGAIN immediately before EACH process, even when bytes were audited previously.
    artifact_hash = digest(exe.read_bytes())
    passed = audit_artifact(tools, folder, exe)
    if not passed or digest(exe.read_bytes()) != artifact_hash:
        return {'case': name, 'exit_code': 2, 'audit_passed': False, 'artifact_sha256': artifact_hash, 'status': 'HARNESS_REJECT'}
    code = command([exe, name], folder, 'runtime', timeout=30)
    return {'case': name, 'exit_code': code, 'audit_passed': True, 'artifact_sha256': artifact_hash,
            'status': runtime_status(code)}


def pure_extra_source():
    return '''using System; using NinjaTrader.NinjaScript.AddOns.Kinfx;
class CompilerPureExtra {
 static void DashboardCommandsRemainHeld() {
  foreach(string command in new[]{"start","flattenSelected","flattenAll","batch"})
   if(DashboardSafety.CanExecute(command))throw new Exception("command escaped hold");
  if(!DashboardSafety.CanExecute("refresh"))throw new Exception("read-only refresh refused");
 }
}'''


def runner_source(class_name):
    return '''using System; using System.Reflection;
class CompilerCaseRunner {
 static int Main(string[] args) {
  try { if(args.Length!=1)throw new Exception("one case required");
   var m=typeof(''' + class_name + ''').GetMethod(args[0],BindingFlags.Static|BindingFlags.NonPublic);
   if(m==null||m.GetParameters().Length!=0)throw new Exception("unknown case");
   m.Invoke(null,null);Console.WriteLine("PASS "+args[0]);return 0;
  } catch(Exception ex) {Console.WriteLine("FAIL "+args[0]);System.IO.File.WriteAllText("case-failure.txt",(ex.InnerException??ex).ToString());return 1;}
 }
}'''


def verify():
    files = {}
    for p in ROOT.rglob('*'):
        name = p.relative_to(ROOT).as_posix()
        if any(x in ('.git', '.artifacts', '__pycache__') for x in p.relative_to(ROOT).parts):
            continue
        if p.is_symlink():
            raise ValueError('public symlink rejected: ' + name)
        if p.is_file():
            files[name] = p.read_bytes()
    findings = scan_public(files)
    required = [SOURCE.as_posix(), 'tests/RiskTests.cs', 'tests/EngineTests.cs', 'tests/EngineSafetyTests.cs', 'tests/EngineIndependentReview.cs', 'tests/test_production_hold.py']
    findings += [{'path': x, 'reason': 'required-source-missing'} for x in required if x not in files]
    if findings:
        for f in findings:
            print('VERIFY_REJECT', f['path'], f['reason'])
        return 1
    # Sibling-owned lexical hold tests run without opt-in runtime environment variables.
    env = dict(os.environ)
    for key in ('KINFX_HOLD_SOURCE', 'KINFX_HOLD_RUNTIME', 'KINFX_HOLD_SCRATCH', 'KINFX_HOLD_CSC'):
        env.pop(key, None)
    env['PYTHONDONTWRITEBYTECODE'] = '1'
    result = subprocess.run([sys.executable, '-m', 'unittest', 'discover', '-s', 'tests', '-p', 'test_production_hold.py'], cwd=ROOT, env=env, capture_output=True)
    print('VERIFY_OK' if result.returncode == 0 else 'HOLD_TESTS_FAIL')
    return 0 if result.returncode == 0 else 1


def main(argv=None):
    args = parser().parse_args(argv)
    run = None
    report = {'mode': args.mode, 'cases': []}
    try:
        if args.mode == 'verify':
            return verify()
        require_windows()
        if args.nt_bin is None:
            raise ValueError('--nt-bin is required for compilation/runtime modes')
        run = new_run(args.artifacts_root, args.mode)
        source_bytes = (ROOT / SOURCE).read_bytes()
        report['source_sha256'] = digest(source_bytes)
        snapshot = run / 'production.cs'
        snapshot.write_bytes(source_bytes)
        tools = build_tools(args.nt_bin, run)
        if args.mode == 'compile':
            fw = tools['framework']
            refs = tools['framework_refs'] + [fw / 'WPF' / (n + '.dll') for n in ('WindowsBase', 'PresentationCore', 'PresentationFramework')]
            refs += [fw / (n + '.dll') for n in ('System.Xaml', 'System.ComponentModel.DataAnnotations')]
            refs += [args.nt_bin.resolve() / (n + '.dll') for n in ('NinjaTrader.Core', 'NinjaTrader.Gui')]
            output = compile_artifact(tools, run, [snapshot], 'full', refs)
            report['compile_only'] = True
            report['artifact_sha256'] = digest(output.read_bytes())
            report['references'] = metadata(output.read_bytes())['references']
            diagnostics = (run / 'compiler-diagnostics.txt').read_text(encoding='utf-8')
            codes = re.findall(r'\b(?:warning|error) (CS\d+):', diagnostics)
            report['diagnostic_counts'] = {code: codes.count(code) for code in sorted(set(codes))}
            report['exit_code'] = 0
            print('COMPILE_ONLY_OK')
        else:
            text = source_bytes.decode('utf-8-sig').replace('\r\n', '\n')
            body = extract(text, 'pure' if args.mode == 'pure' else 'engine')
            header = 'using System;using System.Collections.Generic;using System.Globalization;using System.IO;using System.Linq;using System.Threading;using System.Xml.Linq;'
            if args.mode != 'pure':
                header += 'using NinjaTrader.Cbi;'
            extracted = run / 'extracted.cs'
            extracted.write_text(header + '\nnamespace NinjaTrader.NinjaScript.AddOns.Kinfx {\n' + body + '\n}', encoding='utf-8')
            report['extracted_sha256'] = digest(extracted.read_bytes())
            fixtures = ['RiskTests.cs'] if args.mode == 'pure' else ['EngineTests.cs', 'EngineSafetyTests.cs'] if args.mode == 'engine' else ['EngineIndependentReview.cs']
            report['fixture_sha256'] = {}
            for fixture in fixtures:
                raw = (ROOT / 'tests' / fixture).read_bytes()
                report['fixture_sha256'][fixture] = digest(raw)
                fixture_text = raw.decode('utf-8-sig')
                class_name = re.findall(r'\bclass\s+(RiskTests|EngineTests|EngineSafetyTests)\s*\{', fixture_text)[0]
                names = case_names(fixture_text)
                modes = [(n, 'plain' if args.mode == 'pure' or n in ('ProductionQuarantine', 'ProductionAllProvidersCannotEnable') else 'fake') for n in names]
                # Compile once per fixture/define, run each independent case in a fresh directory/process.
                for compile_mode in sorted({m for _, m in modes}):
                    folder = run / (Path(fixture).stem + '-' + compile_mode)
                    folder.mkdir()
                    f = folder / fixture
                    f.write_bytes(raw)
                    runner = folder / 'runner.cs'
                    runner.write_text(runner_source(class_name), encoding='utf-8')
                    exe = compile_artifact(tools, folder, [extracted, f, runner], compile_mode)
                    for name, mode in modes:
                        if mode != compile_mode:
                            continue
                        case_folder = case_directory(folder, len(report['cases']))
                        case_folder.mkdir()
                        result = execute_case(tools, case_folder, exe, name)
                        result['fixture'] = fixture
                        result['log_directory'] = case_folder.relative_to(run).as_posix()
                        result['command'] = ['<audited-framework-artifact>', name]
                        report['cases'].append(result)
                        print(result['status'], fixture, name)
            if args.mode == 'pure':
                folder = run / 'pure-ui-hold'
                folder.mkdir()
                extra, runner = folder / 'extra.cs', folder / 'runner.cs'
                extra.write_text(pure_extra_source(), encoding='utf-8')
                runner.write_text(runner_source('CompilerPureExtra'), encoding='utf-8')
                exe = compile_artifact(tools, folder, [extracted, extra, runner], 'plain')
                case_folder = folder / 'c000'
                case_folder.mkdir()
                result = execute_case(tools, case_folder, exe, 'DashboardCommandsRemainHeld')
                result['fixture'] = 'generated-original-main-hold-assertions'
                result['log_directory'] = case_folder.relative_to(run).as_posix()
                result['command'] = ['<audited-framework-artifact>', 'DashboardCommandsRemainHeld']
                report['generated_hold_sha256'] = digest(extra.read_bytes())
                report['cases'].append(result)
                print(result['status'], result['case'])
            report['pass'] = sum(r['status'] == 'PASS' for r in report['cases'])
            report['fail'] = sum(r['status'] == 'FAIL' for r in report['cases'])
            report['harness_errors'] = sum(r['status'].startswith('HARNESS') for r in report['cases'])
            report['exit_code'] = 1 if report['fail'] or report['harness_errors'] else 0
        report['source_changed_since_snapshot'] = digest((ROOT / SOURCE).read_bytes()) != report['source_sha256']
    except (ValueError, OSError, IndexError) as ex:
        report['exit_code'] = 2
        report['incident'] = str(ex)
        print('HARNESS_ERROR', str(ex))
    finally:
        if run:
            (run / 'result.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
            print('RESULT', run / 'result.json')
    return report.get('exit_code', 2)


if __name__ == '__main__':
    sys.exit(main())
