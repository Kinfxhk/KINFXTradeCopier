# Developer and status details

Back to the [README](../README.en.md) · [繁體中文](DEVELOPERS.zh-HK.md).

## Acceptance boundary

This is an MIT **source alpha**, not a generally accepted simulation/provider release. **The current exact source is not accepted.** The default build blocks Start / Flatten for every native NT account, including Simulator / Playback. The opt-in SIM TEST build can send orders to Simulator / Playback and exact operator-allow-listed names; this capability is not provider verification or safety acceptance. **Funded/live use is prohibited.**

The front-page scores are a **weighted historical checklist**, not release-readiness percentages. Its real-NT ticks refer to the older snapshot `20dc6707c9e6`; they do not accept later engine changes. Historical [VM test records](VM-PROGRESS.md) show one simulator leader and two operator-attested simulated prop-firm followers on MNQ, Market / Limit / Stop, per-account Flat, TP/SL OCO, leader market exit and follower disconnect/reconnect. There has been real simulation testing; it is limited, not current-source acceptance.

## Current offline evidence

Source file: [`src/AddOns/KINFXTradeCopier.cs`](../src/AddOns/KINFXTradeCopier.cs), SHA-256:

```text
d70b8a49ceeb87e2207fe5c9811203723ea7f0edae90d18ebeab334a10e210a5
```

The [exact-source baseline record](BASELINE-2026-10-14.json) contains the Windows VM replay for that source:

| Suite / check | Result |
|---|---|
| Full NT reference compile / SIM compile (`compile`, `compilesim`) | Both exit 0; compile-only |
| Pure / SIM pure (`pure`, `puresim`) | 38 PASS / 0 FAIL each |
| Synthetic engine (`engine`) | 212 PASS / 0 FAIL |
| Synthetic SIM engine (`enginesim`) | 233 PASS / 0 FAIL |
| Independent review (`independent`) | 24 PASS / 1 known FAIL: `ExplicitManualFlatReconciliationAvailable` |
| WPF UI (`ui`) | 50 PASS / 0 FAIL |

These are **offline results, not real NinjaTrader/provider transaction proof**. Synthetic accounts and UI controls do not establish provider ACK ordering, cancel timing, thread affinity, real OCO atomicity, fills, recovery or funded/live safety. Counts are per-suite invocations, not independent features or completion percentages. The portable Python packaging / SPEC suite is separate and does not run the C# copier. [Build instructions](BUILD.md) · [Test matrix](TEST-MATRIX.md) · [Historical evidence](EVIDENCE.json).

## Implemented scope and remaining gates

- One Monitor / Settings / Logs / Issues dashboard designed for 10–30 accounts, per-row Ratio / Tgt / DLL / ATM / OFF / Flat, highlighted-row bulk risk edits and one-time **From Leader** defaults. Designing for 30 accounts is not real 30-account acceptance.
- Row-owned numeric drafts and Risk-dialog validation, exact XML numeric round-trip and callback-frontier close checks are present. Earlier descriptions of #5 as only partly merged, without Risk-dialog drafts or precision fixes, are stale. Native focus / IME / clipboard / DPI acceptance remains separate.
- Per-follower provider dispatch and refused-mailbox bookkeeping fixes are merged. #4 still needs real NT threading/latency and close/Dispose acceptance; an offline pass does not prove safe shutdown or calls that never return.
- Per-account Flat names the account/instrument in its confirmation. Successful confirmation requires a returned call, native flat/no working orders and the quiet interval; unconfirmed/throwing calls lock and are never retried. Historical sim runs exercised per-account Flat, not every failure path on this source.
- **PR #22 is merged.** Group ownership, fractional allocation, captured/applied frontiers and exact dispatch certificates supersede the historical #3 manual-only waiting-protection gap. An already-explicit, still-active waiting leader exit can protect its own certified disjoint lots; no missing bracket or entry is invented, cancelled/ambiguous intent cannot resume and unknown provider acceptance is never retried. Excess independent no-OCO protection is visibly refused when captured protection already covers exposure; this is not broker OCO atomicity. [Allocation contract](GROUP-ALLOCATION.md). #3 retains safety/provider-acceptance blockers.
- Divergence defaults to lock / notify / refuse unsafe continuation. `FlattenOnce` is saved-but-not-active; no automatic divergence flatten is implemented. Self-managed TP/SL, adoption and restart journal recovery are optional; the safety refusals remain mandatory.

### Issue review dispositions — proposals only

The independent review read #1–#12. The following are **proposed scope/status corrections**, not performed issue closures, label changes or acceptance decisions. Consult the source and issue history before changing the tracker; older [known-issue](KNOWN-ISSUES.md) and [offline-status](OFFLINE-STATUS.md) sections may describe superseded baselines.

