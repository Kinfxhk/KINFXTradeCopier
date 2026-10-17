"""Engine-bytes guard for UI-only work.

The order path lives between the end of the PURE region and the AccountRow class. UI work must not change one byte of it.
Usage: python scripts/engine_guard.py            -> prints the SHA-256 of that span
       python scripts/engine_guard.py --check H  -> exit 1 unless the span hashes to H
"""
import hashlib, sys
from pathlib import Path
SRC = Path(__file__).resolve().parents[1] / 'src' / 'AddOns' / 'KINFXTradeCopier.cs'
t = SRC.read_bytes().decode('utf-8-sig').replace('\r\n', '\n')
a = t.index('// END KINFX PURE\n')
b = t.index('    public sealed class AccountRow')
h = hashlib.sha256(t[a:b].encode('utf-8')).hexdigest()
if '--check' in sys.argv:
    want = sys.argv[sys.argv.index('--check') + 1]
    print('ENGINE_OK' if h == want else 'ENGINE_CHANGED ' + h)
    sys.exit(0 if h == want else 1)
print(h)
