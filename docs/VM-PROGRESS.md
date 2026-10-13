# VM progress log / VM 實測進度

Status: **Current integrated candidate NOT accepted for trading.** Historical real-NinjaTrader SIM transactions below belong to their stated older source hashes; the latest baseline is compilation/offline evidence only. Default trading hold remains; funded/live accounts are excluded.
狀態：**目前整合候選版本尚未通過交易驗收。** 下方真 NinjaTrader 模擬交易記錄只適用於各自標示的舊源碼；最新 baseline 為編譯／離線證據。預設交易封鎖保留，不包括 funded／實盤帳戶。

## Exact-candidate baseline — 2026-10-14 (no new orders)

Candidate commit `9bb76a5282816044bede18a51d5f608251bf5e17`; source SHA-256 `d70b8a49ceeb87e2207fe5c9811203723ea7f0edae90d18ebeab334a10e210a5`; transformed SIM source SHA-256 `cdbf9339a6647ea20aa525ea7a0406e6ddeefb1e0bea1774148cd7369c243740`.

- **Compiled outside the NT process:** default and SIM TEST against installed NT 8.1.8.3 assemblies, both exit 0. Default diagnostics: CS0649 × 9 (unassigned fields) and CS1701 × 1168 (reference identity); SIM: CS1701 × 1293. These warnings are not a count of product defects. This baseline does not establish a new F5 compile/load inside NinjaTrader.
- **Offline on the Windows VM:** pure 38/0; puresim 38/0; engine 212/0; enginesim 233/0; independent 24 PASS / 1 FAIL, zero harness errors. Known FAIL `ExplicitManualFlatReconciliationAvailable` remains visible: the explicit recovery seam is absent (#9); a decided no-retry policy is not a recovery implementation.
- **WPF offline host:** 50 PASS / 0 FAIL, exit 0. This instantiated the real Dashboard with synthetic fixture state, not a broker acceptance run.
- **Scratch installer:** six cases (preview, install, reinstall/backup, duplicate refusal, remove, missing-folder refusal) met expected exits and exact transformed source bytes. Only a fake Documents tree was used; not a clean real-NinjaTrader install/release test.
- **Real NT read-only/control observation:** Dashboard/tab state read through UIA; an existing disconnect latch was explicitly unlocked through the exact-account confirmation and the log confirmed it remained disarmed. No new orders were sent in this baseline. This is not #9 reconciliation proof.
- **Incident retained:** the first VM launcher failed because Windows PowerShell Stop preference treated normal native git stderr as terminating. Corrected git exit-code handling ran the full seven-mode baseline; the initial incident was retained separately, not counted as a product RED.

Machine-readable counts, result hashes and WPF case names: [BASELINE-2026-10-14.json](BASELINE-2026-10-14.json). Open safety/acceptance gates: #3 basic shared-OCO provider behavior, #4 real threading/lifecycle, #7 exact-candidate acceptance, #9 explicit reconciliation. Flatten selected/all, risk thresholds, rejection lock, provider timing/isolation and clean installation still need exact-candidate real-system acceptance. No progress level was raised from this baseline.

## Historical 2026-10-12 initial installation snapshot

At this early snapshot the SIM TEST build had compiled and loaded, and no order had yet been sent. That statement is historical; later transaction evidence is recorded below.

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

## Added later the same day / 同日再加
- **Start confirmation now shows the plan**: leader, instrument, and per follower its Ratio and the lots it will copy for a sample leader order of 2 (`ratio 0.3 → 0 lots (would NOT copy)`). It calls `RiskPolicy.Quantity`, the same function the engine uses, so the preview cannot disagree with behaviour. New pure case `StartSummaryListsLeaderRatioAndExpectedQuantity`. The sample size is fixed at 2 and is a preview, not a limit.
- **CodeQL** workflow for C# (`build-mode: none`, `security-extended`). It reads the source only: a hosted runner has no NinjaTrader assemblies, so it cannot compile the AddOn or run the engine suites. First run: 9 of 9 C# files scanned, 0 alerts. That is a static scan result, not evidence of engine safety.
- Offline status after these changes: Python 457 OK (2 skipped), `verify` OK, `compile` and `compilesim` OK, pure 32, puresim 32, engine 152, enginesim 173, independent 24 with the same 1 known FAIL. NinjaTrader 8.1.8.3 compiled the SIM build with no errors.

## Not done yet / 未做
- First Leader -> Follower copy with real orders (needs a live data feed; market was closed).
- Flatten and Stop-when-flat on real sim accounts.
- ATM mirroring, disconnect/reconnect, late fills, restart recovery.
- "N ticked / M accounts" next to the Start button (the footer already shows it); instrument drop-down.
- Windows CI that runs the engine suites needs NinjaTrader assemblies; not possible on a hosted runner without redistributing them.

## Test limits / 限制
- The author host has no C# compiler; the C# suites run in the VM (see above). Python guard tests: 11 OK (1 skipped).
- Trading in the default build remains disabled. This log does not lift the hold.

## Real NinjaTrader 8.1.8.3 (KinTest VM), 2026-10-13, source `28bc4bb3bc47` + `#define KINFX_SIM_TEST` / 真 NT 實測
Leader `Sim101` (simulated feed) -> Follower A (Lucid) and Follower B (Tradeify), using nonidentifying aliases; all private account-name fragments are withheld (operator-attested in `sim-allow.txt`). Ratio 1, Max 1, no Dry run (the Start dialog was read first: 2 followers, MNQZ6, no `DRY RUN`). Offline suites on the VM at the same source hash: enginesim 207/207, puresim 32/32, independent 24 PASS + 1 known FAIL (`ExplicitManualFlatReconciliationAvailable`), compilesim OK. The SIM TEST build compiled in NinjaTrader (DLL timestamp moved) and the Dashboard reloaded with a new **Flat** column.

Observed (trace `Cbi.Account.*` lines are the evidence, not the UI):
- **Buy 1 market on Sim101:** both followers submitted Buy 1 within ~0.1 s of the leader fill and filled ~0.45 s later; both followers Long 1.
- **Per-account Flat on Lucid only** (button -> confirmation text `FLAT ONE ACCOUNT. FLATTEN exactly 1 account(s), only MNQZ6 | <the Lucid account name>`, answered Yes only after the text matched): exactly one `Close` Sell 1 Market was created for Lucid, filled in ~0.25 s; Lucid went Flat; **Tradeify was untouched and stayed `Copying`**; no latch was written; Lucid read `Off`.
- **Leader Sell 1 afterwards:** only Tradeify received the copied exit (Sell 1 market, filled, Flat). Lucid got nothing, as designed. After the exit Tradeify also reads `Off` (the existing "re-tick after an exit" behaviour).
- **Flat with a working order:** a leader passive Buy Limit 30700 was copied to both followers (Working). Flat on Lucid cancelled **only Lucid's** `KTC>` Limit (Cancel0 -> CancelPending -> Cancelled); Tradeify's stayed Working until the leader order was cancelled, which was then mirrored to Tradeify. No locks at any point.
- **Declined confirmation:** answering No to the Flat dialog sent nothing (no `Close` order created for that account).
- The stale `Exit could not obtain closable exposure within 10s` latches left by the earlier build were unlocked through the normal prompt (account name and unlock wording matched) before Start.

**The original lock sequence was then replayed on real NT** (same build): Buy 1 / Sell 1 twice, a passive Buy Limit 30700 (copied, then cancelled by the leader and mirrored), Buy 2 market (followers sent nothing: above the 1-lot cap), Sell 1. Both followers stayed `Copying` and flat with no latch after more than 10 s; the trace shows no follower order for the Buy 2 and none for the Sell 1. On the previous build this ended in `Exit could not obtain closable exposure within 10s` on both followers.

**Re-run after the market-exit fix (2026-10-13 20:04, source `20dc6707c9e6`, same two followers):** Buy 1, then Stop and Limit under one OCO id (both copied to both followers, Working), per-account Flat on Lucid (its stop and target cancelled, closed, other follower untouched), then a leader Sell 1 Market: Tradeify copied it (Filled, flat) and its stop and target were cancelled once each; Lucid logged `leader exit ignored, nothing to close`; neither locked. Before the fix this exact sequence locked Tradeify after 10 s. A stop and a target WITHOUT a shared OCO id still lock (open, see KNOWN-ISSUES).

**Real disconnect / reconnect (2026-10-13 20:17-20:33, source `20dc6707c9e6`):** both followers held 1 lot with a copied stop; NinjaTrader's Connections menu disconnect button was pressed for **Lucid only** and the Warning (`You have 4 active order(s). You have 2 open position(s). Do you really want to disconnect?`) answered Yes. Observed: Lucid `status=Disconnected` at 20:28:03; the copier locked **only Lucid** within 1 s (`LOCK: Disconnected`); Tradeify kept `Copying`. A new leader limit placed while Lucid was down was copied to Tradeify within 0.2 s and **never** to Lucid. After Lucid reconnected (connection table `Connected`) it stayed `Locked` and received **no catch-up order**. Flatten all then closed all three accounts and cancelled every working order. Not covered: the leader's own connection dropping, a disconnect in the middle of an order, and a reconnect with the follower's order filled meanwhile.

Limits of the earlier `28bc4bb3bc47` run: Flat with protective TP/SL and real reconnect were not yet exercised there; the later `20dc6707c9e6` runs above supplied those limited historical observations. Still not covered on the current candidate: a provider-rejected Flat, a disconnect during Flat, and the 20 s confirmation window on a slow connection. None of the historical observations proves resolution of a hidden accepted Flatten request.
