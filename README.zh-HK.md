# KINFX Trade Copier — 社群共同開發 alpha

[English](README.md) · **廣東話／繁體中文**

<!-- QUICKSTART:START -->
## 可以用嗎？

| | |
|---|---|
| **Demo／模擬帳戶** | **可以，屬早期測試版。** SIM TEST 版支援跟單。已喺真 NinjaTrader 8.1.8.3 測試：1 個 Leader ＋ 2 個 Follower、1 個合約、市價單同限價單。完成度約 **69%**，見下面清單。 |
| **Prop Firm 帳戶** | **只限你自己確認過係「模擬／評估」嘅 Prop Firm 帳戶，而且只用嚟測試。** 已測 2 個呢類帳戶。**未測 funded 帳戶**。完成度約 **62%**。請先睇清楚你間公司對跟單工具嘅規則：本軟件唔保證合規。 |
| **Funded／實盤帳戶** | **唔可以。** 預設版封鎖。請勿加入 allow-list。 |

使用前請知道：呢個係 **alpha**。Follower 斷線重連已喺真 NinjaTrader 測過（只鎖嗰個帳戶，重連後唔補單）；未測：Leader 自己斷線、止損被拒或 Flatten 被拒、止損＋止盈但冇共用 OCO（會鎖帳戶）、10–30 個帳戶、重啟恢復，**未喺真 NinjaTrader 測過**（或者以鎖帳戶方式安全失敗）。請由 **1 手、1 個 Follower、開 Dry run** 開始，並一直開住 NinjaTrader 自己嘅 Orders／Positions 分頁：以嗰度為準，唔係以本視窗為準。佢攔唔到你手動落嘅單，亦唔保證成交。

## 5 步安裝（Windows ＋ NinjaTrader 8）

