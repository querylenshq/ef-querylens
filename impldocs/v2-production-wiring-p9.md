# V2 Production Wiring — Pipeline Integration and VS Code Validation

## Overview

Wire the v2 capture-plan codegen methods into the production evaluation pipeline and validate
end-to-end SQL output in VS Code. This slice closes the gap identified in the Slice 3b review:
`BuildV2CapturePlanInitialization` and `BuildV2CaptureInitializationCode` exist and are tested
in isolation but have no production callers. The compilation pipeline still reads
`request.LocalSymbolGraph` via `StubSynthesizer.BuildInitialStubs` for all requests, ignoring
the v2 capture plan entirely.

## Scope Boundaries

**Feature context**: Completes the v2 codegen integration left incomplete in Slice 3b.

**This slice delivers**:

- Pass `v2Decision` from `EvaluationFlow` into `TryBuildRunnerForCacheMiss`
- When `v2Decision.ShouldUseV2Path = true`, source stubs from v2 capture plan instead of `LocalSymbolGraph`
- Adapter that converts `V2CapturePlanSnapshot` entries into the `List<string>` stubs format consumed by the existing `TryBuildCompilationWithRetries` pipeline (no change to downstream Roslyn compilation)
- Fix silent exception swallowing in `RunnerGenerator.V2Support.BuildV2CapturePlanInitialization`
- Ensure string placeholders in `BuildPlaceholderInitializationCode` use a non-null sentinel to avoid constant-false SQL predicates in `Contains`/`StartsWith` query shapes
- Fix unreachable trailing block in `V2RuntimeAnalyzer.Analyze()`
- End-to-end pipeline tests: `EvaluateAsync` with legacy payload passes, `EvaluateAsync` with v2-rejected payload returns structured diagnostic
- Manual VS Code smoke test steps for hover-over-query with v2 extraction active

**Out of scope / deferred**:

- Removing `LocalSymbolGraph` or `StubSynthesizer` (belongs to Slice 4 cutover)
- Replacing the Roslyn compilation pipeline with v2-native codegen (future optimization)
- Automated LSP-layer integration tests (post-launch)
- Feature flag / per-project v2 toggle (safety net is the rejection diagnostic)

**Depends on**:

- `query-extraction-v2-q7` ✓
- `query-extraction-v2-capture-h2` ✓
- `query-extraction-v2-runtime-m6` ✓
- `query-extraction-v2-runtime-3b` ✓ (gate logic and codegen methods exist)

## Requirements

- [x] `v2Decision` is passed from `EvaluationFlow.EvaluateAsyncInternal` into `TryBuildRunnerForCacheMiss`
- [x] When `v2Decision.ShouldUseV2Path = true`, `stubs` are sourced from the v2 capture plan via a new adapter, not from `StubSynthesizer.BuildInitialStubs`
- [x] The stubs adapter converts `V2CapturePlanEntry` entries with `ReplayInitializer` or `UsePlaceholder` policy into the `List<string>` format used by `TryBuildCompilationWithRetries`
- [x] `Reject`-policy entries are excluded from stubs (matching existing `BuildV2CaptureInitializationCode` null return)
- [x] No change to `TryBuildCompilationWithRetries`, `RunnerGenerator.GenerateRunnerClass`, or any downstream Roslyn compilation method
- [x] Fix unreachable trailing block in `V2RuntimeAnalyzer.Analyze()` (remove or document)
- [x] Update `BuildPlaceholderInitializationCode` so string placeholders emit a deterministic non-null sentinel and avoid constant-false SQL (`0 = 1`) regressions
- [x] Replace bare `catch { continue; }` in `BuildV2CapturePlanInitialization` with logged diagnostic skip
- [x] Add pipeline test: `EvaluateAsync` with legacy (non-v2) `TranslationRequest` returns SQL (guards gate transparency)
- [x] Add pipeline test: `EvaluateAsync` with v2-rejected payload returns `Failed` result containing `BlockReason`
- [x] Add manual VS Code smoke test steps to `end-to-end-testing.md` for v2-wired hover scenario

## Design Decisions

### Decision 1: Augment Stubs Source, Not Replace Pipeline

**Choice:** When `v2Decision.ShouldUseV2Path = true`, call a new `StubSynthesizer.BuildV2Stubs(v2Decision.CapturePlan)` adapter that returns `List<string>` in the same format as `BuildInitialStubs`. Pass this list into the existing `stubs` variable in `TryBuildRunnerForCacheMiss`. No other callsite changes.

**Rationale:**
- The Roslyn compile/emit/load pipeline, `TryBuildCompilationWithRetries`, and `RunnerGenerator.GenerateRunnerClass` all consume a `List<string>` of stub declarations. Converting v2 entries to that same format costs ~20 lines and requires zero changes to any downstream method.
- Replacing the pipeline (using `StatementSyntax` nodes from `BuildV2CapturePlanInitialization` directly) requires threading Roslyn AST objects through `TryBuildCompilationWithRetries` — a much larger change belonging in Slice 4.
- The adapter makes `BuildV2CaptureInitializationCode` the canonical conversion point: it already returns `string?` and is already tested.

