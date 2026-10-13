# Developer and status details

Moved out of the front page so beginners see the install steps first. Back to the [README](../README.md). Nothing here was deleted. Progress numbers on the front page come from [PROGRESS.json](PROGRESS.json).

### Done (in this repository) / 已完成（已在本 repo）

| English | 中文 |
|---|---|
| Three-tab dashboard (Copy / Risk / Logs) built for 10–30 accounts; per-account Ratio, Tgt (daily target), DLL (daily loss), ATM exit-mirroring and OFF in each row | 三個分頁（Copy／Risk／Logs），為 10–30 個帳戶設計；每行都有獨立 Ratio、Tgt（每日目標）、DLL（每日虧損）、ATM 出場跟隨、OFF |
| Bulk edit of Tgt / DLL / ATM for highlighted accounts, and **From Leader** as a one-time default you can fine-tune per row | 批量改 Tgt／DLL／ATM（只改選中嘅帳戶），**From Leader** 一鍵以 Leader 做預設，之後可逐戶手動微調 |
| Per-account **On divergence** setting (`Notify` default, `FlattenOnce` optional). **Stored only**: the engine never flattens by itself | 每戶「反向時反應」設定（預設只通知，可選平倉一次）。**只係儲存**，引擎唔會自動平倉 |
| Settings warnings, locked-account count, draft protection for numeric fields | 設定警告、鎖定帳戶計數、數值欄位草稿保護 |
| Safety policy decided for exits after ambiguous flat telemetry and after a thrown Flatten: lock, keep protection, never retry ([#9](https://github.com/Kinfxhk/KINFXTradeCopier/issues/9)) | 已定政策：持倉但讀到 flat、或 Flatten 拋例外後，鎖帳戶、保留保護單、唔重試（[#9](https://github.com/Kinfxhk/KINFXTradeCopier/issues/9)） |
| Mailbox fix: a callback or poll refused by a stopped worker can no longer leave counters stuck ([#4](https://github.com/Kinfxhk/KINFXTradeCopier/issues/4), partial; independently reviewed) | Mailbox 修正：worker 停止後被拒絕嘅回報／輪詢唔再令計數卡死（[#4](https://github.com/Kinfxhk/KINFXTradeCopier/issues/4)，部分；已獨立覆核） |
| **Per-account Flat button** on every follower row: asks for confirmation naming the account and instrument, flattens only that account, the others keep copying; confirmed flat only from provider evidence, never retried, locks if unconfirmed after 20 s. Tested offline and on real NinjaTrader | **每戶獨立 Flat 按鈕**：每個 follower 行一個，確認框列明帳戶同合約，只平該戶，其他戶繼續跟；要 provider 證據先確認已平，唔重試，20 秒未確認就鎖。離線同真 NinjaTrader 都測過 |
| **Exit lock-up fixed**: after two completed round trips a leader exit with nothing to close locked flat followers after 10 s. Reproduced offline, fixed, replayed on real NinjaTrader | **修正鎖死問題**：兩次來回交易後，leader 平倉但 follower 冇倉，10 秒後鎖死。離線重現、修正，並喺真 NinjaTrader 重放 |
| Disconnect / reconnect and partial-fill cases (8 offline cases, each with a mutant): no catch-up, protection kept, shortfall never made up | 斷線重連同部分成交測試（8 個離線測試，每個都有 mutant）：唔補單、保護單保留、唔補差額 |
| Default build keeps the trading hold on every provider; reproducible test runner, CI, 12 tracked issues, screenshots, bilingual safety notes | 預設版所有 provider 交易封鎖；可重現測試工具、CI、12 個 Issue、截圖、中英安全說明 |

Latest offline results / 最新離線結果 (source `28bc4bb3bc47`, also reproduced on the VM): C# engine **207 PASS / 0 FAIL**, pure 32/32, independent review suite 24 PASS / 1 known FAIL ([#6](https://github.com/Kinfxhk/KINFXTradeCopier/issues/6)), dashboard UI **45/45**, Python 457 OK, full AddOn compile-only OK. Counts are invocations, not a completion percentage; the bar above is the completion estimate. Details and caveats: [TEST-MATRIX](TEST-MATRIX.md).

### Not done / 未完成

| English | 中文 |
|---|---|
| **Real NinjaTrader testing is thin**: one leader, two followers, one instrument (MNQ), Market, Limit and Stop orders (including per-account Flat with a TP/SL, and a leader market exit cancelling the followers' TP/SL). Not yet tested for real: the leader disconnecting, partial fills, a rejected Flatten, a stop + target without an OCO id, slippage, workspace close and restart, multi-monitor DPI, 10–30 accounts | **完全未用真 NinjaTrader 帳戶測試**（連 Sim／Playback 都未）：兩個 Demo 帳戶 Leader→Follower、真實 provider 回報、斷線重連、滑點、關閉 workspace 及重啟、真實多螢幕 DPI、30 戶實際延遲 |
| **The default build still has Start and Flatten disabled**; copying works only in the opt-in SIM TEST build, and only for Simulator / Playback accounts or names in your allow-list. There is no installer or signed release | **Start 同 Flatten 全部封鎖**，所以冇任何跟單，亦冇一鍵全平 |
| Coherent fill frontier before protective resize ([#2](https://github.com/Kinfxhk/KINFXTradeCopier/issues/2)) | 保護單改量前，成交資料一致性（[#2](https://github.com/Kinfxhk/KINFXTradeCopier/issues/2)） |
| Group OCO: a still-open group's protection is no longer cancelled when another group completes ([#3](https://github.com/Kinfxhk/KINFXTradeCopier/issues/3), narrow fix **merged**). **Still open:** if a group completes before the entry is fully filled, the account locks and the other group's waiting exit is never sent (lots unprotected until you act; the lock text now says exactly how many lots are UNPROTECTED and tells you to act in NinjaTrader, and the copier never creates the order itself); fractional allocation | 多組 OCO：一組完成唔再取消另一組仍開放嘅保護（[#3](https://github.com/Kinfxhk/KINFXTradeCopier/issues/3)，窄範圍修正**已合併**）。**仍未完成：**入場未全部成交時一組完成，帳戶會鎖定，另一組等待中嘅出場單唔會送出（倉位冇保護，要人手處理）；出場分配 |
| One stalled follower no longer freezes the others: each account has its own provider-call thread ([#4](https://github.com/Kinfxhk/KINFXTradeCopier/issues/4), merged, offline fake only). **Still open:** real NinjaTrader thread rules for these calls are unverified; Dispose waits at most 3 s | 一個卡住嘅 follower 唔再凍結其他戶：每戶有自己嘅 provider 呼叫線程（[#4](https://github.com/Kinfxhk/KINFXTradeCopier/issues/4)，已合併，只喺離線假環境驗證）。**仍未完成：**真 NinjaTrader 對呢啲呼叫嘅線程規則未驗證；Dispose 最多等 3 秒 |
| Typed Ratio / Max / Target / DLL text is kept, flagged and blocks commit instead of being silently replaced ([#5](https://github.com/Kinfxhk/KINFXTradeCopier/issues/5), partly merged). **Still open:** Risk-dialog fields are only checked on Apply; high-precision XML round-trip; real window focus / IME / paste | Ratio／Max／Target／DLL 打錯嘅字會保留、標示並阻止提交，唔會被悄悄換走（[#5](https://github.com/Kinfxhk/KINFXTradeCopier/issues/5)，部分已合併）。**仍未完成：**Risk 對話框其他欄位只喺按 Apply 時檢查；高精度 XML 往返；真實視窗焦點／輸入法／貼上 |
| Journal and verified restart recovery ([#6](https://github.com/Kinfxhk/KINFXTradeCopier/issues/6)): **shelved by the maintainer**, not worked on now | Journal 及重啟後可驗證恢復（[#6](https://github.com/Kinfxhk/KINFXTradeCopier/issues/6)）：**維護者暫時擱置** |
| Per-account self-managed TP/SL that does not follow the leader's exit ([#10](https://github.com/Kinfxhk/KINFXTradeCopier/issues/10)): not started | 每戶自管 TP／SL、唔跟 Leader 平倉（[#10](https://github.com/Kinfxhk/KINFXTradeCopier/issues/10)）：未開始 |
| Adopt an existing position on a just-connected follower ([#11](https://github.com/Kinfxhk/KINFXTradeCopier/issues/11)): not built | 新連接帳戶承接現有倉位再跟方向（[#11](https://github.com/Kinfxhk/KINFXTradeCopier/issues/11)）：未做 |
| The actual reaction to divergence, e.g. one-time flatten ([#12](https://github.com/Kinfxhk/KINFXTradeCopier/issues/12)): only the setting exists | 反向時真正嘅反應（例如平倉一次）（[#12](https://github.com/Kinfxhk/KINFXTradeCopier/issues/12)）：只有設定，冇行為 |
| Native follower ATM cloning, installer, compiled release, prop-firm compliance checks | 原生 follower ATM 複製、安裝包、編譯版本、prop firm 合規檢查 |

### What you can safely do now / 而家可以安全做嘅事

Read the code, run the offline tests, review or reproduce failures, and send small focused PRs. If you test the SIM TEST build, use only accounts that really are simulated, one leader and at least one follower, read the Start confirmation (it must not say DRY RUN unless you chose it), and keep NinjaTrader's own Orders / Positions tabs open as the source of truth. Do **not** point it at a funded or live account. 睇代碼、跑離線測試、覆核或重現失敗、提交細而專注嘅 PR。**唔好**為咗試下而強行解除交易封鎖。

## What is here

- One native dusk-dark Copy / Risk / Logs dashboard, designed for 10–30 accounts.
- Independent per-account Ratio, target, daily loss and ATM exit-mirroring intent.
- Original engine and Framework-only synthetic test fixtures, with known failures disclosed.
- Separate pure Python NR01/NR02/NR03 **SPEC research**, not a C# engine implementation.
- Test matrix, priority backlog and contribution templates.

The intended later flow is Leader → tick followers → set each Ratio → Start. **Start and Flatten remain held for every real NT8 account.** OFF is not Flatten. ATM means exit-order mirroring, not native follower ATM cloning. Cash is CashValue with its denomination, not equity. No global Ratio setting.

## Start contributing safely

Python 3.10+; no Python packages, trading account or NT8 needed:

```sh
python -B -m unittest discover -s tests -p 'test_*.py' -v
```

These portable checks test packaging/holds and a separate SPEC; they do **not** run the C# copier, a broker or real orders. For Windows Framework-only engine tests and full **compile-only** NT8 checks, see [build instructions](BUILD.md). No vendor assemblies are redistributed. No installer or compiled trading binaries are provided.

## Read before testing

- [Test matrix: untested vs failed vs scoped evidence](TEST-MATRIX.md)
- [**Offline status: done vs needs a real environment**](OFFLINE-STATUS.md)
- [Two-account Sim test **plan** (PLAN ONLY, do not run; trading stays blocked)](SIM-TEST-PLAN.md)
- [Known issues](KNOWN-ISSUES.md)
- [**Do NOT do these things** (actions the software cannot recover from)](DO-NOT-DO.md)
- [Safety hold](SAFETY-HOLD.md)
- [Community roadmap](ROADMAP.md)
- [Contribution workflow](../CONTRIBUTING.md)
- [Testing and redacted bug reports](COMMUNITY-TESTING.md)

Use [Discussions](https://github.com/Kinfxhk/KINFXTradeCopier/discussions) for architecture questions and test-plan proposals; use Issues for reproducible failures and scoped work. Please open an issue with a minimal synthetic trace or failing test, then a focused PR. Start with the [community backlog](https://github.com/Kinfxhk/KINFXTradeCopier/issues):

| Workstream | Issue |
|---|---|
| Clean-clone build / safe test runner | [#1](https://github.com/Kinfxhk/KINFXTradeCopier/issues/1) |
| Fill frontier / protective resize | [#2](https://github.com/Kinfxhk/KINFXTradeCopier/issues/2) |
| Group OCO / fractional allocation | [#3](https://github.com/Kinfxhk/KINFXTradeCopier/issues/3) |
| Mailbox / Dispose / follower isolation | [#4](https://github.com/Kinfxhk/KINFXTradeCopier/issues/4) |
| Numeric drafts / settings publication | [#5](https://github.com/Kinfxhk/KINFXTradeCopier/issues/5) |
| Journal / restart recovery | [#6](https://github.com/Kinfxhk/KINFXTradeCopier/issues/6) |
| Future two-account simulator test plan | [#7](https://github.com/Kinfxhk/KINFXTradeCopier/issues/7) |
| Native UI / accessibility / DPI | [#8](https://github.com/Kinfxhk/KINFXTradeCopier/issues/8) |

**Do not remove the hold just to get a simulation run or a green test.** Simulator acceptance needs a separately reviewed opt-in design and at least a leader plus follower test account; it is not authorized by this alpha.

## Current evidence boundary

Past offline runs tested multiple immutable candidates. Passing those candidates does not mean their fixes are merged here. The published source is based on the previously held original baseline plus publication safety hardening only. Counts are per suite/artifact, not a completion percentage. [Evidence summary](EVIDENCE.json) separates C# engine evidence, UI probes, harness incidents and Python SPEC results.

No full provider acceptance, restart recovery, broker OCO atomicity, native follower ATM, live-copy safety or prop-firm compliance is established. This software cannot intercept every manual/strategy order or guarantee fills. Existing positions must be managed with native NT8/broker tools, not this held preview.
