# KINFX Trade Copier

喺 NinjaTrader 8 入面，將一個帳戶嘅交易複製去多個帳戶（Prop Firm **模擬**帳戶同 Demo 帳戶）。免費、開源（MIT）、早期 **alpha**。

[English](README.md) · **廣東話／繁體中文**

> **Funded／實盤帳戶：請勿使用。** 預設版已封鎖。只有要主動啟用嘅 SIM TEST 版先會跟單，而且只限你確認過係模擬嘅帳戶。

<!-- QUICKSTART:START -->
## 可以用嗎？

| | |
|---|---|
| **Demo／模擬帳戶** | **可以，屬早期測試版。** 完成度約 **69%**。 |
| **Prop Firm 帳戶** | **只限你自己確認過係「模擬／評估」嘅 Prop Firm 帳戶，只用嚟測試。** 完成度約 **62%**。已測 2 個呢類帳戶。請先睇清楚你間公司對跟單工具嘅規則：本軟件唔保證合規。 |
| **Funded／實盤帳戶** | **唔可以。** |
<!-- QUICKSTART:END -->

## 進度

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

每一項嘅證據同限制：[docs/PROGRESS.json](docs/PROGRESS.json)。真 NinjaTrader 測試記錄：[docs/VM-PROGRESS.md](docs/VM-PROGRESS.md)。

## 安裝（Windows ＋ NinjaTrader 8，約 5 分鐘）

1. **下載。** 喺本頁撳 *Code > Download ZIP*，解壓。（或者 `git clone https://github.com/Kinfxhk/KINFXTradeCopier`。）
2. **安裝。** 喺解壓出嚟嘅資料夾用 PowerShell 行：
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\install-simtest.ps1
   ```
   佢會將 SIM TEST 版複製去 `Documents\NinjaTrader 8\bin\Custom\AddOns`，取代舊檔前會備份，唔會碰任何帳戶。加 `-WhatIf` 先預覽，加 `-Remove` 解除安裝。
3. **編譯。** NinjaTrader：*New > NinjaScript Editor*，按 **F5**，等到 *Compile succeeded*。出錯就唔好繼續。
4. **寫明邊啲係模擬帳戶。** *Simulator*／*Playback* 帳戶直接可用。Prop Firm **模擬**帳戶要建立 `Documents\NinjaTrader 8\templates\KINFXTradeCopier\sim-allow.txt`，一行一個帳戶，名稱同 NinjaTrader 嘅 *Account name* 欄完全一樣（`=` 後面可加別名）：
   ```text
   # 只放你自己確認係模擬嘅帳戶
   MYFIRM-SIM-12345 = Firm A
   MYFIRM-SIM-67890 = Firm B
   ```
   寫錯 ＝ 真單落到嗰個帳戶。之後再按一次 F5。

## 第一次使用

1. *New > KINFX Trade Copier*。
2. 揀 **Leader**（例如 `Sim101`），輸入合約（例如 `MNQ 12-26`）。
3. 只勾 **1 個** Follower，Ratio 設 **1**。喺 **Risk** 分頁剔 **Dry run**。
4. 按 **Start**，確認框一定要有 `DRY RUN`。喺 Leader 落一張細單，Logs 分頁應該見到 `DRY RUN would send ...`。按 **Stop**。
5. 取消 Dry run，用 1 手再試一次，之後先加多啲 Follower。

![Copy 分頁](docs/screenshots/1-copy.png)

每行都有獨立嘅 **Ratio、每日目標（Tgt）、每日虧損（DLL）、ATM 出場跟隨、OFF、Flat**。**OFF** 只係停止跟隨；**Flat** 會喺你確認後平嗰一個帳戶。Leader 平倉後 Follower 會變 `Off`，要再剔過（設計如此）。

## 要知道嘅限制

- 呢個係 **alpha**。已喺真 NinjaTrader 8.1.8.3 測試：1 個 Leader、2 個 Follower、1 個合約（MNQ），市價、限價、止損單。**未喺真環境測：** 10–30 個帳戶、Leader 自己斷線、止損或 Flatten 被拒、重啟恢復。
- **Locked** 代表 copier 見到解釋唔到嘅情況。去 NinjaTrader 睇、手動處理，再喺該行嘅齒輪按鈕解鎖。佢唔會重試 Flatten，斷線後亦唔會補單。
- 以 NinjaTrader 自己嘅 Orders／Positions 分頁為準，唔係呢個視窗。佢攔唔到你手動落嘅單，亦唔保證成交。
- 唔好為咗「試下」而拆走預設版嘅安全封鎖。

有問題？開 [issue](https://github.com/Kinfxhk/KINFXTradeCopier/issues) 並貼**遮蓋過**嘅 Logs（唔好有帳戶名、結餘）。

## 更多

[開發者同進度詳情](docs/DEVELOPERS.zh-HK.md) · [唔好做嘅事](docs/DO-NOT-DO.md) · [已知問題](docs/KNOWN-ISSUES.md) · [測試矩陣](docs/TEST-MATRIX.md) · [路線圖](docs/ROADMAP.md) · [參與貢獻](CONTRIBUTING.md) · [安全](SECURITY.md)

## 維護者連結同披露

維護者：KinFx。Prop firm 規則研究、比較表、工具連結同推薦碼：**<https://linktr.ee/kinfx>**

該頁部分係**聯盟（affiliate）／推薦連結**（例如考試優惠碼同夥伴註冊），你用咗維護者可能獲得佣金。佢哋**同本軟件冇關係**：代碼係 MIT，唔含推薦或追蹤邏輯，呢度亦唔推薦任何公司或購買。KinFx 並非 SEC／SFC 持牌顧問，內容不構成投資意見。大部分評估帳戶係模擬帳戶。本項目同該頁列出嘅任何公司冇關聯。

## 授權

[MIT](LICENSE)，© 2026 Kinfxhk。同 NinjaTrader、FlowBots／Replikanto、任何券商或 Prop firm 冇關聯。唔包含競爭對手代碼、廠商 DLL、私人帳戶日誌或憑證。源碼內嘅 Prop 範本係歷史可編輯資料，唔係現行規則或合規認證。
