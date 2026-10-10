# Contributing

We welcome minimal reproductions, tests, documentation, accessibility/UI work and focused safety fixes. Feature completeness is not a prerequisite for collaboration.

1. Read README, safety hold, test matrix and known issues.
2. Open or find an issue; identify exact source commit/hash, intended contract and evidence boundary.
3. Fork and branch. Write one unchanged failing safety assertion before a behavior fix; retain its RED output, then minimal GREEN and regressions.
4. Run portable tests. For C# changes also run Framework-only tests and compile-only NT8 verification with locally licensed assemblies. Do not execute an NT-linked test executable.
5. Include actual commands, source/fixture hashes, PASS/FAIL/ERROR counts and limitations. Disclose harness changes separately; known FAIL is not silently xfail/PASS.
6. Submit a focused PR. Independent review is required for engine, hold and persistence changes.

Never include tokens, account identifiers, balances, private logs, broker credentials, proprietary DLLs, `.artifacts`, raw vendor HTML or machine-specific personal paths. Report security concerns privately through GitHub private vulnerability reporting when available; otherwise contact the maintainer without posting the exploit or secret publicly. No trading/investment advice or compliance claims.

## Safety rules

- Default disarmed; do not remove real-account Start/Flatten holds in unrelated PRs.
- Only local synthetic Account types in Framework-only runtime tests; no NT8/broker account API execution.
- OFF is an entry-authorization boundary, not confirmed broker recall or Flatten.
- Preserve captured fills, unresolved command evidence, risk latches and existing protection.
- Unknown acceptance is not permission to retry; Position=0 is not an execution certificate.
- Keep each Ratio independent; no global overwrite, silent cap or changed rounding oracle.
- Scope passes narrowly: Python SPEC, helper, compile, UI or fake-engine evidence are different gates.

## Pull request checklist

- [ ] Issue and bounded acceptance contract linked.
- [ ] RED→GREEN commands and unchanged assertion identified for behavior changes.
- [ ] Portable tests and relevant C# tests executed (or blocker stated).
- [ ] Existing known failures, warnings and harness incidents preserved.
- [ ] No sensitive data/vendor binaries; safety hold retained.
- [ ] No claim of provider/live acceptance from offline tests.
