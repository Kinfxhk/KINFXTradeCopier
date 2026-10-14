# 開發者與狀態詳情

返回 [README](../README.zh-HK.md) · [English](DEVELOPERS.md)。

## 驗收邊界

本項目是 MIT **源碼 alpha**，不是已獲一般模擬／provider 驗收的發佈版。**目前確切源碼尚未驗收。** 預設版對所有原生 NT 帳戶封鎖 Start／Flatten，包含 Simulator／Playback。主動選用的 SIM TEST 版可以向 Simulator／Playback 及操作員 allow-list 中的確切帳戶名稱發送訂單；此能力並非 provider 認證或安全驗收。**禁止用於 funded／實盤帳戶。**

首頁分數是**加權歷史檢查清單**，並非發佈就緒百分比。真 NT 勾號指向舊快照 `20dc6707c9e6`，不能視為後續引擎變更的驗收。[歷史 VM 記錄](VM-PROGRESS.md)包含 1 個模擬 Leader、2 個操作員確認的模擬 Prop Firm Follower，使用 MNQ、市價／限價／止損單、逐戶 Flat、TP／SL OCO、Leader 市價出場及 Follower 斷線重連。確實已有真實模擬測試，但範圍有限，並非目前源碼的驗收。

## 目前離線證據

源碼：[`src/AddOns/KINFXTradeCopier.cs`](../src/AddOns/KINFXTradeCopier.cs)，SHA-256：

```text
d70b8a49ceeb87e2207fe5c9811203723ea7f0edae90d18ebeab334a10e210a5
```

[確切源碼基線記錄](BASELINE-2026-10-14.json)包含在 Windows VM 上，針對上述源碼重現的以下結果：

| 套件／檢查 | 結果 |
|---|---|
| 完整 NT 參考編譯／SIM 編譯（`compile`、`compilesim`） | 均 exit 0；僅編譯 |
| Pure／SIM pure（`pure`、`puresim`） | 各 38 PASS／0 FAIL |
| 合成引擎（`engine`） | 212 PASS／0 FAIL |
| 合成 SIM 引擎（`enginesim`） | 233 PASS／0 FAIL |
| 獨立覆核（`independent`） | 24 PASS／1 已知 FAIL：`ExplicitManualFlatReconciliationAvailable` |
| WPF UI（`ui`） | 50 PASS／0 FAIL |

以上是**離線結果，不是真實 NinjaTrader／provider 交易證據**。合成帳戶及 UI 控制項不能證明 provider ACK 次序、取消時序、線程要求、真實 OCO 原子性、成交、恢復或 funded／實盤安全。數字是各套件的執行次數，不是獨立功能數或完成百分比。可攜 Python 打包／SPEC 套件另行計算，不會運行 C# copier。[建置說明](BUILD.md) · [測試矩陣](TEST-MATRIX.md) · [歷史證據](EVIDENCE.json)。

## 已實作範圍與剩餘關卡

- 單一 Copy／Risk／Logs 儀表板，為 10–30 個帳戶設計；各行獨立 Ratio／Tgt／DLL／ATM／OFF／Flat，批量風控只作用於已選行，**From Leader** 僅複製一次預設值。為 30 個帳戶設計不等於通過真實 30 戶驗收。
- 已有各行數值草稿、Risk 對話框驗證、精確 XML 數值往返及關閉時的 callback frontier 檢查。舊說明指 #5 僅部分合併、沒有對話框草稿或精度修正，已經過時。原生視窗焦點／IME／剪貼簿／DPI 驗收仍須獨立處理。
- 各 Follower 的 provider dispatch 及 mailbox 拒絕後狀態修正已合併。#4 仍需要真 NT 線程／延遲及關閉／Dispose 驗收；離線通過不能證明安全關閉或永不返回的呼叫。
- 逐戶 Flat 確認框列出帳戶與商品。成功確認須呼叫返回、原生持倉為零／沒有掛單及靜置期；未確認或拋例外會鎖定，絕不重試。歷史模擬測試曾驗證逐戶 Flat，不代表此源碼的每條失敗路徑已驗收。
- **PR #22 已合併。** 分組所有權、分數比例分配、captured／applied frontier 及確切 dispatch 憑證，已取代歷史 #3「等待保護只能人工處理」的缺口。已明確要求、仍有效的 Leader 等待出場單，可以保護其已獲證明的獨立批次；不會憑空建立缺失 bracket 或入場單，已取消／模糊意圖不能恢復，provider 接受狀況不明時絕不重試。已捕捉的保護覆蓋持倉時，多餘的獨立無 OCO 保護會明確拒絕；此規則不等於券商 OCO 原子性。[分配契約](GROUP-ALLOCATION.md)。#3 仍保留安全／provider 驗收阻礙。
- 持倉偏離時預設鎖定／通知／拒絕不安全延續。`FlattenOnce` 已儲存但未生效，不會自動平倉。自管 TP／SL、承接現有持倉及重啟 journal 恢復是可選功能；安全拒絕仍是必要要求。

