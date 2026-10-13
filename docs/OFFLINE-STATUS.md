# Offline status and what needs a real environment / 離線進度與仍需真環境

Source `28bc4bb3bc47` (2026-10-13; also reproduced on the Windows VM) · engine runner 207 PASS / 0 FAIL · dashboard UI 45 PASS · pure 32/32 · independent 24 PASS / 1 known FAIL ([#6](https://github.com/Kinfxhk/KINFXTradeCopier/issues/6), shelved) · Python 459 OK · full AddOn compile-only OK. Completion estimate and what is still only offline: see the progress bar in the [README](../README.md).

Everything in the tables below is **offline, synthetic**: fake accounts. The default build still blocks Start and Flatten for every real account. Separate **real NinjaTrader Simulator / prop-firm Sim** results from the opt-in SIM TEST build are in [VM-PROGRESS](VM-PROGRESS.md). Nothing here is prop-firm or live acceptance. 以下全部係離線合成測試；從未向 NinjaTrader 落過單；真帳戶嘅 Start／Flatten 全部封鎖。

## Done offline / 離線已完成

| Area / 範圍 | What changed / 改咗乜 | Issue | Honest limit / 限制 |
|---|---|---|---|
| UI | Three tabs, per-account Ratio / Tgt / DLL / ATM / OFF, bulk edit, From Leader, per-account divergence setting, red LOCKED footer | — | Synthetic Demo only; real window focus, IME, paste, per-monitor DPI untested |
| Typed numbers / 輸入數值 | Ratio, Max, Target, DLL keep typed text, show an error, block commit; Max rejects 0 and negatives | [#5](https://github.com/Kinfxhk/KINFXTradeCopier/issues/5) | Risk-dialog fields only checked on Apply; high-precision XML round-trip open |
| Flat telemetry / thrown Flatten | Policy decided and tested: lock, keep protection, never retry | [#9](https://github.com/Kinfxhk/KINFXTradeCopier/issues/9) | Cost: account may hold a position with no copied exit until you act |
| Mailbox | Refused work no longer strands counters or flags (4 call sites) | [#4](https://github.com/Kinfxhk/KINFXTradeCopier/issues/4) | Dispose waits at most 3 s |
| Head-of-line blocking | Each follower has its own provider-call thread; one stalled follower no longer freezes the others (10 / 20 / 30 accounts tested) | [#4](https://github.com/Kinfxhk/KINFXTradeCopier/issues/4) | **Real NinjaTrader may require a specific thread for `Account.Submit/Change/Cancel`; unverified** |
| OCO groups | A completed group no longer cancels another group's protection | [#3](https://github.com/Kinfxhk/KINFXTradeCopier/issues/3) | Fractional allocation open |
| Exit-completion gap | Lock text says exactly "N lots UNPROTECTED … act in NinjaTrader"; the copier never creates the order itself | [#3](https://github.com/Kinfxhk/KINFXTradeCopier/issues/3) | **The lots stay unprotected until a human acts** |
| Fill frontier | 150-combination property sweep plus over-cover test; no defect found | [#2](https://github.com/Kinfxhk/KINFXTradeCopier/issues/2) | Only the quantity freeze exists, not the full frontier design |
| Tooling | Reproducible Windows runner, CI, case-count test no longer hard-coded, Linux portable run reproduced by a third party | [#1](https://github.com/Kinfxhk/KINFXTradeCopier/issues/1) | Engine tests need Windows .NET Framework |

Four defects were found by independent review or by my own sweep in my own first versions (a regression in the OCO fix, three in the dispatch fix, one oscillation) and fixed before publication. Each has a test that fails without its fix.

## Needs a real environment / 一定要真環境

None of these can be answered offline. 呢啲離線無法回答。

1. **Two real NinjaTrader Sim or Playback accounts**, leader to follower, real provider callbacks ([#7](https://github.com/Kinfxhk/KINFXTradeCopier/issues/7)). A step-by-step **plan** exists in [SIM-TEST-PLAN](SIM-TEST-PLAN.md); it is a plan only and does not lift the block. Start and Flatten are blocked; unblocking is a separate reviewed decision, not part of this alpha.
2. **Thread rules for provider calls** (the per-account dispatch above). If NinjaTrader requires these calls on one thread, the design must change.
3. **Disconnect, reconnect, late fills, slippage, replacement order identity.**
4. **Window / workspace close and restart**, real multi-monitor DPI, native modal focus, IME input, clipboard paste ([#8](https://github.com/Kinfxhk/KINFXTradeCopier/issues/8)).
5. **Real 30-account latency** and whether one stalled real provider call behaves like the fake one.
6. **Journal and verified restart recovery** ([#6](https://github.com/Kinfxhk/KINFXTradeCopier/issues/6)) is shelved by the maintainer, not unfinished by accident.

## Not built, by decision / 刻意未做

| Item | Why |
|---|---|
| Real reaction to divergence (one-time flatten) [#12](https://github.com/Kinfxhk/KINFXTradeCopier/issues/12) | Only the per-account setting exists. Needs a guarded Flatten and real provider evidence. |
| Per-account self-managed TP / SL [#10](https://github.com/Kinfxhk/KINFXTradeCopier/issues/10) | Postponed by the maintainer. |
| Adopt an existing position on a just-connected follower [#11](https://github.com/Kinfxhk/KINFXTradeCopier/issues/11) | Not started. Depends on who owns the exit. |
| Placing the missing exit automatically | Rejected for now: without provider evidence it could double-cover and reverse an account. |

## How to help / 點樣幫手

Run `python -B -m unittest discover -s tests -p 'test_*.py'`, read [TEST-MATRIX](TEST-MATRIX.md), pick an open Issue, and send a failing test first. The property sweep in `tests/EngineSafetyTests.cs` is a cheap place to add the combination you think is missing. 先寫一個會失敗嘅測試，再改。
