# KINFX Trade Copier

NinjaTrader 8 で、1 つの口座の取引を複数の口座（Prop Firm の**シミュレーション**口座と Demo 口座）へコピーします。無料、オープンソース（MIT）、初期の**ソース alpha**です。

[中英對照](README.md) · [中文（繁體）](README.zh-HK.md) · [English](README.en.md) · **日本語** · [한국어](README.ko.md)

画面の言語はアプリと同じです：English、中文（繁體）、日本語、한국어。

画面の表示：Leader＝リーダー、Follower＝フォロワー、Dry run＝ドライラン、Flat＝決済。手順は確認文と同じ語を使います。

> **既定ビルドでは取引が無効です。Funded／ライブ口座：使用禁止。** これはソース alpha です。**現在のソースは受け入れられていません**。一般的なシミュレーション／provider 用途として認められていません。任意で使う SIM TEST ビルドは Simulator／Playback および操作者が allow-list した口座へ注文を送れます。その能力は安全受入ではありません。

<!-- QUICKSTART:START -->
## 使えますか？

| | |
|---|---|
| **Demo／シミュレーター口座** | **未準備：現在の候補は未受入。** 加重した過去のチェックリストの完了度は約 **83%** であり、リリース準備の割合ではありません。 |
| **Prop Firm 口座** | **未準備：現在の候補は未受入。** 同じ過去のチェックリストの完了度は約 **75%**。ソース `c9fbf2cb3005`（SIM TEST ビルド）は 2026-10-14、実機 NinjaTrader 上で、操作者が確認した*シミュレーション／評価*口座 11 件に対して実行されました。これはリリース受入でも、ファームのコンプライアンス認証でもありません。 |
| **Funded／ライブ口座** | **禁止。** |
<!-- QUICKSTART:END -->

## 進捗

<!-- PROGRESS:START -->
### 進捗：Demo／Prop Firm コピー

```text
Demo         █████████████████████████░░░░░  83.0%
Prop firm    ██████████████████████░░░░░░░░  74.7%
```

✅ 実際の NinjaTrader で検証済み · 🟡 オフラインテストのみ · ⬜ 未実施。必須項目 ✅：Demo 10/14 · Prop 10/15。

過去のチェックリストの証拠であり、現在の候補を受け入れたものではありません。現在の候補は取引について受け入れられていません。安全 P0／受入ゲート：issue-3, issue-4, issue-7, issue-9。Demo ready=False；Prop ready=False。funded／ライブの受入はありません。

