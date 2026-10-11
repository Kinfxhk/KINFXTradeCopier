# Two-account Sim acceptance: test PLAN (not authorization) / 雙帳戶模擬驗收：測試計劃（唔係授權）

**STATUS: PLAN ONLY. DO NOT RUN.** Start and Flatten are blocked for every real NinjaTrader account in this repository, including Simulator and Playback, and **this document does not lift that block.** Running this plan needs a separate, reviewed, simulation-only opt-in build that does not exist yet. See [#7](https://github.com/Kinfxhk/KINFXTradeCopier/issues/7) and [SAFETY-HOLD](SAFETY-HOLD.md). Do not edit the hold to follow this plan.
**狀態：只係計劃，唔好跑。** 本 repo 對所有真 NinjaTrader 帳戶（包括 Sim／Playback）嘅 Start／Flatten 全部封鎖，**呢份文件唔會解除封鎖**。要跑呢份計劃，需要另一個經覆核、只限模擬、明確選用嘅 build，而家未存在。

Why it exists: so that when such a build exists, the first real test answers the questions offline tests cannot, instead of repeating what the fake already proved. 目的：將來有咗該 build，第一次真測試去回答離線測試答唔到嘅問題。

## 0. What offline tests already cover, and what they cannot

| Offline tests cover (fake accounts) | Only a real run can answer |
|---|---|
| Lock rules, quantity freeze, OCO group retirement, unprotected-lots text, draft handling | Does `Account.Submit/Change/Cancel` accept calls from a thread other than the one NinjaTrader used? (Issue #4 dispatch) |
| One blocked follower does not block others (fake blocks on purpose) | How long does a real call take, and does one slow real account delay others? |
| Order within one account | The real order of callbacks across accounts for one leader action |
| Behaviour when Position lags (fake can only lag) | Can the real Position read **ahead** of fills? |
| Accepted-then-throw (fake throws on demand) | What does a real provider do when a call throws after it was accepted? |

The first column is **not evidence for the second.**

## 1. Preconditions (all must be true; otherwise STOP)

1. A **reviewed simulation-only opt-in build** exists, with the hold lifted only for accounts whose provider is Simulator or Playback, and still blocking everything else. It is checked in, hashed, and its diff against this repository's hold is reviewed by someone other than its author, **and the maintainer has explicitly authorised the run in writing in this repository** (see [KNOWN-ISSUES](KNOWN-ISSUES.md) and the README). Without that, this plan stays a plan.
2. **Two or more separate NinjaTrader Simulator accounts** (one leader, at least one follower) on **one** NinjaTrader instance. One demo account cannot prove leader to follower copying. **Hard requirements, not preferences:**
   - **No live or prop connection exists in that NinjaTrader instance at all.** Use a Windows user or machine where none is configured, or disconnect and remove it first. Do not rely on "I will not select it".
   - **Confirm each account really is a simulator, yourself, in NinjaTrader.** This plan does not tell you which screen or label to look at, because that wording has not been verified against a real NinjaTrader install. If you cannot tell for certain that an account is simulation-only, treat it as live and **do not use it**. The copier is written to accept only accounts whose provider is Simulator or Playback (`Sim()` in the source) and to refuse the rest, but that check has never run against a real account, so it is not a substitute for yours.
   - **Both accounts are flat with no working orders** on the chosen instrument before S0. The copier refuses to start otherwise.
3. Instrument: one liquid futures contract, quantity **1** to start. Ratio 1.
4. Market open, or Playback data running.
5. A named human watching NinjaTrader's own Orders and Positions tabs for the whole run. **That tab is the source of truth, not this copier's status.**
6. Evidence capture ready (section 5) **before** step 1.
7. Written abort rule (section 2) agreed by everyone present.
8. **Time limits written down before you start** (they are left blank on purpose so you commit to them in advance): the longest you will wait for a follower order to appear, and for a cancel or change to be acknowledged. Suggested starting point: 5 seconds for an order to appear, 10 seconds for an acknowledgement. Exceeding either is an abort, not "wait a bit longer".

## 2. Abort rule (applies to every step)

**STOP, do not continue, do not retry**, and go to the "if it goes wrong" box when ANY of these happens:
- A follower shows a position or working order you did not expect.
- Any account shows a position larger than 1 lot (or larger than the planned size of that step).
- The copier shows LOCKED, or any lock reason you did not predict for the step.
- A Cancel or Change does not show as acknowledged in NinjaTrader within the time you set beforehand.
- Anything surprising. Surprise is a failure.

**If it goes wrong:** do not press Start, Flatten or unlock in this copier again. Use NinjaTrader's own controls to cancel working orders and flatten each Sim account by hand, and write down what you saw **before** you change anything else.

## 3. Staged steps

**PLAN ONLY, do not run. Start and Flatten are blocked in this repository for every real account, including Simulator and Playback.** Every sentence below that starts with an action ("Start", "place", "disconnect") describes what a future, separately authorised run would do, not something to do now.

Each step lists the **expected result**, a **pass rule**, and what to **record**. Do the steps in order. Do not skip a failed step. One variable changes per step. Do not combine steps.

| # | Step | Expected (pass rule) | Record |
|---|---|---|---|
| S0 | Start the copier with leader and one follower, **no orders** | Armed, no orders, no lock. Both accounts flat. | Source hash, NinjaTrader version, which accounts are leader / follower |
| S1 | Leader: place one **buy limit well BELOW the market** (or a sell limit well ABOVE it), qty 1. Never a price that could fill. | Follower shows one matching working limit, qty 1, price equal, **within the time you set** | Both order IDs, timestamps on each, latency leader to follower |
| S2 | Leader: **change the price** to another price on the **same side**, still far from the market, never across it | Follower price follows; no second order | Change timestamps, whether the follower order ID changed |
| S3 | Leader: **change it back** to the previous price, **only after the follower has shown the S2 price**. Do not change quickly: an unconfirmed Change is skipped by the engine (Issue #4, item 4), which would test a different path | Follower returns to that price (regression guard for the dropped-revert bug) | Final follower price |
| S4 | Leader: **cancel** the entry | Follower order cancelled | Cancel acknowledgement time on each side |
| S5 | Leader: **market entry**, qty 1. **Both accounts are deliberately UNPROTECTED from now until S6.** Decide the S6 prices before this step, place them straight away, and if anything is unexpected flatten by hand in NinjaTrader. | Follower fills qty 1, no more, no less | Fill prices both sides, slippage = follower minus leader |
| S6 | Leader: with the position from S5 open, place a **stop and a target as an OCO pair**, qty 1, same OCO id on both. **Long position: stop BELOW the market, target ABOVE it (reverse for short).** | Follower shows two working orders of the same prices and quantities, and covers exactly the 1 lot held | Whether NinjaTrader reports them as one OCO group on the follower. **This is an open question, not a pass condition**: write what you see. **If the follower's two orders are NOT linked as one OCO group in NinjaTrader, STOP: cancel both and flatten by hand in NinjaTrader, and skip S8**, because a target fill would leave a live stop on a flat account, which can reverse it. |
| S7 | Leader: **move the stop** only to another price on the **protective side** of the market, never across the market | Follower stop moves, target unchanged | Timestamps |
| S8 | Let the **target fill**. Prefer **Playback**, where you can drive price to the target; on a live Simulator feed it may never fill. If it does not fill within your time limit, **abort**. Do not flatten by hand to make the step pass; flattening by hand is only allowed to END the run, after you have written down what you saw | Follower position goes flat, sibling stop cancelled, nothing left working | Order of events on the follower: fill, then sibling cancel; any gap in time. **If any order is still working on the follower more than your acknowledgement time after the fill, cancel it by hand in NinjaTrader immediately; do not wait to watch it.** |
| S9 | **Two followers**, repeat S1 to S4 | Both follow, each independently | Per-follower latency (do not average) |
| S10 | **Pull one follower's connection** (disconnect that account in NinjaTrader), then place a leader order (a far limit as in S1, starting from a flat leader). **If NinjaTrader cannot disconnect one account without also disconnecting the leader, SKIP this step and write that down; never disconnect the leader.** | Healthy follower still copies. The disconnected one is expected to lock with the text `Disconnected — no catch-up` (the source says this and the offline test `StartupAndDisconnect` covers the disconnect lock). An offline fake test (`StartupAndDisconnect`) checks only that after a reconnect the account stays locked and a new leader order is not copied (`no reconnect catch-up`). **Real-provider reconnect behaviour (late callbacks, order state after reconnect) is not tested: record it, do not assume it.** After this step **do not unlock the locked follower to carry on**; use another healthy follower for S11, or end the run. | Lock reason text, behaviour after reconnect (does it stay locked, does it place the missed order, does it do anything), whether the healthy follower was delayed |
| S11 | **OFF** one follower (a follower that is NOT locked from S10) during a working entry, which must be a far limit as in S1 | That follower cancels its entry and keeps any filled exposure and its exits (source: `CancelEntries` only touches entry orders; offline test `OffDelayedExitSafety`). OFF is not Flatten. | What the follower shows while the cancel is pending; whether the cancel is acknowledged within your time limit |
| S12 | **Optional, observation only.** With a follower holding a position and exits, **attempt** to close the copier tab or window | **No pass or fail exists for this step.** Expected: the close is **refused** (the source cancels the window close and shows "Close blocked: copier still manages positions / working orders"). Record the message. **Do NOT force it:** no killing NinjaTrader, no pulling the network, no closing in a way that bypasses the refusal; that is exactly what [DO-NOT-DO](DO-NOT-DO.md) forbids. Restart recovery is not implemented ([#6](https://github.com/Kinfxhk/KINFXTradeCopier/issues/6)) and this plan does **not** test it; that needs its own reviewed plan. | The exact refusal message; what the copier shows afterwards |

**Ending every run:** when you finish, or abort, cancel all working orders and flatten each Sim account **in NinjaTrader**, confirm both accounts show flat with no working orders, and only then close the copier. The copier refuses to close while it still owns exposure or an unresolved order, and that refusal is correct; do not work around it.

**Not in this plan on purpose:** flatten-all, divergence reaction, adopting an existing position, self-managed TP/SL, partial-fill recovery beyond what S5 to S8 happen to show, any live or prop account, any size above 1.

## 4. Questions to answer explicitly (write the answer, even if "unknown")

1. Did the per-account dispatch (Issue #4) work against real NinjaTrader, or did any call fail with a thread error or misbehave?
2. Real latency per account and per step, and whether a slow real account delayed another.
3. Did Position ever read ahead of fills?
4. Did any callback arrive in an order the fake never produced?
5. Slippage on market entries and exits.
6. What happened on disconnect and reconnect on a real provider (the offline test only covers a fake).
7. What exactly did the copier show when closing was attempted with a position open?

## 5. Evidence to capture

For each step: the **exact commit and source hash**, the **step id**, **timestamps from NinjaTrader's own log** (not the copier's), the **Orders and Positions tabs as screenshots**, the copier's Logs tab text, and what you expected versus saw. **Redact** account names, IDs, balances and file paths **before** sharing anything. Use fake labels such as `LEADER`, `F1`, `F2`. Attach nothing that identifies a real person or a live account. Report with the issue template and keep the original unedited failure output.

## 6. What a pass does and does not mean

A clean run of S0 to S11 on Sim means only: **those steps worked on that day, with those two Sim accounts, one instrument, one lot.** It does **not** show live or prop safety, OCO atomicity at a real broker, behaviour at size, restart recovery, or compliance with any firm's rules. Any failure becomes a new failing test in `tests/` **first**, then a fix. A passing run does not remove the hold; lifting it is a separate reviewed decision.

## 中文摘要

1. **呢份係計劃，唔係授權。** Start／Flatten 全封鎖，要等一個經覆核、只限模擬嘅 build，加埋維護者喺本 repo 書面明確授權，先可以跑。**唔好為咗跟呢份計劃去改 hold、加 define 或拆封鎖**（詳見 [SAFETY-HOLD](SAFETY-HOLD.md)、[#7](https://github.com/Kinfxhk/KINFXTradeCopier/issues/7)）。
2. 需要至少**兩個獨立 Sim 帳戶**（一個 Leader、一個 Follower），同一個 NinjaTrader；一個帳戶證明唔到跟單。**硬性要求：** 該 NinjaTrader 入面**根本冇連住任何真倉或 Prop 帳戶**（唔係「我唔去揀佢」，而係要移除或另開 Windows 用戶）；要自己喺 NinjaTrader 親自確認每個帳戶都係純模擬（本文件冇講係邊個畫面，因為冇對住真 NinjaTrader 核實過；唔肯定就當佢係真倉，唔好用）；兩個帳戶開始前都要平倉、冇掛單。
3. 只用 1 手、一隻合約、Ratio 1。要有人**全程睇住 NinjaTrader 自己嘅 Orders／Positions 分頁**，嗰度先係真相，唔係本軟件嘅狀態。
4. **任何意外就停**，唔好重試，唔好再撳 Start／Flatten／解鎖；用 NinjaTrader 自己嘅控制手動取消同平倉，**先記低見到乜**。等待時間要**開始前寫低**（建議：單出現 5 秒、確認 10 秒），超時即係中止，唔係「再等多陣」。每次完結或中止，都要喺 NinjaTrader 取消所有掛單同平倉，確認兩邊都係平倉先關閉本軟件。
5. 步驟 S0 至 S12 逐步做，每步只改一樣嘢，每步要有預期結果、通過準則同要錄嘅證據。S12 係**可選、只係觀察**：持倉時嘗試關閉 copier，預期被拒絕，記低訊息，**唔好強行關閉**（唔好殺 NinjaTrader、唔好拔網絡），因為重啟恢復未做，呢份計劃唔測。S10 斷線後預期鎖定（有代碼同離線 fake 測試）；**真 provider 重連之後會點冇測過，要記錄，唔好當作已知**；如果 NinjaTrader 做唔到單斷一個帳戶（會連 Leader 一齊斷），就**跳過 S10**。S6 如果 Follower 兩張單冇連成 OCO，**即刻停**、手動取消同平倉，跳過 S8。S8 如果目標價冇成交就**中止**，唔好為咗「令佢成功」而手動平倉；手動平倉只可以用嚟**結束**今次測試。**關閉被拒係正確嘅，唔好繞過。**
6. 要明確回答：每戶線程嗰個設計喺真 NinjaTrader 有冇出事、真實延遲、Position 會唔會跑喺成交前面、斷線重連點樣（真 provider）、持倉時嘗試關閉會見到乜訊息。
7. 分享證據前要遮蓋帳戶名、ID、餘額同檔案路徑，用 LEADER／F1／F2 代替。
8. 乾淨跑完只代表「嗰日、嗰兩個 Sim 帳戶、1 手」成功，**唔代表**真倉或 Prop 安全，亦唔會解除封鎖。有失敗就先寫一個失敗測試，再修。
