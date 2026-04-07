# V2 Cutover: Core Runtime Stub Synthesis Cutover

## Overview

Remove the legacy dual-path stub synthesis from `QueryEvaluator` so that all evaluation requests must carry a v2 capture plan. The current code branches on `v2Decision.ShouldUseV2Path`: when true it calls `BuildV2Stubs()`; when false it falls back to `BuildInitialStubs()` from the legacy `LocalSymbolGraph`. This slice makes `BuildV2Stubs()` the unconditional path, deletes the `BuildInitialStubs()` fallback, and migrates all tests to supply v2 payloads.

Delivered in two phases within this impldoc. Phase 1 ships the evaluation pipeline change plus the first batch of test migrations. Phase 2 completes test migration and deletes the now-dead legacy code.

## Scope Boundaries

**Feature context**: Slice 3 of v2 cutover cleanup (execution dependency of LSP cutover Slice 2)

**This slice delivers**:

- A `BuildV2Request` test helper that converts simple type-hint dicts into proper `V2CapturePlanSnapshot` + `V2QueryExtractionPlanSnapshot`, eliminating repetitive v2 payload construction in tests
- Migration of all `QueryEvaluatorTests.*` files from `LocalSymbolGraph`-only requests to v2 capture-plan requests
- Removal of the `else BuildInitialStubs()` dispatch leg from `QueryEvaluator.EvaluationPipeline`
- An explicit block in `V2RuntimeAnalyzer` for requests that arrive with no v2 payloads at all, so the decision point is never silently ambiguous
- Deletion of `StubSynthesizer.cs` (legacy stub synthesis; only `StubSynthesizer.V2Support.cs` remains)
- Removal of `TranslationRequest.LocalSymbolGraph` after all callers are gone

**Out of scope / deferred**:

- LSP cutover (removing `AdaptCapturePlanToLocalSymbolGraph` adapter) — blocked on Core completion; becomes Slice 2 after this
- Non-QueryEvaluator test suites (Daemon, Lsp) — DaemonRuntimeTests and HoverPreviewService tests reference `LocalSymbolGraph` but they go in Slice 4 (test/docs cleanup)
- Updating `architecture.md` — Slice 4 doc cleanup

**Depends on**: `v2-cutover-inventory-k8` (completed)

## Requirements

### Phase 1

- [ ] Add `BuildV2Request()` builder helper to `QueryEvaluatorTests.cs` base class that accepts simple type-hint dictionaries and emits a `TranslationRequest` with a properly populated `V2CapturePlanSnapshot`
- [ ] Migrate `QueryEvaluatorTests.BasicAndCache.cs` — no local variables, straightforward
- [ ] Migrate `QueryEvaluatorTests.DbContextResolution.cs` — no local variables, straightforward
- [ ] Migrate `QueryEvaluatorTests.ExpressionAndParameters.cs` — simple local variable captures
- [ ] Migrate `QueryEvaluatorTests.EfFunctions.cs` — no local variables
- [ ] Migrate `QueryEvaluatorTests.OwnershipBoundary.cs` — simple captures
- [ ] Remove the `else BuildInitialStubs()` leg from `QueryEvaluator.EvaluationPipeline.cs`
- [ ] Update `V2RuntimeAnalyzer.Analyze()` to return a `BlockReason` when no v2 payloads are present (replaces the current silent `ShouldUseV2Path = false`)
- [ ] All 771+ currently-passing Core tests remain passing (7 pre-existing failures unrelated to v2 stay as-is)

### Phase 2

- [ ] Migrate `QueryEvaluatorTests.ErrorHandling.cs`
- [ ] Migrate `QueryEvaluatorTests.AdvancedScenarios.cs`
- [ ] Migrate `QueryEvaluatorTests.SynthesisAndHeuristics.cs`
- [ ] Migrate `QueryEvaluatorTests.InternalIntrospection.cs`
- [ ] Migrate `QueryEvaluatorTests.SqlServerAndMetadata.cs`
- [ ] Delete `StubSynthesizer.cs` (legacy file; `StubSynthesizer.V2Support.cs` stays)
- [ ] Remove `LocalSymbolGraph` property from `TranslationRequest`
- [ ] Remove `BuildSymbolGraph()` test helper from `QueryEvaluatorTests.cs`
- [ ] Remove `LocalSymbolGraphEntry` type from `TranslationRequest.cs` (or mark obsolete until LSP adapter is removed)
- [ ] All 771+ Core tests remain passing

