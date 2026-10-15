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
        # Readiness requires the complete reviewed gate inventory, even in a toy fixture.
        fake = {
            'safety_gates': [
                {'id': identifier, 'status': 'accepted', 'targets': ['demo', 'prop'],
                 'evidence': 'synthetic fixture only'}
                for identifier in ('issue-3', 'issue-4', 'issue-7', 'issue-9')
            ],
            'items': [
                {'id': identifier, 'weight': 10, 'level': 2, 'must': True,
                 'targets': ['demo', 'prop'], 'group': 'g', 'en': 'e', 'zh': 'z',
                 'evidence': 'synthetic fixture only'}
                for identifier in ('copy_core', 'ratio_cap', 'exit_nolock', 'flat_one',
                                   'flat_all', 'protect_tpsl', 'locks', 'disconnect',
                                   'partial_fill', 'frontier', 'isolation', 'scale_real',
                                   'risk_limits', 'package')
            ],
        }
        s = mod.score(fake, 'demo')
        self.assertEqual(s['percent'], 50.0)
        self.assertFalse(s['ready'])
        for item in fake['items']:
            item['level'] = 3
        self.assertTrue(mod.score(fake, 'demo')['ready'])
        self.assertEqual(mod.score(fake, 'demo')['percent'], 100.0)

    def test_quickstart_commands_and_paths_are_exact(self):
        names = ('README.md', 'README.en.md', 'README.zh-HK.md', 'README.ja.md', 'README.ko.md')
        for name in names:
            text = (ROOT / name).read_text(encoding='utf-8')
            self.assertIn('scripts\\install-simtest.ps1', text, name)
            self.assertNotIn('scripts\\\\install-simtest', text, name)
            self.assertIn('templates\\KINFXTradeCopier\\sim-allow.txt', text, name)
            self.assertIn('DRY RUN', text, name)
            self.assertNotIn('git checkout release-candidate', text, name)
        self.assertTrue((ROOT / 'scripts' / 'install-simtest.ps1').is_file())

    def test_license_and_alpha_boundary_are_present(self):
        self.assertIn('MIT License', (ROOT / 'LICENSE').read_text(encoding='utf-8'))
        for name in ('README.md', 'README.en.md'):
            readme = (ROOT / name).read_text(encoding='utf-8')
            self.assertIn('TRADING DISABLED', readme, name)
            self.assertIn('not a C# engine implementation', readme, name)

    def test_synthetic_screenshot_provenance(self):
        for name, terms in (
            ('README.md', ('synthetic DEMO', 'fake accounts and numbers', 'not NinjaTrader/provider transaction evidence',
                           '合成 DEMO', '虛構帳戶與數值', '並非 NinjaTrader／provider 交易證據')),
            ('README.en.md', ('synthetic DEMO', 'fake accounts and numbers', 'not NinjaTrader/provider transaction evidence')),
            ('README.zh-HK.md', ('合成 DEMO', '虛構帳戶與數值', '並非 NinjaTrader／provider 交易證據')),
            ('README.ja.md', ('合成 DEMO', '架空の口座と数値', 'NinjaTrader／provider の取引証拠ではありません')),
            ('README.ko.md', ('합성 DEMO', '가상 계좌와 수치', 'NinjaTrader／provider 거래 증거가 아닙니다')),
        ):
            text = (ROOT / name).read_text(encoding='utf-8')
            for term in terms:
                self.assertIn(term, text, name)
            self.assertIn('docs/screenshots/Screenshots.cs', text, name)
        for name in ('DEVELOPERS.md', 'DEVELOPERS.zh-HK.md'):
            text = (ROOT / 'docs' / name).read_text(encoding='utf-8')
            for link in ('screenshots/1-copy.png', 'screenshots/2-risk.png',
                         'screenshots/3-logs.png', 'screenshots/Screenshots.cs',
                         '../src/AddOns/KINFXTradeCopier.cs'):
                self.assertIn(link, text, name)
                self.assertTrue((ROOT / 'docs' / link).is_file(), link)

    def test_readme_rehearsal_cleanup_precedes_stop(self):
        for name, heading, connected, clean, cleanup, detached, scope in (
            ('README.md', '## First run', 'connected', 'zero position and no working orders',
             'cancel working leader orders and close the leader position using native NinjaTrader',
             'Only after the session is detached', 'Add followers only while stopped'),
            ('README.en.md', '## First run', 'connected', 'zero position and no working orders',
             'cancel working leader orders and close the leader position using native NinjaTrader',
             'Only after the session is detached', 'Add followers only while stopped'),
            ('README.md', '## 第一次使用', '已連線', '持倉為零且沒有掛單',
             '使用 NinjaTrader 原生工具取消 Leader 掛單並平掉 Leader 持倉',
             '只有在工作階段已解除連接後', '僅在停止時新增 Follower'),
            ('README.zh-HK.md', '## 第一次使用', '已連線', '持倉為零且沒有掛單',
             '使用 NinjaTrader 原生工具取消 Leader 掛單並平掉 Leader 持倉',
             '只有在工作階段已解除連接後', '僅在停止時新增 Follower'),
            ('README.ja.md', '## 初回の使い方', '接続済み', 'ポジションはゼロで、未約定の注文はありません',
             'NinjaTrader の標準機能で Leader の未約定注文を取り消し、Leader のポジションを決済してから',
             'セッションの接続が解除された後にのみ', '停止中にのみフォロワーを追加'),
            ('README.ko.md', '## 처음 사용', '연결됨', '포지션은 0이고 미체결 주문은 없습니다',
             'NinjaTrader 기본 화면에서 Leader의 미체결 주문을 취소하고 Leader 포지션을 청산한 뒤',
             '세션 연결이 해제된 뒤에만', '중지 상태에서만 팔로워를 추가'),
        ):
            text = (ROOT / name).read_text(encoding='utf-8')
            first_run = text.split(heading, 1)[1].split('![', 1)[0]
            for term in (connected, clean, cleanup, detached, scope):
                self.assertIn(term, first_run, name)
            self.assertLess(first_run.index(cleanup), first_run.index('**Stop**'), name)
            self.assertLess(first_run.index(detached), first_run.index(scope), name)
            self.assertIn('Start', first_run[first_run.index(scope):], name)

    def test_readme_allowlist_reload_contract(self):
        for name, terms in (
            ('README.md', ('Create the directory first', 'does not create this directory',
                           'static initialization', 'restart NinjaTrader', 'Pressing F5 alone',
                           '先建立資料夾', '不會建立此資料夾', '靜態初始化', '重新啟動 NinjaTrader', '僅按 F5')),
            ('README.en.md', ('Create the directory first', 'does not create this directory',
                              'static initialization', 'restart NinjaTrader', 'Pressing F5 alone')),
            ('README.zh-HK.md', ('先建立資料夾', '不會建立此資料夾',
                               '靜態初始化', '重新啟動 NinjaTrader', '僅按 F5')),
            ('README.ja.md', ('先にフォルダーを作成', 'このフォルダーは作成しません',
                              '静的初期化', 'NinjaTrader を再起動', 'F5 だけでは')),
            ('README.ko.md', ('먼저 폴더를 만드십시오', '이 폴더를 만들지 않습니다',
                              '정적 초기화', 'NinjaTrader를 다시 시작', 'F5만 누르는 것')),
        ):
            text = (ROOT / name).read_text(encoding='utf-8')
            for term in terms:
                self.assertIn(term, text, name)

    def test_historical_vm_evidence_uses_nonidentifying_aliases(self):
        text = (ROOT / 'docs' / 'VM-PROGRESS.md').read_text(encoding='utf-8')
        self.assertNotRegex(text, r'(?:…|\.\.\.)\d{4,}')
        self.assertIn('Follower A', text)
        self.assertIn('Follower B', text)

    def test_current_candidate_is_not_historical_acceptance(self):
        for name, terms in (
            ('README.md', ('current source is not accepted', 'historical checklist', 'not a release-readiness percentage',
                           '目前源碼尚未驗收', '歷史檢查清單', '並非發佈就緒百分比')),
            ('README.en.md', ('current source is not accepted', 'historical checklist', 'not a release-readiness percentage')),
            ('README.zh-HK.md', ('目前源碼尚未驗收', '歷史檢查清單', '並非發佈就緒百分比')),
            ('README.ja.md', ('現在のソースは受け入れられていません', '過去のチェックリスト', 'リリース準備の割合ではありません')),
            ('README.ko.md', ('현재 소스는 수용되지 않았습니다', '과거 체크리스트', '릴리스 준비 비율이 아닙니다')),
        ):
            text = (ROOT / name).read_text(encoding='utf-8')
            for term in terms:
                self.assertIn(term, text, name)
        self.assertNotIn('成就', (ROOT / 'README.md').read_text(encoding='utf-8'))
        self.assertNotIn('成就', (ROOT / 'README.zh-HK.md').read_text(encoding='utf-8'))
        for name in ('README.md', 'README.zh-HK.md'):
            text = (ROOT / name).read_text(encoding='utf-8')
            for token in ('唔', '嘅', '咗', '冇', '喺', '哋', '嚟', '咩', '嗰'):
                self.assertNotIn(token, text, '%s contains colloquial %s' % (name, token))
        landing = (ROOT / 'README.md').read_text(encoding='utf-8')
        self.assertLess(landing.index('## 可以用嗎？'), landing.index('## Can I use it?'))
        self.assertLess(landing.index('### 進度：'), landing.index('### Progress:'))
        self.assertLess(landing.index('## 第一次使用'), landing.index('## First run'))
        for link in ('README.zh-HK.md', 'README.en.md', 'README.ja.md', 'README.ko.md'):
            self.assertIn('](%s)' % link, landing)
        for name in ('DEVELOPERS.md', 'DEVELOPERS.zh-HK.md'):
            text = (ROOT / 'docs' / name).read_text(encoding='utf-8')
            for term in ('d70b8a49ceeb87e2207fe5c9811203723ea7f0edae90d18ebeab334a10e210a5',
                         '20dc6707c9e6', 'ExplicitManualFlatReconciliationAvailable',
                         'AcknowledgeFlattenConfirmed', '#22', '#9'):
                self.assertIn(term, text, name)

if __name__ == '__main__':
    unittest.main(verbosity=2)