- ✅ コアコピー：エントリー／決済／取消／変更
- ✅ 口座ごとの比率＋最大枚数
- ✅ 無ポジションのフォロワーはロックしない
- ✅ 各口座の決済ボタン
- ✅ 選択／全口座の Flatten
- ✅ TP／SL ミラー＋OCO
- ✅ 危険時は自動ロック、自動再試行なし
- ✅ 切断／再接続は安全
- 🟡 部分約定に対応
- 🟡 数量変更前の約定一致 (#2)
- 🟡 1口座の停滞が他口座を止めない (#4)
- ✅ 実機 NT で 10–30 口座
- ✅ ダッシュボード（表、一括編集、ロック理由） _（任意）_
- ✅ 口座ごとの日次目標／損失上限
- ⬜ 乖離時に一度だけ Flatten (#12) _（任意）_
- ⬜ 既存ポジションの引き継ぎ (#11) _（任意）_
- ⬜ 口座ごとの自己管理 TP/SL (#10) _（任意）_
- ⬜ 再起動からの復旧（#6、保留） _（任意）_
- ⬜ インストーラー＋レビュー後のリリース
- ⬜ Prop Firm ルール確認（本ソフトは認証しない）

これまでの実機テスト：シミュレーターのリーダー 1、最大 11 の**シミュレーション** Prop Firm 評価口座（Lucid 5、Tradeify 6）、MNQ、成行＋指値＋逆指値、TP/SL OCO。funded またはライブ口座は一度も使用していません。各項目と制限：[`docs/PROGRESS.json`](docs/PROGRESS.json)。配点は加重：✅ 100%、🟡 50%、設計のみ 10%。スナップショット 2026-10-15、ソース `bfc8a2531acf`。
<!-- PROGRESS:END -->

チェックと百分率は**加重した過去のチェックリスト**であり、**リリース準備の割合ではありません**。直近の実機 NinjaTrader 実行（2026-10-15）はソース `bfc8a2531acf`（SIM TEST ビルド）です。テスト証拠であり、現在の正確なソースの受入ではありません。[各項目の証拠と制限](docs/PROGRESS.json) · [過去の NT テスト記録](docs/VM-PROGRESS.md) · [現在のオフライン結果と安全上の障害](docs/DEVELOPERS.md)。

## インストール（Windows ＋ NinjaTrader 8。レビュー済みテスト候補のみ）

**参照用であり、一般公開の利用案内ではありません。** コンパイルできたという理由だけで、現在の未受入候補を導入したり取引に使ったりしないでください。先に正確なソースと安全ゲートを確認します。

1. **ダウンロード。** このページの *Code > Download ZIP* を解凍します。（または `git clone https://github.com/Kinfxhk/KINFXTradeCopier`。）
2. **インストール。** 解凍したフォルダーで PowerShell を実行します：
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\install-simtest.ps1
   ```
   SIM TEST ビルドを `Documents\NinjaTrader 8\bin\Custom\AddOns` にコピーし、置き換える前にバックアップします。口座は操作しません。`-WhatIf` はプレビュー、`-Remove` は削除です。
3. **コンパイル。** NinjaTrader で *New > NinjaScript Editor* を開き、**F5** を押し、*Compile succeeded* を待ちます。エラーなら中止します。
4. **口座は自動で読み込まれます。** copier を開くと、すべての NinjaTrader 口座が一覧され、チェックできます。口座名を手入力する必要はありません。Simulator／Playback 以外で、copier がまだ模擬と確認していない口座（例：Prop Firm の評価口座）に対して初めて **Start** または **Flatten** を押すと、**その口座名を示すリスク警告**が出ます。KINFX は評価口座と funded／ライブ口座を区別できません。**はい**＝模擬口座だと確認したことになり、`Documents\NinjaTrader 8\templates\KINFXTradeCopier\sim-allow.txt` に保存され、以後は尋ねません。**いいえ**＝保存も送信もしません。この警告はオフにできません。再び尋ねさせるには、ファイルからその行を削除します。

   ファイルを手で書くこともできます。**先にフォルダーを作成**してください：`Documents\NinjaTrader 8\templates\KINFXTradeCopier`（インストーラーは**このフォルダーは作成しません**）。1 行に 1 つの NinjaTrader Account `Name`。大文字小文字を区別し、完全一致が必要です。ログイン名や表示別名は不可です（`= 別名` は任意）：
   ```text
   # 操作者が模擬だと確認した口座だけ
   MYFIRM-SIM-12345 = Firm A
   MYFIRM-SIM-67890 = Firm B
   ```
   誤った名前は funded／ライブ口座への注文を許可し得ます。ファイルは操作者の申告であり、provider の検証ではありません。手編集したファイルは**静的初期化**のとき一度だけ読まれ、ダッシュボードを開き直しても再読込されません。編集後は停止かつフラットのまま copier のウィンドウを閉じ、**NinjaTrader を再起動**してください。**F5 だけでは**文書化されたホットリロードを保証しません。（リスク警告での回答は直ちに有効になります。）

## 初回の使い方

1. *New > KINFX Trade Copier*。これは別途レビューした SIM TEST 候補のリハーサル一覧であり、**現在の未受入ソースを実行する許可ではありません**。
2. **Leader**（例：`Sim101`）と正確な限月（例：`MNQ 12-26`）を選びます。NinjaTrader 標準の Orders／Positions タブで、Leader と選択したすべての Follower が**接続済み**であり、その銘柄の**ポジションはゼロで、未約定の注文はありません**。Start は既存ポジションの引き継ぎも、取りこぼしの補充もしません。
3. Follower は **1 つだけ** チェックし、Ratio は **1**。**Risk** タブで **Dry run** をオンにします。
4. **Start** を押し、Leader、Follower、限月、Ratio、`DRY RUN` の確認を読みます。Leader の小さい模擬注文で、Logs に `DRY RUN would send ...` が出ます。Dry run が抑止するのは copier の送信だけで、**Leader に自分で出す注文は抑止しません**。停止する前に、**NinjaTrader の標準機能で Leader の未約定注文を取り消し、Leader のポジションを決済してから**、Leader と Follower がフラットで未約定注文がないことを確認します。それから **Stop** を押し、セッションが切り離されたことを確認します。拒否されたらリハーサルを中止して調査し、無理にロック解除しないでください。
5. **セッションの接続が解除された後にのみ** Dry run を外せます。1 枚の模擬注文を送るには、正確なソースの別途レビューと許可、および `DRY RUN` のない新しい Start 確認が必要です。現在の候補はこの関門を通過していません。**停止中にのみフォロワーを追加**し、選択した全口座が接続済みでクリーンであることを再確認し、範囲全体を含む**新しい Start 確認**を読みます。

![Copy タブ — 合成 DEMO](docs/screenshots/1-copy.png)

**スクリーンショットの出所：** 実際のダッシュボードコントロールを **合成 DEMO** モードで描画し、**架空の口座と数値**を示します。接続された口座、エンジン、注文はありません。これは **NinjaTrader／provider の取引証拠ではありません**。[描画ソース](docs/screenshots/Screenshots.cs) · [Risk／Logs のギャラリーとソース](docs/DEVELOPERS.md#dashboard-gallery-and-source)。

各行に **Ratio、日次目標（Tgt）、日次損失（DLL）、ATM 決済ミラー、OFF、Flat** があります。**OFF** は新規エントリーを止め、所有するエントリー注文の取消を要求します。ポジションと決済注文は残ります。**Flat** は確認のあと、その 1 口座だけを決済します。Leader の決済後、Follower は `Off` になり、再び明示的にチェックする必要があります。

## 制限

- **ソース alpha** であり、一般に受け入れられた取引ビルドではありません。NinjaTrader 8.1.8.3 の実行（直近は 2026-10-15、ソース `bfc8a2531acf`）は、シミュレーターのリーダー 1、操作者が確認した Prop Firm 模擬フォロワー最大 11、MNQ です。**まだ証明されていません：** 20–30 口座、Leader の切断、実際の部分約定、停滞した provider 呼び出し、拒否された Flatten、再起動リカバリ。オフラインのコンパイル／テストはこれらを証明しません。
- **Locked** の行は、標準の NinjaTrader で調査し照合する必要があります。リスクラッチのリセットは、不確実なエンジン ledger、隠れた受理済み要求、例外を投げた Flatten の**照合ではありません**。明示的で検証済みのフラット回復ゲートは未実装です（[#9](https://github.com/Kinfxhk/KINFXTradeCopier/issues/9)、既知の FAIL `ExplicitManualFlatReconciliationAvailable`）。証拠が不確実なままなら、武装解除／ロックを維持し、レビューを求めてください。copier は Flatten を再試行せず、切断後に取りこぼしを補充しません。
- 正とするのは NinjaTrader 標準の Orders／Positions タブであり、このウィンドウではありません。手で出した注文は止められず、約定は保証しません。
- 試すために、既定ビルドの安全ホールドを外さないでください。

問題があれば、**秘匿した** Logs（口座名も残高もなし）を添えて [issue](https://github.com/Kinfxhk/KINFXTradeCopier/issues) を開いてください。

## 関連

[開発者向け（English）](docs/DEVELOPERS.md) · [開發者（中文）](docs/DEVELOPERS.zh-HK.md) · [禁止事項](docs/DO-NOT-DO.md) · [既知の問題](docs/KNOWN-ISSUES.md) · [テスト行列](docs/TEST-MATRIX.md) · [ロードマップ](docs/ROADMAP.md) · [貢献](CONTRIBUTING.md) · [セキュリティ](SECURITY.md)

開発者向け：ポータブル検査（`python -B -m unittest discover -s tests -p 'test_*.py' -v`）はパッケージングと、別の Python SPEC 研究を対象にします。SPEC は C# エンジン実装ではありません。これらの検査は copier、証券会社、実際の注文を実行しません。

## メンテナーのリンクと開示

メンテナー：KinFx。Prop Firm のルール調査、比較表、ツールのリンク、紹介コード：**<https://linktr.ee/kinfx>**

そのページの一部は**アフィリエイト／紹介リンク**です（例：評価用コードやパートナー登録）。使うとメンテナーが報酬を得ることがあります。これらは**このソフトウェアに影響しません**。コードは MIT で、紹介や追跡のロジックはなく、ここで会社や購入を勧めてはいません。KinFx は SEC／SFC の免許を持つ助言者ではなく、内容は投資助言ではありません。評価口座の多くはシミュレーションです。このプロジェクトは、そこに挙がる会社と提携していません。

## ライセンス

[MIT](LICENSE)、© 2026 Kinfxhk。NinjaTrader、FlowBots／Replikanto、いずれの証券会社・Prop Firm とも提携していません。競合のコード、ベンダー DLL、私的な口座ログ、資格情報は含みません。ソース内の Prop テンプレートは過去の編集可能なメタデータであり、現行ルールやコンプライアンス認証ではありません。
