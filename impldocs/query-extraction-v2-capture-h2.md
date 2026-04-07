# Query Extraction V2 Slice 2

## Overview

Implement the second slice of query extraction v2.0 by replacing legacy free-variable replay heuristics with a deterministic capture plan that is produced from the v2 extraction IR. This slice focuses on what values and expressions are safe to carry into execution scaffolding and how unsupported captures are diagnosed, without yet rewriting the runtime contract.

## Scope Boundaries

**Feature context**: Slice 2 of Query Extraction V2

**This slice delivers**:

- A capture-plan model that replaces direct dependency on ad hoc `LocalSymbolGraph` replay behavior
- Deterministic capture classification: replay expression, synthesize placeholder, or reject with diagnostic
- Capture rules for helper-inlined queries, including expression-parameter captures used in composed helper returns
- Explicit treatment of unsafe captures (branch-local state, invocation-only values, unsupported anonymous dependencies)
- Compatibility adapter from capture plan into current downstream request payload shape until runtime slice lands

**Out of scope / deferred**:

- Full runtime/request-contract rewrite and new runner code generation path _(planned for impldoc `query-extraction-v2-runtime-??`)_
- Final cutover and legacy extraction/capture removal _(planned for impldoc `query-extraction-v2-cutover-??`)_
- Async helper body inlining and procedural helper evaluation
- Control-flow execution/simulation of branch-specific values

**Depends on**: `query-extraction-v2-q7` should be complete first

**Expected implementation order**:

1. `query-extraction-v2-q7`
2. `query-extraction-v2-capture-h2`
3. `query-extraction-v2-runtime-??`
4. `query-extraction-v2-cutover-??`

## Requirements

- [x] Define a capture-plan contract that is generated from v2 extraction output and is independent from current replay heuristics
- [x] Classify each capture deterministically into one of: `ReplayInitializer`, `UsePlaceholder`, or `Reject`
- [x] Support captures needed by helper-inlined query chains where helper return expressions compose one or more expression parameters
- [x] Preserve deterministic ordering for captured symbols and dependencies so generated scaffolding is stable
- [x] Replace fragile dependency downgrades based on incidental syntax shape with explicit policy rules and diagnostics
- [x] Ensure unsupported capture patterns fail explicitly with actionable diagnostics
- [x] Provide an adapter that maps capture-plan output into the current request payload so runtime slice can be delivered independently later
- [x] Add tests that lock down capture behavior for direct chains, helper-composed chains, query expressions, and rejected capture shapes

## Design Decisions

### Decision 1: Capture plan becomes the only source of truth for symbol replay policy

**Choice:** Introduce a dedicated capture-plan layer and stop deriving replay behavior from scattered conditional checks in extraction helpers.

**Rationale:** Current behavior is difficult to reason about and has produced regressions when small extraction changes alter replay outcomes. A capture-plan contract makes policy visible, testable, and reviewable.

**Alternatives considered:**

- Keep incremental heuristic checks in existing extraction methods — rejected because behavior remains coupled and fragile
- Defer capture redesign to runtime slice — rejected because runtime rewrite would then inherit unclear capture semantics

### Decision 2: Deterministic policy over best-effort guessing

**Choice:** Unsupported or unsafe captures should be rejected explicitly rather than silently guessed into object placeholders.

**Rationale:** V2 is intentionally not backward compatible. Silent guessing hides extraction quality issues and produces unpredictable SQL generation outcomes.

**Alternatives considered:**

- Continue permissive guessing for unsupported captures — rejected because it obscures failures
- Hybrid mode with silent fallback for some paths — rejected because policy becomes opaque

### Decision 3: Runtime compatibility adapter is temporary and explicit

**Choice:** Keep a narrow adapter that translates capture plan to current runtime payload fields only until slice 3 ships.

**Rationale:** This allows independent validation of capture semantics while avoiding an all-at-once runtime rewrite.

**Alternatives considered:**

- Rewrite runtime in this slice — rejected because it expands scope beyond independently reviewable size
- Keep legacy payload semantics as primary — rejected because it blocks clean capture semantics

