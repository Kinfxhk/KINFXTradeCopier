# KINFX Trade Copier — 社群共同開發 alpha

[English](README.md) · **廣東話／繁體中文**

**交易功能已封鎖。呢個唔係可用嘅跟單產品。請勿用於真倉、Prop、Sim 或 Playback 落單。**

呢個係 Kinfxhk 原創、MIT 授權嘅 NinjaTrader 8 AddOn，提早公開，等大家一齊覆核、重現失敗、改進。我哋唔等功能齊晒先分享。呢個 repo 係開發基線，唔係已經整合晒所有研究修正嘅成品。

## 目標用法（日後）

Leader → 勾選 follower → 每戶設定 Ratio → Start。可選擇一併跟 ATM 出場。大量帳戶（10–30 個）要易管理：主表格緊湊、少按鈕多圖示，少用嘅設定放喺 Risk 分頁。

**Start 同 Flatten 對所有真 NT8 帳戶仍然封鎖。** OFF 唔等於 Flatten。ATM 係指「跟出場單」，唔係複製 follower 原生 ATM。現金顯示 CashValue 同其幣種，唔係權益。冇全局 Ratio。

## 進度：已完成同未完成

以下全部係**離線合成測試**（假帳戶，從未向 NinjaTrader 落過單），唔等於真實環境驗收。

### 已完成（已在本 repo）

- 三個分頁（Copy／Risk／Logs），為 10–30 個帳戶設計；每行有獨立 Ratio、Tgt（每日目標）、DLL（每日虧損）、ATM 出場跟隨、OFF。
- 批量改 Tgt／DLL／ATM（只改選中帳戶）；**From Leader** 以 Leader 做一次性預設，之後可逐戶微調（Ratio 同勾選唔會被改）。
- 每戶「反向時反應」設定：`Notify`（預設，只通知）或 `FlattenOnce`。**只係儲存**，引擎唔會自動平倉；揀咗 FlattenOnce 嘅行會顯示「呢個 alpha 未生效」。
- 設定警告、鎖定帳戶計數、數值欄位草稿保護。
- 已定安全政策（[#9](https://github.com/Kinfxhk/KINFXTradeCopier/issues/9)）：持倉但讀到 flat，唔取消唯一保護單；Flatten 拋例外後，唔複製新出場單、唔重試，鎖帳戶並通知。代價：該帳戶可能有倉位但冇出場單，要人手處理。
- 多組 OCO 修正（[#3](https://github.com/Kinfxhk/KINFXTradeCopier/issues/3)，部分）：已寫失敗測試先行，並經獨立覆核；覆核搵到我第一版嘅回歸，已改正後先發佈。
- Mailbox 修正（[#4](https://github.com/Kinfxhk/KINFXTradeCopier/issues/4)，部分）：worker 停止後被拒絕嘅回報、輪詢、跟單請求、平倉請求，唔再令計數或旗標卡死。已獨立覆核。
- 所有 provider 交易封鎖；可重現測試工具、CI、12 個 Issue、截圖、中英安全說明。

最新已發佈離線結果：C# engine **111 PASS／0 FAIL**；pure 12／12；獨立覆核套件 24 PASS／1 已知 FAIL（[#6](https://github.com/Kinfxhk/KINFXTradeCopier/issues/6)）；Dashboard UI 32／32；Python 450 OK；完整 AddOn 只編譯成功（從未載入執行）。詳情同限制見 [TEST-MATRIX](docs/TEST-MATRIX.md)。

### 未完成

- **完全未用真 NinjaTrader 帳戶測試**（連 Sim／Playback 都未）：兩個 Demo 帳戶 Leader→Follower、真實 provider 回報、斷線重連、滑點、關閉 workspace 及重啟、真實多螢幕 DPI、30 戶實際延遲。
- **Start 同 Flatten 全部封鎖**：所以冇任何跟單，亦冇一鍵全平。
- 保護單改量前，成交資料一致性（[#2](https://github.com/Kinfxhk/KINFXTradeCopier/issues/2)）。
- 多組 OCO（[#3](https://github.com/Kinfxhk/KINFXTradeCopier/issues/3)）：一組完成唔再取消另一組仍開放嘅保護，窄範圍修正**已合併**。**仍未完成：**入場未全部成交時一組完成，帳戶會鎖定，另一組等待中嘅出場單唔會送出（倉位冇保護，要人手處理）；出場分配。
- 一個卡住嘅 follower 仍可能阻住其他戶；Dispose 跨多輪排乾（[#4](https://github.com/Kinfxhk/KINFXTradeCopier/issues/4)）。
- 全部數值草稿同設定發佈（[#5](https://github.com/Kinfxhk/KINFXTradeCopier/issues/5)）。
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
