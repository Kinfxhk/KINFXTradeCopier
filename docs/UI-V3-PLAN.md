# KINFX Trade Copier — UI v3 plan (for worker agents)

Base commit: `07d358d` on branch `feat/ui-v3` (repo `KINFXTradeCopier-final`).
Each worker has its own git worktree, already at that commit:

| WP | Worktree | Branch | Owns |
|---|---|---|---|
| WP1 | `C:/ktcwt/wp1` | `feat/ui-v3-wp1` | Header bar, instrument picker, contract roll |
| WP2 | `C:/ktcwt/wp2` | `feat/ui-v3-wp2` | Page 1 Monitor + Page 2 Settings |
| WP3 | `C:/ktcwt/wp3` | `feat/ui-v3-wp3` | Page 3 Logs |

Single file under change: `src/AddOns/KINFXTradeCopier.cs` (NinjaTrader 8 AddOn, C# 7.3, WPF built in code, no XAML).

## 0. Hard rules (every WP; breaking any one = work rejected)

1. **Engine bytes are frozen.** The span from `// END KINFX PURE` to `public sealed class AccountRow` must not change.
   Check: `python scripts/engine_guard.py --check 55562fe620ad39ef4c5778babec5339bb5599e47d87fc02b5990b017d8892cf3` must print `ENGINE_OK` (UI v3 work packages).
   After UI v3 the S1 commit changed the engine on purpose (ResumeEntries, ExitCompleted telemetry). The current hash is
   `9cd00717835ee20464621eb8a7d9c01a4cc54ecd55c39309f8e7d90e1ace15d7`. The 2026-10-21 fix (an exit ignored as 'nothing to close' no longer locks a fill of a LATER entry) changed it on purpose to
   `cfb487f31e30e6123a547dc9f63a5c1c8d8b8ba8925d3768ae85f32e02979fe3`; UI-only work must keep that one.
   You may add NEW pure helper classes inside the PURE region (between `// BEGIN KINFX PURE` and `// BEGIN LANG TABLE`), and edit `AccountRow`, `DashboardView`, `KINFXTradeCopierTab` (UI side only).
2. **Never touch other WPs' code.** Only edit the methods/hooks listed for your WP. Hooks already exist as empty methods (see §1). If you need something from another WP, write it inside your own hook.
3. **No trading behaviour change.** Do not change what `Command(...)`, `Start()`, `Flatten(...)`, `FlatOne`, `Follow`, `Pause`, `UnlockAll`, `Unlock`, `engine.*` calls do. Buttons may move to another page; they must still call the same `Send("...")` command string, with the same `Tag` string, and keep `editButtons` membership (disabled while attached) exactly as today.
4. **Keep every existing test green without editing its assertions.** `tests/BulkUiTests.cs` currently has 58 cases. You may only change a test when a control moved and the test locates it by tab index — then change ONLY the locator (e.g. `Sections.SelectedIndex`), never the `Check(...)` condition. Write in your report each test line you touched, before → after.
5. **Strings**: every new user-visible English string goes through `Lang.T("...")` / `Lang.F("...",args)`, and gets a row in YOUR file `i18n/strings.<wp>.json` (array of `{"en","zh","ja","ko"}`; `zh` = Traditional Chinese written language 書面語, never Cantonese words like 嘅/咗/唔/係/冇/啲). Then run `python scripts/gen_lang.py` (regenerates the LANG TABLE in the .cs). Do not edit `i18n/strings.json` or the LANG TABLE by hand. A duplicate English key with the main table is an error — reuse an existing key instead (grep `i18n/strings.json` first).
6. **Product wording**: "Trade Copier". Never "desk", "station", "跟單台", "signal".
7. **No Process / network / file writes outside the AddOn's own folder** (`DirectoryPath` in `KINFXTradeCopierTab`). The production-hold tests scan for this.
8. **Dark theme**: use the existing brushes `DashboardView.Ink, Muted, Panel, Line, Accent, Canvas, Good, Warn, Bad`, and the existing factories `Button(...)`, `TextTool(...)`, `Icon(...)`, `Input(...)`, `Combo(...)`, `PlainCheckTemplate()`. Never a default WPF CheckBox/ComboBox template (they paint white; the WPF suite checks pixels).
9. Do not commit generated artifacts. Do not push. Do not run anything against a real NinjaTrader account.
10. C# 7.3 only: no `switch` expressions, no `using var`, no records, no `??=`, no target-typed `new()`.

## 1. Existing structure you work in (already in the base commit)

`DashboardView` constructor now calls, in order:
`BuildHeader()` (rows 0+1) → `BuildSections()` (TabControl `Sections`) → `BuildMonitorPage()` (tab 0, was "Copy") → `BuildSettingsPage()` (tab 1, was "Risk") → `BuildLogPage()` (tab 2) → `BuildFooter()` (row 3).

Empty hooks (fill only your own):

| Hook | Called from | Owner |
|---|---|---|
| `EditableHeader(bool)` | end of `Editable(canEdit)` | WP1 |
| `UpdateHeaderState()` | end of `UpdateCards()` | WP1 |
| `EditableMonitor(bool)`, `UpdateMonitor()` | `Editable` / `UpdateCards` | WP2 |
| `EditableSettings(bool)` | `Editable` | WP2 |
| `EditableLogs(bool)` | `Editable` | WP3 |
| `WireWp1()` / `WireWp2()` / `WireWp3()` | end of `KINFXTradeCopierTab.WireDashboard()` (runs again after a language change rebuilds the view) | WP1/2/3 |
| `internal string HeaderNotice` | WP1 writes (NT version notice), WP2 shows it in the Monitor issue banner | — |
| `internal bool SessionAttached` | set by `SetSessionState` | read-only |

Tests: put new WPF cases in `tests/UiTests<Wp1|Wp2|Wp3>.cs` as
```csharp
using System; using System.Linq; using System.Windows; using System.Windows.Controls; using NinjaTrader.NinjaScript.AddOns.Kinfx;
public static class UiTestsWp1 {
  public static Action[] Cases(){ return new Action[]{ MyCase1, MyCase2 }; }
  static void MyCase1(){ var v=BulkUiTests.Fresh(); ... BulkUiTests.Check(cond,"message"); }
}
```
Helpers available as `BulkUiTests.X`: `Fresh()` (demo view, 30 synthetic rows, editable), `Layout(e)`, `Check(bool,msg)`, `Select(v,idx...)`, `Visual<T>(root)`, `Col(grid,key)`, `FindDescendant<T>`, `Luma(el)`, `WhitePx(el)`, `Texts(root)`, `Truncated(el)`, `CellBox`, `TypeBad`.
Pure tests (no WPF): add parameterless `static void Name()` methods to `tests/RiskTests.cs` under your marker line. `scripts/offline.py pure` discovers and runs every such method by itself; do not edit `Main`. Assert with `Eq(want,got)`. Pure tests can only use code inside the PURE region (+ the LANG TABLE).

Demo rows in `Fresh()`: `Rows[0]` leader "Demo 01", rows 1..29 followers; Group = Tradeify (1–10), Lucid (11–20), Topstep (21–30); Target 1000, Loss 500, Ratio 1, MaxContracts 4.

## 2. Commands (run from your worktree root)

```bash
NB="C:/Program Files/NinjaTrader 8/bin"
python -B scripts/offline.py compilesim --nt-bin "$NB" --artifacts-root C:/ktcart     # full NT compile, SIM build
python -B scripts/offline.py compile    --nt-bin "$NB" --artifacts-root C:/ktcart     # full NT compile, default build
python -B scripts/offline.py pure       --nt-bin "$NB" --artifacts-root C:/ktcart     # pure tests (expect 45 + yours, 0 fail)
python -B scripts/offline.py puresim    --nt-bin "$NB" --artifacts-root C:/ktcart
KTC_UI_OUT=C:/ktcart/ui-<wp> bash scripts/runui.sh | tail -3                           # WPF suite: 58 + yours, last line UI_TOTAL pass=N fail=0
python -B -m unittest discover -s tests -p 'test_*.py' 2>&1 | tail -3                   # 485 portable tests, OK
python scripts/gen_lang.py --check
python scripts/engine_guard.py --check cfb487f31e30e6123a547dc9f63a5c1c8d8b8ba8925d3768ae85f32e02979fe3   # after the 2026-10-21 ignored-exit fix
```
Result JSON of each offline run: `RESULT <path>/result.json` (fields `pass`, `fail`, `exit_code`). A compile error shows in `<artifact>/compiler-diagnostics.txt`.
WPF compile errors: `C:/ktcart/ui-<wp>/compiler-diagnostics.txt`.
Each command takes 20 s – 4 min. Use a `timeout` of at least 400 s. Do not run two WPF runs in the same `KTC_UI_OUT` at once.

Commit on your branch when every command above is green:
`git -c user.name=Kinfxhk -c user.email=178037814+Kinfxhk@users.noreply.github.com commit -am "..."` (add new files with `git add` first).

## 3. WP1 — Header bar, instrument picker, contract roll

Code you own: `BuildHeader()`, `EditableHeader`, `UpdateHeaderState`, `WireWp1`, class `ContractRoll` (PURE), a new PURE class `InstrumentCatalog`, `NormalizeInstrumentBox()` / `InstrumentText()` in the tab, `tests/UiTestsWp1.cs`, WP1 marker in `RiskTests.cs`, `i18n/strings.wp1.json`.

### 3.1 Instrument picker (user request: "要打字搵 … 彈列表出 NQ GC MNQ CL ES … 自動揀果期")
- Replace the plain `InstrumentBox` TextBox by an **editable ComboBox** (keep a field named `InstrumentBox`? NO — keep `internal TextBox InstrumentBox` as the editable text part so every existing caller and test still works). Recommended: a `Grid` holding `InstrumentBox` (TextBox, same as now) plus a small dropdown button (`Icon("\uE70D", ...)`, Tag `"instPick"`) that opens a `Popup`/`ContextMenu` list. Clicking an item writes the **front contract** text into `InstrumentBox.Text` (e.g. `MNQ 12-26`) and then raises the same normalization as Enter (tab side: `NormalizeInstrumentBox()`; view side: expose `internal event Action InstrumentPicked;` and subscribe in `WireWp1`).
- List contents (pure, `InstrumentCatalog.Items`): ordered groups with a header row per group
  - Equity index: `MNQ, NQ, MES, ES, MYM, YM, M2K, RTY`
  - Metals: `MGC, GC, SIL, SI`
  - Energy: `MCL, CL`
  Each item shows `ROOT  front-month  description` e.g. `MNQ  12-26  Micro Nasdaq-100`. Descriptions: short English, translated via strings.wp1.json.
- Also list "recent" (last 5 roots the user chose, most recent first) at the top. Persist in `DirectoryPath/instruments-recent.txt`, one root per line, ignore unknown roots. Write it in the tab (WireWp1/NormalizeInstrumentBox side), never from the view.
- Disabled while attached (`EditableHeader(false)` disables the dropdown button too). Typing stays possible exactly as today.
- Popup must be dark (Panel background, Ink text, Line border; hover = Accent border). Add a pixel test with `WhitePx` that the open list has < 40 near-white pixels (render the popup child inside a DashboardView-hosted Border if Popup does not render off-screen; say which you did).

### 3.2 Contract roll — correct CME rule (current code rolls on the 1st of the expiry month = WRONG)
NinjaTrader's own rollover table (read from the local NT 8.1.8.3 database, MasterInstruments.UserData for MNQ; ES showed the same pattern) says **contract X becomes the front contract on date D**:

| Contract | Front from |
|---|---|
| 09-26 | 2026-06-12 |
| 12-26 | 2026-09-14 |
| 03-27 | 2026-12-14 |
| 06-27 | 2027-03-09 |
| 09-27 | 2027-06-07 |

Implement as data plus fallback:
- New PURE table `InstrumentCatalog.QuarterlyRolls` = the 5 rows above (same for every quarterly index root `ES MES NQ MNQ YM MYM RTY M2K`).
- `ContractRoll.Front(root, now)` for quarterly roots: the newest table row whose date <= `now.Date` gives the front contract. Outside the table range: fallback = the next quarterly contract whose roll date (3rd Friday of the PREVIOUS quarter's expiry month minus 4 days, i.e. the Monday of expiry week) is <= now; and `InstrumentCatalog.IsEstimated(root, now)` returns true so the tab can log once: `Contract roll date estimated for {0}; check NinjaTrader's Instrument Manager.`
- Gold `GC/MGC` (Feb Apr Jun Aug Oct Dec), Silver `SI/SIL` (Mar May Jul Sep Dec), Crude `CL/MCL` (every month): **never auto-pick.** `Front` keeps returning `null` for them. Add `SI`, `SIL` to `ContractRoll` roots with their months (SafeFront=false) so explicit `SI 12-26` / `SIZ6` normalizes. The picker lists the next two listed months for these roots as separate items (`GC  12-26`, `GC  02-27`) labelled `choose month`.
- Existing tests `RiskTests.ContractRollQuarterly` / `ContractInputNormalize` must still pass UNCHANGED (their dates are far from a roll; e.g. `Front("MNQ",2026-12-01)` stays `MNQ 12-26`).
- New pure tests: `Front("MNQ",2026-12-13)=="MNQ 12-26"`, `Front("MNQ",2026-12-14)=="MNQ 03-27"`, `Front("ES",2026-09-13)=="ES 09-26"`, `Front("ES",2026-09-14)=="ES 12-26"`, `Front("NQ",2027-03-08)=="NQ 03-27"`, `Front("NQ",2027-03-09)=="NQ 06-27"`, `Front("GC",x)==null`, `Normalize("SIZ6",now)=="SI 12-26"`, one fallback date after 2027-06-07 with `IsEstimated==true`.
- Mutant: put back the old "1st of the month" rule and report which new test goes red.
- Leader trading another month (roadmap §3.4) needs engine changes — **not in this WP** (engine frozen). Instead, header hint: when the box holds a quarterly contract that is not `Front(root, now)`, show Warn text `Front contract is now {0}` and a TextTool `Use {0}` (Tag `useFront`, editButtons, stopped only) that only rewrites the text box.

### 3.3 Header bar (mockup)
- Keep: brand `KINFX / Trade Copier`, language picker, build badge `StatusText` (keep the field and its text source `DashboardSafety.HeaderShown` unchanged — tests read it).
- Session bar order: `Lead [combo]  Inst [box][▾]  → N followers   [spacer]   State: <badge>  Dry run: on/off (SIM build only)   [▶ Start] [Pause new entries] [Stop] [Refresh]`.
- `State` badge (`TextBlock`, Tag `"stateBadge"`): text from `SessionAttached` + whether any row is Paused: `Stopped` (Muted), `Copying` (Good), `Paused` (Warn). Update in `UpdateHeaderState()`.
- Dry-run badge (Tag `"dryBadge"`, `#if KINFX_SIM_TEST` only): mirrors `DryRunBox.IsChecked` (DryRunBox itself stays on the Settings page, owned by WP2; subscribe to its Checked/Unchecked in `UpdateHeaderState` lazily, or just poll it there).
- Pause button: same `Icon`/command `"pause"`, but give it a text label `Pause new entries` (TextTool is fine, keep Tag `"pause"`). Stop keeps Tag `"stop"`, Refresh keeps `"refresh"`. Start stays `StartButton`.
- NT version (roadmap §2.6): in `WireWp1` (tab side) read `NinjaTrader.Core.Globals.ProductVersion`. Tested version constant `"8.1.8.3"` in a new PURE helper `NtVersion.Tested` + `NtVersion.Notice(string actual)` → `""` when equal, otherwise `Lang.F("NinjaTrader {0} is not the tested version ({1}). It may still work; report problems.", actual, Tested)`. Tab sets `dashboard.HeaderNotice` and logs it once. Pure test for `Notice`.

### 3.4 WP1 acceptance (WPF, in UiTestsWp1)
- Picker exists (Tag `instPick`), lists at least the 14 roots above, picking `MNQ` at a fixed date sets `InstrumentBox.Text` to the front contract (inject the clock: `internal static Func<DateTime> Now=()=>DateTime.Now;` on InstrumentCatalog for tests).
- Picker disabled after `Editable(false)`, enabled again after `Editable(true)`.
- State badge reads Stopped / Copying / Paused for the three states (`SetSessionState(true,"x")` to attach).
- `useFront` shows only when the typed contract is stale and is hidden otherwise.
- No clipped header text in any of the 4 languages at width 1160 (use `Truncated`).

## 4. WP2 — Page 1 Monitor + Page 2 Settings

Code you own: `BuildMonitorPage()`, `BuildSettingsPage()`, `BuildFooter()`, `EditableMonitor`, `EditableSettings`, `UpdateMonitor`, `WireWp2`, `BulkApply` / `BulkFromLeader` (add fields only), new PURE class `MonitorSummary`, `tests/UiTestsWp2.cs`, WP2 marker in RiskTests, `i18n/strings.wp2.json`.

### 4.1 Tab headers
Tab 0 header `Monitor` (zh 監察, ja モニター, ko 모니터), tab 1 `Settings` (key exists, zh 設定), tab 2 `Logs` (header owned by WP3). Do not change the test `EveryVisibleHeaderAndTabIsTranslatedExceptKeptAbbreviations`; it keeps passing because the new names are translated.

### 4.2 Page 0 Monitor (top → bottom)
1. **Summary cards** (4, Tag `card:copying`, `card:locked`, `card:unprotected`, `card:pnl`): `Copying / ticked` (e.g. `8 / 10`), `Locked accounts` (= `LockedCount`), `Unprotected lots` (sum over follower rows of the number parsed from a lock reason containing `lot UNPROTECTED`/`lots UNPROTECTED` — parse in pure `MonitorSummary.UnprotectedLots(string reason)`; 0 when absent), `Today P/L` (sum of finite follower Pnl + currency, or `Mixed CCY`). Pure `MonitorSummary` computes these from simple inputs; cards refresh in `UpdateMonitor()`.
2. **Issue banner** (Tag `issues`, collapsed when empty): one line per issue, max 6 lines + "… and N more". Sources: each locked follower (`Alias: <translated reason shortened to 90 chars>` red, with a small `Logs` TextTool Tag `issue:logs` that sets `Sections.SelectedIndex=2`), the Unlock all button (moved here from the Copy toolbar, keep Tag `unlockAll`, keep `Send("unlockAll")`, keep editButtons), advice rows (Warn, `Alias: <advice>` with `Settings` TextTool Tag `issue:settings` → `Sections.SelectedIndex=1`), and `HeaderNotice` when not empty.
3. **Toolbar**: search box (`search`, same field), state filter (`filter`, same items/indices), Tick all / Untick all icons (same Tags `tickAll`/`tickNone`), group toggle: move the `groupBox` CheckBox here (label `Group by firm`; it still sets `GroupByAccount`). Columns button stays (AttachColumnMenu adds it to the bar passed as `columnsBarCopy`).
4. **AccountGrid**: keep the 14 columns, their keys and order exactly as today (tests, columns.txt and the user's standing requirement "per-row Ratio editable on the main table" depend on them). Only addition: when `GroupByAccount` is on, a `GroupStyle` header `Firm (count) · P/L sum` (Panel background, Accent text; expander, collapsed state remembered per group name in memory).
5. **Bottom action row** (right aligned): `Unlock all` is in the banner, here: `Flatten selected` (TextTool, Tag `flattenSelected`, `Send("flattenSelected")`, `IsEnabled=DashboardSafety.CanExecute("flattenSelected")`, Bad foreground) and `Flatten all` (Tag `flattenAll`, same pattern). They MOVE here from the Risk toolbar (remove the icon versions there). Keep their tooltips' text keys.
6. Remove the bulk bar from page 0 (moves to page 1, §4.3). `BulkBarPresent` must then check the settings page: change its body to `bulkBar!=null&&RiskGrid.Parent!=null` — the test `BarIsOnCopyTabAndKeepsHolds` asserts `v.BulkBarPresent` and `AccountGrid.Columns.Count==14`; both stay true. (This is a body change of a property, not of a test.)

### 4.3 Page 1 Settings (top → bottom)
1. **Session section** (`Border` Panel, title `This session`): Dry run (`DryRunBox`, SIM only, same field), Trading-day button (`daySettings` Tag, same command), Ask before switches (`AskStartBox`, `AskFlatOneBox`, `AskFlattenBox` — same fields, same Tag `ask:<kind>`), `Live (RO)` (`ShowLive`, same field and visibility rule), Save (`save` Tag), Batch/presets (`batch` Tag, same enable rule), Pause/Resume highlighted (`pauseSelected`, `resumeSelected` Tags).
2. **Bulk section** (title `Bulk edit (highlighted accounts; Ratio never changes)`): replace the 8 bulk buttons by: a field ComboBox (Tag `bulkField`, items in this order: `Daily target`, `Daily loss`, `ATM exit mirroring`, `Flat at limit`, `On divergence`), a value box (the old `bulkValue` TextBox for Tgt/DLL) OR a value ComboBox (`on`/`off`, or `Notify`/`Flatten once`) shown depending on the field, an `Apply` TextTool (Tag `bulkApply`), and the `From Leader` TextTool (Tag `bulkLeader`). Apply maps to the existing `BulkClick(field,text)` with the same internal codes: `Tgt`, `DLL`, `ATM`+`on|off`, `LimitFlat`+`on|off`, `Diverge`+`Notify|FlattenOnce`. Set `bulkBar` to this section's panel (Tag `bulkbar`). `BulkApply`/`BulkFromLeader` logic unchanged.
3. **RiskGrid**: keep columns and keys exactly as today. Rows where `Preset` is empty/"Custom" and `MaxContracts==1`: Max cell Warn border (DataTrigger on a new read-only `AccountRow.MaxIsDefault` bool). Tooltip: `Max is still 1: a leader order larger than this is skipped entirely`.
4. Footer note text as today.
5. `SelectedRows()` uses `Sections.SelectedIndex==1?RiskGrid:AccountGrid` — keep.

### 4.4 Footer
Keep `summary`, `selection`, `SessionText`, `adviceLine`, `lockedLine` fields and texts (tests read `LockedText`, `AdviceText`, `SelectionSummary`).

### 4.5 WP2 acceptance (UiTestsWp2)
- Cards exist with tags; values correct for: 2 locked followers (one reason `... 2 lots UNPROTECTED ...`), leader locked (not counted), mixed currency.
- Banner hidden with no issues; shows a line per locked row; `issue:logs` switches to tab 2; `unlockAll` button inside the banner sends exactly one `unlockAll` on a non-demo view and is disabled after `Editable(false)`.
- `flattenSelected`/`flattenAll` live on tab 0 and stay disabled in the default (held) build when `CanExecute` is false — test with `DashboardSafety.CanExecute("flattenAll")` as oracle.
- Bulk section on tab 1: choosing `Daily target` + `750` + Apply on highlighted rows 1,2 gives Target 750 there only; `ATM exit mirroring` + `off`; `On divergence` + `Flatten once`; Ratio untouched; refused when `Editable(false)`.
- Group header shows `Tradeify (10)` when `GroupByAccount=true`.
- MaxIsDefault Warn border on a row with Preset "" and Max 1; not on Max 4.
- All four languages: no clipped text on tabs 0 and 1 at 1160×840 (`Truncated`), no near-white blocks (`WhitePx` < 40) on the new combo/check controls.

## 5. WP3 — Page 3 Logs

Code you own: `BuildLogPage()`, `EditableLogs`, `WireWp3`, `ClearLog()`, the tab's `Log(...)`, `LogText()`, `RedrawLog()`, `logs` queue, new PURE classes `LogLine` + `LogClassifier` + `LogExport`, `tests/UiTestsWp3.cs`, WP3 marker in RiskTests, `i18n/strings.wp3.json`.

Engine log lines are free English text, emitted through the tab's `Log(string)` (from engine callback and UI). Do not change their text.

### 5.1 Model (pure)
- `LogLine { DateTime Time; string Level; string Account; string Category; string Text; }` where Level ∈ `Info, Order, Warn, Lock, Error`.
- `LogClassifier.Classify(string englishMessage, IEnumerable<string> accountNames) → LogLine` (time set by caller). Rules (first match wins, case-sensitive substrings):
  - `" LOCK: "` or `"LOCK"` + `"Coverage not established"` → Lock; Category `Lock`
  - starts with `"ENGINE ERROR"`, `"PROVIDER DISPATCH ERROR"`, `"PROVIDER COMPLETION ERROR"`, `"PERSISTENCE ERROR"`, `"DURABLE LATCH WRITE FAILED"`, `"FLATTEN FAILED"`, or contains `"UI action failed"` → Error
  - starts with `"COPY "`, `"DRY RUN would send"`, `"Flatten request"`, `"Flat requested"` → Order
  - contains `"refused"`, `"blocked"`, `"BLOCKED"`, `"Refused"`, `"not recognised"`, `"not available"`, `"Trading-day boundary"`, `"Confirmation skipped"`, `"NOT saved"` → Warn
  - else Info.
  - Account = the longest account name from the list that occurs in the message (ordinal), else `""`.
  - Category for non-lock lines: `Order`, `Session` (starts with `Armed`/`Start`/`Stop`/`Paused`), `Settings` (contains `Settings`/`saved`/`Unlock`/`unlock`), `Accounts` (`Account list`), else `General`.
- `LogExport.Mask(string text, IEnumerable<string> names)` replaces each account name (longest first) by `ColumnPrefs.DefaultAlias(name)` … but DefaultAlias keeps the last 4 chars; for export use a stronger mask: `ACCOUNT-<n>` numbered by first appearance. Pure test.
- Tests: ≥ 12 classifier cases taken from real engine strings (grep `log("` in the .cs), mask cases incl. a name that is a prefix of another.

### 5.2 Tab side
- `logs` becomes `Queue<LogLine>` (raw English kept in `Text`), max 2000 (was 300). `Log(message)` classifies with current `dashboard.Rows` names, enqueues, appends to the view **incrementally** (no full redraw — fixes the zh lag), and appends one line to the daily file `DirectoryPath/logs/kinfx-yyyyMMdd.log` (`yyyy-MM-dd HH:mm:ss  LEVEL  message`, UTF-8, append; catch every IO exception silently once-per-session-logged; create folder lazily; this is inside DirectoryPath so rule 7 is fine).
- Language change (`ChangeLanguage` rebuilds the view and calls `RedrawLog()`): `RedrawLog` re-fills the view from the queue (full refill only here).

### 5.3 View (page 2)
- Top note (`DashboardSafety.LogNoteShown`, Warn) stays.
- Toolbar: level filter ComboBox (Tag `logLevel`: All, Info, Order, Warn, Lock, Error — filter by index), account filter ComboBox (Tag `logAccount`: All + current row names/aliases, refreshed when the list opens), search TextBox (Tag `logSearch`), `Auto-scroll` CheckBox (PlainCheckTemplate, default on), `Export (account names masked)` TextTool (Tag `logExport`; tab side writes `DirectoryPath/logs/export-yyyyMMdd-HHmmss.txt` with masked text and logs the path; NOT an editButton — allowed while attached), `Open folder` — **skip this one** (would need Process.Start, forbidden by rule 7), `Clear log` (keep Tag `logClear`, `ClearLog()` clears the view AND the in-memory queue display, not the daily file).
- Body: a read-only `DataGrid` (`LogGrid`, `GridTable()` style, columns Time `HH:mm:ss`, Level (colored text: Info Muted, Order Good, Warn Warn, Lock Bad, Error Bad bold), Account, Category, Message (star, wrapping off, tooltip full text)). Message text shown via `Lang.Show(Text)`.
- **Keep `LogBox`** (TextBox) for compatibility: tests `LogsReadableAndTopAligned` / `LogClearWorksAndIsEditableIndependent` read `LogBox` properties (foreground luminance ≥ 0.80, VerticalContentAlignment Top, IsReadOnly, `ClearLog()` empties `LogBox.Text`). Keep it as a collapsed-by-default "Plain text" view toggled by a `Plain text` CheckBox (Tag `logPlain`), kept in sync (append). Its properties stay as today.
- Tab header `Logs` shows a Warn count badge of Warn+Lock+Error lines since the page was last viewed, e.g. `Logs (3)` — implement as `TabItem.Header` = StackPanel? NO: tests compare `tab.Header` to strings. Keep `Header` a string: `Lang.T("Logs")` when 0, otherwise `Lang.T("Logs")+" ("+n+")"`. Check the test `EveryVisibleHeaderAndTabIsTranslatedExceptKeptAbbreviations` still passes (it only flags `h=="Logs"` in non-English languages).
- Public/internal API for tests: `internal DataGrid LogGrid; internal void AddLog(LogLine l); internal int LogUnseen;`.

### 5.4 WP3 acceptance
- Classifier/mask pure tests (≥ 12 + ≥ 4).
- WPF: 100 AddLog calls → grid has 100 rows; level filter `Lock` shows only Lock rows; account filter; search; Clear empties grid and LogBox; unseen badge increments for Warn/Lock/Error only and resets when tab 2 selected; 2000-line cap; zh language renders translated level names; no clipped toolbar text in 4 languages; no white pixels on the two ComboBoxes / CheckBoxes.
- Timing: adding 500 lines one by one in zh takes < 1.5 s in the test host (print the measured ms).

## 6. Report (each WP, final answer)
1. Branch + commit hash. 2. Engine guard output. 3. Exact command outputs (last lines) for compile, compilesim, pure, puresim, runui, unittest, gen_lang --check. 4. Every existing test line changed (before → after) and why. 5. New test names. 6. Mutant evidence (one per new behaviour: what you broke, which test went red). 7. Anything you could not do.