### Issue 覆核建議——尚未執行

獨立覆核已閱讀 #1–#12。以下是**範圍／狀態修正建議**，不是已執行的關閉 Issue、標籤變更或驗收決定。修改追蹤狀態之前，應核對源碼與 Issue 歷史；[已知問題](KNOWN-ISSUES.md)及[離線狀態](OFFLINE-STATUS.md)的部分舊段落可能描述已被取代的基線。

| Issue | 建議處理與邊界 |
|---|---|
| [#1](https://github.com/Kinfxhk/KINFXTradeCopier/issues/1) 建置／測試工具 | 已有確切源碼 VM 編譯及離線重現；建議按工具範圍完成處理，不代表 provider 驗收。 |
| [#2](https://github.com/Kinfxhk/KINFXTradeCopier/issues/2) 成交 frontier／改量 | 目前整合已有 frontier／預留量控制及測試，不應重複「frontier 未整合」的舊缺口。建議關閉或重訂已實作離線範圍，保留 provider ACK／驗收限制。 |
| [#3](https://github.com/Kinfxhk/KINFXTradeCopier/issues/3) 多組 OCO | 已合併 #22 取代等待保護／分數分配缺口；保留安全與 provider 驗收阻礙。 |
| [#4](https://github.com/Kinfxhk/KINFXTradeCopier/issues/4) Mailbox／Dispose／隔離 | 部分修正已合併；保留安全及真 NT 關閉／線程／延遲阻礙。 |
| [#5](https://github.com/Kinfxhk/KINFXTradeCopier/issues/5) 數值草稿／設定 | 草稿、對話框及 XML 修正已合併；建議按已覆蓋範圍完成處理，不代表原生鍵盤／DPI 或全部設定發佈契約已驗收。 |
| [#6](https://github.com/Kinfxhk/KINFXTradeCopier/issues/6) Journal／重啟 | 可選的持久 journal／重啟工作，目前擱置；不能吸收或掩蓋 #9 必要的工作階段內明確恢復關卡。 |
| [#7](https://github.com/Kinfxhk/KINFXTradeCopier/issues/7) 模擬驗收 | 已有有限歷史模擬測試；保留確切源碼驗收、中止／核對及安全阻礙。 |
| [#8](https://github.com/Kinfxhk/KINFXTradeCopier/issues/8) 原生 UI／DPI | 已有 WPF 離線檢查及 i18n；原生鍵盤、無障礙及多螢幕 DPI 尚未證明。 |
| [#9](https://github.com/Kinfxhk/KINFXTradeCopier/issues/9) 不確定 Flatten／恢復 | 保留安全阻礙：缺少明確、可驗證已平倉的 ledger 核對路徑，詳見下文。政策決定不等於完成。 |
| [#10](https://github.com/Kinfxhk/KINFXTradeCopier/issues/10) 自管 TP／SL | 可選，未實作；ATM 是跟隨 Leader 出場單，不是 Follower 原生 ATM 複製。 |
| [#11](https://github.com/Kinfxhk/KINFXTradeCopier/issues/11) 承接持倉 | 可選，未實作；Start 必須繼續拒絕現有持倉／掛單。 |
| [#12](https://github.com/Kinfxhk/KINFXTradeCopier/issues/12) 偏離時自動平倉 | 可選，目前僅儲存設定；即使延期自動平倉，鎖定／通知／安全拒絕仍屬必要。 |

### #9：仍缺少明確恢復關卡

獨立測試 `ExplicitManualFlatReconciliationAvailable` 檢查 `AcknowledgeFlattenConfirmed`，因缺少此明確、可驗證已平倉的核對路徑而失敗。這是目前工作階段內的安全／恢復關卡，**不只是可選的 #6 重啟 journal**。Position0、可見訂單全部 terminal、成功手動平倉、一般放棄確認或 UI 風控鎖存重設，都不能證明隱藏的已接受請求已結束，亦不能核對引擎自有成交證據。

模糊的已平倉遙測會保留現有保護單。Flatten 拋例外後，copier 阻止新的跟單出場且不會重試，因原請求可能已獲接受。**代價：**核對完成之前可能仍有持倉而沒有跟單出場。若仍有不確定性，保持鎖定／未啟動；不要清除鎖存或重啟來令 ledger 看似乾淨。原生 Orders／Positions 是操作證據，但不會自動授權恢復跟單。

<a id="dashboard-gallery-and-source"></a>
## 儀表板圖庫與源碼

以下圖片使用實際 `DashboardView` 控制項，以**合成 DEMO** 模式呈現**虛構帳戶與數值**，未連接帳戶、引擎或訂單。圖片**並非 NinjaTrader／provider 交易證據**。

[儀表板／引擎源碼](../src/AddOns/KINFXTradeCopier.cs) · [截圖呈現程式](screenshots/Screenshots.cs) · [語言表](../i18n/strings.json) · [SIM TEST 安裝腳本源碼](../scripts/install-simtest.ps1)

![Copy 分頁 — 合成 DEMO](screenshots/1-copy.png)
![Risk 分頁 — 合成 DEMO](screenshots/2-risk.png)
![Logs 分頁 — 合成 DEMO](screenshots/3-logs.png)

## 不涉及交易的貢獻方式

需要 Python 3.10+，不需要額外套件、帳戶或 NT8：

```sh
python -B -m unittest discover -s tests -p 'test_*.py' -v
```

可攜檢查涵蓋打包／安全封鎖及獨立 Python SPEC，**不是 C# 引擎實作**或券商執行。可以閱讀代碼、重現合成失敗及提出專注修正。本 repo 不提供廠商 DLL 或已編譯交易程式。有複製源碼的 SIM TEST 安裝腳本，但沒有簽署／已覆核的一般發佈版。

[建置](BUILD.md) · [測試／遮蓋敏感資料的報告](COMMUNITY-TESTING.md) · [貢獻流程](../CONTRIBUTING.md) · [請勿執行的操作](DO-NOT-DO.md) · [安全封鎖](SAFETY-HOLD.md) · [模擬計劃（歷史計劃，不代表授權）](SIM-TEST-PLAN.md) · [路線圖](ROADMAP.md)。

日後獲授權的 SIM TEST 演練必須遵循 [README 清理次序](../README.zh-HK.md#第一次使用)：Leader／Follower 已連線且原生狀態乾淨；**Stop 之前**使用原生 NT 取消 Leader 掛單及平掉 Leader 持倉；確認解除連接後才修改 Dry run；只在停止時新增 Follower，並重新閱讀 Start 確認。OFF 不等於 Flatten，不會平倉。不得為了測試通過而移除預設封鎖。

## 尚未證明

目前源碼的真實 provider 驗收、Leader 斷線、真實部分成交／止損或 Flatten 被拒、不確定已接受請求、完整關閉／恢復、10–30 戶真實延遲及多螢幕 DPI。沒有實盤安全、原生 Follower ATM 或 Prop Firm 合規認證。本軟件無法攔截每筆手動／策略訂單，亦無法保證成交。現有持倉應使用原生 NT／券商工具管理，不應依賴不確定的 copier ledger。
