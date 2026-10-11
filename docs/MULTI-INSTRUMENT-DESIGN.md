# Multi-instrument copying — design proposal (NOT implemented)

Status: **proposal for review. No engine code is changed by this document.** Trading stays disabled in the public alpha ([SAFETY-HOLD.md](SAFETY-HOLD.md)); this is design work for the later Sim-first milestone ([ROADMAP.md](ROADMAP.md) M2).
繁體中文摘要在文末。

## 1. Problem

Today one copier session manages **exactly one instrument**. `CopierEngine` holds a single `Instrument instrument` (41 reads in the source) and every follower order is created with `f.Account.CreateOrder(instrument, ...)`. Consequences, all read from the source:

- A leader order on any other instrument is ignored (`s.InstrumentName != instrument.FullName`).
- Cleanliness (`Clean`), owned-position and exposure checks look at the selected instrument only. Positions and working orders on another instrument are invisible to the engine.
- A user who trades GC and NQ at the same time cannot copy both.

Requested by the maintainer (2026-10-11): several instruments at the same time, each with its own settings. First asked as 4–5; **revised the same day to a hard cap of 3 lanes** for stability and to keep resource use modest. The cap is deliberately low so nobody reads this feature as "open as many as you like". Each lane may also have **its own leader account** (decision D2 below).

## 2. Why "open several copier tabs" is rejected

| Reason | Evidence in source |
|---|---|
| Same account cannot be owned twice | `SessionOwnership.Acquire` keys on `"account:" + name`; the second window is refused. This is deliberate double-order protection. |
| Risk limits are **account-wide** | Daily target / loss use `RealizedProfitLoss + UnrealizedProfitLoss` of the whole account. Two copiers would each see the combined P/L but only know about their own positions, so neither can correctly lock the account. |
| Shared latch file | `risk-latches.xml` is one file; two windows would overwrite each other. |
| Flat check is per instrument | A second copier would see the account as "clean" while the first one holds a position. |

Relaxing `SessionOwnership` to allow it is explicitly **not** proposed.

## 3. Proposal: one session, one account-level risk, many instrument lanes

```
Session (one per copier window)
 ├─ Followers[]                                      (unchanged)
 ├─ Account-level risk: target / DLL / latch / pause  (unchanged, shared by all lanes)
 └─ Lane[] — one per instrument (HARD CAP 3)
      ├─ Instrument, Leader account                   (per lane; leaders are NOT shared)
      ├─ Ratio, MaxContracts, enabled                 (per lane, per follower)
      ├─ Order links, OCO groups, identity maps        (per lane)
      └─ Owned-position ledger, reserved exit capacity (per lane)
```

### 3.1 Invariants that must not weaken

1. **Account-level risk is evaluated once per account, over all lanes.** A lane cannot reset, bypass or unlock it. A latch on an account stops *every* lane for that account.
2. **A lane never sees another lane's orders.** Order identity, OCO grouping, `FindFollowerLink`, reservation and exit-budget calculations are keyed by `(account, instrument)`. An order whose instrument matches no lane is treated as **unknown exposure** and locks the account, exactly like an unknown follower order today.
3. **No adoption.** Start still requires every selected account to be flat on **every** lane instrument and to have no working order on them. Unknown exposure on a lane instrument at Start refuses to start.
4. **Exposure outside the lane set is not invisible.** If an account holds a position or working order on an instrument that is not a lane, the engine does not touch it, and the dashboard shows a visible "external exposure" flag. It is never copied and never counted as owned.
5. **Per-lane failure isolation.** An uncertain acceptance, fill divergence or error locks the *account on that lane*; whether it also stops the other lanes is the Open question Q2.
6. **Live accounts remain refused.** The Sim/Playback scope check is unchanged and applies to the union of accounts.
7. **Adding or removing a lane while a session is attached is refused.** Lanes are fixed at Start, like accounts.
8. **The lane count is capped at 3 in code, not in the UI only.** Start refuses a 4th lane; the limit is a constant covered by a test, so a settings file cannot raise it.
9. **One role per account across all lanes.** An account is either the leader of exactly one lane or a follower; it can never lead one lane and follow another, and two lanes cannot share a leader account (D2). This keeps the account-level risk and the "no adoption" rule unambiguous. A scope that breaks this is refused at Start.
10. **A leader's orders only ever feed its own lane.** An order from lane A's leader on lane B's instrument is unknown activity (logged, never copied).

### 3.2 Per-lane settings

| Setting | Level | Why |
|---|---|---|
| Leader account | per lane (not shared between lanes) | Decision D2. Each instrument can follow a different leader. |
| Ratio, MaxContracts, lane on/off | per follower **and** per lane | The requirement: each instrument separate. Default = follower's current value, so existing settings files keep working. |
| Daily target, daily loss, DLL, latch | per **account** | Prop-firm rules apply to the account, not the instrument. |
| ATM mirroring | per follower (unchanged) | |
| Pause | per follower, optionally per lane | A lane pause must not clear an account latch. |

Settings file: add a lane list; an old file without it loads as a single lane = the saved instrument, so nothing already saved breaks. Unreadable lane data keeps the existing fail-closed behaviour (`settingsCorrupt`, Start blocked).

### 3.3 Quantity across lanes