## Design Decisions

### Decision 1: Migrate tests before removing dispatch code

**Choice:** Migrate all test files to v2 payloads first, then remove `else BuildInitialStubs()` as the last code change in Phase 1.

**Rationale:** Removing the dispatch code first would break ~550 unmigrated tests at once, making incremental validation impossible. Migrating first keeps the suite green at every commit, and the dispatch removal becomes a single clean cut once no test exercises the legacy path.

**Alternatives considered:**

- Remove dispatch first then ship mass-fix — rejected: too much churn in one diff, no way to validate each fix independently.

### Decision 2: Block no-v2-payload requests in V2RuntimeAnalyzer

**Choice:** When both `V2ExtractionPlan` and `V2CapturePlan` are `null`, return a `BlockReason` of `"no-v2-payloads"` instead of the current silent `ShouldUseV2Path = false`.

**Rationale:** The current silent pass-through exists only to support the legacy fallback once the legacy path is gone there is no safe default. Making the gate explicit ensures any request that skips v2 extraction produces a visible diagnostic rather than a mysterious empty-stubs failure.

**Impact on existing test `EvaluateAsync_LegacyRequest_UnaffectedByV2Gate`:** This test asserts that a request with no v2 payloads succeeds via the legacy path. After Phase 1, it must be rewritten to supply a valid `V2CapturePlan`. Rename it `EvaluateAsync_MinimalV2Request_ReturnsSql` and update the request. The test remains in `V2ProductionWiring`.

**Alternatives considered:**

- Leave silent pass-through permanently — rejected per v2-cutover-inventory-k8 Decision 2 (explicit unsupported diagnostics).
- Throw `InvalidOperationException` instead of returning BlockReason — rejected: callers expect `QueryTranslationResult` failures, not exceptions.

### Decision 3: Build V2 test helper that mirrors LocalSymbolGraph API

**Choice:** Add `BuildV2Request(expression, localVariableTypes?, localSymbolHints?, ...)` to the test base class that mirrors the signature of `TranslateAsync()` but emits a `V2CapturePlanSnapshot`.

**Rationale:** The existing `TranslateAsync()` delegates to `BuildSymbolGraph()` from type-hint dicts. The migration should be a near-mechanical swap of `TranslateAsync` → `TranslateV2Async` (new wrapper) at each call site, not a hand-crafted v2 payload for every test. This minimises diff noise and makes intent clear.

**Conversion rules for the helper:**

| LocalSymbolGraphEntry field | V2CapturePlanEntry field |
|---|---|
| `Name` | `Name` |
| `TypeName` | `TypeName` |
| `Kind` | `Kind` |
| `InitializerExpression` | `InitializerExpression` |
| `DeclarationOrder` | `DeclarationOrder` |
| `Dependencies` | `Dependencies` |
| `Scope` | `Scope` |
| `ReplayPolicy` | `CapturePolicy` |

`V2QueryExtractionPlanSnapshot` for test requests can be minimal: `Expression`, `ContextVariableName = "db"`, `RootContextVariableName = "db"`, `BoundaryKind = "Queryable"`.

### Decision 4: Keep StubSynthesizer.cs in Phase 1 (delete in Phase 2)

**Choice:** Phase 1 makes `StubSynthesizer.BuildInitialStubs()` unreachable but does not yet delete the source file.

**Rationale:** Deleting the file before all test files are migrated would cause compilation errors in test files that still import or reference `StubSynthesizer` reflection helpers (`BuildStubDeclaration` is invoked via reflection in `InternalIntrospection` tests). Deferring keeps the build green throughout phase 1.

### Decision 5: Incremental migration by file, one commit per file

**Choice:** Each test file migration is a separate commit: `refactor(v2-cutover-core-runtime-s5): migrate QueryEvaluatorTests.BasicAndCache to v2 payloads`.

**Rationale:** Smaller commits are easier to review, bisect, and revert if a specific migration is wrong. Running the full test suite after each migration confirms no regression slipped in.

## Implementation Plan

### Phase 1

1. **Add `TranslateV2Async()` and `BuildV2Request()` helpers** to `QueryEvaluatorTests.cs`. Mirror the `TranslateAsync` signature. Add a private `BuildCapturePlan(expression, types, hints)` method that builds a `V2CapturePlanSnapshot` with `IsComplete = true`. Also add `BuildMinimalExtractionPlan(expression)`.

