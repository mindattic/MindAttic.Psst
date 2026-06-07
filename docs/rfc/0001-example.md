---
codex: 1
project: MindAttic.Psst
code: PST
layer: rfc
status: planned
updated: 2026-06-07
---

# RFC 0001 — Direct-test coverage for the pure helpers

## Problem

Several pure, deterministic units (`CarrierGateways`, `PsstViaResolver`, `ContactBook`/
`ContactStore`, `DescribeExitCode`) currently have **no dedicated test file**. Their behavior is
only exercised transitively through `PsstNotifierTests` / `PsstConfigurationTests`. That leaves
documented behaviors marked 🟡 in [USER_STORIES.md](../USER_STORIES.md) (B2, B3, D3, A3) even
though the code is shipped and working. The headline goal is "every documented behavior has a
direct test" ([BIBLE §8](../BIBLE.md#PST-§8)).

## Options compared

1. **Do nothing** — accept transitive coverage. Cheap, but the stories stay 🟡 and a regression in
   `CarrierGateways.Combine` (e.g. dedup ordering) could pass CI silently.
2. **Add focused unit-test files** for each pure helper. Pure functions, no I/O — fast, isolated,
   high signal. Modest effort.
3. **Add full CLI integration tests** that shell out to `psst.exe`. Highest fidelity but slow,
   Windows-/scheduler-coupled, and flaky for the audio/`schtasks` paths.

## Decision

Adopt **Option 2** for the pure helpers, and defer Option 3 (real `schtasks`/audio round-trips) to
a later, Windows-trait-gated integration RFC.

## What NOT to do

- Do **not** test live SMS delivery or real Twilio/carrier endpoints in unit tests — keep network
  behind the existing `HttpMessageHandler` seam used by `TwilioSmsClientTests`.
- Do **not** assert real audio output or actual Task Scheduler state in the default test run —
  those stay manual / trait-gated.
- Do **not** change production code to make it testable beyond what is already injectable.

## Phased plan (with risk)

1. `CarrierGatewaysTests` — normalize/fanout/combine. *Risk: low.*
2. `PsstViaResolverTests` — precedence incl. env var (inject via the `envVarValue` parameter to
   avoid global env mutation). *Risk: low.*
3. `ContactBookTests` + `ContactStoreTests` — round-trip via a temp roaming root
   (`MINDATTIC_VAULT_ROAMING_ROOT`). *Risk: low–medium (filesystem temp cleanup).*
4. `DescribeExitCode` mapping test (may require widening visibility to `internal` +
   `InternalsVisibleTo`, which already exists for the CLI). *Risk: low.*

## Graduates into

- [BIBLE §6](../BIBLE.md#PST-§6) (Verified state — upgrade entries to cite the new tests)
- [USER_STORIES.md](../USER_STORIES.md): promotes PST-US-B2, B3, D3, A3 from 🟡 to ✅; closes
  backlog items PST-US-B6, B7, D5, A4.