**Alternatives considered:**
- Full v2 codegen path bypassing `StubSynthesizer` — deferred to Slice 4; higher risk, larger diff.
- Keep `LocalSymbolGraph` path for v2 requests as interim — rejected because it defeats the purpose; v2 capture plan and `LocalSymbolGraph` can diverge.

### Decision 2: Pass `v2Decision` as a Parameter to `TryBuildRunnerForCacheMiss`

**Choice:** Add `V2RuntimeDecision v2Decision` as a parameter to `TryBuildRunnerForCacheMiss`. Inside the method, branch on `v2Decision.ShouldUseV2Path` to select stub source.

**Rationale:**
- `TryBuildRunnerForCacheMiss` already receives `request` and `dbContextType`; adding `v2Decision` is consistent with that pattern.
- Alternative of re-running `V2RuntimeAnalyzer.Analyze(request)` inside the method would be idempotent but wasteful and harder to test.
- The `v2Decision` object was already computed in `EvaluationFlow` — it should flow through rather than be recomputed.

**Alternatives considered:**
- Store `v2Decision` on `QueryEvaluator` instance — rejected because `TryBuildRunnerForCacheMiss` is called per-request, not per-evaluator-lifetime.
- Re-run `Analyze()` inside the method — rejected as redundant computation.

### Decision 3: No Feature Flag

**Choice:** Ship v2 wiring without a per-project or global toggle. The existing rejection diagnostic path (`BlockReason is not null → return Failure`) is the safety net for unsupported shapes.

**Rationale:**
- A flag adds config surface, documentation, and test cases for every combination of flag state × payload type.
- The v2 rejection diagnostic already hard-blocks unsupported shapes with an explicit error. A user with an unsupported query gets a diagnostic; supported queries get SQL.
- If a regression is found post-launch, the fastest fix is a targeted code change, not a config toggle.

**Alternatives considered:**
- `efquerylens.v2.enabled` setting — rejected because diagnostic gating is sufficient and simpler.
- Per-project flag via `.querylens.json` — rejected as premature; can be added in Slice 4 if needed.

### Decision 4: Pipeline-Level Tests Use Existing Test Fixtures

**Choice:** The two new pipeline tests (`legacy payload → SQL`, `v2-rejected payload → Failure`) use the existing `QueryEvaluatorTests` fixture pattern. No new test doubles or mocks.

**Rationale:**
- The existing `QueryEvaluatorTests.Evaluate_*` tests demonstrate the correct way to exercise `EvaluateAsync` with real compilation. Reusing that pattern keeps tests consistent.
- The v2-rejected payload test needs to return `Failure` without reaching Roslyn compilation; the `BlockReason` gate fires before `TryBuildRunnerForCacheMiss` is entered.

## Implementation Plan

1. **Read `TryBuildRunnerForCacheMiss` signature** in `QueryEvaluator.EvaluationPipeline.cs` and confirm parameter list before changing it
2. **Add `StubSynthesizer.BuildV2Stubs`** in a new partial class `StubSynthesizer.V2Support.cs`:
   - Accepts `V2CapturePlanSnapshot`
   - Iterates entries, calls `EvalSourceBuilder.BuildV2CaptureInitializationCode(entry)` for each
   - Collects non-null results into `List<string>` and returns
3. **Extend `TryBuildRunnerForCacheMiss` signature** to accept `V2RuntimeDecision v2Decision`
4. **Branch stubs source** inside `TryBuildRunnerForCacheMiss`: if `v2Decision.ShouldUseV2Path`, call `StubSynthesizer.BuildV2Stubs(v2Decision.CapturePlan)`; otherwise call `StubSynthesizer.BuildInitialStubs(request, dbContextType)` as today
5. **Update `EvaluationFlow.EvaluateAsyncInternal`** to pass `v2Decision` when calling `TryBuildRunnerForCacheMiss`
6. **Fix `V2RuntimeAnalyzer.Analyze()`** — remove the unreachable trailing block (the `extraction-only-no-capture` return at the end is already handled by the "partial v2 state" block higher up)
7. **Fix `BuildPlaceholderInitializationCode`** — emit a deterministic non-null sentinel for string placeholders while preserving `default(T)` behavior for non-string types
8. **Fix `BuildV2CapturePlanInitialization`** — replace bare `catch { continue; }` with a `Debug.Fail` or comment explaining the skip is intentional for malformed expressions
9. **Add pipeline test**: `EvaluateAsync_LegacyRequest_UnaffectedByV2Gate` — asserts a standard non-v2 `TranslationRequest` produces a SQL result after the wiring change
10. **Add pipeline test**: `EvaluateAsync_V2RejectedCapture_ReturnsBlockedDiagnostic` — asserts a request with a capture plan containing diagnostics returns `Failure` with `"capture-rejected:"` in the message
11. **Update `end-to-end-testing.md`** with VS Code smoke test steps for the v2-wired path
12. **Run full test suite** and confirm no regressions

## Dependencies

