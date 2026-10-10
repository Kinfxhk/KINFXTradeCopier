# Public alpha safety hold

**Trading is DISABLED for ALL providers, including Sim / Simulator, Playback,
and live accounts.** This is a collaborative experimental alpha, not a trading
release, provider acceptance result, or certified firewall. Do not install this
source in NinjaTrader to trade. No binary release or live/simulation order test
is authorized by this proof.

## Direct compilation boundary

`CopierEngine.Start` and `CopierEngine.Flatten` begin with an unconditional
assembly-object identity comparison:

```csharp
if(typeof(Account).Assembly!=typeof(CopierEngine).Assembly)
```

If `Account` is external to the copier target, both methods call the existing
`BlockEntries`, log the hold, and return **before** the preprocessor branch,
argument enumeration, work scheduling, subscriptions, or account operations.
A real NT `Account` is external to the custom copier assembly, so defining
`KINFX_OFFLINE_FAKE` in a direct compiler/editor build does not remove this hold.
Assembly names, provider names, account labels, configuration files and runtime
flags do not grant permission. No real-execution override is provided.

Only an intentionally synthetic `Account` declared in the **same artifact** can
pass this first comparison. It still cannot reach the offline branch unless
`KINFX_OFFLINE_FAKE` is defined. Without that symbol, the existing all-account
hold remains. A separately compiled harmless stub is rejected by production
exactly as any other external `Account` is; it is permitted in the **test
metadata auditor only** to exercise this denial. It never grants production
permission.

This boundary is not an adversarial security sandbox. Modified source, forged
local types, reflection/private-state mutation, hostile callbacks, and unrelated
NinjaTrader/manual strategies are outside this claim. The alpha does not recall
existing broker work, certify OCO/fill ordering, enforce prop-firm rules, or fix
the engine's known lifecycle/reconciliation problems. Existing retained engine
code and simulation-only strings inside its offline branch are not public
trading permission.

## What was actually tested

`tests/test_production_hold.py` uses only Python standard-library `unittest`.

### Portable source checks — limited lexical/syntax evidence

```text
python -B -m unittest discover -s tests -p test_production_hold.py -v
```

These checks locate the balanced Start/Flatten bodies while masking comments
and strings, require the unconditional guard as the first executable statement
outside directives, retain the no-symbol holds, and inspect the existing UI
command holds. They also inventory the current `entries=true`, `attached=true`,
`engine.Start` and `engine.Flatten` occurrences, and check constructor, restore,
settings import and timer paths for automatic activation.

They are **not a C# AST audit**, whole-program reachability proof, or runtime
behavioral tests. They require no compiler/dependencies and are suitable for
Linux CI; the actual execution reported here ran on Windows. The optional
Framework test is explicitly skipped unless its prerequisites are supplied.

### Optional actual Framework entry behavior

On Windows, provide `KINFX_HOLD_CSC` pointing to the installed Framework
`v4.0.30319/csc.exe` and `KINFX_HOLD_SCRATCH` pointing to an existing, explicitly
chosen scratch directory, then run the same unittest command. These are
**test-harness inputs only**, not production bypass flags. `KINFX_HOLD_SOURCE`
can optionally select an immutable source snapshot for reproduction; normal CI
reads the repository source.

Every run creates a unique child directory, snapshots the selected source,
compiles fresh representative methods, records compiler/audit/runtime logs and
artifact SHA-256 hashes, and confines temporary compiler output to that child.
Do not supply real NT assemblies or a prebuilt account DLL. The test generates
its own empty `NinjaTrader.Cbi.Account` declaration, either in the target or in
`HarmlessAccountStub.dll`; it has no account properties, events or broker API.
Only Framework `mscorlib` is referenced, plus that freshly built stub in external
identity cases. No NT/native DLL is loaded or executed.

The extracted representative method preserves the **actual production method
signature, exact entry statements, unconditional guard, no-symbol hold and
conditional directives**. The rest of the fake-only engine branch is deliberately
replaced with `reached++`. The exact production `BlockEntries` method is also
extracted. This safer seam proves entry denial/admission without constructing a
native Window/NTTab, running an engine task closure, or invoking any Account API.
It does **not** execute the complete Start/Flatten bodies or certify the engine.

