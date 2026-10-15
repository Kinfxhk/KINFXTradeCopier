# KINFX Trade Copier

在 NinjaTrader 8 中，將一個帳戶的交易複製至多個帳戶（Prop Firm **模擬**帳戶及 Demo 帳戶）。免費、開源（MIT），屬早期**源碼 alpha**。

[中英對照](README.md) · **中文（繁體）** · [English](README.en.md) · [日本語](README.ja.md) · [한국어](README.ko.md)

介面語言與程式一致：English、中文（繁體）、日本語、한국어。本文為香港書面語。

介面中 Leader 顯示為「主帳戶」，Follower 顯示為「跟單帳戶」，Dry run 顯示為「試運行」，Flat 顯示為「平倉」。以下步驟沿用確認框中的原文。

> **預設版已停用交易。Funded／實盤帳戶：禁止使用。** 本項目是源碼 alpha，**目前源碼尚未驗收**，不能視為已供一般模擬／provider 使用的版本。主動選用的 SIM TEST 版可以向 Simulator／Playback 及操作員 allow-list 帳戶發送訂單，但此能力不是安全驗收。

<!-- QUICKSTART:START -->
## 可以用嗎？

| | |
|---|---|
| **Demo／模擬帳戶** | **尚未就緒：目前候選版本未驗收。** 完成度約 **83%**，僅指加權歷史檢查清單，並非發佈就緒百分比。 |
| **Prop Firm 帳戶** | **尚未就緒：目前候選版本未驗收。** 完成度約 **75%**，僅指同一歷史檢查清單。源碼 `c9fbf2cb3005`（SIM TEST 版）已於 2026-10-14 在真實 NinjaTrader 上，以 11 個操作員確認的模擬／評估帳戶執行；此非發佈驗收，亦非公司合規認證。 |
| **Funded／實盤帳戶** | **禁止使用。** |
<!-- QUICKSTART:END -->

## 進度

<!-- PROGRESS:START -->
### 進度：Demo／Prop Firm 跟單完成度

```text
Demo         █████████████████████████░░░░░  83.0%
Prop firm    ██████████████████████░░░░░░░░  74.7%
```

✅ 已在真實 NinjaTrader 測試 · 🟡 只有離線測試 · ⬜ 尚未完成。必要項目 ✅：Demo 10/14 · Prop 10/15。

以下為歷史清單證據，不代表目前候選版本已通過驗收。目前候選版本尚未通過驗收，不可宣稱可用。安全 P0／驗收關卡：issue-3, issue-4, issue-7, issue-9。Demo ready=False；Prop ready=False。不包括 funded／實盤驗收。

