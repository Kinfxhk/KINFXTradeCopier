"""Readiness is an acceptance gate, not only a weighted checklist."""
import copy
import importlib.util
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('progress_gate', ROOT / 'scripts' / 'progress.py')
assert spec is not None and spec.loader is not None
progress = importlib.util.module_from_spec(spec)
spec.loader.exec_module(progress)


class ReadinessGateTests(unittest.TestCase):
    def candidate(self):
        data = copy.deepcopy(progress.load())
        for item in data['items']:
            item['level'] = 3
        data['safety_gates'] = [
            {'id': identifier, 'status': 'accepted', 'targets': ['demo', 'prop'],
             'evidence': 'Synthetic reviewed acceptance record, never provider evidence'}
            for identifier in ('issue-3', 'issue-4', 'issue-7', 'issue-9')
        ]
        return data

    def test_required_checklist_inventory_cannot_disappear(self):
        for target in ('demo', 'prop'):
            data = self.candidate()
            required = [i['id'] for i in data['items'] if i['must'] and target in i['targets']]
            for identifier in required:
                for mutation in ('delete', 'optional', 'retarget'):
                    changed = copy.deepcopy(data)
                    item = next(i for i in changed['items'] if i['id'] == identifier)
                    if mutation == 'delete':
                        changed['items'].remove(item)
                    elif mutation == 'optional':
                        item['must'] = False
                    else:
                        item['targets'] = ['prop' if target == 'demo' else 'demo']
                    with self.subTest(target=target, identifier=identifier, mutation=mutation):
                        self.assertFalse(progress.score(changed, target)['ready'])
        data = self.candidate()
        data['items'] = []
        for target in ('demo', 'prop'):
            self.assertFalse(progress.score(data, target)['ready'])

    def test_duplicate_item_cannot_replace_inventory(self):
        data = self.candidate()
        data['items'].append(copy.deepcopy(data['items'][0]))
        self.assertFalse(progress.score(data, 'demo')['ready'])

    def test_invalid_item_fields_fail_closed(self):
        for key, value in (('must', 'true'), ('level', True), ('level', 4),
                           ('weight', float('nan')), ('weight', -1), ('evidence', ''),
                           ('targets', []), ('targets', 'demo')):
            data = self.candidate()
            data['items'][0][key] = value
            with self.subTest(key=key, value=value):
                self.assertFalse(progress.score(data, 'demo')['ready'])

    def test_required_gate_inventory_cannot_disappear(self):
        for replacement in ([], [{'id': 'unrelated', 'status': 'accepted',
                                   'targets': ['demo', 'prop'], 'evidence': 'irrelevant'}]):
            data = self.candidate()
            data['safety_gates'] = replacement
            for target in ('demo', 'prop'):
                with self.subTest(replacement=replacement, target=target):
                    self.assertFalse(progress.score(data, target)['ready'])
        for identifier in ('issue-3', 'issue-4', 'issue-7', 'issue-9'):
            data = self.candidate()
            data['safety_gates'] = [g for g in data['safety_gates'] if g['id'] != identifier]
            for target in ('demo', 'prop'):
                with self.subTest(missing=identifier, target=target):
                    self.assertFalse(progress.score(data, target)['ready'])

    def test_required_gate_target_coverage_cannot_be_narrowed(self):
        for identifier in ('issue-3', 'issue-4', 'issue-7', 'issue-9'):
            for narrowed in (['demo'], ['prop']):
                data = self.candidate()
                gate = next(g for g in data['safety_gates'] if g['id'] == identifier)
                gate['targets'] = narrowed
                for target in ('demo', 'prop'):
                    with self.subTest(identifier=identifier, narrowed=narrowed, target=target):
                        self.assertFalse(progress.score(data, target)['ready'])

    def test_unknown_score_target_is_rejected(self):
        for target in ('funded', 'deom', '', None, [], {}):
            with self.subTest(target=target):
                with self.assertRaises(ValueError):
                    progress.score(progress.load(), target)

    def test_unknown_checklist_target_is_rejected(self):
        for target in ('funded', 'deom', '', None, [], {}):
            with self.subTest(target=target):
                with self.assertRaises(ValueError):
                    progress.checklist_blockers(self.candidate(), target)

    def test_complete_reviewed_gate_inventory_can_pass(self):
        for target in ('demo', 'prop'):
            self.assertTrue(progress.score(self.candidate(), target)['ready'])

    def test_open_p0_blocks_complete_checklist(self):
        data = self.candidate()
        gate = next(g for g in data['safety_gates'] if g['id'] == 'issue-9')
        gate['status'] = 'open'
        gate['evidence'] = 'No reviewed recovery path'
        for target in ('demo', 'prop'):
            with self.subTest(target=target):
                result = progress.score(data, target)
                self.assertEqual(result['must_done'], result['must_total'])
                self.assertFalse(result['ready'], 'An open safety P0 must block readiness')

    def test_absent_gate_metadata_fails_closed(self):
        data = self.candidate()
        del data['safety_gates']
        self.assertFalse(progress.score(data, 'demo')['ready'])

    def test_accepted_gate_requires_evidence(self):
        data = self.candidate()
        gate = next(g for g in data['safety_gates'] if g['id'] == 'issue-9')
        gate['evidence'] = ''
        self.assertFalse(progress.score(data, 'demo')['ready'])
        gate['evidence'] = 'Reviewed exact-candidate acceptance record'
        self.assertTrue(progress.score(data, 'demo')['ready'])

    def test_unknown_status_or_malformed_metadata_fails_closed(self):
        for gates in (None, {}, [{'id': 'issue-9', 'status': 'closed',
                                'targets': ['demo'], 'evidence': 'GitHub closure alone'}],
                      [{'id': 'issue-9', 'status': 'accepted', 'targets': [], 'evidence': 'x'}]):
            with self.subTest(gates=gates):
                data = self.candidate()
                data['safety_gates'] = gates
                self.assertFalse(progress.score(data, 'demo')['ready'])

    def test_prop_requires_demo_acceptance(self):
        data = self.candidate()
        data['safety_gates'] = [{'id': 'issue-4', 'status': 'open',
                                 'targets': ['demo'], 'evidence': 'Threading unaccepted'}]
        self.assertFalse(progress.score(data, 'prop')['ready'])

    def test_no_open_gate_does_not_bypass_missing_must_have(self):
        data = self.candidate()
        next(item for item in data['items'] if item['must'])['level'] = 2
        self.assertFalse(progress.score(data, 'demo')['ready'])

    def test_generated_chinese_uses_written_language(self):
        text = progress.render(progress.load(), 'zh')
        for colloquial in ('冇', '唔', '同限制', '真 NT 跑', '未做'):
            with self.subTest(colloquial=colloquial):
                self.assertNotIn(colloquial, text)
        self.assertIn('歷史', text)

    def test_render_distinguishes_historical_evidence_from_acceptance(self):
        for lang in ('en', 'zh'):
            text = progress.render(progress.load(), lang)
            self.assertIn('P0', text)
            self.assertIn(progress.load()['snapshot']['source_sha256_prefix'], text)
            self.assertRegex(progress.load()['snapshot']['source_sha256_prefix'], r'^[0-9a-f]{12}$')
            self.assertIn('not accepted' if lang == 'en' else '尚未通過驗收', text)


if __name__ == '__main__':
    unittest.main()