Before each representative executable runs, a Framework-only metadata/IL
auditor reflection-only inspects the target and intentionally harmless stub.
It checks all assembly references and resolved IL member operands against the
exact permitted artifacts and `mscorlib`, requires an empty Account stub,
rejects Account member operands, native/UI bases, P/Invoke/native/internal-call
methods, and indirect IL signatures. The stub itself is preflighted before the
matrix. This is a bounded fixture audit, not a general certified firewall.

The actual matrix contains 16 runtime cases: Start/Flatten × symbol off/on ×
same/external assembly identity × normal/throwing harmless logger. Arguments
are null deliberately: external denial must occur before touching/enumerating
arguments. A logger failure must still leave entries blocked and cannot fall
through into the fake branch. Assertions independently check assembly identity,
reach counter, blocked entries, existing control generation and log count.

| Account identity | Fake symbol | Expected representative entry |
| --- | --- | --- |
| Same artifact, synthetic | Off | Blocked |
| External, harmless test stub | Off | Blocked |
| Same artifact, synthetic | On | Offline spy reached; never a broker API |
| External, harmless test stub | On | Blocked |

Full-source compile-only verification is a separate tool-builder responsibility;
this test does not load or execute a full NT-linked artifact. Offline passing
results do not constitute provider/runtime or trading acceptance.

## RED → GREEN evidence

Immutable public-source baseline SHA-256:

```text
1def30ce3d522393a8daa0ebb829e7630ab6b9b2d66549d3ea5e6309436420c1
```

Minimal held-source SHA-256:

```text
32de3089ca3e4ab7f41e94a19267c6115493dd9560cf6afa23e57508d4fa3be8
```

The unchanged final test fixture used for the product RED and GREEN runs:

```text
8e314d0e20a0545475fa5119adba68861db1aa3f9411cd15f1696ed60cc205ad
```

Before the source change, actual external/symbol-on Start and Flatten entries
reached the spy instead of blocking (four behavioral failures, including logger
variants). The two source guard checks also failed:

```text
RED exit=1
FAIL method=Start define=true external=true throwing=false reached=1 entries=True logged=0 generation=0
FAIL method=Flatten define=true external=true throwing=false reached=1 entries=True logged=0 generation=0
Ran 4 tests in 2.149s
FAILED (failures=6)
warnings=0 errors=0
```

After only the two entry guard insertions, all 16 actual behavior cases passed:

```text
GREEN exit=0
PASS method=Start define=true external=true throwing=false reached=0 entries=False logged=1 generation=1
PASS method=Flatten define=true external=true throwing=false reached=0 entries=False logged=1 generation=1
Ran 4 tests in 2.039s
OK
warnings=0 errors=0
```

Here unittest counts four test methods; the behavioral method contains 16
subcases, and the source guard method contains two method-entry subcases. Do not
call six RED assertions six independent defects.

Earlier harness attempts encountered compiler-output decoding and a missing
Framework `HashSet` reference, plus an overbroad syntax match on `timer.Start`.
They were retained as harness errors, not product RED proof. The corrected
fixture then reproduced the real baseline failure before any production edit;
its SHA-256 was unchanged for GREEN. No engine bug fixes/candidate integration,
installation, GUI/account execution, or original/installed source writes were
performed. The source modification adds seven LF lines only and preserves all
other original bytes. Original and installed source hashes were checked again
unchanged separately from this public-source proof.

## Later source revisions (hold unchanged)

`32de3089…` above is the hold-hardening revision and the base for the recorded RED/GREEN runs. Later UI-only commits change the source hash but not the hold:

- Bulk Tgt / DLL / ATM bar and one-shot **From Leader** copy on the Copy tab (`BulkApply`, `BulkFromLeader`, pure `BulkEdit.TryParseAmount`): SHA-256 `d84823a630121bc343c9f5c384eb58fe0a94b3e3f5162e331172dbe1ac24d756`.

The bulk bar edits highlighted follower rows only, validates before mutating, never touches Ratio, the follow tick, latches or any engine path, and is disabled while attached. Start / Flatten / the old `batch` dialog remain held (`tests/test_production_hold.py` and `BulkUiTests.cs` `NeverTouchesFollowOrTrading`).

- Settings notes (`SettingsAdvice`, advisory only, never blocks), Logs readability/Clear-log button, Ratio-cell width test. Same source hash as above.
- Tgt / DLL row-owned text drafts (`TargetText` / `LossText`): same source hash as above.
- Footer shows a red count of locked follower accounts (`LockedCount` / `LockedText`). Display only; no new action. Same source hash as above.