- ✅ 核心跟單：入場／平倉／取消／改單
- ✅ 每戶 Ratio ＋ 最大手數
- ✅ Follower 無持倉時不會鎖定
- ✅ 每個帳戶獨立 Flat 按鈕
- ✅ 批量／全部 Flatten
- ✅ TP／SL 跟隨 ＋ OCO
- ✅ 異常自動鎖定帳戶，不會自動重試
- ✅ 斷線重連安全
- 🟡 部分成交處理
- 🟡 改量前成交一致 (#2)
- 🟡 單一帳戶停滯不影響其他帳戶 (#4)
- ✅ 真實 NT 的 10–30 個帳戶測試
- ✅ 介面（表格、批量改、鎖定原因） _（可選）_
- ✅ 每戶每日目標／虧損上限
- ⬜ 反向時平倉一次 (#12) _（可選）_
- ⬜ 承接現有倉位 (#11) _（可選）_
- ⬜ 每戶自管 TP/SL (#10) _（可選）_
- ⬜ 重啟恢復 (#6，擱置) _（可選）_
- ⬜ 安裝包 ＋ 覆核後發佈
- ⬜ Prop Firm 規則檢查（本軟件不作認證）

歷史真實測試：1 個模擬 Leader、最多 11 個**模擬** Prop Firm 評估帳戶（Lucid 5 個、Tradeify 6 個）、MNQ、市價＋限價＋停損單、TP/SL OCO。從未使用 funded 或實盤帳戶。各項詳情與限制：[`docs/PROGRESS.json`](docs/PROGRESS.json)。計分按權重：✅ 100%、🟡 50%、只有設計 10%。快照 2026-10-15，源碼 `8a8deb196fa8`。
<!-- PROGRESS:END -->

勾號及分數是**加權歷史檢查清單**，**並非發佈就緒百分比**。最近一次真實 NinjaTrader 測試（2026-10-15）使用源碼 `bfc8a2531acf`（SIM TEST 版），屬測試證據，不是目前確切源碼的驗收。[各項證據與限制](docs/PROGRESS.json) · [歷史 NT 測試記錄](docs/VM-PROGRESS.md) · [目前離線結果與安全阻礙](docs/DEVELOPERS.zh-HK.md)。

## 安裝（Windows ＋ NinjaTrader 8；僅限已覆核測試候選版本）

**以下供參考，不是一般發佈的使用邀請。** 不要僅因編譯成功便安裝或使用目前未驗收候選版本交易；應先覆核確切源碼與安全關卡。

**最簡單：直接匯入 NinjaTrader。** 從 [v0.2.0-simtest-alpha](https://github.com/Kinfxhk/KINFXTradeCopier/releases/tag/v0.2.0-simtest-alpha) 發佈頁下載 `KINFXTradeCopier-SIMTEST.zip`（由 NinjaTrader 8 本身匯出，毋須解壓；發佈說明列出其 SHA-256）。在 NinjaTrader 選擇 *Control Center > Tools > Import > NinjaScript Add-On...*，選取該 zip，NinjaTrader 詢問是否信任來源時按 **Yes**（若提示檔案已存在，按 **Yes** 只會取代本外掛的舊版本），按提示重新啟動 NinjaTrader，再開啟 *New > KINFX Trade Copier*，然後由下方第 4 步繼續。

**或從源碼安裝：**

1. **下載。** 在本頁選擇 *Code > Download ZIP* 並解壓。（或執行 `git clone https://github.com/Kinfxhk/KINFXTradeCopier`。）
2. **安裝。** 在解壓後的資料夾，以 PowerShell 執行：
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\install-simtest.ps1
   ```
   腳本會將 SIM TEST 源碼複製至 `Documents\NinjaTrader 8\bin\Custom\AddOns`，取代舊檔前會備份，不會操作帳戶。`-WhatIf` 可預覽，`-Remove` 可移除。
3. **編譯。** 在 NinjaTrader 選擇 *New > NinjaScript Editor*，按 **F5**，等待 *Compile succeeded*。若出錯，請停止。
4. **帳戶自動載入。** 開啟 copier 後會列出所有 NinjaTrader 帳戶，可直接勾選，無須輸入帳戶名稱。首次對 copier 尚未確認為模擬的帳戶（Simulator／Playback 以外，例如 Prop Firm 評估帳戶）按 **Start** 或 **Flatten** 時，會顯示**列明該帳戶的風險警告**：KINFX 無法分辨評估帳戶與已注資或實盤帳戶。按**是**＝您確認為模擬帳戶，帳戶會寫入 `Documents\NinjaTrader 8\templates\KINFXTradeCopier\sim-allow.txt`，以後不再詢問；按**否**＝不儲存、不發送任何指令。此警告不能關閉。如需再次詢問，請從檔案刪除該行。

   仍可手動填寫檔案。請**先建立資料夾** `Documents\NinjaTrader 8\templates\KINFXTradeCopier`（安裝腳本**不會建立此資料夾**），每行填寫一個 NinjaTrader Account `Name`，區分大小寫、須完全相符，不能使用登入名稱或顯示別名（`= 別名` 為可選）：
   ```text
   # 僅列出操作員已確認為模擬的帳戶
   MYFIRM-SIM-12345 = Firm A
   MYFIRM-SIM-67890 = Firm B
   ```
   錯誤名稱可能授權向 funded／實盤帳戶發送訂單：這是操作員聲明，不是 provider 認證。手動修改的檔案只在**靜態初始化**時讀取，不會因重新開啟儀表板而重讀：修改後請保持停止及已平倉，關閉 copier 視窗，再**重新啟動 NinjaTrader**。**僅按 F5** 並不保證即時重載。（在風險警告中的回答會即時生效。）

## 第一次使用

1. *New > KINFX Trade Copier*。以下是另行覆核 SIM TEST 候選版本的演練清單，**並非允許使用目前未驗收源碼交易**。
2. 選擇 **Leader**（例如 `Sim101`）並輸入確切合約（例如 `MNQ 12-26`）。在 NinjaTrader 原生 Orders／Positions 分頁確認 Leader 及所有已選 Follower **已連線**，該商品的**持倉為零且沒有掛單**。Start 不會承接現有倉位或補單。
3. 僅勾選 **1 個** Follower，Ratio 設為 **1**。在 **Risk** 分頁勾選 **Dry run**。
4. 按 **Start**，核對 Leader、Follower、合約、Ratio 及 `DRY RUN` 確認內容。Leader 的小量模擬訂單應令 Logs 顯示 `DRY RUN would send ...`。Dry run 只阻止 copier 發送，**不會阻止您在 Leader 下單**。停止之前，**使用 NinjaTrader 原生工具取消 Leader 掛單並平掉 Leader 持倉**，確認 Leader 及 Follower 均已平倉且沒有掛單，再按 **Stop** 並確認工作階段解除連接。如果拒絕停止，請中止演練並調查，不要強行解鎖。
5. **只有在工作階段已解除連接後**才可取消勾選 Dry run。發送 1 手模擬訂單仍須另行覆核及授權確切源碼，並重新閱讀**沒有** `DRY RUN` 的 Start 確認；目前候選版本未通過此關卡。**僅在停止時新增 Follower**，再次核對所有已選帳戶已連線且狀態乾淨，並閱讀涵蓋完整範圍的**新 Start 確認**。

![Copy 分頁 — 合成 DEMO](docs/screenshots/1-copy.png)

**截圖來源：**使用實際儀表板控制項，以**合成 DEMO** 模式呈現**虛構帳戶與數值**，未連接帳戶、引擎或訂單。截圖**並非 NinjaTrader／provider 交易證據**。[呈現程式](docs/screenshots/Screenshots.cs) · [Risk／Logs 圖庫與源碼連結](docs/DEVELOPERS.zh-HK.md#dashboard-gallery-and-source)。

每行都有獨立的 **Ratio、每日目標（Tgt）、每日虧損（DLL）、ATM 出場跟隨、OFF、Flat**。**OFF** 停止跟隨並請求取消自有入場單，但保留持倉及出場單；**Flat** 會在確認後平掉該帳戶。Leader 出場後，Follower 會轉為 `Off`，須再次明確勾選。

## 使用限制

- 本項目是**源碼 alpha**，不是已一般驗收的交易版本。NinjaTrader 8.1.8.3 測試（最近一次 2026-10-15，源碼 `bfc8a2531acf`）使用 1 個模擬 Leader、最多 11 個操作員確認的 Prop Firm 模擬 Follower 及 MNQ。**尚未證明：**20–30 戶、Leader 斷線、真實部分成交、provider 呼叫停滯、Flatten 被拒及重啟恢復。離線編譯／測試不能證明以上行為。
- **Locked** 須在原生 NinjaTrader 調查並核對。風控鎖存重設**不等於**核對不確定的引擎 ledger、隱藏已接受請求或拋例外的 Flatten。仍缺少明確、可驗證已平倉的恢復關卡（[#9](https://github.com/Kinfxhk/KINFXTradeCopier/issues/9)，已知 FAIL `ExplicitManualFlatReconciliationAvailable`）；若證據仍不確定，請保持未啟動／鎖定並尋求覆核。copier 不會重試 Flatten 或在斷線後補單。
- 以 NinjaTrader 原生 Orders／Positions 分頁為操作依據，而非此視窗。本軟件不能阻止手動下單或保證成交。
- 不得為了試用而移除預設版安全封鎖。

遇到問題？請開立 [issue](https://github.com/Kinfxhk/KINFXTradeCopier/issues) 並附上**已遮蓋敏感資料**的 Logs（不含帳戶名稱、結餘）。

## 更多

[開發者與狀態詳情](docs/DEVELOPERS.zh-HK.md) · [請勿執行的操作](docs/DO-NOT-DO.md) · [已知問題](docs/KNOWN-ISSUES.md) · [測試矩陣](docs/TEST-MATRIX.md) · [路線圖](docs/ROADMAP.md) · [參與貢獻](CONTRIBUTING.md) · [安全](SECURITY.md)

開發者：可攜檢查（`python -B -m unittest discover -s tests -p 'test_*.py' -v`）涵蓋打包，以及另一套 Python SPEC 研究。SPEC 不是 C# 引擎實作；這些檢查不會運行跟單程式、券商或真實訂單。

## 維護者連結與披露

維護者：KinFx。Prop Firm 規則研究、比較表、工具連結及推薦碼：**<https://linktr.ee/kinfx>**

該頁部分是**聯盟（affiliate）／推薦連結**（例如評估優惠碼及夥伴註冊），使用後維護者可能獲得佣金。這些連結**不影響本軟件**：代碼採用 MIT 授權，不含推薦或追蹤邏輯，本頁亦不推薦任何公司或購買。KinFx 並非 SEC／SFC 持牌顧問，內容不構成投資意見。大部分評估帳戶為模擬帳戶。本項目與該頁列出的公司沒有關聯。

## 授權

[MIT](LICENSE)，© 2026 Kinfxhk。與 NinjaTrader、FlowBots／Replikanto、任何券商或 Prop Firm 沒有關聯。不包含競爭對手代碼、廠商 DLL、私人帳戶日誌或憑證。源碼內的 Prop 範本是歷史可編輯資料，不是現行規則或合規認證。
