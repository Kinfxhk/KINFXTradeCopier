"""Portable, stdlib-only tooling regression tests (no platform DLLs)."""
import importlib.util
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.dont_write_bytecode = True


def tooling():
    path = ROOT / 'scripts' / 'offline.py'
    if not path.exists():
        return None
    spec = importlib.util.spec_from_file_location('offline', path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class ExtractionTests(unittest.TestCase):
    def test_extract_exact_production_regions_and_fail_closed(self):
        m = tooling()
        self.assertIsNotNone(m, 'portable offline tooling is not implemented')
        text = 'header\n// BEGIN KINFX PURE\nclass Policy {}\n// END KINFX PURE\nclass Engine {}\n    public sealed class AccountRow : INotifyPropertyChanged\nui'
        self.assertEqual('class Policy {}\n', m.extract(text, 'pure'))
        self.assertEqual('class Policy {}\n// END KINFX PURE\nclass Engine {}\n', m.extract(text, 'engine'))
        with self.assertRaises(ValueError):
            m.extract(text.replace('// END KINFX PURE', ''), 'pure')
        with self.assertRaises(ValueError):
            m.extract(text + '\n// BEGIN KINFX PURE', 'engine')


class PublicSafetyTests(unittest.TestCase):
    def test_public_scanner_reports_only_relative_paths_not_contents(self):
        m = tooling()
        self.assertTrue(hasattr(m, 'scan_public'), 'public verifier is missing')
        files = {'src/ok.cs': b'class X {}', 'docs/secret.md': b'api_key = ' + b'abcdefghijklmnopqrstuvwxyz',
                 'tools/bad.dll': b'MZ', 'docs/path.md': b'location ' + b'C:' + b'\\Users\\' + b'private\\data',
                 '.artifacts/private.log': b'api_key = not-public'}
        findings = m.scan_public(files)
        self.assertEqual({'docs/secret.md', 'tools/bad.dll', 'docs/path.md'}, {f['path'] for f in findings})
        self.assertNotIn('abcdefghijklmnopqrstuvwxyz', str(findings))
        self.assertEqual([], m.scan_public({'docs/BUILD.md': b'python scripts/offline.py --nt-bin "$NT_BIN"'}))

    def test_windows_runtime_gate_is_fail_closed(self):
        m = tooling()
        self.assertTrue(hasattr(m, 'require_windows'), 'platform gate is missing')
        with self.assertRaises(ValueError):
            m.require_windows('linux')
        m.require_windows('win32')


class MetadataTests(unittest.TestCase):
    def test_invalid_and_truncated_pe_rejected_without_loading(self):
        path = ROOT / 'scripts' / 'pe_audit.py'
        self.assertTrue(path.exists(), 'stdlib metadata auditor is missing')
        spec = importlib.util.spec_from_file_location('pe_audit', path)
        m = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(m)
        for data in (b'', b'MZ', b'MZ' + bytes(300)):
            with self.assertRaises(ValueError):
                m.metadata(data)


class RunnerTests(unittest.TestCase):
    def test_case_discovery_keeps_every_declared_zero_argument_case(self):
        m = tooling()
        self.assertTrue(hasattr(m, 'case_names'), 'case discovery is missing')
        text = 'class X { static void Main(string[] args) {} static void Eq(bool x) {} static void First() {} static void Second() {} }'
        self.assertEqual(['First', 'Second'], m.case_names(text))
        risk = (ROOT / 'tests/RiskTests.cs').read_text(encoding='utf-8')
        self.assertEqual(8, len(m.case_names(risk)))
        self.assertTrue(hasattr(m, 'pure_extra_source'), 'original risk Main UI hold assertions need an independent case')
        self.assertIn('DashboardCommandsRemainHeld', m.pure_extra_source())
        independent = (ROOT / 'tests/EngineIndependentReview.cs').read_text(encoding='utf-8')
        self.assertIn('ProductionAllProvidersCannotEnable', m.case_names(independent))

    def test_failed_process_is_not_always_an_engine_assertion(self):
        m = tooling()
        self.assertTrue(hasattr(m, 'runtime_status'), 'runtime crash classification missing')
        self.assertEqual('PASS', m.runtime_status(0))
        self.assertEqual('FAIL', m.runtime_status(1))
        self.assertEqual('HARNESS_TIMEOUT', m.runtime_status(124))
        self.assertEqual('HARNESS_CRASH', m.runtime_status(-532462766))

    def test_short_case_directories_keep_full_names_in_metadata(self):
        m = tooling()
        self.assertTrue(hasattr(m, 'case_directory'), 'short Framework-safe case layout missing')
        self.assertEqual(Path('group/c012'), m.case_directory(Path('group'), 12))
        self.assertLess(len(m.case_directory(Path('group'), 12).name), 10)

    def test_cli_modes_and_unique_output_contract(self):
        m = tooling()
        self.assertTrue(hasattr(m, 'parser'), 'entrypoint is missing')
        for mode in ('verify', 'pure', 'engine', 'independent', 'compile'):
            self.assertEqual(mode, m.parser().parse_args([mode]).mode)
        import contextlib
        import io
        with contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
            m.parser().parse_args(['compile', '--define', 'KINFX_OFFLINE_FAKE'])
        self.assertTrue(hasattr(m, 'new_run'), 'unique artifacts allocation missing')
        import tempfile
        import os
        # CI may choose TMPDIR; no writes outside the delegated scratch here.
        base = Path(os.environ.get('TMPDIR', tempfile.gettempdir()))
        with tempfile.TemporaryDirectory(prefix='tooling-test-', dir=base) as root:
            one, two = m.new_run(Path(root), 'pure'), m.new_run(Path(root), 'pure')
            self.assertNotEqual(one, two)
            self.assertEqual('.artifacts', one.parent.name)


if __name__ == '__main__':
    unittest.main()
