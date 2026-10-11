# VM progress log / VM 實測進度 (2026-10-12, updated)

Status: **SIM TEST build compiled and loaded in real NinjaTrader 8.1.8.3 (VM). No order has been sent yet.**
狀態：SIM TEST 版已喺真 NinjaTrader 編譯並載入；**未落過任何單**。

## Verified in NinjaTrader (no orders) / 已驗證（未落單）
- SIM TEST source compiles in NinjaScript Editor: no compile errors in the NT log.
- Dashboard opens; all four tabs (Dashboard / Copy / Risk / Logs) open without freezing NinjaTrader; Refresh and the bulk buttons raise no errors (pressed with nothing selected, so no settings changed).
- Header, Start tooltip and Logs note follow the build (SIM text in the SIM build, REVIEW HOLD text in the default build).
- Start / Flatten selected / Flatten all / Batch are enabled in the SIM build only.
- Default Leader is `Sim101` when no earlier choice is saved.

## Finding: prop-firm sim accounts are invisible by default / 發現
Evaluation accounts on Tradovate-based connections (e.g. Lucid, Tradeify) sit under a connection NinjaTrader records as `Mode=Live`, and `Account` has no Live/Demo field. Provider-only filtering therefore hides them.
Fix (SIM TEST build only): an operator allow-list `Documents\NinjaTrader 8\templates\KINFXTradeCopier\sim-allow.txt`, one line per account: `EXACT ACCOUNT NAME = optional alias`. Exact, case-sensitive match; empty, missing or unreadable file = Simulator / Playback only. The alias is display text only and never decides eligibility. The default build is unchanged and still blocks every account.
**Risk:** the operator attests the account is simulation. A wrong entry would let the copier send orders to a funded account.

## Added 2026-10-12 / 新增
- **Dry run** (SIM TEST build only; Risk tab checkbox, off by default): the engine decides and logs every Submit / Change / Cancel / Flatten as `DRY RUN would send to <account>: ...` and sends nothing. Fixed for the whole session (refused while attached); the Start and Flatten confirmations say so. The default build has no such member.
- **Competitor-issue replay**: `RejectedProtectiveStopLocksAndNeverResubmits` (a follower protective STOP is rejected by the exchange after the leader's stop filled, cf. a public issue on another NT8 copier). Required: lock, no resubmission, no market-order fallback, no automatic flatten. Mutants: market-fallback turns it red; removing both lock paths turns it red; removing only one lock path does not (the second path still locks), which is why the case also checks the three non-actions.
- **Verified on the VM (real .NET Framework + NT 8.1.8.3 assemblies), source ahead of `3739528`:** pure 31, puresim 31, engine 152, enginesim 173, independent 24 with 1 known FAIL (`ExplicitManualFlatReconciliationAvailable`, issue #6, unchanged). Python hold guard OK. These replace the earlier statement that C# tests were not run locally: they now run on the VM.
- Fixed a mistake in my own SIM-only tests: they did not compile, so `enginesim` had silently run 36 cases. Lesson: compare the PASS count before and after, not only the absence of FAIL.

## Not done yet / 未做
- First Leader -> Follower copy with real orders (needs a live data feed; market was closed).
- Flatten and Stop-when-flat on real sim accounts.
- ATM mirroring, disconnect/reconnect, late fills, restart recovery.
- Per-account Start summary ("N ticked / M accounts") next to Start; instrument drop-down.

## Test limits / 限制
- The author host has no C# compiler; the C# suites run in the VM (see above). Python guard tests: 11 OK (1 skipped).
- Trading in the default build remains disabled. This log does not lift the hold.