## Implementation Plan

1. Define capture-plan model types and policy enums in contracts used by extraction/runtime boundary code.
2. Implement capture-plan builder that consumes slice 1 extraction output and resolves symbol/dependency ordering deterministically.
3. Implement classification rules for replay, placeholder, and rejection with explicit diagnostics for unsafe capture shapes.
4. Add helper-aware capture handling for inlined helper expressions with multiple expression parameters composed into return query chains.
5. Implement temporary adapter from capture plan to existing request payload fields used by current runtime path.
6. Remove or bypass legacy replay-policy branches that conflict with capture-plan output in extraction flow.
7. Add and update tests for accepted/rejected capture scenarios, including helper-composed query chains and query-expression forms.
8. Run targeted LSP and evaluator tests to verify stability before runtime rewrite.

## Dependencies

- Slice 1 extraction IR and helper inlining behavior in `query-extraction-v2-q7`
- Existing request contract and evaluator payload flow in `src/EFQueryLens.Core/Contracts` and `src/EFQueryLens.Core/Scripting`
- Existing test suites under `tests/EFQueryLens.Core.Tests/Lsp` and `tests/EFQueryLens.Core.Tests/Scripting`

## Testing Strategy

### Unit Tests

- Verify deterministic capture ordering and dependency resolution for local/parameter captures
- Verify replay/placeholder/reject classification rules for supported and unsupported capture shapes
- Verify helper-composed captures where one or more expression parameters participate in returned query chains
- Verify explicit diagnostics for rejected captures

### Integration Tests

- Validate extraction plus capture-plan output for representative hover scenarios across direct, helper-composed, and query-expression queries
- Validate adapter output still drives current runtime path for supported capture scenarios

### Manual Smoke Tests

Steps for `end-to-end-testing.md`:

1. Hover a helper-composed query using multiple expression parameters — expected result: extraction and capture classification succeed with deterministic symbol ordering
2. Hover a query requiring placeholder capture — expected result: explicit placeholder classification appears and SQL execution still works where supported
3. Hover a query with unsupported branch-local capture shape — expected result: explicit capture diagnostic is shown and query is rejected for execution

## Acceptance Criteria

- [x] Capture-plan model exists and drives capture policy decisions
- [x] Replay/placeholder/reject decisions are deterministic and test-covered
- [x] Helper-composed capture scenarios with multiple expression parameters are supported when safe
- [x] Unsupported capture shapes fail with explicit diagnostics
- [x] Legacy conflicting replay heuristics are removed or bypassed in slice 2 paths
- [x] Adapter to existing runtime payload exists and passes targeted tests

## Review Findings

_Populated by the implementer after code review, security review, and quality analysis. Only findings that resulted in code changes are recorded here. Deferred items go to `todos.md`. Valid sources: `code-reviewer`, `red-team`, `query-analyzer`, `trivy`, `sonarqube`._

| Source | Finding | Resolution |
| --- | --- | --- |
| red-team | Replay initializers could carry executable syntax into capture replay. | Added `QLV2_CAPTURE_UNSAFE_INITIALIZER` classification that rejects replay initializers containing invocation/object creation/assignment/await/query syntax. |

## Quality Report

_Populated by the implementer after all scans complete. Captures the final quality snapshot for the permanent record._

### Security Scan (Trivy)

Security scan skipped — Trivy not installed. Run `trivy fs . --scanners vuln,secret,misconfig` manually.

### Code Quality (SonarQube)

Code quality analysis skipped — SonarQube not configured. Run `node .github/scripts/tsp-setup-sonarqube.js` to set up.

## Change Log

| Date | Change | Reason |
| --- | --- | --- |
| 2026-04-03 | Initial impldoc drafted | Planned v2 slice 2 to replace legacy capture heuristics with deterministic capture-plan policy and diagnostics. |
| 2026-04-04 | Scope check: developer chose to proceed with 8-step plan across 3 areas | Continued slice implementation as approved.
| 2026-04-04 | Implementation complete | All requirements and acceptance criteria met. 1 review finding addressed. Deferred: none. |