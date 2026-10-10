# Known issues and evidence limits

This source alpha is open for collaboration **with trading disabled**. These issues block later simulator/provider acceptance, not publication of disarmed source and synthetic tests. No live readiness, automatic protection recovery or merged-candidate acceptance is claimed.

Historical source/candidate hashes and scoped counts are in [EVIDENCE.json](EVIDENCE.json); the missing-test plan is [TEST-MATRIX.md](TEST-MATRIX.md). Findings below attach to their recorded implementations. The public baseline-derived snapshot has publication-hold edits, not the repair clones; this audit did not execute its engine. Reproduce against the exact current source before closing an issue.

## P0 safety categories

### F01 — protective resizing can issue an unsafe intermediate request

**Known baseline product defect; later scratch frontier controls pass, not integrated.** [E02, E03]

Synthetic reproduction:
1. Maintain acknowledged protective total4 and captured positive fill2 while the mutable wrapper is already Filled4.
2. Report Position1 while owned execution evidence is2; the baseline requests total3 instead of preserving/quarantining the total4 protection.
3. Separately queue older Working0 before PartFilled2. Baseline requests total2 and later total4. The final correction cannot undo the earlier external request.

`FreshPositionAheadDoesNotUndersize` and `FreshLateWorkingBeforePartialCannotUndersize` both failed, each repeated five times on two copies with the same baseline hash. A later clone's core16 passes those controls; that does not certify existing broker coverage, final public bytes or all ACK interleavings.

