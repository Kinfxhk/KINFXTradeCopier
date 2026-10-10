"""Actual pure Python research kernel assertions, NOT C# engine acceptance."""
import copy
import dataclasses
import hashlib
import importlib.util
import json
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
SPEC = ROOT / 'spec'
path = SPEC / 'spec_kernel.py'
spec = importlib.util.spec_from_file_location('kinfx_public_spec', path)
kernel = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = kernel
spec.loader.exec_module(kernel)

def normalize(value):
    return json.loads(json.dumps(value, ensure_ascii=True))

def execute(test, trace):
    frozen = copy.deepcopy(trace)
    result = normalize(kernel.validate(copy.deepcopy(trace)))
    test.assertEqual(trace, frozen, 'input changed')
    if not result['accepted']:
        failure = result['failure']
        test.assertEqual(failure['before'], failure['after'])
        prefix = normalize(kernel.validate(copy.deepcopy(trace[:failure['event'] - 1])))
        test.assertTrue(prefix['accepted'])
        test.assertEqual(prefix['state'], result['state'])
        test.assertEqual(normalize(kernel.validate(copy.deepcopy(trace) + [{'tag': 'invalid-suffix'}])), result)
    return result

class ExactResearchKernel(unittest.TestCase):
    def test_kernel_bytes_match_reviewed_candidate(self):
        self.assertEqual(hashlib.sha256(path.read_bytes()).hexdigest(),
                         'f9974ad29b5ef6ba7619434a47d0b97597fc9b15f9a5c8e9c7a589176875a024')

class ReviewedSafety(unittest.TestCase):
    pass

class IndependentSafety(unittest.TestCase):
    pass

def reviewed_method(row):
    def test(self):
        result = execute(self, row['input'])
        self.assertEqual(result['accepted'], row['expected_accepted'], row['name'])
    return test

for suite, count in [('review388', 388), ('minimal4', 4)]:
    rows = json.loads((SPEC / (suite + '.json')).read_bytes())
    assert len(rows) == count
    for index, row in enumerate(rows):
        setattr(ReviewedSafety, 'test_' + suite + '_%04d' % index, reviewed_method(row))

rows = json.loads((SPEC / 'independent40.json').read_bytes())
assert len(rows) == 40

def independent_method(row):
    def test(self):
        result = execute(self, row['input'])
        self.assertEqual(result['accepted'], row['expected'], row['name'])
        if row['property'] is not None:
            self.assertEqual(result['failure']['property'], row['property'])
        checks = row['checks']
        orders = {o['id']: o for o in result['state']['orders']}
        requests = {r['id']: r for r in result['state']['requests']}
        for identity, fields in checks.get('order', {}).items():
            for key, value in fields.items():
                self.assertEqual(orders[identity][key], value)
        for identity, status in checks.get('request', {}).items():
            self.assertEqual(requests[identity]['status'], status)
        if 'blocked' in checks:
            self.assertEqual(result['state']['blocked'], checks['blocked'])
        if 'raw_hint' in checks:
            self.assertEqual(row['input'][1]['order']['accepted_stop_price'], checks['raw_hint'])
        if not result['accepted']:
            state = kernel.State()
            for event in copy.deepcopy(row['input'][:result['failure']['event'] - 1]):
                state = kernel.advance(state, kernel.decode(event))
            before = copy.deepcopy(dataclasses.asdict(state))
            with self.assertRaises(kernel.Violation) as caught:
                kernel.advance(state, kernel.decode(copy.deepcopy(row['input'][result['failure']['event'] - 1])))
            self.assertEqual(caught.exception.prop, row['property'])
            self.assertEqual(dataclasses.asdict(state), before)
            self.assertEqual(normalize(before), result['state'])
    return test

for index, row in enumerate(rows):
    setattr(IndependentSafety, 'test_independent40_%04d' % index, independent_method(row))

if __name__ == '__main__':
    unittest.main(verbosity=2)
