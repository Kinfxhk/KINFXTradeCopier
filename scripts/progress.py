#!/usr/bin/env python3
"""Compute the readiness progress bars from docs/PROGRESS.json and keep the public READMEs in sync.

  python scripts/progress.py           rewrite the marked blocks
  python scripts/progress.py --check   exit 1 if a README block is stale (used by the portable tests)
  python scripts/progress.py --print   print the computed numbers only

README.md (landing page) is Traditional Chinese (Hong Kong written Chinese) only; README.zh-HK.md is the same text
kept for old links. README.en.md, README.ja.md and README.ko.md are one language each.
Those files match the in-app codes en / zh / ja / ko (zh stays README.zh-HK.md).

Scoring is deliberately conservative and mechanical: an item earns its weight only at the evidence level it has
reached (see "credit" in PROGRESS.json). Offline fake-account tests are worth half; only a run on real NinjaTrader
Simulator / prop-firm Sim accounts is worth the full weight. 100% on the Demo bar would still not be a release:
it would mean the Demo checklist is complete, and the release hold is lifted separately by a reviewed decision.
"""
import json
import math
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
DATA = ROOT / 'docs' / 'PROGRESS.json'
START, END = '<!-- PROGRESS:START -->', '<!-- PROGRESS:END -->'
CREDIT = {int(k): v["credit"] for k, v in json.loads((ROOT / "docs" / "PROGRESS.json").read_text(encoding="utf-8"))["levels"].items()}
WIDTH = 30
SUPPORTED_TARGETS = ('demo', 'prop')
# Acceptance inventory is pinned in reviewed code: deleting/retargeting a JSON
# record must not silently retire a safety gate. Changing this is a policy edit.
REQUIRED_SAFETY_GATES = frozenset(('issue-3', 'issue-4', 'issue-7', 'issue-9'))
REQUIRED_DEMO_ITEMS = frozenset((
    'copy_core', 'ratio_cap', 'exit_nolock', 'flat_one', 'flat_all',
    'protect_tpsl', 'locks', 'disconnect', 'partial_fill', 'frontier',
    'isolation', 'scale_real', 'risk_limits', 'package',
))
REQUIRED_PROP_ITEMS = REQUIRED_DEMO_ITEMS | {'firm_rules'}


def load():
    return json.loads(DATA.read_text(encoding='utf-8'))


def safety_blockers(data, target):
    """Explicit acceptance records are required; a GitHub closure is not proof."""
    if target not in SUPPORTED_TARGETS:
        raise ValueError('Unsupported readiness target: %r' % (target,))
    gates = data.get('safety_gates')
    if not isinstance(gates, list):
        return ['safety-gate metadata missing or invalid']
    blockers, seen = [], set()
    required_targets = {target, 'demo'} if target == 'prop' else {target}
    for gate in gates:
        if not isinstance(gate, dict):
            return ['invalid safety-gate record']
        identifier, targets = gate.get('id'), gate.get('targets')
        status, evidence = gate.get('status'), gate.get('evidence')
        if (not isinstance(identifier, str) or not identifier.strip() or identifier in seen
                or not isinstance(targets, list) or not targets
                or any(t not in ('demo', 'prop') for t in targets)
                or status not in ('open', 'accepted')
                or not isinstance(evidence, str) or not evidence.strip()):
            return ['invalid safety-gate record']
        if identifier in REQUIRED_SAFETY_GATES and set(targets) != set(SUPPORTED_TARGETS):
            return ['required safety-gate target coverage missing: ' + identifier]
        seen.add(identifier)
        if required_targets.intersection(targets) and status != 'accepted':
            blockers.append(identifier)
    blockers.extend('required safety gate missing: ' + identifier
                    for identifier in sorted(REQUIRED_SAFETY_GATES - seen))
    return blockers


def checklist_blockers(data, target):
    """Retiring a must-have requires changing the reviewed policy inventory."""
    if target not in SUPPORTED_TARGETS:
        raise ValueError('Unsupported readiness target: %r' % (target,))
    items = data.get('items')
    if not isinstance(items, list):
        return ['checklist metadata missing or invalid']
    records = {}
    for item in items:
        if not isinstance(item, dict):
            return ['invalid checklist record']
        identifier, targets = item.get('id'), item.get('targets')
        weight, evidence = item.get('weight'), item.get('evidence')
        if (not isinstance(identifier, str) or not identifier.strip() or identifier in records
                or not isinstance(targets, list) or not targets
                or any(t not in SUPPORTED_TARGETS for t in targets)
                or type(item.get('must')) is not bool
                or type(item.get('level')) is not int or item['level'] not in CREDIT
                or not isinstance(weight, (int, float)) or isinstance(weight, bool)
                or not math.isfinite(weight) or weight <= 0
                or not isinstance(evidence, str) or not evidence.strip()):
            return ['invalid checklist record']
        records[identifier] = item
    required = REQUIRED_PROP_ITEMS if target == 'prop' else REQUIRED_DEMO_ITEMS
    blockers = []
    for identifier in sorted(required):
        item = records.get(identifier)
        coverage = {'demo', 'prop'} if identifier in REQUIRED_DEMO_ITEMS else {'prop'}
        if item is None or item['must'] is not True or not coverage.issubset(item['targets']):
            blockers.append('required checklist item missing or downgraded: ' + identifier)
    return blockers


