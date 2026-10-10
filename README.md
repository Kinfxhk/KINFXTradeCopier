# KINFX Trade Copier — community development alpha

**TRADING DISABLED. Not a working trade-copier release. Do not use for live, prop, Sim or Playback orders.**

Original MIT-licensed NinjaTrader 8 AddOn by Kinfxhk, published early so people can review, reproduce failures and improve it together. We are not waiting for every feature before sharing the source. This repository is a development baseline, not an accepted integration of all researched fixes.

## What is here

- One native dusk-dark Copy / Risk / Logs dashboard, designed for 10–30 accounts.
- Independent per-account Ratio, target, daily loss and ATM exit-mirroring intent.
- Original engine and Framework-only synthetic test fixtures, with known failures disclosed.
- Separate pure Python NR01/NR02/NR03 **SPEC research**, not a C# engine implementation.
- Test matrix, priority backlog and contribution templates.

The intended later flow is Leader → tick followers → set each Ratio → Start. **Start and Flatten remain held for every real NT8 account.** OFF is not Flatten. ATM means exit-order mirroring, not native follower ATM cloning. Cash is CashValue with its denomination, not equity. No global Ratio setting.

## What it looks like

Rendered from the real `DashboardView` in synthetic DEMO mode: no NinjaTrader account, no engine, no orders. Start/Flatten are disabled in this alpha. Names are fake. Source: [`docs/screenshots/Screenshots.cs`](docs/screenshots/Screenshots.cs). Balances/P&L are made-up demo values; unknown cash is shown as an unavailable dash. These are 1160x720 offscreen renders, not a real NinjaTrader window, so fonts/DPI can differ. An automated visual read suggested the Ratio column may be tight at this width; see [#8](https://github.com/Kinfxhk/KINFXTradeCopier/issues/8).

Copy tab: per-account Ratio / Tgt / DLL / ATM / OFF are editable in each row, and the **Bulk** bar sets Tgt, DLL or ATM on all highlighted followers at once, and **From Leader** copies the leader row's Tgt / DLL / ATM to them as a one-time default you can then fine-tune per row (Ratio and ticks are never touched). Self-managed per-account TP/SL is not implemented yet: [#10](https://github.com/Kinfxhk/KINFXTradeCopier/issues/10).

| Copy | Risk | Logs |
|---|---|---|
| ![Copy tab: per-account Ratio, Tgt, DLL, ATM, OFF](docs/screenshots/1-copy.png) | ![Risk tab: advanced per-account settings](docs/screenshots/2-risk.png) | ![Logs tab](docs/screenshots/3-logs.png) |

## Start contributing safely

Python 3.10+; no Python packages, trading account or NT8 needed:

```sh
python -B -m unittest discover -s tests -p 'test_*.py' -v
```

These portable checks test packaging/holds and a separate SPEC; they do **not** run the C# copier, a broker or real orders. For Windows Framework-only engine tests and full **compile-only** NT8 checks, see [build instructions](docs/BUILD.md). No vendor assemblies are redistributed. No installer or compiled trading binaries are provided.

## Read before testing

- [Test matrix: untested vs failed vs scoped evidence](docs/TEST-MATRIX.md)
- [Known issues](docs/KNOWN-ISSUES.md)
- [**Do NOT do these things** (actions the software cannot recover from)](docs/DO-NOT-DO.md)
- [Safety hold](docs/SAFETY-HOLD.md)
- [Community roadmap](docs/ROADMAP.md)
- [Contribution workflow](CONTRIBUTING.md)
- [Testing and redacted bug reports](docs/COMMUNITY-TESTING.md)

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

Past offline runs tested multiple immutable candidates. Passing those candidates does not mean their fixes are merged here. The published source is based on the previously held original baseline plus publication safety hardening only. Counts are per suite/artifact, not a completion percentage. [Evidence summary](docs/EVIDENCE.json) separates C# engine evidence, UI probes, harness incidents and Python SPEC results.

No full provider acceptance, restart recovery, broker OCO atomicity, native follower ATM, live-copy safety or prop-firm compliance is established. This software cannot intercept every manual/strategy order or guarantee fills. Existing positions must be managed with native NT8/broker tools, not this held preview.

## License and attribution

[MIT](LICENSE), © 2026 Kinfxhk. Not affiliated with NinjaTrader, FlowBots/Replikanto, any broker or prop firm. No competitor code, vendor DLLs, downloaded research HTML, private account logs or credentials are included. Prop templates embedded in the source are historical editable metadata, not current rules or compliance certification.

## 廣東話

呢個係**公開共同開發 alpha，唔係可跟單正式版**。先分享原創source、可重現測試及已知FAIL，大家透過Issue／PR分工；唔再由一個人做晒全部功能先公開。真NT8帳戶（包括Sim／Playback）交易仍封鎖，Python SPEC PASS唔等於C# engine安全。
