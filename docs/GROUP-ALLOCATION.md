# Issue #3 allocation and protection contract

PR #22 integrates group allocation into the actual extracted CopierEngine, not a separate oracle. All real Account Start/Flatten holds remain. The current command frontier is an authorization boundary; it cannot establish broker OCO atomicity or prevent fills that already raced a cancellation.

## Ownership and integer allocation

An explicit OCO id identifies an exit group within one follower/session. A group owns fixed, disjoint FIFO ordinal intervals of copied entry fills on the matching side. This is a deterministic copier allocation, **not an assertion of broker/ATM parentage**. Cumulative decimal group demand assigns differences of successive integer floors. For example Ratio .5 and leader groups 1 + 3 + 6 allocate 0 + 2 + 3 follower lots, covering the copied 5-lot entry; independent per-group flooring would lose one lot. Zero-quota groups keep their explicit intent so a later supported plan revision can allocate them.

Alternatives within an OCO group share that group's quota. A smaller target can exhaust itself without completing a larger stop's ownership; remaining protection is resized against the group's held lots. Account Position is only an additional closable bound, never ownership evidence. Existing lagged-Position quantity freezes remain intact.

A quantity reduction releases unused ordinals only after the requested/acknowledged totals match and all group members' captured remaining quantities fit. Cancelling a group likewise retains ownership until captured terminal ACKs; its consumed prefix cannot be reassigned. A returned Change, mutated wrapper or accepted-then-throw never proves release.

## Completion and dispatch certificates

Each certificate carries group id, session, allocation revision, integer quota, captured entry/exit fills, held/future lots and captured leader/follower frontiers. Completion requires positive quota, all allocated entry fills and exactly that many group exit fills, settled frontiers, matching identities/actions, disjoint claims, account-owned net support and no unresolved group quantity command. Unknown manual activity and ambiguous execution evidence invalidate support. Positive fill evidence is retained even when terminal metadata is inconsistent.

A completion cancel checks the same revision/session/frontiers at provider dispatch. A changed frontier vetoes the call and retains the working order until a new settled certificate proves retirement. Unknown/ambiguous support freezes retirement for manual review. Multiple positive OCO exit fills retain both executions and lock; no speculative recovery/flatten or assumption of atomic cancellation is made.

## Complete protection plan and delayed fills

`ProtectionPlanFor` snapshots group certificates plus owned, captured covered and uncovered lots. Working OCO alternatives count at most their group's held lots, never add together or conceal another group's uncovered allocation. Unacknowledged submissions are not coverage.

Completing A stops new entries when the account is flat; it never certifies B complete. B's already-explicit, still-active waiting exit may protect B's subsequently captured allocated lots. This deliberately replaces the prior manual-only waiting-exit policy. No missing order is invented. Cancelled intent cannot resume. Every new group submission checks its exact certificate again at dispatch and serializes group submissions across captured acceptance. Existing amendments retain their ordered stream under conservative ownership bounds; pending commands cannot certify completion or release claims. A locally vetoed **unsent** Submit can be discarded and re-evaluated; a provider call with unknown acceptance cannot be retried. The account remains OFF/locked as applicable. Dynamic telemetry reports current uncovered lots, not a stale pre-acceptance warning. Without valid explicit protection the exact manual-action warning remains.

## Independent exits without OCO

No OCO identity is inferred from a stop/target shape. Independent exits reserve separately. When acknowledged protection already covers owned/actual exposure and no entry or protection quantity/cancel command is pending, an excess unsubmitted non-market exit is visibly refused without cancelling sole protection or locking an otherwise healthy follower after 10 seconds. The refused intent is not retried by polling or amendment. Pending ACKs and future entry fills still use the existing conservative waiting path. Explicit market closes retain upstream #24 sizing and orphan-protection cancellation behavior.

## Acceptance evidence

| Requirement | Actual engine regression |
|---|---|
| Stable entry/group ownership; A cannot retire B | `IndependentEntryOwnershipKeepsOtherGroupWaitingProtection`, `OcoCompletedGroupDoesNotCancelOtherProtectedGroup` |
| Fractional integer remainder coverage | `FractionalGroupsCoverEveryIntegerLot` (six ratios, three groups, both OCO members) |
| Partial exit, unequal target and sole stop | `UnequalTargetDoesNotRetireSoleStop`, `PartialTerminalDoesNotCancelSoleProtection` |
| Release only acknowledged unused ownership | `GroupClaimReductionRequiresCapturedAck`, `CancelledGroupAckReleasesOnlyUnconsumedClaims` (partial and unfilled variants) |
| Complete delayed protection and callback permutations | `CertifiedLateEntryFillProtectsWaitingGroup` (queued/sequential, entry-first/exit-first) |
| Stale callbacks / amend before Submit ACK | `OlderWorkingCallbackKeepsEntryOwnershipCertificate`, `EntryAmendBeforeSubmitAckIsApplied` |
| Exact frontier at dispatch | `GroupRetirementRechecksProviderDispatchFrontier`, `WaitingGroupSubmitRechecksLeaderFrontier` |
| Unknown same-OCO work fails conservatively | `UnknownSameOcoActivityCannotCertifyGroupRetirement` |
| Double-fill/reversal counterexamples | `GroupDoubleFillPermutationsRetainReversalEvidence` (both permutations) |
| Unacknowledged same-OCO cancellation; existing sibling preservation | All original PR #22 same-OCO cases and controls retained |
| Independent no-OCO excess intent / pending ACKs | `StopAndTargetWithoutOcoDoNotLockFollower`, `IndependentExitWaitsForCapturedReductionAck`, `UnackedCancelledIndependentExitStillWaits` |
| Missing intent and actual coverage accounting | `UnprotectedLotsTextIsExactAndNeverOptimistic`, `UnprotectedLotsFigureIsExact` |

Run the repository's audited offline runner as documented in BUILD.md. Linux Mono results are supplemental evidence only. Official Windows Framework/full NT8 compilation and real provider cancel timing/atomicity are not verified here. Those limits remain explicit and do not reduce the implemented issue scope or authorize trading. Merge and release remain upstream maintainer decisions.