def score(data, target):
    if target not in SUPPORTED_TARGETS:
        raise ValueError('Unsupported readiness target: %r' % (target,))
    gates = safety_blockers(data, target) + checklist_blockers(data, target)
    if 'invalid checklist record' in gates or 'checklist metadata missing or invalid' in gates:
        return {'items': [], 'percent': 0.0, 'must_total': 0, 'must_done': 0,
                'blockers': [], 'safety_blockers': gates, 'ready': False}
    items = [i for i in data['items'] if target in i['targets']]
    total = sum(i['weight'] for i in items)
    earned = sum(i['weight'] * CREDIT[i['level']] for i in items)
    musts = [i for i in items if i['must']]
    blockers = [i for i in musts if i['level'] < 3]
    demo_missing = target == 'prop' and any(i['must'] and i['level'] < 3
                                          for i in data['items'] if 'demo' in i['targets'])
    return {
        'items': items,
        'percent': 100.0 * earned / total if total else 0.0,
        'must_total': len(musts),
        'must_done': len(musts) - len(blockers),
        'blockers': blockers,
        'safety_blockers': gates,
        'ready': not blockers and not gates and not demo_missing,
    }


def bar(percent):
    filled = int(round(WIDTH * percent / 100.0))
    return '█' * filled + '░' * (WIDTH - filled)


def by_group(items):
    order, groups = [], {}
    for i in items:
        if i['group'] not in groups:
            groups[i['group']] = []
            order.append(i['group'])
        groups[i['group']].append(i)
    return [(g, groups[g]) for g in order]


ICON = {0: '⬜', 1: '⬜', 2: '🟡', 3: '✅'}

# Landing page is Chinese only (Chinese first, user request 2026-10-15). Other files are one UI language each.
# In-app Chinese is code "zh"; the public file remains README.zh-HK.md.
READMES = (
    ('README.md', ('zh',)),
    ('README.en.md', ('en',)),
    ('README.zh-HK.md', ('zh',)),
    ('README.ja.md', ('ja',)),
    ('README.ko.md', ('ko',)),
)

TEXT = {
    'en': {
        'heading': 'Progress: Demo / Prop-firm copying',
        'legend': '✅ tested on real NinjaTrader · 🟡 offline tests only · ⬜ not yet. Must-haves ✅: Demo %d/%d · Prop %d/%d.',
        'disclaimer': 'Historical checklist evidence, not acceptance of the current candidate. The current candidate is not accepted for trading. Safety P0 / acceptance gates: %s. Demo ready=%s; Prop ready=%s. No funded/live acceptance.',
        'none': 'none open',
        'optional': ' _(optional)_',
        'ach': 'ach_en',
        'footer': 'Real tests so far: 1 simulator leader, up to 11 **simulated** prop-firm evaluation accounts (5 Lucid, 6 Tradeify), MNQ, Market + Limit + Stop, TP/SL OCO. No funded or live account was ever used. Each tick and its limits: [`docs/PROGRESS.json`](docs/PROGRESS.json). Score = weighted: ✅ 100%%, 🟡 50%%, design only 10%%. Snapshot %s, source `%s`.',
    },
    'zh': {
        'heading': '進度：Demo／Prop Firm 跟單完成度',
        'legend': '✅ 已在真實 NinjaTrader 測試 · 🟡 只有離線測試 · ⬜ 尚未完成。必要項目 ✅：Demo %d/%d · Prop %d/%d。',
        'disclaimer': '以下為歷史清單證據，不代表目前候選版本已通過驗收。目前候選版本尚未通過驗收，不可宣稱可用。安全 P0／驗收關卡：%s。Demo ready=%s；Prop ready=%s。不包括 funded／實盤驗收。',
        'none': '無未完成關卡',
        'optional': ' _（可選）_',
        'ach': 'ach_zh',
        'footer': '歷史真實測試：1 個模擬 Leader、最多 11 個**模擬** Prop Firm 評估帳戶（Lucid 5 個、Tradeify 6 個）、MNQ、市價＋限價＋停損單、TP/SL OCO。從未使用 funded 或實盤帳戶。各項詳情與限制：[`docs/PROGRESS.json`](docs/PROGRESS.json)。計分按權重：✅ 100%%、🟡 50%%、只有設計 10%%。快照 %s，源碼 `%s`。',
    },
    'ja': {
        'heading': '進捗：Demo／Prop Firm コピー',
        'legend': '✅ 実際の NinjaTrader で検証済み · 🟡 オフラインテストのみ · ⬜ 未実施。必須項目 ✅：Demo %d/%d · Prop %d/%d。',
        'disclaimer': '過去のチェックリストの証拠であり、現在の候補を受け入れたものではありません。現在の候補は取引について受け入れられていません。安全 P0／受入ゲート：%s。Demo ready=%s；Prop ready=%s。funded／ライブの受入はありません。',
        'none': '未完了の関門なし',
        'optional': ' _（任意）_',
        'ach': 'ach_ja',
        'footer': 'これまでの実機テスト：シミュレーターのリーダー 1、最大 11 の**シミュレーション** Prop Firm 評価口座（Lucid 5、Tradeify 6）、MNQ、成行＋指値＋逆指値、TP/SL OCO。funded またはライブ口座は一度も使用していません。各項目と制限：[`docs/PROGRESS.json`](docs/PROGRESS.json)。配点は加重：✅ 100%%、🟡 50%%、設計のみ 10%%。スナップショット %s、ソース `%s`。',
    },
    'ko': {
        'heading': '진행도: Demo／Prop Firm 카피',
        'legend': '✅ 실제 NinjaTrader에서 검증함 · 🟡 오프라인 테스트만 · ⬜ 아직 아님. 필수 항목 ✅: Demo %d/%d · Prop %d/%d.',
        'disclaimer': '과거 체크리스트의 증거이며, 현재 후보를 수용한 것이 아닙니다. 현재 후보는 거래용으로 수용되지 않았습니다. 안전 P0／수용 관문: %s. Demo ready=%s; Prop ready=%s. funded／실계좌 수용은 없습니다.',
        'none': '미완료 관문 없음',
        'optional': ' _（선택）_',
        'ach': 'ach_ko',
        'footer': '지금까지의 실제 테스트: 시뮬레이터 리더 1개, 최대 11개의 **시뮬레이션** Prop Firm 평가 계좌(Lucid 5개, Tradeify 6개), MNQ, 시장가＋지정가＋역지정가, TP/SL OCO. funded 또는 실계좌는 사용한 적이 없습니다. 각 항목과 제한: [`docs/PROGRESS.json`](docs/PROGRESS.json). 배점은 가중치: ✅ 100%%, 🟡 50%%, 설계만 10%%. 스냅샷 %s, 소스 `%s`.',
    },
}