**Task:** preserve captured/applied account frontiers, requested/acknowledged/reserved totals and one unresolved command; assert every request under delayed/no ACK and accepted-then-throw. [Issue #2](https://github.com/Kinfxhk/KINFXTradeCopier/issues/2)

### OCO — completed group versus account-wide remaining exposure

**Known residual product defect on tested repair clones; no group-completion implementation acceptance.** [E03, E10]

Synthetic reproduction: allocate entry exposure4 to groups A2 and B2. Fill A's target2 completely while B still owns2. A's stop should be retired only with group-scoped completion proof; B's protection must remain. `ReviewOcoCompletedSliceCancelOnlyItsGroup` remains FAIL on frozen/cancellation candidates (OCO10 9 PASS / 1 FAIL each). Account-wide owned-zero cannot distinguish this case; guessed group allocation can cancel the sole valid stop.

Also retain pending-amendment completion, partial terminal target, unequal sole-stop, late entry and OCO double-fill cases. Largest-member OCO budgeting is not a simultaneous-fill bound or broker reduce-only guarantee. Pure certificate/allocation algebra is research, not a production repair or native ATM implementation.

**Task:** define stable slice allocation and immutable group/session/frontier certificates, then test all emitted cancellations and both-side double fills without dropping positive evidence. [Issue #3](https://github.com/Kinfxhk/KINFXTradeCopier/issues/3)

### Mailbox, public Dispose and head-of-line blocking

**Known fake-engine failures; real multi-account latency remains untested.** [E10, E13]

Synthetic reproduction:
- Admit callbacks during a blocked cleanliness read in public Dispose; span multiple bounded drain turns with 130 positive envelopes. Owner evidence retained64 and stranded66. A pre-read captured==applied check does not certify post-read cleanliness.
- Stop the worker and Post again: rejection must be observable rather than silently accepted/discarded.
- Block one follower's Submit or Change while 10/20/30 followers share the worker. Measure healthy-follower progress and OFF/cancellation delay; a drain batch limit is not mailbox capacity or worker isolation.

`RequestMailboxHardBound`, `RequestRejectedPostMustBeObservable` and `RequestPublicDisposeCannotLoseAcceptedCallbacks` remain FAIL. Historical scale130 is 63 PASS / 67 FAIL, including blocked-call/HOL scenarios; those are overlapping invocations, not 67 independent defect categories. Later ON/cancellation frontier repairs explicitly did not fix mailbox/Dispose/HOL.

**Task:** lossless bounded admission, explicit refusal/continuation semantics, no false clean detach, and isolated slow-account execution with deterministic barriers. Do not fix overload by dropping fill payloads. [Issue #4](https://github.com/Kinfxhk/KINFXTradeCopier/issues/4)

### Settings drafts, identity membership and publication contracts

**Scoped membership repair passes; not integrated. Publication failures need policy adjudication.** [E03, E05]

Synthetic reproduction: add the same AccountRow reference twice, remove one occurrence, then rename/export the remaining visible row. A unique-identity HashSet can lose ownership while an occurrence remains. Candidate alias4 and collection22 pass, but this does not accept all native draft/token paths.

For nested Export/Persist/Save, an observer can finish a valid inner publication before invalidating the outer token. Six broad unchanged-everything failures include that already-completed inner operation. They cannot automatically be called outer corruption. Fresh run2 has 41 PASS / 14 FAIL: six broad oracles, six proposed whole-root policy controls, one strict-zero-transient visibility control and one representation-only XML-ordering failure. The latter retains its runtime FAIL despite equal field maps and unchanged disk/index bytes. No whole-root policy was adopted.

**Task:** agree operation-local refusal versus whole-resource isolation; preserve valid completed inner work unless the contract forbids it. Validate all row-owned numeric drafts, actual recycling, selected identity revisions, modal tokens and exact persistence methods. [Issue #5](https://github.com/Kinfxhk/KINFXTradeCopier/issues/5)

### Journal admission, callback reentry and recovery truth

**Standalone fixes pass their scope; not integrated. Nested Dispose policy FAIL and provider closure unknown remain.** [E04, E06, E07]

Synthetic reproductions:
- An existing-latch or committed-proposal idempotent shortcut can acknowledge malformed UTF-16 without codec admission.
- Configured process-local SHA callbacks can reenter before frame admission, producing acknowledged duplicate sequence/predecessor frames which do not all replay.
- An internal synthetic verifier mutates revision during Prepare/Commit; a stale proof must be refused before proposal/frame publication, not after writing an invalid tail.

Early-admission fixes alone leave three minimal reentry failures. The three-fix candidate has 908 PASS / 3 FAIL among 911 final-candidate invocations; the encompassing 25-run/948 ledger also includes RED/intermediate/base runs. Independent fresh23 is 23 PASS versus original 5 PASS / 18 FAIL; retained20 remains 17 PASS / 3 nested Dispose release-expectation FAIL. Those failures concern the documented policy requiring a later explicit Dispose rather than immediate release from a nested fault callback; do not conflate them with engine Dispose callback loss.

Custom-crypto/internal-verifier probes do not prove default production reachability. A synthetic committed proposal, hash chain, Position0 or all visible orders terminal does not authenticate closure of a hidden accepted Flatten request. The baseline XML risk-latch save tests are not a durable execution-recovery journal.

**Task:** define nested-disposal contract, authenticated scoped provider/operator evidence and every ambiguous-request resolution; integrate versioned recovery, restart, rotation and power-loss tests while preserving authorization latches and original fills. [Issue #6](https://github.com/Kinfxhk/KINFXTradeCopier/issues/6)

## Keep non-product failures visible

- **Pure SPEC:** final validator388 and contract191 PASS do not exercise the C# engine. semantic91 still has 89 PASS / 2 schema FAIL (`register_schema_session`, `register_schema_generation`). Registration-price hints, Cancel capacity and ACKs below positive fills have scoped validator corrections, not integrated dispatch acceptance. The preserved initial harness failures and corrected legal-prefix fixtures must remain separately identified. [E08, E09]
- **Harness:** nonexistent/misordered reflection method invocation, missing fixture-relative seed files, output file locks and compiler-host crashes can fail before the intended assertion. Preserve these attempts; corrected invocation is not a product RED→GREEN. One secondary fake callback legitimately advanced the exit ledger in an unisolated probe; isolated and unisolated results are both retained. [E01, E10]
- **Fixture callback gap (fixed in tests, not product):** `AtmMode`, `Lifecycle` and `SubsetFlatten` marked the fake follower entry Filled/Position but never emitted that fill as an `OrderUpdate`. The engine correctly refuses to copy exits across an unobserved fill, so they timed out at "exit copy". Each now emits the fill callback (3 added `Emit` calls, no assertion changed): engine 89P/5F → 92P/2F, same production SHA. `FlatWorkingExits` and `FailedFlattenKeepsProtection` already emit it and still fail, so they are **not** explained by this gap. A scratch probe (repo untouched) shows why; they are **old-oracle vs current-policy conflicts**, left failing on purpose ([Issue #9](https://github.com/Kinfxhk/KINFXTradeCopier/issues/9)):
  - `FlatWorkingExits` expects `Cancels==1` when Position reads flat while owned exposure is 2. Engine result: `cancels=0 submits=2 locks=1`: it locks and keeps the sole protective exits. This matches the current "ambiguous flat telemetry must not cancel sole protection" policy, so the old assertion is probably obsolete, but nobody has decided that.
  - `FailedFlattenKeepsProtection` expects a new leader exit to be copied (`Submits==2`) after a Flatten call throws. Engine result: `submits=1 locked=1`: the follower stays in Flattening, so the new exit is skipped. Current policy blocks concurrent recovery exits while a close may have been accepted. Mirroring the exit risks a duplicate reduction; skipping it can leave new exposure unprotected. This is a policy trade-off, not a bug I can settle offline.
- **Pure performance policy:** G01 index review retains 333 PASS / 35 FAIL; equivalent snapshots and deterministic node/scan budgets are not provider latency acceptance. A prior scope incident remains separate from technical findings; a clean parent replay does not erase it. [E11]
- **Build tooling:** a scratch standalone driver passes 18 boundary probes and one NT compile-only. It does not fix PowerShell Roslyn StackOverflow root cause, long-path limitations or bypass through direct editor/compiler builds. Source and reference warnings have different origins; warning counts are not defect counts. [E12; issue #1](https://github.com/Kinfxhk/KINFXTradeCopier/issues/1)

## Unknown acceptance, not a solved defect

No controlled acceptance with **two or more actual NT demo accounts** is established. Real provider identity replacement, ordering/reconnect/late fills, actual execution slippage, unknown Submit/Change/Cancel/Flatten acceptance and OCO double fills remain unknown. NT shutdown/workspace detach, thirty-account real latency/HOL, integrated authenticated journal restart/rotation/power loss, true per-monitor DPI and native modal draft/token lifetime remain untested. [Future simulator design #7](https://github.com/Kinfxhk/KINFXTradeCopier/issues/7), [native UI/DPI #8](https://github.com/Kinfxhk/KINFXTradeCopier/issues/8)

Do not unlock trading by editing a define or removing a hold. A later simulator design needs explicit authorization, reviewed exact source and measurable abort/reconciliation criteria. Live operation is outside this alpha's acceptance.

**廣東話摘要：** 最重要未解決嘅係保護單縮量、分組 OCO 完成、mailbox／Dispose／HOL，同整合後嘅 settings／journal 驗收。Oracle／harness／policy FAIL 唔可以一律當產品 bug；scratch GREEN 亦唔代表已合併。可以先協作發佈，但唔可以宣稱可交易。