2. **Migrate `QueryEvaluatorTests.BasicAndCache.cs`** — All tests call `TranslateAsync` with no local variables. Replace with `TranslateV2Async`. Commit: `refactor(v2-cutover-core-runtime-s5): migrate BasicAndCache tests to v2 payloads`.

3. **Migrate `QueryEvaluatorTests.DbContextResolution.cs`** — Same shape, no local variables. Commit: `refactor(v2-cutover-core-runtime-s5): migrate DbContextResolution tests to v2 payloads`.

4. **Migrate `QueryEvaluatorTests.ExpressionAndParameters.cs`** — Tests include local variable captures (`localVariableTypes` arg). Map each type-hint dict to `V2CapturePlanEntry` with `CapturePolicy = UsePlaceholder`. Commit.

5. **Migrate `QueryEvaluatorTests.EfFunctions.cs`** — No local variables. Commit.

6. **Migrate `QueryEvaluatorTests.OwnershipBoundary.cs`** — Simple local variable captures. Commit.

7. **Update `V2RuntimeAnalyzer.Analyze()`** — Change no-v2-payload case from returning `ShouldUseV2Path = false` (no block) to returning `BlockReason = "no-v2-payloads"`, `BlockMessage = "Request has no v2 extraction or capture plan. All requests must supply a v2 capture plan."`. Update `V2RuntimeAnalyzerTests` to assert new block behavior. Commit: `feat(v2-cutover-core-runtime-s5): block no-v2-payload requests in V2RuntimeAnalyzer`.

8. **Rewrite `EvaluateAsync_LegacyRequest_UnaffectedByV2Gate`** in `QueryEvaluatorTests.V2ProductionWiring.cs` — rename to `EvaluateAsync_MinimalV2Request_ReturnsSql`, supply empty-entries `V2CapturePlanSnapshot`. Commit: `refactor(v2-cutover-core-runtime-s5): update legacy gate test to v2 payload`.

9. **Remove `else BuildInitialStubs()` from `QueryEvaluator.EvaluationPipeline.cs`** — Replace dual-path with unconditional `BuildV2Stubs(v2Decision.CapturePlan!, ...)`. The guard in `EvaluationFlow.cs` already blocks `BlockReason != null` requests before reaching this code, so `CapturePlan` is always non-null here. Add `Debug.Assert(v2Decision.ShouldUseV2Path && v2Decision.CapturePlan is not null)` for clarity. Also remove the now-dead log lines referencing `request.LocalSymbolGraph`. Run full test suite. Commit: `feat(v2-cutover-core-runtime-s5): remove legacy BuildInitialStubs dispatch leg`.

### Phase 2

10. **Migrate `QueryEvaluatorTests.ErrorHandling.cs`** — Commit.

11. **Migrate `QueryEvaluatorTests.AdvancedScenarios.cs`** — Note: 2 pre-existing failures in async-runner tests are unrelated to v2; they fail regardless. Document this in commit message. Commit.

12. **Migrate `QueryEvaluatorTests.SynthesisAndHeuristics.cs`** — Includes complex local symbol hint scenarios. Use `BuildV2Request` with `InitializerExpression` populated for ReplayInitializer entries. Commit.

13. **Migrate `QueryEvaluatorTests.InternalIntrospection.cs`** — Tests use `BuildStubDeclarationForRequestForTest` which reflects into `StubSynthesizer.BuildStubDeclaration`. After migration, these reflection-based tests must pivot to exercising `EvalSourceBuilder.BuildV2CaptureInitializationCode` instead. Commit.

14. **Migrate `QueryEvaluatorTests.SqlServerAndMetadata.cs`** — Commit.

15. **Delete `StubSynthesizer.cs`** — Remove the legacy file. `StubSynthesizer.V2Support.cs` survives (it defines the partial class methods). Commit: `feat(v2-cutover-core-runtime-s5): delete legacy StubSynthesizer`.

16. **Remove `LocalSymbolGraph` from `TranslationRequest`** — Delete the field and `LocalSymbolGraphEntry` type definition. Fix any remaining references in non-test code (expect: LSP `AdaptCapturePlanToLocalSymbolGraph` and `HoverPreviewService` — these are LSP-side and belong to Slice 2 LSP Cutover). Commit: `feat(v2-cutover-core-runtime-s5): remove LocalSymbolGraph from TranslationRequest`.