| Issue | Proposed disposition / boundary |
|---|---|
| [#1](https://github.com/Kinfxhk/KINFXTradeCopier/issues/1) build / test runner | Current exact-source VM compile and offline reproduction exist; propose scoped tooling completion, not provider acceptance. |
| [#2](https://github.com/Kinfxhk/KINFXTradeCopier/issues/2) fill frontier / resize | Current integration has frontier/reservation controls and tests; do not repeat the old standalone “frontier not integrated” gap. Propose close/re-scope the implemented offline work while preserving provider ACK/acceptance limits. |
| [#3](https://github.com/Kinfxhk/KINFXTradeCopier/issues/3) group OCO | Merged #22 supersedes waiting-protection/fractional gaps; retain safety and provider-acceptance blockers. |
| [#4](https://github.com/Kinfxhk/KINFXTradeCopier/issues/4) mailbox / Dispose / isolation | Partial repairs merged; retain safety and real NT shutdown/threading/latency blockers. |
| [#5](https://github.com/Kinfxhk/KINFXTradeCopier/issues/5) numeric drafts / settings | Draft, dialog and XML fixes merged; propose scoped completion, not native keyboard/DPI or every publication contract acceptance. |
| [#6](https://github.com/Kinfxhk/KINFXTradeCopier/issues/6) journal / restart | Optional durable journal/restart work, currently shelved. Must not absorb or hide #9's mandatory in-session explicit recovery gate. |
| [#7](https://github.com/Kinfxhk/KINFXTradeCopier/issues/7) simulator acceptance | Historical limited sim runs exist; retain exact-source acceptance, abort/reconciliation and safety blockers. |
| [#8](https://github.com/Kinfxhk/KINFXTradeCopier/issues/8) native UI / DPI | WPF offline checks and i18n implemented; real native keyboard, accessibility and multi-monitor DPI remain unproved. |
| [#9](https://github.com/Kinfxhk/KINFXTradeCopier/issues/9) uncertain Flatten / recovery | Retain safety blocker: explicit verified-flat ledger reconciliation is missing; see below. Policy choice alone is not completion. |
| [#10](https://github.com/Kinfxhk/KINFXTradeCopier/issues/10) self-managed TP/SL | Optional, not implemented; ATM means leader exit mirroring, not a follower-native ATM clone. |
| [#11](https://github.com/Kinfxhk/KINFXTradeCopier/issues/11) position adoption | Optional, not implemented; Start must continue refusing existing positions/working orders. |
| [#12](https://github.com/Kinfxhk/KINFXTradeCopier/issues/12) automatic divergence flatten | Optional, stored setting only; lock/notify/safety refusal is mandatory even if automatic flatten is deferred. |

### #9: explicit recovery gate is still missing

`ExplicitManualFlatReconciliationAvailable` independently checks for `AcknowledgeFlattenConfirmed` and fails because that explicit verified-flat reconciliation path is missing. This is a current in-session safety/recovery gate, **not merely optional #6 restart journaling**. A Position0 reading, all visible orders terminal, a successful manual close, normal abandonment or a UI risk-latch reset does not prove closure of a hidden accepted request or reconcile owned execution evidence.

Ambiguous flat telemetry retains the existing protective exit. After a thrown Flatten the copier blocks new copied exits and does not retry: the request may already have been accepted. **Cost:** exposure can remain without a copied exit until native/provider investigation and verified reconciliation. Stay locked/disarmed if uncertainty persists; do not clear a latch or restart to make the ledger look clean. Native Orders / Positions are operational evidence, but not an automatic authorization to resume.

## Dashboard gallery and source

All images render the real `DashboardView` control in **synthetic DEMO** mode: **fake accounts and numbers**, no connected account, engine or orders. They are **not NinjaTrader/provider transaction evidence**.

[Dashboard / engine source](../src/AddOns/KINFXTradeCopier.cs) · [Screenshot renderer](screenshots/Screenshots.cs) · [Language table](../i18n/strings.json) · [SIM TEST installer source](../scripts/install-simtest.ps1)

Older gallery (same control, synthetic DEMO; tab layout as rendered by `Screenshots.cs`). The current four-tab tour is in the [README](../README.en.md#interface-tour).

![Monitor tab — synthetic DEMO](screenshots/1-copy.png)
![Settings tab — synthetic DEMO](screenshots/2-risk.png)
![Logs tab — synthetic DEMO](screenshots/3-logs.png)

## Contribute without trading

Python 3.10+, no extra packages, account or NT8 required:

```sh
python -B -m unittest discover -s tests -p 'test_*.py' -v
```

These portable checks cover packaging/holds and a separate Python SPEC, **not a C# engine implementation** or broker execution. Read code, reproduce synthetic failures and propose focused patches. No vendor assemblies or compiled trading binaries are provided. A source-copy SIM TEST installer exists; there is no signed/reviewed general release.

[Build](BUILD.md) · [Testing / redacted reports](COMMUNITY-TESTING.md) · [Contribution workflow](../CONTRIBUTING.md) · [Do NOT do these](DO-NOT-DO.md) · [Safety hold](SAFETY-HOLD.md) · [Simulator plan (historical plan, not authorization)](SIM-TEST-PLAN.md) · [Roadmap](ROADMAP.md).

Any future authorized SIM TEST rehearsal must follow the [README cleanup sequence](../README.en.md#first-run): connected, native-clean leader/followers; cancel leader orders and close leader via native NT **before Stop**; verify detached before changing Dry run; add followers only while stopped with a new Start confirmation. OFF is not Flatten and does not close positions. Never remove the default hold to get a green test.

## Still unproved

Current-source real provider acceptance, leader disconnect, real partial fills/rejected stop or Flatten, unknown accepted requests, full shutdown/recovery, 10–30 account real latency and multi-monitor DPI. No live-copy safety, native follower ATM or prop-firm compliance certification is established. This software cannot intercept every manual/strategy order or guarantee fills. Existing positions belong in native NT/broker tools, not an uncertain copier ledger.
