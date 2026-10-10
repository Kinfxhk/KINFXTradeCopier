"""Opt-in fresh Windows compiler/auditor controls; no NT artifact execution."""
import importlib.util
import os
from pathlib import Path
import sys
import unittest
sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
import offline


@unittest.skipUnless(sys.platform == 'win32' and os.environ.get('KINFX_TOOLING_RUNTIME'), 'requires explicit isolated Windows scratch')
class RuntimeControls(unittest.TestCase):
    def test_fresh_compiler_auditor_and_case_execution(self):
        self.assertTrue(hasattr(offline, 'build_tools'), 'fresh tool bootstrap is missing')
        run = offline.new_run(Path(os.environ['KINFX_TOOLING_RUNTIME']), 'controls')
        tools = offline.build_tools(Path(os.environ['KINFX_NT_BIN']), run)
        samples = {
            'safe': 'class CompilerCaseRunner {static int Main(){return 0;}}',
            'fake': 'namespace NinjaTrader.Cbi {class Account {public void Submit(){}}} class CompilerCaseRunner {static int Main(){new NinjaTrader.Cbi.Account().Submit();return 0;}}',
            'pinvoke': 'class CompilerCaseRunner {[System.Runtime.InteropServices.DllImport("kernel32.dll")]static extern int GetTickCount();static int Main(){return 0;}}',
            'window': 'class NativeWindow {} class CompilerCaseRunner : NativeWindow {static int Main(){return 0;}}',
            'network': 'class CompilerCaseRunner {static int Main(){new System.Net.WebClient();return 0;}}',
        }
        for name, text in samples.items():
            with self.subTest(name=name):
                folder = run / name
                folder.mkdir()
                source = folder / 'sample.cs'
                source.write_text(text, encoding='utf-8')
                exe = offline.compile_artifact(tools, folder, [source], 'plain')
                self.assertEqual(name in ('safe', 'fake'), offline.audit_artifact(tools, folder, exe))
                if name in ('safe', 'fake'):
                    result = offline.execute_case(tools, folder, exe, name)
                    self.assertEqual(0, result['exit_code'])
                    self.assertTrue(result['audit_passed'])
        # An external Account reference is rejected before any runtime launch.
        folder = run / 'external-account'
        folder.mkdir()
        stub = folder / 'stub.cs'
        stub.write_text('namespace NinjaTrader.Cbi {public class Account {public void Submit(){}}}', encoding='utf-8')
        external = folder / 'external.dll'
        code = offline.command([tools['framework'] / 'csc.exe', '/nologo', '/noconfig', '/nostdlib+',
                                '/target:library', '/r:' + str(tools['framework_refs'][0]),
                                '/out:' + str(external), stub], folder, 'stub-compile')
        self.assertEqual(0, code)
        target = folder / 'target.cs'
        target.write_text('class CompilerCaseRunner {static int Main(){new NinjaTrader.Cbi.Account().Submit();return 0;}}', encoding='utf-8')
        with self.assertRaises(ValueError):
            offline.compile_artifact(tools, folder, [target], 'plain', references=tools['framework_refs'] + [external])
        self.assertFalse((folder / 'artifact.exe').exists())
        foreign_exe = folder / 'foreign.exe'
        code = offline.command([tools['framework'] / 'csc.exe', '/nologo', '/noconfig', '/nostdlib+',
                                '/target:exe', '/r:' + str(tools['framework_refs'][0]), '/r:' + str(external),
                                '/out:' + str(foreign_exe), target], folder, 'external-target-compile')
        self.assertEqual(0, code)
        self.assertFalse(offline.audit_artifact(tools, folder, foreign_exe))
        # Active escaped define must be refused even without NT compile references.
        folder = run / 'escaped-define'
        folder.mkdir()
        source = folder / 'sample.cs'
        source.write_text('#define KINFX_OFFLINE_F\\u0041KE\nclass X {}', encoding='utf-8')
        with self.assertRaises(ValueError):
            offline.compile_artifact(tools, folder, [source], 'full', references=tools['framework_refs'])
        self.assertFalse((folder / 'artifact.dll').exists())


if __name__ == '__main__':
    unittest.main()
