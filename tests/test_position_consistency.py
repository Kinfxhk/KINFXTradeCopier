"""Read-only position-consistency guards. Stdlib only. Not trading acceptance."""
import json
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "src/AddOns/KINFXTradeCopier.cs"
PROGRESS = ROOT / "docs/PROGRESS.json"
STRINGS = ROOT / "i18n/strings.json"


def mask_noncode(text):
    pattern = r'//[^\n]*|/\*[\s\S]*?\*/|@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\''
    return re.sub(pattern, lambda m: "".join("\n" if c == "\n" else " " for c in m[0]), text)


def method(text, signature):
    code = mask_noncode(text)
    matches = list(re.finditer(re.escape(signature), code))
    if len(matches) != 1:
        raise AssertionError("expected one declaration: " + signature)
    start = matches[0].start()
    opening = code.index("{", matches[0].end())
    depth = 1
    for end in range(opening + 1, len(code)):
        depth += (code[end] == "{") - (code[end] == "}")
        if not depth:
            return text[start:end + 1]
    raise AssertionError("unclosed method: " + signature)


class PositionConsistencyIsolationTests(unittest.TestCase):
    def setUp(self):
        self.raw = SOURCE.read_text(encoding="utf-8")
        self.code = mask_noncode(self.raw)

    def test_order_path_does_not_call_the_helper(self):
        process = method(self.raw, "void Process(OrderSnapshot s)")
        submit = method(self.raw, "void Submit(CopyLink link,string errPrefix=")
        self.assertNotIn("PositionConsistency", process)
        self.assertNotIn("PositionConsistency", submit)
        self.assertNotIn("DisplayPrefs", process)
        self.assertNotIn("DisplayPrefs", submit)
        self.assertEqual(len(re.findall(r"\bengine\.Start\s*\(", self.code)), 1)
        self.assertEqual(len(re.findall(r"\bengine\.Flatten\s*\(", self.code)), 1)
        # Start and ResumeEntries (S1) are the only two entry switches.
        self.assertEqual(len(re.findall(r"\bentries\s*=\s*true\b", self.code)), 2)

    def test_helper_has_no_order_verbs(self):
        body = method(self.raw, "public static PositionConsistencyResult Evaluate(PositionSnap leader,PositionSnap follower,double tickSize,int thresholdTicks)")
        # The four-argument overload delegates to the clock overload; neither one trades.
        self.assertNotRegex(body, r"\b(Submit|Flatten|Cancel|Change|BlockEntries)\s*\(")
        clock = method(self.raw, "public static PositionConsistencyResult Evaluate(PositionSnap leader,PositionSnap follower,double tickSize,int thresholdTicks,DateTime nowUtc,TimeSpan grace)")
        self.assertNotRegex(clock, r"\b(Submit|Flatten|Cancel|Change|BlockEntries)\s*\(")

    def test_safety_gates_and_strings_stay_display_only(self):
        progress = json.loads(PROGRESS.read_text(encoding="utf-8"))
        gates = {item["id"]: item["status"] for item in progress["safety_gates"]}
        self.assertEqual({"issue-3": "open", "issue-4": "open", "issue-7": "open", "issue-9": "open"}, gates)
        rows = json.loads(STRINGS.read_text(encoding="utf-8"))
        disclaimer = next(row for row in rows if row["en"].startswith("Same side/qty does not mean same fill price"))
        self.assertIn("不會自動交易", disclaimer["zh"])
        self.assertIn("no auto trade", disclaimer["en"])
        self.assertNotIn('"ready": true', PROGRESS.read_text(encoding="utf-8"))
