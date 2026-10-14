# KINFX Trade Copier

Copy trades from one NinjaTrader 8 account to many others (prop-firm **simulated** accounts and demo accounts). Free, open source (MIT), early **alpha**.

**English** · [繁體中文](README.zh-HK.md)

> **TRADING DISABLED in the default build. Funded and live accounts: do not use.** This is a source alpha; the **current source is not accepted** for general simulation/provider use. The opt-in SIM TEST build can send orders to Simulator / Playback and operator-allow-listed accounts; that capability is not safety acceptance.

<!-- QUICKSTART:START -->
## Can I use it?

| | |
|---|---|
| **Demo / Simulator accounts** | **Not ready: current candidate unaccepted.** About **69%** done in the weighted historical checklist, not a release-readiness percentage. |
| **Prop-firm accounts** | **Not ready: current candidate unaccepted.** About **62%** done in the same historical checklist. An older build was tested on 2 operator-attested *simulated / evaluation* accounts; this is not current-source acceptance or firm compliance. |
| **Funded or live accounts** | **No.** |
<!-- QUICKSTART:END -->

## Progress

<!-- PROGRESS:START -->
### Achievements: Demo / Prop-firm copying

```text
Demo         █████████████████████░░░░░░░░░  68.9%
Prop firm    ███████████████████░░░░░░░░░░░  62.1%
```

✅ tested on real NinjaTrader · 🟡 offline tests only · ⬜ not yet. Must-haves ✅: Demo 6/14 · Prop 6/15.

Historical checklist evidence, not acceptance of the current candidate. The current candidate is not accepted for trading. Safety P0 / acceptance gates: issue-3, issue-4, issue-7, issue-9. Demo ready=False; Prop ready=False. No funded/live acceptance.

