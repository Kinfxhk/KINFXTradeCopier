# Proposed journal lifecycle and restart acceptance contract

Proposal for [issue #6](https://github.com/Kinfxhk/KINFXTradeCopier/issues/6), pending maintainer decision. This is not an implemented journal or a verified recovery path. The public AddOn persists settings and risk latches; the standalone journal and its three nested-Dispose failures exist only as historical evidence references in [KNOWN-ISSUES](KNOWN-ISSUES.md) and [EVIDENCE.json](EVIDENCE.json). Recovery is explicitly shelved in [OFFLINE-STATUS](OFFLINE-STATUS.md).

## Nested Dispose decision proposed for review

Use deferred, automatic resource release after the active operation returns, rather than requiring a second explicit Dispose. This gives an IDisposable caller one meaningful request and avoids leaking a stream after a reentrant error callback. Do not interrupt or roll back an acknowledged durable frame.

| State | Allowed transition | Admission and resources |
|---|---|---|
| Open | Append/replay starts → Active | Admit one serialized operation |
| Active | Dispose called, including from a fault callback → Closing | Immediately refuse new operations; defer stream release until active frame completes |
| Active | Operation completes without Dispose → Open, or Faulted | No optimistic projection after an unacknowledged flush |
| Closing | Active operation unwinds → Closed | Release once in finally, even after a projection/fault callback throws |
| Faulted | Dispose → Closed | Retain diagnostic/prefix evidence; release once |
| Closed | Dispose → Closed | Idempotent; no append or callback reentry |

A nested Dispose must not wait for itself. A different thread requesting Dispose needs a specified bounded wait or asynchronous completion contract; timeout must report incomplete disposal and must not certify durable completion. Capture this decision in implementation documentation before changing the three existing test oracles. Retain their original failing evidence.

## Required deterministic lifecycle checks

Run the original three release-expectation cases against the exact original and candidate journal bytes, with unchanged assertions: Dispose before write, after write/before flush, and after flush/before projection. Add independent controls for ordinary Dispose, repeat Dispose, cross-thread Dispose, callback reentry into append/replay, and a callback throwing during cleanup. Assert both admission refusal and actual one-time resource release. Test stream ownership explicitly; do not close a borrowed stream silently.

Instrument write, durable flush, acknowledgement and projection separately. A failed write or flush must not produce an acknowledged frame. A projection failure after acknowledgement must retain the committed frame for replay and must not invite a second append. Record callback delivery guarantees; no exactly-once claim across a crash without an explicit durable protocol.

## Restart acceptance matrix

| Scenario | Required evidence / refusal |
|---|---|
| Clean restart with an acknowledged frame | Replay its exact validated prefix once; retain durable risk latches; keep trading disarmed |
| Partial last write / truncated record | Retain validated prefix, reject suffix visibly; never synthesize a complete frame |
| Malformed UTF-8 or UTF-16, over-limit record | Refuse before state mutation or acknowledgement; preserve prior prefix |
| Write, flush or projection fault | Distinguish which boundary committed; replay acknowledged frames only |
| Interrupted rotation / replacement | Select a generation using a specified durable manifest; reject ambiguous generations; test simultaneous append/rotation |
| Process killed before/after each boundary | Reopen in a new process; validate prefix and latch preservation; report filesystem/runtime assumptions |
| Power loss | Separate experiment and storage guarantees; process-kill tests cannot certify it |
| Unknown accepted Submit/Change/Flatten | Remain disarmed and unresolved even if Position is zero and visible orders are terminal |

Checksums and hash chains detect specific corruption under their assumptions; they do not authenticate broker acceptance or prove an account is flat. Replaying a ledger restores local evidence, not broker truth. Neither restart nor local verified-prefix replay may clear a risk latch, adopt an existing position, retry an uncertain reduction, or lift the all-provider Start/Flatten hold.

## Integration gate

Before an implementation PR: provide an accessible standalone journal source/commit and runner; settle the nested-Dispose and stream-ownership policies; identify the exact production admission/acknowledgement/replay boundary; rerun the retained failures and independent review. Keep journal integration separate from engine close-race fixes (#4).

Acceptance artifacts must identify immutable source/fixture hashes, runtime/compiler versions, command lines, result counts, retained failures and exact scope. No proprietary assemblies, real credentials, production calls or raw sensitive logs belong in the PR. Native provider reconciliation and authorized simulator acceptance remain separate milestones.
