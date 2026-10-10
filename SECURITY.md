# Security and trading safety

Only the current development branch is maintained; there is no supported live-trading release. All versions in this initial alpha are **not accepted for trading**.

Do not post credentials, account numbers or unredacted brokerage/platform logs. [GitHub private vulnerability reporting is enabled](https://github.com/Kinfxhk/KINFXTradeCopier/security/advisories/new). Report sensitive concerns there rather than in public Issues. If reporting publicly, describe impact without secrets or exploit payloads.

Unexpected activation, hold bypass, unauthorized order dispatch, cross-account identity mixing, protection cancellation or unsafe duplicate closes are P0 safety concerns. Stop using the test build and use native platform/broker controls for actual positions. No repository setting is a broker-side kill switch.

CI runs untrusted contributions with read-only permissions, no repository secrets, no trading connection and no privileged pull_request_target workflow. Proprietary platform binaries are never uploaded. Known unsafe engine behavior is disclosed rather than hidden by packaging green checks.
