# Offline build and tests

**Experimental alpha — TRADING DISABLED for every real platform provider, including Simulator and Playback.** Compilation and synthetic test results do not authorize installation, account connection, or orders.

## Portable verification (Python 3.10+)

No packages, NinjaTrader, .NET, network, credentials, or GUI are required:

```sh
python -B -m unittest discover -s tests -p 'test_tooling*.py' -v
python -B -m unittest discover -s tests -p 'test_production_hold.py' -v
python -B -m unittest discover -s tests -p 'test_package.py' -v
python -B scripts/offline.py verify
```

Windows runtime controls are skipped unless explicitly enabled. The portable verifier checks source/fixture layout, common secret/binary/personal-path patterns and retained production holds. Secret detection is heuristic, not a complete security audit; hold checks are limited lexical tests, not a C# AST proof or certified firewall. Repository CI supplies additional package/workflow checks.

## Windows offline execution

Requires an already installed 64-bit .NET Framework compiler and an already installed NinjaTrader `bin` containing Roslyn tooling. Supply its location; the runner does not discover account configurations. No package install or download is performed. From a shell, set `NT_BIN` to that installed bin directory and use the appropriate shell quoting for the variable.

```sh
python -B scripts/offline.py pure --nt-bin "$NT_BIN"
python -B scripts/offline.py engine --nt-bin "$NT_BIN"
python -B scripts/offline.py independent --nt-bin "$NT_BIN"
python -B scripts/offline.py compile --nt-bin "$NT_BIN"
```

PowerShell uses `$env:NT_BIN` instead of `$NT_BIN`. `--artifacts-root "$SCRATCH_ROOT"` optionally relocates outputs to a chosen private scratch parent. Each invocation creates a new `.artifacts/<mode>-<unique-id>/`; never upload that directory or raw logs. Keep the scratch parent reasonably short because legacy Framework file APIs can reject long paths.

| Mode | Executes | References |
| --- | --- | --- |
| `pure` | Each zero-argument case in the retained risk fixture, independently, plus a separate replay of its original `Main` UI-hold assertions | Framework only |
| `engine` | Every zero-argument case in both retained engine fixtures, independently | Framework plus same-artifact fake Cbi types only |
| `independent` | Every zero-argument independent-review case, including extra probes omitted by its original `Main` | Framework plus same-artifact fake Cbi types only |
| `compile` | Compiler host only; **never emitted AddOn** | Full source compiled against read-only NT Core/Gui and Framework WPF metadata |

The runner snapshots the current production bytes on each invocation. It extracts the exact marked pure region or exact pure+engine prefix, rather than copying policy/engine implementations. It preserves fixture bytes and asserts no test outcomes in advance. Every case gets a fresh Framework process and a separate numbered directory; `result.json` retains its full name, exit status, source/test/executable hashes and aggregate counts. `ProductionQuarantine` and `ProductionAllProvidersCannotEnable` use no fake symbol. Fake builds enable only the same-artifact synthetic path; installed NT DLLs are absent from those targets.

## Runtime boundary

A fresh compiler EXE is bootstrapped using Framework64 `csc.exe`. Before compiler execution, stdlib PE metadata inspection inventories the complete installed Microsoft compiler dependency closure and rejects unexpected dependencies. These tooling libraries have a **separate trusted compiler exception** (some declare native functions); they are not test runtime references. Only allowlisted Microsoft tooling dependencies are loaded by the compiler resolver. NT Core/Gui are read as compile metadata, never loaded into the compiler process for execution; the emitted full AddOn is never loaded or run.

Before **each** pure/fake process, the target is inspected statically by Python, then by a fresh Framework-only reflection-only IL auditor. Allowed external references are exactly `mscorlib`, `System`, `System.Core`, `System.Xml`, `System.Xml.Linq`. The auditor rejects P/Invoke/native methods, indirect signatures, Window/NTTab inheritance, network/process types and dynamic assembly loading. Account member operands are permitted only when their declaring Account belongs to that exact audited artifact. A changed hash after auditing is rejected. These are defense-in-depth checks for reviewed fixtures, not an operating-system sandbox for hostile arbitrary C#.

`compile` rejects active `KINFX_OFFLINE_FAKE` directives (including Unicode-escaped identifiers) before opening output. Unknown CLI options, including define injection, are rejected. Full compilation includes no fake symbol and reads the source dynamically; tests from another source hash are not same-source evidence.

Exit codes: `0` = requested checks passed; `1` = retained test assertion failures/public verification refusal; `2` = compilation/audit/bootstrap/harness failure. Crashes/timeouts remain harness evidence rather than being counted as engine assertion failures. Existing red engine/recovery assertions must remain red; do not loosen them to manufacture a green badge. Overlapping cases in the two engine fixtures are separate invocations, not unique safety properties. Legacy CS1701/CS1702 reference identity warnings are recorded separately from source errors.

## Explicit compiler/auditor controls

Set `KINFX_TOOLING_RUNTIME` to an isolated scratch parent and `KINFX_NT_BIN` to the installed bin, then run:

```sh
python -B -m unittest discover -s tests -p 'test_tooling_runtime.py' -v
```

These controls compile harmless safe/local-fake positives and P/Invoke, native-window and network negatives; only audited positives execute. They also test active Unicode fake-symbol rejection. Failed attempts and compiler/audit incidents remain beside later corrected invocations. There are no real, Sim, Playback, broker or GUI acceptance tests in this tooling.
