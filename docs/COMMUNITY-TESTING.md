# Safe community testing

## Allowed now

- Read and review code; run stdlib portable tests on an isolated checkout.
- Windows: compile with locally installed licensed NT8 references, **compile-only**.
- Run only audited Framework-only synthetic engine/policy tests. Fake Account methods must resolve to the same harmless test assembly.
- Inspect disarmed preview source/UI design. Native GUI installation/activation has not been authorized by these automated checks.

## Not an alpha acceptance claim

Do not send real, prop, Sim or Playback orders with this repository. Two-account controlled simulation is a future milestone, after a reviewed opt-in gate and unresolved safety defects are addressed. A single demo account cannot prove Leader→Follower copying. Broker-specific testing must declare adapter, NT8 version, account type, event sequence and timestamp semantics, with no account identifiers in public reports.

## Useful scenarios to contribute

1. Captured fill2 / stale applied0 / Position1 or2 while protective Change is planned: assert every outbound quantity, not just eventual state.
2. Submit/Change/Cancel/Flatten accepted then throw, delayed ACK, missing ACK, duplicate or old callback: no blind retry or fictional clean state.
3. Multiple exit groups: one completed group cannot retire unrelated waiting protection.
4. OFF during blocked CreateOrder or queued callback: no new unauthorized entry; retain owned exposure and exits.
5. 10/20/30 synthetic followers: one stalled account must not freeze healthy accounts; record per-account latency and queue evidence, not aggregate throughput alone.
6. Invalid hidden Target/DLL/Ratio drafts; scrolling/recycling/polling/save: errors and drafts survive, all-row validation prevents partial mutation.
7. Crash/restart and journal rejection: distinguish process crash from power loss and checksum from authenticated closure.

## Bug report evidence

Use the issue template. Include exact commit, test name, input trace, actual command/exit and expected invariant. Redact account IDs, balances, platform paths and credentials **before** uploading. Prefer fresh fake IDs and deterministic fixtures. Keep original failure output alongside corrected harness runs. Do not post raw screenshots of a live account.

Portable CI success means only its named suites passed; it does not certify the NT8 engine or provider.
