# KINFX Trade Copier

Copy trades from one NinjaTrader 8 account to many others (prop-firm **simulated** accounts and demo accounts). Free, open source (MIT), early **alpha**.

**English** · [廣東話／繁體中文](README.zh-HK.md)

> **TRADING DISABLED in the default build. Funded and live accounts: do not use.** Only the opt-in SIM TEST build copies, and only to accounts you have confirmed are simulated.

<!-- QUICKSTART:START -->
## Can I use it?

| | |
|---|---|
| **Demo / Simulator accounts** | **Yes, as an early test build.** About **69%** done. |
| **Prop-firm accounts** | **Only your prop-firm *simulated / evaluation* accounts, for testing.** About **62%** done. Tested on 2 such accounts. Check your firm's copier rules first: nothing here certifies compliance. |
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

Every tick and its limits: [docs/PROGRESS.json](docs/PROGRESS.json). Real-NinjaTrader test log: [docs/VM-PROGRESS.md](docs/VM-PROGRESS.md).

## Install (Windows + NinjaTrader 8, about 5 minutes)

1. **Download.** *Code > Download ZIP* on this page, then unzip. (Or `git clone https://github.com/Kinfxhk/KINFXTradeCopier`.)
2. **Install.** In the unzipped folder run in PowerShell:
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\install-simtest.ps1
   ```
   It copies the SIM TEST build into `Documents\NinjaTrader 8\bin\Custom\AddOns`, backs up anything it replaces and touches no account. `-WhatIf` previews, `-Remove` uninstalls.
3. **Compile.** In NinjaTrader: *New > NinjaScript Editor*, press **F5**, wait for *Compile succeeded*. If it errors, stop.
4. **Name your simulated accounts.** *Simulator* and *Playback* accounts work as they are. For prop-firm **simulated** accounts, create `Documents\NinjaTrader 8\templates\KINFXTradeCopier\sim-allow.txt`, one account per line, exactly as the *Account name* column in NinjaTrader shows it (an alias after `=` is optional):
   ```text
   # only accounts YOU have confirmed are simulated
   MYFIRM-SIM-12345 = Firm A
   MYFIRM-SIM-67890 = Firm B
   ```
   A wrong entry means real orders to that account. Press F5 again.

## First run

1. *New > KINFX Trade Copier*.
2. Pick the **Leader** (for example `Sim101`) and type the contract (for example `MNQ 12-26`).
3. Tick **one** follower, Ratio **1**. On the **Risk** tab tick **Dry run**.
4. Press **Start**. The confirmation must say `DRY RUN`. Place one small order on the leader; the Logs tab should show `DRY RUN would send ...`. Press **Stop**.
5. Untick Dry run and repeat with 1 lot. Then add more followers.

![Copy tab](docs/screenshots/1-copy.png)

Each row has its own **Ratio, daily target (Tgt), daily loss (DLL), ATM exit-mirroring, OFF and Flat**. **OFF** only stops following; **Flat** closes that one account after you confirm. After a leader exit the follower goes to `Off` and must be ticked again (on purpose).

## Know the limits

- It is an **alpha**. Tested on real NinjaTrader 8.1.8.3 with 1 leader, 2 followers and 1 instrument (MNQ), Market, Limit and Stop orders. **Not yet tested for real:** 10-30 accounts, the leader's own disconnect, a rejected stop or Flatten, restart recovery.
- A **Locked** row means the copier saw something it could not explain. Look in NinjaTrader, fix it by hand, then unlock from the row's gear button. It never retries a Flatten and never catches up after a disconnect.
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
