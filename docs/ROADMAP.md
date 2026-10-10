# Roadmap — publish together, enable trading deliberately

The immediate deliverable is a **collaborative, disarmed source alpha**, not a solo feature-complete copier. Existing safety findings are public work items. Feature parity with commercial copiers is **not** a publication or release gate; vendor history is context, not proof of our defects, implementation or acceptance.

Repository: [Kinfxhk/KINFXTradeCopier](https://github.com/Kinfxhk/KINFXTradeCopier). Existing milestone names are **M0 publication**, **M1 community offline**, **M2 Sim design**. This roadmap does not mark any milestone complete or imply that source has already been pushed. Issue links were supplied after the parent verified their existence; this audit made no remote writes.

## M0 — publication safety, documentation and tests

**Goal:** let contributors review useful source now without mistaking it for trading-ready software.

- Preserve Start/Flatten holds in Dashboard commands and direct engine paths for **all external accounts**, including NT Simulator/Playback. Fake-only paths must not become an external-account escape through a compile define.
- Publish original source, license, synthetic tests and reproducible compile-only/fake tooling. Pin exact final bytes; clearly distinguish historical baseline, safety-hold changes and unmerged research candidates.
- Include [TEST-MATRIX.md](TEST-MATRIX.md), [KNOWN-ISSUES.md](KNOWN-ISSUES.md) and the sanitized [EVIDENCE.json](EVIDENCE.json) index. Do not replace failures with a headline assertion count.
- Make clean-clone build instructions configurable and truthful about required locally installed, lawfully obtained NT references. Do not ship proprietary DLLs, scraped vendor HTML, private logs/transcripts, credentials or personal account identities.
- Invite minimal synthetic reproducers, test review, tooling portability and design discussion through the existing issues. Publishing does not close the safety tasks or authorize installing/running trading code.

**Completion evidence to collect:** exact public source/test manifests, hold checks, safe test/build outcomes and repository-content readback. These are acceptance criteria, not claims already satisfied. Start with [#1 — clean clone/build](https://github.com/Kinfxhk/KINFXTradeCopier/issues/1).

## M1 — community offline safety work

**Goal:** reproduce P0 failures and review small exact-base changes together while account execution remains held.

| Priority | Community slice | Completion evidence |
|---|---|---|
| P0 | [#2 — fill frontier / protective F01](https://github.com/Kinfxhk/KINFXTradeCopier/issues/2) | Every emitted request respects captured positive evidence; queued/out-of-order payloads, lagged Position and unknown ACKs cannot silently shrink coverage. |
| P0 | [#3 — group OCO](https://github.com/Kinfxhk/KINFXTradeCopier/issues/3) | Stable fractional slice allocation, A-complete/B-still-owned cancellation proof, partial/late fills, pending amendments and double-fill reversal evidence. Pure certificates alone are insufficient. |
| P0 | [#4 — mailbox / Dispose / HOL](https://github.com/Kinfxhk/KINFXTradeCopier/issues/4) | No lost accepted callbacks, observable rejected admission, honest detach/continuation, deterministic blocked-provider isolation at 10/20/30 synthetic accounts. |
| P0 | [#5 — numeric drafts / settings](https://github.com/Kinfxhk/KINFXTradeCopier/issues/5) | All-field drafts, true recycling, precise accepted values, selected identities/tokens and agreed nested-publication contract through exact persistence methods. |
| P0 | [#6 — journal / restart](https://github.com/Kinfxhk/KINFXTradeCopier/issues/6) | Durable frame admission/replay and scoped authenticated closure design; preserved fills/latches, no auto-arm; rotation, restart and interrupted-write tests. |
| P0 infrastructure | [#1 — clean clone / build](https://github.com/Kinfxhk/KINFXTradeCopier/issues/1) | Repeatable clean-checkout builds, fake-symbol/reference isolation, CI without account access and truthful output/cleanup outcomes. |
| P1 native usability | [#8 — native UI / DPI](https://github.com/Kinfxhk/KINFXTradeCopier/issues/8) | Plan native modal/draft/token lifetime and true per-monitor DPI tests separately from logical-scale/offline layout probes. |

**Contribution contract:** pin the immutable base and fixture; retain RED and negative controls; propose one narrow patch with explicit safety policy; independently replay the exact candidate; report retained failures. Do not copy a complete older clone over a newer source. Passing candidates stay proposals until review, explicit integration and new exact-integrated regression results.

**Integration gate:** compile the complete final public source, rerun pure/fake/UI/storage/tooling suites and all retained adversarial branches on those exact bytes; keep harness/policy disagreements separately adjudicated. A collection of individually passing candidates is not an integrated PASS. No automatic milestone closure follows from a test count.

## M2 — deliberate Sim-first design, not automatic enablement

Use [#7 — future simulator design](https://github.com/Kinfxhk/KINFXTradeCopier/issues/7) to agree a narrow acceptance protocol **after P0 safety review**. This milestone is design work; it is not current account-test permission.

Before any later authorized account run:
1. Name exact reviewed source, NT/provider versions, supported entry/protection policy and exclusions. Keep unsupported late bracket discovery/pre-entry protection honest; exit mirroring is not native follower ATM.
2. Specify operator confirmation, selected-account/instrument scope, quantity cap, abort and manual reconciliation criteria. Unknown API return/exception is not rejection or flatness. Never auto-retry, catch up or rearm uncertain work.
3. Stage at least two actual NT demo/simulator accounts: one leader and at least one follower, then deliberate expansion. Fake objects called Sim and a 30-instance reducer are not this acceptance.
4. Accept partial fills, cancellation/no ACK, replacement identity, ordering, disconnect/reconnect/late fill, slippage and unknown acceptance separately. Capture actual per-account latency/HOL for any thirty-account capacity claim.
5. Exercise native shutdown/workspace removal/restart, authenticated recovery, journal rotation/power-loss assumptions and native UI/DPI/modal lifetimes. Preserve unresolved evidence and durable latches.

**Until separately authorized and accepted:** external-account Start/Flatten holds remain. No install, account API call or GUI trading action is implied by this document. Simulator success would still not certify live providers, OCO atomicity, fills or prop-firm compliance.

## Later optional scope

Native follower ATM, broader instrument/provider support, richer telemetry and commercial-feature comparisons can be discussed after safety foundations. Add a documented contract and acceptance slice before each feature. No feature-count parity promise, live-release date or autonomous trading certification is offered.

**廣東話摘要：** M0 先公開安全封住嘅源碼、文件同測試，邀請大家一齊做；M1 合作修 P0 同驗整合；M2 先設計、再另行批准真正 Sim 驗收。唔等功能齊晒先開源，亦唔因為大量 PASS 就解封交易。