1. **下載。** `git clone https://github.com/Kinfxhk/KINFXTradeCopier`（或 *Code > Download ZIP* 解壓）。要最新版請用 `release-candidate` 分支：`git checkout release-candidate`。
2. **安裝。** 喺 repo 資料夾用 PowerShell 行：
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\install-simtest.ps1
   ```
   佢會將 SIM TEST 版複製去 `Documents\NinjaTrader 8\bin\Custom\AddOns`，取代舊檔前會備份，並核對檔案 hash，唔會碰任何帳戶。加 `-WhatIf` 先預覽，加 `-Remove` 解除安裝。
3. **編譯。** NinjaTrader：*New > NinjaScript Editor*，按 **F5**，等到 *Compile succeeded*。（出錯代表呢個 add-on 或你 Custom 資料夾入面另一個編譯唔到，請唔好繼續。）
4. **講明邊啲係模擬帳戶。** 預設只有 *Simulator*／*Playback* 帳戶可用。Prop Firm **模擬**帳戶要建立文字檔 `Documents\NinjaTrader 8\templates\KINFXTradeCopier\sim-allow.txt`，一行一個帳戶，名稱同 NinjaTrader 顯示完全一樣，`=` 後面可加別名：
   ```text
   # 只放你自己確認係模擬嘅帳戶
   MYFIRM-SIM-12345 = Firm A
   MYFIRM-SIM-67890 = Firm B
   ```
   寫錯 ＝ 真單落到嗰個帳戶。之後再按一次 F5。
5. **開啟，先做 Dry run。** *New > KINFX Trade Copier*。揀 **Leader**（例如 `Sim101`），輸入合約（例如 `MNQ 12-26`），只勾 **1 個** Follower，Ratio 設 **1**。喺 **Risk** 分頁剔 **Dry run**。按 **Start**：確認框一定要有 `DRY RUN`。喺 Leader 落一張細單，Logs 分頁應該見到 `DRY RUN would send ...`。按 **Stop**。之後先取消 Dry run，用 1 手再試一次。

**要知道：** Leader 平倉後 Follower 嗰行會變 `Off`，要再剔過（設計如此）。**OFF** 只係停止跟隨；每行嘅 **Flat** 會喺確認後平嗰一個帳戶。**Locked** 代表 copier 見到解釋唔到嘅情況：去 NinjaTrader 睇、手動處理，再喺該行嘅齒輪按鈕解鎖。佢唔會重試 Flatten，斷線後亦唔會補單。

有問題？開 issue 並貼**遮蓋過**嘅 Logs（唔好有帳戶名、結餘）。詳細測試範圍：[docs/VM-PROGRESS.md](docs/VM-PROGRESS.md)。
<!-- QUICKSTART:END -->

**預設版交易功能已封鎖。呢個唔係已發佈嘅跟單產品。請勿用於實盤或 funded Prop 帳戶。**

另有一個 **SIM TEST 版**（要主動啟用，同一份源碼加 `KINFX_SIM_TEST` 編譯），可以喺 NinjaTrader **Simulator／Playback** 帳戶之間，以及你喺 allow-list 列明（由你確認係模擬）嘅帳戶之間跟單。已經喺真 NinjaTrader 8.1.8.3 跑過：1 個 Simulator Leader、2 個 Prop Firm **模擬**評估帳戶、1 個合約、市價單同限價單。目前嘅真實證據就係咁多，見下面嘅進度條。

呢個係 Kinfxhk 原創、MIT 授權嘅 NinjaTrader 8 AddOn，提早公開，等大家一齊覆核、重現失敗、改進。我哋唔等功能齊晒先分享。呢個 repo 係開發基線，唔係已經整合晒所有研究修正嘅成品。

## 目標用法（日後）

Leader → 勾選 follower → 每戶設定 Ratio → Start。可選擇一併跟 ATM 出場。大量帳戶（10–30 個）要易管理：主表格緊湊、少按鈕多圖示，少用嘅設定放喺 Risk 分頁。

**Start 同 Flatten 對所有真 NT8 帳戶仍然封鎖。** OFF 唔等於 Flatten。ATM 係指「跟出場單」，唔係複製 follower 原生 ATM。現金顯示 CashValue 同其幣種，唔係權益。冇全局 Ratio。

## 進度：已完成同未完成

證據分開兩種：**離線**數字用假帳戶，只證明邏輯；**真 NinjaTrader** 結果來自跑 NinjaTrader 8.1.8.3 嘅 VM（SIM TEST 版），連同限制列喺 [VM-PROGRESS](docs/VM-PROGRESS.md)。兩者都唔等於 Prop Firm 或實盤驗收。

<!-- PROGRESS:START -->
### 成就：Demo／Prop Firm 跟單完成度

```text
Demo         █████████████████████░░░░░░░░░  68.9%
Prop firm    ███████████████████░░░░░░░░░░░  62.1%
```

✅ 真 NinjaTrader 已測 · 🟡 只有離線測試 · ⬜ 未做。必要項目 ✅：Demo 6/14 · Prop 6/15。

- ✅ 核心跟單：入場／平倉／取消／改單
- ✅ 每戶 Ratio ＋ 最大手數
- ✅ Follower 冇倉唔會鎖死
- ✅ 每個帳戶獨立 Flat 按鈕
- 🟡 批量／全部 Flatten
- ✅ TP／SL 跟隨 ＋ OCO
- 🟡 異常自動鎖帳戶、唔自動重試
- ✅ 斷線重連安全
- 🟡 部分成交處理
- 🟡 改量前成交一致 (#2)
- 🟡 一戶卡住唔影響其他 (#4)
- ⬜ 真 NT 跑 10-30 個帳戶
- ✅ 介面（表格、批量改、鎖定原因） _（可選）_
- 🟡 每戶每日目標／虧損上限
- ⬜ 反向時平倉一次 (#12) _（可選）_
- ⬜ 承接現有倉位 (#11) _（可選）_
- ⬜ 每戶自管 TP/SL (#10) _（可選）_
- ⬜ 重啟恢復 (#6，擱置) _（可選）_
- ⬜ 安裝包 ＋ 覆核後發佈
- ⬜ Prop Firm 規則檢查（本軟件唔保證）

目前真實測試：1 個模擬 Leader、2 個**模擬** Prop Firm 評估帳戶、MNQ、市價＋限價。從未用過 funded 或實盤帳戶。每項詳情同限制：[`docs/PROGRESS.json`](docs/PROGRESS.json)。計分按權重：✅ 100%、🟡 50%、只有設計 10%。快照 2026-10-13，源碼 `20dc6707c9e6`。
<!-- PROGRESS:END -->

### 已完成（已在本 repo）

- 三個分頁（Copy／Risk／Logs），為 10–30 個帳戶設計；每行有獨立 Ratio、Tgt（每日目標）、DLL（每日虧損）、ATM 出場跟隨、OFF。
- 批量改 Tgt／DLL／ATM（只改選中帳戶）；**From Leader** 以 Leader 做一次性預設，之後可逐戶微調（Ratio 同勾選唔會被改）。
- 每戶「反向時反應」設定：`Notify`（預設，只通知）或 `FlattenOnce`。**只係儲存**，引擎唔會自動平倉；揀咗 FlattenOnce 嘅行會顯示「呢個 alpha 未生效」。
- 設定警告、鎖定帳戶計數、數值欄位草稿保護。
- 已定安全政策（[#9](https://github.com/Kinfxhk/KINFXTradeCopier/issues/9)）：持倉但讀到 flat，唔取消唯一保護單；Flatten 拋例外後，唔複製新出場單、唔重試，鎖帳戶並通知。代價：該帳戶可能有倉位但冇出場單，要人手處理。
- 多組 OCO 修正（[#3](https://github.com/Kinfxhk/KINFXTradeCopier/issues/3)，部分）：已寫失敗測試先行，並經獨立覆核；覆核搵到我第一版嘅回歸，已改正後先發佈。
- Mailbox 修正（[#4](https://github.com/Kinfxhk/KINFXTradeCopier/issues/4)，部分）：worker 停止後被拒絕嘅回報、輪詢、跟單請求、平倉請求，唔再令計數或旗標卡死。已獨立覆核。
- **每戶獨立 Flat 按鈕**：每個 follower 行一個，確認框列明帳戶同合約，只平該戶，其他戶繼續跟；要 provider 證據（倉位 0、冇 working 單、靜置 3 秒）先確認已平，唔重試，20 秒未確認就鎖。離線同真 NinjaTrader 都測過。
- **修正鎖死問題**：兩次來回交易後，leader 平倉但 follower 冇倉，10 秒後會鎖死兩個 follower。離線重現（要兩次來回先觸發）、修正，並喺真 NinjaTrader 重放，冇再鎖。
- 斷線重連同部分成交測試（8 個離線測試，每個都有 mutant）：唔補單、保護單保留、唔補差額。
- 預設版所有 provider 交易封鎖；可重現測試工具、CI、12 個 Issue、截圖、中英安全說明。

最新離線結果（源碼 `28bc4bb3bc47`，VM 上同樣重現）：C# engine **207 PASS／0 FAIL**；pure 32／32；獨立覆核套件 24 PASS／1 已知 FAIL（[#6](https://github.com/Kinfxhk/KINFXTradeCopier/issues/6)）；Dashboard UI **45／45**；Python 457 OK；完整 AddOn compile-only OK。數字係「次數」，唔係完成百分比；上面嘅進度條先係完成度估算。詳情見 [TEST-MATRIX](docs/TEST-MATRIX.md)。

### 未完成

- **真 NinjaTrader 測試仍然好少**：1 個 Leader、2 個 Follower、1 個合約（MNQ）、市價、限價同止損單（含每戶 Flat 配 TP／SL，以及 Leader 市價平倉時取消 follower 嘅 TP／SL）。未真測：Leader 斷線、部分成交、止損＋止盈但冇共用 OCO（會鎖帳戶）、Flatten 被拒、滑點、關閉 workspace 及重啟、多螢幕 DPI、10–30 個帳戶：兩個 Demo 帳戶 Leader→Follower、真實 provider 回報、斷線重連、滑點、關閉 workspace 及重啟、真實多螢幕 DPI、30 戶實際延遲。
- **預設版 Start 同 Flatten 仍然封鎖**；只有主動啟用嘅 SIM TEST 版，先可以喺 Simulator／Playback 帳戶或你 allow-list 內嘅帳戶跟單。冇安裝包，亦冇簽署發佈版。
- 保護單改量前，成交資料一致性（[#2](https://github.com/Kinfxhk/KINFXTradeCopier/issues/2)）。
- 多組 OCO（[#3](https://github.com/Kinfxhk/KINFXTradeCopier/issues/3)）：一組完成唔再取消另一組仍開放嘅保護，窄範圍修正**已合併**。**仍未完成：**入場未全部成交時一組完成，帳戶會鎖定，另一組等待中嘅出場單唔會送出（倉位冇保護，要人手處理；鎖定文字會明確寫出「X 手冇保護」同叫你去 NinjaTrader 處理，本軟件唔會自己建單）；出場分配。
- 一個卡住嘅 follower 唔再凍結其他戶（[#4](https://github.com/Kinfxhk/KINFXTradeCopier/issues/4)，已合併，只喺離線假環境驗證）。**仍未完成：**真 NinjaTrader 對呢啲呼叫嘅線程規則未驗證；Dispose 最多等 3 秒。
- Ratio／Max／Target／DLL 打錯嘅字會保留、標示並阻止提交，唔會被悄悄換走（[#5](https://github.com/Kinfxhk/KINFXTradeCopier/issues/5)，部分已合併）。**仍未完成：**Risk 對話框其他欄位只喺按 Apply 時檢查；高精度 XML 往返；真實視窗焦點、輸入法、貼上。
- Journal 同重啟後可驗證恢復（[#6](https://github.com/Kinfxhk/KINFXTradeCopier/issues/6)）：**維護者暫時擱置**。
- 每戶自管 TP／SL、唔跟 Leader 平倉（[#10](https://github.com/Kinfxhk/KINFXTradeCopier/issues/10)）：未開始。
- 新連接帳戶承接現有倉位再跟方向（[#11](https://github.com/Kinfxhk/KINFXTradeCopier/issues/11)）：未做。
- 反向時真正嘅反應，例如平倉一次（[#12](https://github.com/Kinfxhk/KINFXTradeCopier/issues/12)）：只有設定，冇行為。
- 原生 follower ATM 複製、安裝包、編譯版本、prop firm 合規檢查。

### 而家可以安全做嘅事

睇代碼、跑離線測試、覆核或重現失敗、提交細而專注嘅 PR。**唔好**為咗試下而強行解除交易封鎖。

## 介面截圖

由真實 `DashboardView` 喺合成 DEMO 模式渲染：冇 NinjaTrader 帳戶、冇引擎、冇落單。帳戶名同數字全部係假。來源：[`docs/screenshots/Screenshots.cs`](docs/screenshots/Screenshots.cs)。

| Copy | Risk | Logs |
|---|---|---|
| ![Copy 分頁](docs/screenshots/1-copy.png) | ![Risk 分頁](docs/screenshots/2-risk.png) | ![Logs 分頁](docs/screenshots/3-logs.png) |

## 安全參與

需要 Python 3.10+；唔需要額外 Python 套件、交易帳戶或 NT8：

```sh
python -B -m unittest discover -s tests -p 'test_*.py' -v
```

呢啲可攜檢查只驗證打包同封鎖，以及獨立 SPEC，**唔會**運行 C# 跟單引擎、券商或真實訂單。Windows 上嘅 Framework-only 引擎測試同完整 NT8 只編譯檢查，見[建置說明](docs/BUILD.md)。本 repo 唔包含任何廠商 DLL。

## 先睇再測試

- [測試矩陣：未測試／失敗／有限範圍證據](docs/TEST-MATRIX.md)
- [**離線進度：已完成 vs 仍需真環境**](docs/OFFLINE-STATUS.md)
- [雙帳戶 Sim 測試**計劃**（只係計劃，唔好跑；交易仍然封鎖）](docs/SIM-TEST-PLAN.md)
- [已知問題](docs/KNOWN-ISSUES.md)
- [**絕對唔好做嘅事**（軟件無法恢復嘅操作）](docs/DO-NOT-DO.md)
- [安全封鎖](docs/SAFETY-HOLD.md)
- [社群路線圖](docs/ROADMAP.md)
- [貢獻流程](CONTRIBUTING.md)
- [測試同匿名 bug 報告](docs/COMMUNITY-TESTING.md)

架構同測試計劃請用 [Discussions](https://github.com/Kinfxhk/KINFXTradeCopier/discussions)；可重現失敗同分工請開 Issue，附最小合成 trace 或失敗測試，再提交專注嘅 PR。

| 工作 | Issue |
|---|---|
| 乾淨 clone 建置／安全測試工具 | [#1](https://github.com/Kinfxhk/KINFXTradeCopier/issues/1) |
| 成交一致性／保護單改量 | [#2](https://github.com/Kinfxhk/KINFXTradeCopier/issues/2) |
| 多組 OCO／出場分配 | [#3](https://github.com/Kinfxhk/KINFXTradeCopier/issues/3) |
| Mailbox／Dispose／各戶隔離 | [#4](https://github.com/Kinfxhk/KINFXTradeCopier/issues/4) |
| 數值草稿／設定發佈 | [#5](https://github.com/Kinfxhk/KINFXTradeCopier/issues/5) |
| Journal／重啟恢復 | [#6](https://github.com/Kinfxhk/KINFXTradeCopier/issues/6) |
| 日後兩帳戶模擬器測試計劃 | [#7](https://github.com/Kinfxhk/KINFXTradeCopier/issues/7) |
| 原生 UI／無障礙／DPI | [#8](https://github.com/Kinfxhk/KINFXTradeCopier/issues/8) |
| 平倉後及 Flatten 失敗後政策 | [#9](https://github.com/Kinfxhk/KINFXTradeCopier/issues/9) |
| 每戶自管 TP／SL | [#10](https://github.com/Kinfxhk/KINFXTradeCopier/issues/10) |
| 新連接帳戶承接倉位 | [#11](https://github.com/Kinfxhk/KINFXTradeCopier/issues/11) |
| 反向時反應：只通知 vs 平倉一次 | [#12](https://github.com/Kinfxhk/KINFXTradeCopier/issues/12) |

**唔好為咗跑到模擬或者令測試變綠而移除封鎖。** Sim 驗收需要另行覆核、明確選用嘅設計，並至少一個 Leader 加一個 follower 測試帳戶；呢個 alpha 並未授權。

## 證據範圍

過往離線測試測過多個不可變候選版本。通過嗰啲候選唔代表佢哋嘅修正已合併入呢度。已發佈源碼係先前封鎖嘅原創基線，加上發佈安全加固，再加上本頁列出嘅有限修正。數字係按套件／產物計，唔係完成百分比。[證據摘要](docs/EVIDENCE.json)分開列出 C# 引擎、UI、測試工具事故同 Python SPEC 結果。

未驗證：完整 provider 驗收、重啟恢復、券商 OCO 原子性、原生 follower ATM、真倉跟單安全、Prop firm 合規。本軟件無法攔截所有手動／策略訂單，亦無法保證成交。現有倉位請用 NT8／券商原生工具管理，唔好靠呢個封鎖版。

## 維護者連結同披露

維護者：KinFx。Prop firm 規則研究、比較表、工具連結同推薦碼：**<https://linktr.ee/kinfx>**

該頁部分係**聯盟（affiliate）／推薦連結**（例如考試優惠碼同夥伴註冊），你用咗維護者可能獲得佣金。佢哋**同本軟件冇關係**：代碼係 MIT，唔含推薦或追蹤邏輯，呢度亦唔推薦任何公司或消費。KinFx 非證監會持牌，內容非投資建議，考試帳戶多數係模擬。本項目同該頁列出嘅公司無關聯。

## 授權同聲明

[MIT](LICENSE)，© 2026 Kinfxhk。同 NinjaTrader、FlowBots／Replikanto、任何券商或 Prop firm 冇關聯。唔包含競爭對手代碼、廠商 DLL、下載嘅研究 HTML、私人帳戶日誌或憑證。源碼內嘅 Prop 範本係歷史可編輯資料，唔係現行規則或合規證明。