def render(data, lang):
    if lang not in TEXT:
        raise ValueError('Unsupported README language: %r' % (lang,))
    t = TEXT[lang]
    snap = data['snapshot']
    d, p = score(data, 'demo'), score(data, 'prop')
    gates = d['safety_blockers'] + [g for g in p['safety_blockers'] if g not in d['safety_blockers']]
    out = []
    out.append('### %s' % t['heading'])
    out.append('')
    out.append('```text')
    out.append('%-12s %s %5.1f%%' % ('Demo', bar(d['percent']), d['percent']))
    out.append('%-12s %s %5.1f%%' % ('Prop firm', bar(p['percent']), p['percent']))
    out.append('```')
    out.append('')
    out.append(t['legend'] % (d['must_done'], d['must_total'], p['must_done'], p['must_total']))
    out.append('')
    out.append(t['disclaimer'] % (', '.join(gates) or t['none'], d['ready'], p['ready']))
    out.append('')
    for group, items in by_group(data['items']):
        for i in items:
            label = i.get(t['ach'])
            if not isinstance(label, str) or not label.strip():
                raise ValueError('missing %s for %s' % (t['ach'], i.get('id')))
            tag = t['optional'] if not i['must'] else ''
            out.append('- %s %s%s' % (ICON[i['level']], label, tag))
    out.append('')
    out.append(t['footer'] % (snap['date'], snap['source_sha256_prefix']))
    return '\n'.join(out)


def render_langs(data, langs):
    return '\n\n'.join(render(data, lang) for lang in langs)


QUICK_PERCENT = re.compile(
    r'(?:About |完成度約 |完了度は約 |완료도는 약 )\*\*(\d+)%\*\*'
)


def quick_percentages(text):
    return [int(n) for n in QUICK_PERCENT.findall(text)]


def splice(text, block):
    if START not in text or END not in text:
        raise SystemExit('missing %s / %s markers' % (START, END))
    head, rest = text.split(START, 1)
    _, tail = rest.split(END, 1)
    return head + START + '\n' + block + '\n' + END + tail


def main(argv):
    data = load()
    for key in ('demo', 'prop'):
        s = score(data, key)
        print('%s %5.1f%%  must %d/%d  ready=%s' % (key, s['percent'], s['must_done'], s['must_total'], s['ready']))
    if '--print' in argv:
        return 0
    stale = []
    want = [int(round(score(data, 'demo')['percent'])), int(round(score(data, 'prop')['percent']))]
    for name, langs in READMES:
        path = ROOT / name
        raw = path.read_bytes()
        crlf = b'\r\n' in raw
        text = raw.decode('utf-8-sig').replace('\r\n', '\n')
        new = splice(text, render_langs(data, langs))
        found = quick_percentages(text)
        expect = want * len(langs)
        if found != expect:
            print('QUICKSTART percentages %s != computed %s in %s' % (found, expect, name))
            stale.append(name + ' (quickstart %)')
        if new != text:
            stale.append(name)
            if '--check' not in argv:
                if crlf:
                    new = new.replace('\n', '\r\n')
                path.write_bytes(new.encode('utf-8'))
                print('updated', name)
    if '--check' in argv and stale:
        print('STALE: ' + ', '.join(stale) + ' (run python scripts/progress.py)')
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
