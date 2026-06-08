# Review

## Scope
- Impldoc reviewed: `impldocs/hover-generation-speed-robustness.md`
- Change summary: daemon timeout/caching/concurrency and hover wait-budget were implemented, but the change set also introduces setup/scaffolding flows and broad parsing/evaluation/plugin changes across 85 files (+5170/-572).
- Review date: 2026-06-08

## Verdict
Blocked

Reason: the implementation contains a concrete behavior mismatch with the impldoc timeout status requirement, and the PR scope is significantly broader than the declared impldoc, with insufficient evidence to review the expanded blast radius safely.

## Top Merge Risks
- Timeout and other translation failures are surfaced with `Ready`-path semantics in the hover pipeline instead of an unavailable/error status.
- The change set mixes multiple features (hover robustness + setup scaffolding + cross-IDE command surfaces + parser/evaluator rewrites), making regression isolation and rollback risky.
- Timeout returns quickly but engine slots are only released when underlying work completes; sustained non-cancelable hangs can still saturate capacity.
- Cross-IDE setup command flow is newly introduced without commensurate integration evidence.

## Findings
- Severity: Critical
  Area: correctness
  Location: `src/EFQueryLens.Lsp/Services/HoverPreviewService.Pipeline.cs`
  Issue: failed translations (including daemon timeout failures) are converted via `Fail(..., sourceLine)` where default status is `QueryTranslationStatus.Ready`, rather than `DaemonUnavailable` or another explicit error status.
  Why it matters: the impldoc explicitly requires clear timeout failure semantics; reporting failure via a ready-status path can misclassify UI state and weaken downstream error handling assumptions.
  Required justification or evidence: show why timeout/failure status propagation was intentionally designed as Ready, or change mapping/tests to prove timeout/failure status is surfaced as unavailable/error end-to-end.
  Suggested reviewer probe: inspect the failure path from daemon timeout (`TranslationCoordinator`) through hover formatting and confirm user-visible status and telemetry semantics.

- Severity: Major
  Area: impldoc
  Location: decision area (entire change set)
  Issue: implementation scope exceeds impldoc boundaries. New setup/scaffolding feature surfaces were added (`/setup` daemon endpoint, scaffolding subsystem, VS Code/Rider/Visual Studio setup commands) with no corresponding impldoc scope expansion.
  Why it matters: reviewers cannot evaluate acceptance/failure criteria against a stable spec when multiple features are bundled under a single robustness impldoc.
  Required justification or evidence: either split this into separate impldocs/PRs or provide explicit approved scope expansion with acceptance criteria for setup/scaffolding and IDE command flows.
  Suggested reviewer probe: verify whether every non-hover/non-daemon robustness file changed is justified by the approved impldoc and has independent rollback strategy.

- Severity: Major
  Area: testing
  Location: cross-IDE setup paths (`src/EFQueryLens.Lsp/Hosting/LanguageServerHandler.Commands.cs`, `src/Plugins/ef-querylens-vscode/src/commands/registry.ts`, Rider/VS setup files)
  Issue: unit tests cover coordinator/store/wait-budget well, but no integration evidence is provided for new setup RPC flows across plugin hosts and daemon endpoint behavior under failure conditions.
  Why it matters: multi-host command and request-shape mismatches are high-probability post-merge failures and are not detectable from core-only unit tests.
  Required justification or evidence: provide passing integration/E2E tests (or explicit manual matrix) for setup detect/apply on at least one real host per plugin family and daemon failure modes.
  Suggested reviewer probe: challenge request/response contract compatibility, cancellation behavior, and user-facing errors for setup detect/apply across VS Code, Rider, and Visual Studio.

- Severity: Major
  Area: impldoc
  Location: `impldocs/INDEX.md`
  Issue: reviewed impldoc is not registered in the implementation index.
  Why it matters: missing registry metadata breaks spec traceability and weakens review/audit workflow.
  Required justification or evidence: add/maintain INDEX entry with stable impldoc id and status, or explain why the repo workflow is intentionally bypassed.
  Suggested reviewer probe: require traceability from diff to impldoc id before merge.

- Severity: Moderate
  Area: operability
  Location: `src/EFQueryLens.Daemon/TranslationCoordinator.cs`
  Issue: timeout returns promptly, but engine gate slots are released only when underlying work finishes; if work ignores cancellation and hangs, capacity can remain consumed indefinitely.
  Why it matters: under repeated pathological queries, this can still degrade or stall service despite request-level timeout.
  Required justification or evidence: provide stress/failure-path evidence that capacity recovers under non-cooperative workloads, or document/monitor this limit explicitly.
  Suggested reviewer probe: simulate N non-cancelable hung translations equal to max concurrency and verify interactive recovery behavior.

## Impldoc Gaps
- No constraints for introducing new setup/scaffolding functionality in this slice.
- No explicit acceptance criteria for timeout status mapping from daemon failure to LSP hover status.
- Optional observability counters were listed but left unspecified as to whether deferral is acceptable for this merge.

## Evidence Gaps
- Missing full-suite evidence for a change spanning daemon, LSP, core scripting, and three IDE hosts.
- Missing integration/E2E evidence for setup detect/apply flows and host-specific UX outcomes.
- Missing failure-path evidence for prolonged non-cancelable engine work exhausting concurrency slots.
- Missing review packet for deterministic reviewer context bundling.

## Human MR Reviewer Focus
- Validate failure-status propagation for daemon timeout/failure from `TranslationCoordinator` to hover output.
- Challenge scope expansion: require explicit decision on whether setup/scaffolding belongs in this impldoc/PR.
- Probe cross-host setup RPC contract compatibility and failure UX.
- Inspect rollback feasibility: can robustness fixes be reverted independently from setup/scaffolding additions?
- Verify perf/operability under hung engine workloads and warm storms with realistic concurrency settings.

## Residual Concerns
- Durable cache introduces local state lifecycle (size/retention/corruption recovery) that remains lightly instrumented.
- Broad parser/evaluator modifications in the same PR increase hidden coupling risk not directly addressed by this impldoc.
- Architecture guidance is effectively empty, so architectural fit assessments are underconstrained.

## History
- 2026-06-08: Initial merge-readiness review created.