- ✅ Core copy: entry / exit / cancel / modify
- ✅ Per-account Ratio + max-lot cap
- ✅ No lock-up on a flat follower
- ✅ Flat button on every account
- 🟡 Flatten selected / all
- ✅ TP / SL mirroring + OCO
- 🟡 Auto-lock on danger, no auto-retry
- ✅ Disconnect / reconnect safe
- 🟡 Partial fills handled
- 🟡 Fill frontier before resize (#2)
- 🟡 One stuck account never freezes others (#4)
- ⬜ 10-30 accounts on real NT
- ✅ Dashboard (table, bulk edit, lock reasons) _(optional)_
- 🟡 Daily target / loss limit per account
- ⬜ Flatten once on divergence (#12) _(optional)_
- ⬜ Adopt existing position (#11) _(optional)_
- ⬜ Self-managed TP/SL (#10) _(optional)_
- ⬜ Restart recovery (#6, shelved) _(optional)_
- ⬜ Installer + reviewed release
- ⬜ Prop-firm rule check (never certified)

Real tests so far: 1 simulator leader, 2 **simulated** prop-firm evaluation accounts, MNQ, Market + Limit. No funded or live account was ever used. Each tick and its limits: [`docs/PROGRESS.json`](docs/PROGRESS.json). Score = weighted: ✅ 100%, 🟡 50%, design only 10%. Snapshot 2026-10-13, source `20dc6707c9e6`.
<!-- PROGRESS:END -->

The ticks and percentages are a **weighted historical checklist**, **not a release-readiness percentage**. The real-NinjaTrader snapshot refers to older source `20dc6707c9e6`, not acceptance of the current exact source. [Tick evidence and limits](docs/PROGRESS.json) · [Historical NT test log](docs/VM-PROGRESS.md) · [Current offline results and safety blockers](docs/DEVELOPERS.md).

## Install (Windows + NinjaTrader 8; reviewed test candidates only)

**Reference instructions, not a general-release invitation.** Do not install or trade the current unaccepted candidate merely because it compiles. Review the exact source and safety gates first.

1. **Download.** *Code > Download ZIP* on this page, then unzip. (Or `git clone https://github.com/Kinfxhk/KINFXTradeCopier`.)
2. **Install.** In the unzipped folder run in PowerShell:
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\install-simtest.ps1
   ```
   It copies the SIM TEST build into `Documents\NinjaTrader 8\bin\Custom\AddOns`, backs up anything it replaces and touches no account. `-WhatIf` previews, `-Remove` uninstalls.
3. **Compile.** In NinjaTrader: *New > NinjaScript Editor*, press **F5**, wait for *Compile succeeded*. If it errors, stop.
4. **Name your simulated accounts.** Simulator / Playback need no allow-list. For operator-confirmed prop-firm **simulated** accounts, **Create the directory first**: `Documents\NinjaTrader 8\templates\KINFXTradeCopier` (the installer **does not create this directory**). Then create `Documents\NinjaTrader 8\templates\KINFXTradeCopier\sim-allow.txt`, one exact, case-sensitive NinjaTrader Account `Name` per line (not the login or display alias; `= alias` is optional):
   ```text
   # only accounts YOU have confirmed are simulated
   MYFIRM-SIM-12345 = Firm A
   MYFIRM-SIM-67890 = Firm B
   ```
   A wrong entry can authorize orders to a funded/live account: the file is operator attestation, not provider verification. The file is read once at **static initialization**, not when you reopen the dashboard. After creating/changing it, remain stopped and flat, close copier windows, compile the intended SIM TEST source with F5 if needed, then **restart NinjaTrader** and reopen the dashboard to check eligibility before any Start. **Pressing F5 alone** is not a documented hot-reload guarantee. Missing/unreadable file refuses non-Simulator/Playback accounts.

## First run

1. *New > KINFX Trade Copier*. This is a rehearsal checklist for a separately reviewed SIM TEST candidate, **not permission to run the current unaccepted source**.
2. Pick the **Leader** (for example `Sim101`) and exact contract (for example `MNQ 12-26`). In NinjaTrader's native Orders / Positions tabs, confirm the leader and every selected follower are **connected**, with **zero position and no working orders** for that instrument. Start has no adoption or catch-up.
3. Tick **one** follower, Ratio **1**. On the **Risk** tab tick **Dry run**.
4. Press **Start**; read the leader, follower, contract, Ratio and `DRY RUN` confirmation. A small simulated leader order should produce `DRY RUN would send ...` in Logs. Dry run suppresses copier sends, **not orders you place on the leader**. Before stopping, **cancel working leader orders and close the leader position using native NinjaTrader**; verify the leader and followers are flat with no working orders. Then press **Stop** and verify the session detaches. If it refuses, stop the rehearsal and investigate; do not force an unlock.
5. **Only after the session is detached** may you untick Dry run. Sending a 1-lot simulated order requires separate exact-source review/authorization and a new Start confirmation **without** `DRY RUN`; the current candidate has not passed that gate. **Add followers only while stopped**, recheck all selected accounts are connected and clean, and read a **new Start confirmation** for the full scope.

![Copy tab — synthetic DEMO](docs/screenshots/1-copy.png)

**Screenshot provenance:** the real dashboard control rendered in **synthetic DEMO** mode with **fake accounts and numbers**, no connected account, engine or orders. It is **not NinjaTrader/provider transaction evidence**. [Rendering source](docs/screenshots/Screenshots.cs) · [Risk / Logs gallery and source links](docs/DEVELOPERS.md#dashboard-gallery-and-source).

Each row has its own **Ratio, daily target (Tgt), daily loss (DLL), ATM exit-mirroring, OFF and Flat**. **OFF** blocks new entries and requests cancellation of owned entry orders; positions and exits remain. **Flat** closes that one account after you confirm. After a leader exit the follower goes to `Off` and must be ticked again (on purpose).

## Know the limits

- It is a **source alpha**, not a generally accepted trading build. Historical NinjaTrader 8.1.8.3 runs used 1 simulator leader, 2 operator-attested simulated followers and MNQ; the progress snapshot is source `20dc6707c9e6`. **Not proved on the current source:** real provider ordering/acceptance, 10–30 accounts, leader disconnect, real partial fills, rejected stop / Flatten and recovery. Offline compile/tests do not prove these.
- A **Locked** row requires native NinjaTrader investigation and reconciliation. A risk-latch reset is **not** reconciliation of an uncertain engine ledger, hidden accepted request or thrown Flatten. The explicit verified-flat recovery gate is missing ([#9](https://github.com/Kinfxhk/KINFXTradeCopier/issues/9); known FAIL `ExplicitManualFlatReconciliationAvailable`); remain disarmed/locked and seek review if evidence remains uncertain. The copier never retries a Flatten or catches up after disconnect.
- NinjaTrader's own Orders and Positions tabs are the truth, not this window. It cannot stop orders you place by hand and cannot guarantee fills.
- Do not remove the safety hold in the default build to "just try it".

Trouble? Open an [issue](https://github.com/Kinfxhk/KINFXTradeCopier/issues) with a **redacted** Logs tab (no account names, no balances).

## More

[Developer and status details](docs/DEVELOPERS.md) · [Do NOT do these](docs/DO-NOT-DO.md) · [Known issues](docs/KNOWN-ISSUES.md) · [Test matrix](docs/TEST-MATRIX.md) · [Roadmap](docs/ROADMAP.md) · [Contributing](CONTRIBUTING.md) · [Security](SECURITY.md)

Developers: the portable checks (`python -B -m unittest discover -s tests -p 'test_*.py' -v`) cover packaging and a separate Python SPEC research; the SPEC is not a C# engine implementation and these checks do not run the copier, a broker or real orders.

## Maintainer links and disclosure

Maintainer: KinFx. Prop-firm rule research, comparison tables, tool links and referral codes: **<https://linktr.ee/kinfx>**

Some links on that page are **affiliate / referral links** (for example prop-firm evaluation codes and partner sign-ups); the maintainer may earn a commission if you use them. They have **no influence on this software**: the code is MIT, contains no referral logic or tracking, and nothing here recommends a firm or a purchase. KinFx is not an SEC/SFC-licensed adviser; content is not investment advice. Most evaluation accounts are simulated. This project is not affiliated with any firm listed there.

## License

[MIT](LICENSE), © 2026 Kinfxhk. Not affiliated with NinjaTrader, FlowBots/Replikanto, any broker or prop firm. No competitor code, vendor DLLs, private account logs or credentials are included. Prop templates in the source are historical editable metadata, not current rules or compliance certification.
