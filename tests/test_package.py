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

    def test_license_and_alpha_boundary_are_present(self):
        self.assertIn('MIT License', (ROOT / 'LICENSE').read_text(encoding='utf-8'))
        readme = (ROOT / 'README.md').read_text(encoding='utf-8')
        self.assertIn('TRADING DISABLED', readme)
        self.assertIn('not a C# engine implementation', readme)

if __name__ == '__main__':
    unittest.main(verbosity=2)
