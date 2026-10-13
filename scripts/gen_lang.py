#!/usr/bin/env python3
"""Regenerate the Lang table inside src/AddOns/KINFXTradeCopier.cs from i18n/strings.json.

    python scripts/gen_lang.py          # rewrite the block between BEGIN/END LANG TABLE
    python scripts/gen_lang.py --check  # exit 1 if the source is out of date

Edit translations in i18n/strings.json only (one object per English sentence: en, zh, ja, ko).
The English text is the key. {0} {1} ... placeholders must be identical in every language.
"""
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
SRC = ROOT / 'src/AddOns/KINFXTradeCopier.cs'
DATA = ROOT / 'i18n/strings.json'
TEMPLATE = ROOT / 'i18n/Lang.template.cs'
BEGIN, END = '// BEGIN LANG TABLE', '// END LANG TABLE'
BS = chr(92)
Q = chr(34)


def lit(s):
    out = [Q]
    for ch in s:
        if ch == Q:
            out.append(BS + Q)
        elif ch == BS:
            out.append(BS + BS)
        elif ch == '\n':
            out.append(BS + 'n')
        elif ch == '\r':
            out.append(BS + 'r')
        elif ch == '\t':
            out.append(BS + 't')
        else:
            out.append(ch)
    out.append(Q)
    return ''.join(out)


def placeholders(s):
    return sorted(re.findall(r'\{\d+\}', s))


def validate(rows):
    seen = set()
    for r in rows:
        if r['en'] in seen:
            raise SystemExit('duplicate key: ' + r['en'][:60])
        seen.add(r['en'])
        for lang in ('zh', 'ja', 'ko'):
            if not r[lang].strip():
                raise SystemExit('empty %s: %s' % (lang, r['en'][:60]))
            if placeholders(r[lang]) != placeholders(r['en']):
                raise SystemExit('placeholder mismatch %s: %s' % (lang, r['en'][:60]))


# Sentences that only exist in the SIM TEST build are compiled only there, so the default build carries none of their text
# (the production-hold guard asserts that "SIM TEST:" never appears in the default build).
SIM_ONLY = re.compile(r'^(SIM TEST|DRY RUN|Dry run)|DRY RUN would send')


def render(rows):
    def line(r):
        return '            t[%s] = new[] { %s, %s, %s };' % (lit(r['en']), lit(r['zh']), lit(r['ja']), lit(r['ko']))
    common = [line(r) for r in rows if not SIM_ONLY.search(r['en'])]
    sim = [line(r) for r in rows if SIM_ONLY.search(r['en'])]
    body = common + (['#if KINFX_SIM_TEST'] + sim + ['#endif'] if sim else [])
    block = TEMPLATE.read_text(encoding='utf-8').replace('//@@ROWS@@', '\n'.join(body))
    return block.rstrip('\n') + '\n'


def main():
    rows = json.loads(DATA.read_text(encoding='utf-8'))
    validate(rows)
    block = render(rows)
    raw = SRC.read_bytes()
    bom = raw.startswith(b'\xef\xbb\xbf')
    text = raw.decode('utf-8-sig')
    crlf = '\r\n' in text
    text = text.replace('\r\n', '\n')
    if BEGIN in text and END in text:
        a = text.index(BEGIN)
        b = text.index('\n', text.index(END)) + 1
        new = text[:a] + block + text[b:]
    else:
        marker = '// END KINFX PURE\n'
        assert text.count(marker) == 1, 'PURE end marker missing'
        i = text.index(marker)
        new = text[:i] + block + text[i:]
    if crlf:
        new = new.replace('\n', '\r\n')
    data = (b'\xef\xbb\xbf' if bom else b'') + new.encode('utf-8')
    if '--check' in sys.argv:
        if data != raw:
            print('Lang table out of date; run scripts/gen_lang.py')
            sys.exit(1)
        print('Lang table up to date (%d sentences)' % len(rows))
        return
    SRC.write_bytes(data)
    print('wrote Lang table: %d sentences' % len(rows))


if __name__ == '__main__':
    main()
