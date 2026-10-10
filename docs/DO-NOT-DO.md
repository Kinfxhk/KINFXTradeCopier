# Do NOT do these things

This is an **alpha**. Trading is disabled for every real account in this repository, so none of this can hurt a real account today. It matters from the moment someone enables copying in their own build, and for anyone reviewing the design.

For each item below the software **cannot recover by itself**, and there is **no tested recovery path**. The only safe answer is to avoid it. Where the engine detects it, it locks the account and tells you to reconcile by hand in NinjaTrader or with your broker.

Source of truth: the lock messages in `src/AddOns/KINFXTradeCopier.cs`. Each row names the message the engine shows. "Detected" means the engine notices and locks; it does not mean it fixes anything. Offline synthetic tests only, no real provider behaviour.

## Never, while copying is running

| Do not | Why | What the engine does today |
|---|---|---|
| **Place, change or cancel orders by hand on a follower account** (same instrument) | The engine cannot tell your order from its own, and it will not adopt it. | Locks: `Unknown follower order activity — no adoption / manual review`. |
| **Close or reduce a follower position by hand** while the engine has protective orders working | Position stops matching what the engine believes it owns. The protective order can end up larger than the position, or a later fill can reverse you. | Locks: `Position diverges from owned execution ledger — manual recovery required`. Leaves existing protection unchanged. |
| **Assume a protective exit shrinks when Position lags** | While Position reads lower than the engine's owned fills, the engine deliberately does **not** shrink an acknowledged exit (quantity freeze), so the exit can be **larger than your real position**. If the position really was reduced, a triggered exit can reverse you. | Quantity freeze only; no lock, no cancel, no warning beyond the log. Check the exit size in NinjaTrader yourself. |
| **Add to a follower position by hand** | Exposure the engine does not own is never protected. | Locks: `Unknown excess position — protect owned subset only`. Only the owned part is protected. |
| **Reverse the leader** (long to short) in one step | The engine will not guess whether this is a reversal or a mistake. | Locks: `Ambiguous reversal blocked`. |
| **Use the same account as leader and follower**, or list a follower twice | Orders would copy back onto themselves. | Start is refused (`LeaderAlsoFollowerIsRefused`, `DuplicateFollowerListIsRefused`, offline fakes). |
| **Change the leader order type, action, OCO or time-in-force after it is placed** | The follower copy cannot be amended safely. | Locks: `Unsupported leader action / type / OCO / time-in-force transition … manual recovery required`. |
| **Kill NinjaTrader, pull the network or power off** with open positions or working orders | Restart recovery is **not verified** (Issue #6). After a restart nothing proves what the follower still owns. | Not testable offline. Expect to reconcile by hand. |
| **Press Flatten repeatedly, or Flatten and then re-follow quickly** | A Flatten call can be accepted by the provider and still report an error. A second close can reverse you. | Locks: `Flatten acceptance uncertain … duplicate reductions blocked until explicit provider reconciliation`. Flatten is disabled in this alpha. |
| **Run two copier tabs or windows on the same accounts** | Two engines would submit twice. | The source refuses a second owner of the same account (`SessionOwnership`). **No test covers this**, and real multi-window behaviour is untested. |
| **Expect a connection that dropped to catch up** | There is deliberately no catch-up after a disconnect. | Locks: `Disconnected — no catch-up`, `Leader disconnected — no catch-up`. |

## Never, even when copying is stopped

| Do not | Why |
|---|---|
| **Start copying on an account that already has a position or working orders** | There is no "adopt existing position" feature (planned, [#11](https://github.com/Kinfxhk/KINFXTradeCopier/issues/11)). The source refuses Start for such accounts (`No adoption or catch-up`); no separate test for this path was found. Do not try to work around it by hand. |
| **Unlock a locked account just to get it moving again** | A lock means the engine lost track of something. Unlock only after you have checked the account yourself in NinjaTrader. The software only checks that the instrument is flat with no working orders and that the risk conditions allow it; it cannot know whether you reconciled everything else. Unlock never re-arms copying. |
| **Treat a green or empty status as "protected"** | Submit returning is not acceptance, and a lock is not coverage. Look at the actual orders in NinjaTrader. |
| **Edit settings files by hand** | Corrupt settings put the tab in a refused state. Ratio uses invariant-culture numbers; a decimal comma can be misread (e.g. `0,125` as 125). |

## Easy to get wrong in the settings

- **Tgt or DLL of 0 means OFF**, not "no limit needed". That account has no daily P&L lock at all. Check this after **From Leader** and bulk edits.
- **A tiny Ratio can round the copied size to zero lots**, so nothing is copied and nothing looks wrong. Check the resulting quantity.
- **ATM here means mirroring the leader's exit orders.** It is not NinjaTrader's own ATM on the follower. With ATM off, that follower has no TP or SL of its own, but it **still copies a leader market exit**. A hand-tuned follower is still taken out when the leader flattens (Issue [#10](https://github.com/Kinfxhk/KINFXTradeCopier/issues/10)).
- **Balance is CashValue, not net liquidation.** Do not read it as available margin.
- **P/L is observed for the current session.** It is not your broker's or prop firm's daily accounting. Local limits cannot stop every manual or strategy order, and nothing here is a compliance check.

## If something goes wrong anyway

1. Stop trusting the screen. Open NinjaTrader's own Positions and Orders tabs.
2. Make the account safe with native NinjaTrader or broker tools, not with this copier.
3. Leave the account locked until it is flat with no working orders.
4. Report it with a redacted, synthetic-style trace, never account numbers or balances (see `docs/COMMUNITY-TESTING.md`).

## Planned, not built

- Adopting an existing position on a just-connected follower: [#11](https://github.com/Kinfxhk/KINFXTradeCopier/issues/11).
- Choosing per account between notify-only and flatten-once when a follower goes the wrong way: [#12](https://github.com/Kinfxhk/KINFXTradeCopier/issues/12). Today the engine only locks and shows red; it never flattens by itself.

## Not covered yet

Whether the engine copes with a real provider's reconnects, late fills and slippage is **untested**. See `docs/TEST-MATRIX.md` and `docs/KNOWN-ISSUES.md`. Absence from this page does not mean something is safe.

## 廣東話摘要

呢個係 alpha，所有真實帳戶交易都封鎖。以下情況**軟件無法自己恢復，亦冇已驗證嘅恢復流程**，所以一定唔好做：

- 跟單期間**喺 follower 手動落單、改單、平倉或加倉**。
- **Leader 一下子反手**、改 leader 嘅單類型／OCO。
- **同一帳戶又做 leader 又做 follower**，或者開兩個跟單視窗。
- **強制關閉 NinjaTrader、拔網線、斷電**（重啟恢復未驗證，#6）。
- **連續按 Flatten，或者 Flatten 後即刻重新跟**。
- **帳戶已有倉位或掛單就想開始跟單**（唔支援，#11）。
- **為咗繼續而解鎖被鎖帳戶**：要先自己確認帳戶已平倉、冇掛單。

設定方面：Tgt／DLL 填 0 = 關閉，唔係「無限制都 OK」；Ratio 太細會變 0 手；ATM 係鏡射 leader 出場單，唔係 NT 原生 ATM，關咗 ATM 嘅帳戶仍會跟 leader 市價平倉；餘額係 CashValue，唔係淨值。

出事時：睇 NinjaTrader 自己嘅 Positions／Orders，用 NT 或券商工具處理，唔好靠呢個程式。