- `StubSynthesizer` in `src/EFQueryLens.Core/Scripting/StubSynthesis/StubSynthesizer.cs`
- `QueryEvaluator.EvaluationPipeline.cs` — contains `TryBuildRunnerForCacheMiss`
- `QueryEvaluator.EvaluationFlow.cs` — contains the v2 gate and call to `TryBuildRunnerForCacheMiss`
- `EvalSourceBuilder.V2Support.cs` — `BuildV2CaptureInitializationCode` (already exists)
- `RunnerGenerator.V2Support.cs` — `BuildV2CapturePlanInitialization` (already exists; used by adapter)
- `V2RuntimeAdapter.cs` — `V2RuntimeDecision`, `V2RuntimeAnalyzer.Analyze()`
- `TranslationRequest.cs` — `V2CapturePlanSnapshot`, `V2CapturePlanEntry`
- VS Code dev launch setup for manual smoke testing

## Testing Strategy

### Unit Tests

1. **`StubSynthesizer.BuildV2Stubs` tests** — new file `StubSynthesizerV2Tests.cs`
   - Single `ReplayInitializer` entry → returns one stub string
   - `UsePlaceholder` entry → returns one stub string with `default(T)` form
   - `Reject` entry → excluded from stubs list
   - Mixed entries → only non-Reject entries appear
   - Null capture plan → returns empty list

### Pipeline Tests (new, targeting `EvaluateAsync`)

2. **`EvaluateAsync_LegacyRequest_UnaffectedByV2Gate`**
   - Constructs a `TranslationRequest` with no v2 payloads
   - Calls `QueryEvaluator.EvaluateAsync()` on a real project assembly
   - Asserts result is not `Failed` and SQL is non-empty

3. **`EvaluateAsync_V2RejectedCapture_ReturnsBlockedDiagnostic`**
   - Constructs a `TranslationRequest` with a capture plan containing at least one `V2CaptureDiagnostic`
   - Asserts `QueryTranslationResult.IsSuccess == false`
   - Asserts failure message contains `"capture-rejected:"`

### Manual VS Code Smoke Tests (added to `end-to-end-testing.md`)

4. **V2-wired direct chain hover** — open SampleSqlServerApp, hover over `db.Orders.Where(o => o.IsActive).ToListAsync()`, verify SQL panel shows SQL
5. **V2 rejection hover** — hover over an unsupported helper with control flow, verify hover shows diagnostic code and message (not a crash or empty panel)

## Acceptance Criteria

- [x] `v2Decision` is passed to `TryBuildRunnerForCacheMiss` and used to select stub source
- [x] `StubSynthesizer.BuildV2Stubs` adapter correctly converts capture plan entries to stubs
- [x] When `ShouldUseV2Path = true`, the v2 capture plan drives stub generation (verified by new unit tests)
- [x] When `ShouldUseV2Path = false` (no v2 payloads), legacy stubs path runs unchanged (verified by pipeline test)
- [x] V2-rejected payload returns `Failure` with `capture-rejected:` diagnostic (verified by pipeline test)
- [x] Unreachable trailing block removed from `Analyze()`
- [x] String placeholders in `BuildPlaceholderInitializationCode` emit deterministic non-null sentinel values and avoid `0 = 1` SQL regressions for `Contains`/`StartsWith`
- [x] Silent exception swallowing replaced in `BuildV2CapturePlanInitialization`
- [x] All existing `QueryEvaluatorTests.Evaluate_*` tests continue to pass (P0: no regression)
- [ ] VS Code manual smoke test completed: hover over direct chain returns SQL

## Review Findings

_Populated after code review._

| Source | Finding | Resolution |
| --- | --- | --- |

## Quality Report

_Populated after scans._

### Security Scan (Trivy)

_"Security scan skipped — Trivy not installed."_

### Code Quality (SonarQube)

_"Code quality analysis skipped — SonarQube not configured."_

## Change Log

| Date | Change | Reason |
| --- | --- | --- |
| 2026-04-04 | Updated v2 string placeholder semantics to emit non-null sentinel (`"qlstub0"`) for `UsePlaceholder` string captures; added regressions for capture classification and SQL shape | Real-world hover query (`Contains(term) || StartsWith(term)`) produced constant-false SQL (`0 = 1`) when `term` was captured as `default(string)` (`null`). |
| 2026-04-04 | Implementation in-progress complete; awaiting manual VS Code smoke run | Wired `v2Decision` into `TryBuildRunnerForCacheMiss`, added `StubSynthesizer.BuildV2Stubs` adapter, added pipeline tests (`EvaluateAsync_LegacyRequest_UnaffectedByV2Gate`, `EvaluateAsync_V2RejectedCapture_ReturnsBlockedDiagnostic`), fixed `Analyze()` unreachable branch, updated placeholder comment, and replaced silent catch with diagnostic `Debug.Fail`. Targeted tests and `QueryEvaluatorTests.Evaluate_*` regression suite pass. |
| 2026-04-04 | Impldoc created | Closes the wiring gap identified in the Slice 3b review (dead-code codegen methods, no production callers). Scoped as augment-only (Option A) to keep diff small. |
