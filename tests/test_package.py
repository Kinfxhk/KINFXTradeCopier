"""Public packaging hygiene, not proof of complete secret detection or trading safety."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]
SKIP = {'.git', '.artifacts', '__pycache__'}
FORBIDDEN = {'.dll', '.exe', '.pdb', '.zip', '.pem', '.key'}
PATTERNS = {
    'private-key': re.compile('-----BEGIN ' + '(?:RSA |OPENSSH |EC )?PRIVATE KEY-----'),
    'github-token': re.compile('(?:gh' + 'p_|github_' + 'pat_)[A-Za-z0-9_]{20,}'),
    'personal-home-path': re.compile(r'C:[/\\]Users[/\\]', re.IGNORECASE),
}

def public_files():
    for path in ROOT.rglob('*'):
        rel = path.relative_to(ROOT)
        if path.is_file() and not any(part in SKIP for part in rel.parts):
            yield path, rel

class PublicPackage(unittest.TestCase):
    def test_no_vendor_binaries_keys_or_environment_files(self):
        bad = [str(rel) for path, rel in public_files()
               if path.suffix.lower() in FORBIDDEN or path.name == '.env' or path.name.startswith('.env.')]
        self.assertEqual(bad, [])

    def test_common_sensitive_patterns_metadata_only(self):
        found = []
        for path, rel in public_files():
            if path.suffix.lower() == '.png':
                self.assertTrue(path.read_bytes().startswith(bytes([0x89]) + b'PNG'), str(rel))
                continue
            text = path.read_text(encoding='utf-8-sig')
            for name, pattern in PATTERNS.items():
                for match in pattern.finditer(text):
                    found.append((str(rel), text.count('\n', 0, match.start()) + 1, name))
        self.assertEqual(found, [], 'potential sensitive content: paths/line/rule only')

    def test_no_privileged_workflow_or_secret_access(self):
        for path in (ROOT / '.github' / 'workflows').glob('*.yml'):
            text = path.read_text(encoding='utf-8')
            self.assertNotIn('pull_' + 'request_target', text)
            self.assertNotIn('secrets' + '.', text)
            self.assertIn('contents: read', text)

    def test_progress_block_in_readmes_matches_progress_json(self):
        import subprocess, sys
        result = subprocess.run([sys.executable, '-B', str(ROOT / 'scripts' / 'progress.py'), '--check'],
                                capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_progress_scoring_never_credits_offline_as_real(self):
        import importlib.util
        spec = importlib.util.spec_from_file_location('progress', ROOT / 'scripts' / 'progress.py')
        mod = importlib.util.module_from_spec(spec); spec.loader.exec_module(mod)
        data = mod.load()
        for item in data['items']:
            self.assertIn(item['level'], (0, 1, 2, 3))
            self.assertTrue(item['evidence'].strip(), item['id'])
        fake = {'items': [{'id': 'x', 'weight': 10, 'level': 2, 'must': True, 'targets': ['demo'], 'group': 'g', 'en': 'e', 'zh': 'z', 'evidence': 'e'}]}
        s = mod.score(fake, 'demo')
        self.assertEqual(s['percent'], 50.0)
        self.assertFalse(s['ready'])
        fake['items'][0]['level'] = 3
        self.assertTrue(mod.score(fake, 'demo')['ready'])
        self.assertEqual(mod.score(fake, 'demo')['percent'], 100.0)

    def test_quickstart_commands_and_paths_are_exact(self):
        for name in ('README.md', 'README.zh-HK.md'):
            text = (ROOT / name).read_text(encoding='utf-8')
            self.assertIn('scripts\\install-simtest.ps1', text, name)
            self.assertNotIn('scripts\\\\install-simtest', text, name)
            self.assertIn('templates\\KINFXTradeCopier\\sim-allow.txt', text, name)
            self.assertIn('DRY RUN', text, name)
            self.assertIn('git checkout release-candidate', text, name)
        self.assertTrue((ROOT / 'scripts' / 'install-simtest.ps1').is_file())

    def test_license_and_alpha_boundary_are_present(self):
        self.assertIn('MIT License', (ROOT / 'LICENSE').read_text(encoding='utf-8'))
        readme = (ROOT / 'README.md').read_text(encoding='utf-8')
        self.assertIn('TRADING DISABLED', readme)
        self.assertIn('not a C# engine implementation', readme)

if __name__ == '__main__':
    unittest.main(verbosity=2)