17. **Remove `BuildSymbolGraph()` test helper** from `QueryEvaluatorTests.cs`. Commit.

## Dependencies

- `v2-cutover-inventory-k8` (completed)
- `query-extraction-v2-runtime-3b` (BuildV2Stubs wired, completed)
- `v2-production-wiring-p9` (StubSynthesizer.V2Support in place, in-progress)

## Testing Strategy

### Unit Tests

- `V2RuntimeAnalyzerTests`: add `Analyze_NoV2Payloads_ReturnsBlockedWithReason` — assert `BlockReason = "no-v2-payloads"` (replaces current `Analyze_NoV2Payloads_ReturnsLegacyPath`)
- After each test file migration: run `dotnet test --filter <FileClass>` to confirm no regression
- After dispatch removal: run full `dotnet test` suite — expect 771+ passing, same 7 pre-existing failures, 1 skip

### Smoke Tests

After Phase 2, manually hover over sample app queries to confirm SQL generation:
1. `SampleMySqlApp` — direct `db.Orders.Where(o => o.UserId == 5)` → SQL with WHERE
2. `SampleDbContextFactoryApp` — factory-root query → SQL with WHERE
3. `SamplePostgresApp` — direct chain with captured variable → SQL with placeholder value

### Regression Check

Pre-existing failures that must remain exactly as-is (neither fixed nor regressed):

| Test | Failure Reason |
|---|---|
| `Evaluate_AsyncRunnerMode_AsyncTerminal_ReturnsSql` | `ct` variable missing from async runner compilation |
| `Evaluate_ConcatAfterDtoProjection_FailsWithSetOperationError` | Error message assertion incorrect after v2 |
| `Evaluate_ConcatAfterDtoProjection_AfterPreNormalization_TranslatesSuccessfully` | `ct` variable missing |
| `Evaluate_ConcatAfterDtoProjection_WithLocalVariableInline_FailsWithSetOperationError` | Same |
| `Evaluate_NonCtCancellationTokenName_InAsyncTerminal_IsSynthesized` | `ct` variable missing |

Fixing these is out of scope for this slice. Do not mask them.

## Acceptance Criteria

### Phase 1

- [ ] `BuildV2Request()` and `TranslateV2Async()` helpers present in `QueryEvaluatorTests.cs`
- [ ] 5 test files migrated: BasicAndCache, DbContextResolution, ExpressionAndParameters, EfFunctions, OwnershipBoundary
- [ ] `V2RuntimeAnalyzer` blocks no-v2-payload requests with `BlockReason = "no-v2-payloads"`
- [ ] `else BuildInitialStubs()` leg removed from `QueryEvaluator.EvaluationPipeline`
- [ ] 771+ Core tests passing; 7 pre-existing failures unchanged

### Phase 2

- [ ] All 6 remaining test files migrated to v2 payloads
- [ ] `StubSynthesizer.cs` deleted; `StubSynthesizer.V2Support.cs` intact
- [ ] `LocalSymbolGraph` property removed from `TranslationRequest`
- [ ] `LocalSymbolGraphEntry` type removed from `TranslationRequest.cs` (or marked with `[Obsolete]` if LSP adapter still references it — resolve in LSP Slice 2)
- [ ] `BuildSymbolGraph()` test helper deleted
- [ ] 771+ Core tests passing; same 7 pre-existing failures unchanged
- [ ] Sample-app hover smoke tests pass (MySql direct, factory-root, Postgres captured variable)

## Review Findings

_Populated by the implementer after code review, security review, and quality analysis._

| Source | Finding | Resolution |
| --- | --- | --- |

## Quality Report

### Security Scan (Trivy)

_Security scan skipped — Trivy not installed. Run `trivy fs . --scanners vuln,secret,misconfig` manually._

### Code Quality (SonarQube)

_Code quality analysis skipped — SonarQube not configured. Run `node .github/scripts/tsp-setup-sonarqube.js` to set up._

## Change Log

| Date | Change | Reason |
| --- | --- | --- |
| 2026-04-06 | Initial impldoc drafted | Core runtime cutover is the blocking dependency for all downstream cleanup slices; beginning with test migration + dispatch removal. |
