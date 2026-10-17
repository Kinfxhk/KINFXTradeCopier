#!/bin/bash
# Usage: tools/runui.sh   (run from the repo root of a worktree)
# Compiles src + tests/BulkUiTests.cs + every tests/UiTestsWp*.cs into a WPF test dll and runs it on an STA thread.
# Prints one PASS/FAIL line per case; last line "UI_TOTAL pass=N fail=M". Exit 0 only when fail=0.
set -u
REPO="$(pwd -W 2>/dev/null || pwd)"
OUT="${KTC_UI_OUT:-C:/ktcart/ui-$(basename "$REPO")}"
mkdir -p "$OUT"
cp -f C:/ktcart/ui/launch.exe C:/ktcart/ui/references.txt "$OUT/"
{ echo "$REPO/src/AddOns/KINFXTradeCopier.cs"; echo "$REPO/tests/BulkUiTests.cs"; for f in "$REPO"/tests/UiTestsWp*.cs; do [ -f "$f" ] && echo "$f"; done; } > "$OUT/sources.txt"
cd "$OUT"
H=$(ls -t C:/ktcart/.artifacts/*/CompilerHost.exe | head -1)
rm -f bulkui.dll compiler-diagnostics.txt
"$H" "C:/Program Files/NinjaTrader 8/bin" fullsim sources.txt references.txt bulkui.dll > host.txt 2>&1
if [ ! -f bulkui.dll ] || grep -q " error CS" compiler-diagnostics.txt 2>/dev/null; then echo COMPILE_FAILED; cat host.txt | head -5; grep -i " error " compiler-diagnostics.txt | head -25; exit 2; fi
timeout 280 ./launch.exe "$(pwd -W)/bulkui.dll" > ui-out.txt 2>&1
cat ui-out.txt
P=$(grep -c '^PASS' ui-out.txt); F=$(grep -c '^FAIL' ui-out.txt)
echo "UI_TOTAL pass=$P fail=$F"
[ "$F" = "0" ] && [ "$P" -gt 0 ] && ! grep -q "BadImageFormat\|HARNESS" ui-out.txt