Ratio is a **contract-count** multiplier, as today. The first version does **not** convert between instruments (no NQ→MNQ value scaling). Cross-instrument mapping (a leader trading NQ, a follower trading MNQ) is a separate later feature with its own review, because price scale, tick value and order prices differ. If wanted later: only a whitelist of same-underlying micro pairs, ratio defined on notional value, unknown pairs refused.

## 4. Refactor plan (small, reviewable steps)

Each step keeps all existing tests green and adds its own RED test first.

1. **Introduce `Lane` and key all per-instrument dictionaries by `(account, instrument)`**, with exactly one lane and one leader. Behaviour must be byte-identical to today: the existing 135 engine cases are the oracle. No new feature in this step.
2. **Lane-aware `Clean` / `OwnedPosition` / exposure** and the unknown-order rule (3.1 #2, #4). New tests: order on a non-lane instrument locks; position on a non-lane instrument is flagged but never copied.
3. **Two lanes, one follower, two different leaders**: copy both, with cross-lane negative controls (an OCO id, an order id, or a fill on lane A must never affect lane B; exit budget of lane A never uses lane B's position).
4. **Account-level risk across lanes**: loss on lane A latches the account and stops lane B on the same account; a lane cannot unlock it.
5. **Persistence**: lane list in XML, old-file compatibility, corrupt-lane fail-closed.
6. **UI**: lane table (instrument, leader, Ratio, Max, on/off per follower), contract picker, at most 3 lanes with a clear "max 3" label.
7. **Scale tests** with 10/20/30 accounts × 3 lanes under the existing delayed, reordered and missing-callback fakes.

Steps 1–2 are the high-risk core. Independent review is required before step 3 starts and again before any enablement.

## 5. Decisions and open questions

**Decided by the maintainer (2026-10-11):**
- **D1.** Lane limit is **3**, hard-coded (stability and resource use; do not suggest unlimited use).
- **D2.** Lanes **do not share a leader**: each lane has its own leader account. Consequence: invariants 9 and 10 above (one role per account, a leader feeds only its own lane).
- **D3.** Ratio and MaxContracts are set **per instrument, per follower**.

**Still open:**
- **Q2.** When one lane hits an uncertain state (unknown acceptance, divergence), should the account stop on **all lanes** (proposed default, safest, because risk is account-wide) or only that lane? Until answered, the design assumes **all lanes**.
- **Q4.** A follower with Ratio 0 on a lane means "skip this lane for this account" — confirm.
- **Q5.** Should a leader order on a non-lane instrument produce a visible, rate-limited log warning (proposed: yes)?
- **Q6.** Should the dashboard warn when the same machine runs several copier windows (proposed: a soft warning only; `SessionOwnership` already refuses overlapping accounts)?

## 6. Test evidence required before this can be called done

Offline and synthetic only, until a Sim acceptance protocol exists:

- All existing engine, pure, independent and Python tests unchanged and green after step 1.
- Per-lane isolation mutation tests: removing the `(account, instrument)` key from any one map must turn a test red.
- 10/20/30 accounts × 1–3 lanes fault matrix: delayed, reordered, duplicated, missing callbacks; uncertain acceptance on one lane.
- Independent review of steps 1–2 (diff-level) and of the final candidate.
- Nothing here is provider acceptance and nothing authorizes trading.

---

## 摘要（繁體中文）

**現況：** 一個 copier session 只管**一個商品**。leader 喺其他商品落嘅單會被忽略，持倉同掛單檢查亦只睇揀咗嗰個商品。

**唔建議多開 TAB／多個 copier：** 引擎設計上同一帳戶只准一個 copier 持有（防雙重落單）；日虧損／目標係**帳戶級**，兩個 copier 會各自睇到合計虧損但只知自己嘅倉，無法正確鎖戶；`risk-latches.xml` 會互相覆蓋；「flat」檢查亦只查自己嘅商品。

**建議：** 一個 session、一套帳戶級風控、多條「商品通道（lane）」，最多 **3** 條（程式碼寫死，唔係淨係介面限制），每條通道有**自己嘅 leader**（唔共用）。每條通道有自己嘅訂單配對、OCO、持倉帳本同保留額度；Ratio／Max 張數**每個商品、每個 follower 分開設**；日目標／DLL／鎖戶仍然係**帳戶級**，任何通道觸發都停晒該帳戶。通道以外嘅持倉只顯示警告，絕不跟單、絕不當成自己嘅倉。

**唔做：** 跨商品換算（例如 NQ→MNQ）留待之後獨立設計。Live 帳戶仍然全部拒絕。

**步驟：** 先將所有以商品為鍵嘅資料改成 `(帳戶, 商品)`，只有一條通道，行為必須同今日完全一致，以現有 135 個引擎測試做標準；再逐步加通道、帳戶級風控、設定檔、介面同 10／20／30 戶壓力測試。第 1、2 步風險最高，要獨立覆核先可以做第 3 步。

**已決定：** 上限 3 條；每條通道獨立 leader；Ratio／Max 每個商品、每個 follower 分開設。一個帳戶喺所有通道只可以有一個角色（要麼係某條通道嘅 leader，要麼係 follower），否則 Start 拒絕。

**仲未決定：** 單一通道出現不確定狀態時，係咪停晒所有通道（暫時按「停晒」設計，因為風控係帳戶級）；Ratio 0 嘅意思；非通道商品嘅警告；同一部機開多個 copier 視窗時係咪加警告。

**呢份文件冇改動任何引擎代碼，亦唔代表可以交易。**
