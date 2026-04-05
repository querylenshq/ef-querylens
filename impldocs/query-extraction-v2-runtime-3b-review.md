# Review

## Scope
- **Impldoc reviewed**: `query-extraction-v2-runtime-3b.md`
- **Change summary**: Slice 3b integrates V2RuntimeAdapter decision logic into `QueryEvaluator`, adds `EvalSourceBuilder.V2Support` and `RunnerGenerator.V2Support` for policy-driven codegen, and delivers 33 v2 tests. Includes a post-implementation regression fix to the v2 gate condition.
- **Review date**: 2026-04-04

## Verdict

**At Risk**

The gate logic and the rejection diagnostic path work correctly. But the v2 codegen methods — `BuildV2CapturePlanInitialization` and `BuildV2CaptureInitializationCode` — have **zero production callers**. When a request carries a valid v2 payload (`ShouldUseV2Path = true`), execution falls through to the existing legacy path unchanged. The new methods are dead code. The accepted codegen does not change based on v2 payloads. This means the impldoc's codegen requirements are not met at the production wiring level, even though the methods themselves exist and are correctly tested in isolation.

---

## Top Merge Risks

1. **V2 codegen is not wired into the execution pipeline.** `BuildV2CapturePlanInitialization` and `BuildV2CaptureInitializationCode` are never called by `TryBuildRunnerForCacheMiss`, `EvalSourceBuilder.TemplateBlocks`, `RunnerGenerator.GenerateRunnerClass`, or any other production path. When a valid v2 payload arrives, it takes the legacy path as if v2 didn't exist. The runtime behavior for v2 queries is identical to legacy queries. This is the highest-risk finding.

2. **The "integration tests" do not exercise `QueryEvaluator.EvaluateAsync()`.** `QueryEvaluatorV2IntegrationTests` calls `V2RuntimeAnalyzer.Analyze()` directly. None of the 33 new tests exercise the actual evaluation pipeline entry point with a v2 payload. The wiring regression (P0 condition bug) survived the full test run precisely because no test invoked `EvaluateAsyncInternal` with a v2 payload.

3. **P0 condition bug shipped with the initial implementation.** The condition `if (!v2Decision.ShouldUseV2Path)` blocked all non-v2 queries — a total outage across 103 tests. It was caught only after a post-commit code review pass, not by any test. This implies the test coverage for `EvaluateAsyncInternal` under v2 conditions is insufficient.

4. **`v2Decision` is computed but never used downstream.** `v2Decision.ExtractionPlan` and `v2Decision.CapturePlan` are captured in the returned `V2RuntimeDecision` but are never passed to `TryBuildRunnerForCacheMiss` or any downstream method. The validated capture-plan data is silently discarded.

---

## Findings

### Finding 1
- **Severity**: Major
- **Area**: correctness, impldoc fidelity
- **Location**: `QueryEvaluator.EvaluationFlow.cs` line 68–82, `EvalSourceBuilder.TemplateBlocks.cs` `AppendRunner`, `RunnerGenerator.GenerateRunnerClass`
- **Issue**: When `v2Decision.ShouldUseV2Path = true`, execution falls through to `TryBuildRunnerForCacheMiss` which invokes `RunnerGenerator.GenerateRunnerClass(... stubs ...)` using the legacy `stubs` list derived from `LocalSymbolGraph`. The v2 capture plan is not passed and not used. `BuildV2CapturePlanInitialization` is never called.
- **Why it matters**: The impldoc states RunnerGenerator should "emit initialization code for replay/placeholder/reject policies" and EvalSourceBuilder should "interpret and emit code for replay/placeholder/reject policies." Neither method is invoked in production. Acceptance criteria [x] for both are not verified by a test that calls the pipeline end-to-end.
- **Required justification**: Either show the call path where `BuildV2CapturePlanInitialization` feeds into `GenerateRunnerClass`, or acknowledge this is deferred and update the impldoc scope accordingly.
- **Suggested reviewer probe**: Search all callers of `GenerateRunnerClass` and `AppendRunner`. Ask: where does the v2 capture plan enter `stubs`?

### Finding 2
- **Severity**: Major
- **Area**: testing, evidence quality
- **Location**: `tests/EFQueryLens.Core.Tests/Scripting/QueryEvaluatorV2IntegrationTests.cs`
- **Issue**: All four "integration tests" call `V2RuntimeAnalyzer.Analyze(request)` directly. None call `QueryEvaluator.EvaluateAsync()` or exercise the pipeline. These are unit tests for the analyzer, not integration tests for the evaluator. The impldoc calls for "integration tests covering: direct terminal chain, helper composition, invalid payload rejection" through the evaluator. None exist.
- **Why it matters**: The P0 condition bug (`!ShouldUseV2Path` vs `BlockReason is not null`) was present for the duration of implementation and was caught by an agent code review, not by a test. An actual integration test calling `EvaluateAsync` with a legacy payload would have caught this immediately.
- **Required justification**: None of the three integration test scenarios described in the impldoc (direct chain, helper composition, rejection) are tested end-to-end. What is the rationale for accepting unit tests against the analyzer in lieu of evaluator pipeline tests?
- **Suggested reviewer probe**: Ask whether any test validates that a non-v2 `TranslationRequest` produces SQL after this change, and whether any test validates that a v2-rejected payload returns a structured diagnostic from `EvaluateAsync`.

### Finding 3
- **Severity**: Moderate
- **Area**: correctness, reviewer-focus
- **Location**: `V2RuntimeAdapter.cs` `Analyze()` method, ~line 63–80
- **Issue**: The method has two separate code paths that produce `ShouldUseV2Path = false` with no `BlockReason`: (1) no v2 payloads (returns `new V2RuntimeDecision { ShouldUseV2Path = false }`) — correct. (2) Extraction-only, no capture — sets `BlockReason = "extraction-only-no-capture"`. But there are duplicate semantics between the "partial v2 state" block (lines ~63–70) and the trailing "extraction plan only (no capture)" block (~lines 96–107). Both handle `V2ExtractionPlan is not null && V2CapturePlan is null` but are reached via different paths in the if-chain. The trailing block is unreachable in the current control flow.
- **Why it matters**: Dead code in the decision method creates false confidence. A human reviewer reading the method without running coverage will believe there are more decision paths than actually execute.
- **Required justification**: Show the test or coverage data that confirms the trailing unreachable block is intentionally defensive or explain its intent.
- **Suggested reviewer probe**: Trace control flow through `Analyze()` with `V2ExtractionPlan != null, V2CapturePlan == null`. Confirm the trailing block is never reached.

### Finding 4
- **Severity**: Moderate
- **Area**: impldoc fidelity
- **Location**: `impldocs/query-extraction-v2-runtime-3b.md` Acceptance Criteria
- **Issue**: The impldoc marks `[x] EvalSourceBuilder correctly interprets and emits code for replay/placeholder/reject policies` and `[x] RunnerGenerator correctly initializes v2 capture-plan entries`. These criteria are satisfied by unit tests against the isolated methods, not by observable pipeline behavior. The criteria language implies production wiring ("correctly emits", "correctly initializes") but the evidence is unit tests for dead code. If the intent was "the method exists and returns correct output," the criterion should say so explicitly.
- **Why it matters**: The impldoc at completion should reflect what was actually built. What was built is: the methods exist and are correct in isolation, but are not called in production. This is a different and narrower claim.
- **Required justification**: Update criteria language or add a note distinguishing "method exists with correct logic" from "method is invoked in pipeline execution."

### Finding 5
- **Severity**: Minor
- **Area**: correctness
- **Location**: `RunnerGenerator.V2Support.cs` `BuildV2CapturePlanInitialization()` line ~42–46
- **Issue**: Parse failures are silently swallowed: `catch { continue; }`. A malformed initializer expression from `EvalSourceBuilder.BuildV2CaptureInitializationCode` would silently skip the entry with no diagnostic. If this method were in production use, callers would receive fewer statements than expected with no indication of failure.
- **Why it matters**: At production wiring time, this silent failure will produce incorrect codegen without surfacing a problem. Acceptable only if the capture-plan contract guarantees syntactically valid expressions, which it does not today.
- **Suggested reviewer probe**: Ask whether `V2CapturePlanEntry.InitializerExpression` is validated for syntactic correctness before being written into the snapshot, and whether parse failures should produce a codegen diagnostic rather than a silent skip.

### Finding 6
- **Severity**: Minor
- **Area**: correctness
- **Location**: `EvalSourceBuilder.V2Support.cs` `BuildPlaceholderInitializationCode()` and `BuildReplayInitializerCode()` fallback
- **Issue**: Both methods emit `var {name} = default({typeName})`. For strings (`string`, `System.String`), `default(string)` is `null` — not empty string. The JSDoc comment in `BuildPlaceholderInitializationCode` says "For strings: empty string" but the implementation emits `default(string)` which is `null`. The comment is contradicted by the implementation.
- **Why it matters**: If a query uses a string capture-as-placeholder and the generated `null` causes a NullReferenceException in the query body (e.g., `.Where(u => u.Name.Contains(searchTerm))`), the user would see a runtime failure rather than a SQL result. This becomes a real issue when the method is wired in.
- **Suggested reviewer probe**: Ask whether `null` vs empty string matters for EF LINQ translation (it does — `Contains(null)` may translate differently or throw).

---

## Impldoc Gaps

1. **The impldoc asserts "SQL parity validation via manual harness testing" is complete** (`[x] Manual harness spot-checks confirm SQL parity for supported v2 scenarios`). The change log defers this to "post-merge validation" and the end-to-end-testing.md documents it as future smoke test steps. The acceptance criterion and the change log are in direct contradiction.

2. **The impldoc does not describe the integration point** between `V2RuntimeDecision` (returned from `Analyze()`) and the downstream codegen. Implementation step 4 says "If `ShouldUseV2Path: true`, proceed to v2 codegen paths" but the spec does not identify which method, which parameter, or which call site. This gap allowed the implementation to satisfy the gate in isolation while leaving codegen unconnected.

3. **No decision recorded for `v2Decision` downstream handoff**. The impldoc has three design decisions (rejection diagnostic, policy-driven codegen, early tests). None covers how the `V2RuntimeDecision` object flows through to `TryBuildRunnerForCacheMiss`. This is the most consequential design question of the slice and it has no recorded reasoning.

---

## Evidence Gaps

1. **No test calls `EvaluateAsync` / `EvaluateAsyncInternal` with a v2 payload.** The v2 pipeline integration is untested end-to-end.

2. **No test calls `EvaluateAsync` with a legacy (non-v2) payload after the gate change.** The P0 regression fix (commit 464e568) is validated only by the pre-existing `QueryEvaluatorTests.Evaluate_*` suite — those tests do not assert the gate skip specifically, they just happen to pass because the bug is gone. There is no targeted guard test.

3. **No test for unreachable code in `V2RuntimeAnalyzer.Analyze()`** (the trailing extraction-only block). Coverage data would surface this; none is provided.

4. **No evidence for string placeholder behavior.** The `default(string) = null` issue in `BuildPlaceholderInitializationCode` has no test covering string type behavior under the placeholder policy.

5. **SQL parity evidence is deferred.** The acceptance criterion is checked, but the supporting evidence (harness spot-check) is flagged in the change log as not yet done.

---

## Human MR Reviewer Focus

1. **First**: Verify that `BuildV2CapturePlanInitialization` is called anywhere in the production code path. If it is not, determine whether this slice's codegen requirements are actually met or whether the scope should be revised.

2. **Second**: Check the v2 gate in `EvaluationFlow.cs` lines 68–82. Confirm the gate is transparent to non-v2 requests (verify by running the pre-existing `QueryEvaluatorTests.Evaluate_*` suite yourself). Confirm the gate correctly blocks v2-rejected payloads (no test currently covers this path end-to-end).

3. **Third**: Read `Analyze()` in `V2RuntimeAdapter.cs` and trace the control flow for partial-v2 state. Identify the unreachable trailing block and decide whether it should be removed or guarded.

4. **Fourth**: Evaluate the comment/implementation mismatch in `BuildPlaceholderInitializationCode` (claims empty string, emits null). Consider the downstream impact when this is wired in.

5. **Fifth**: Evaluate whether the acceptance criterion `[x] Manual harness spot-checks confirm SQL parity` is legitimately checked, given the change log simultaneously defers it.

---

## Residual Concerns

- **V2 path produces no observable behavior change at runtime today.** For v2 payloads, the legacy path runs as-is (correct behavior for now since Slice 4 hasn't cut over). But the impldoc frames this slice as delivering "codegen integration" — what was actually delivered is the gate (which works) and isolated codegen methods (which aren't called). That gap should be explicit.

- **Silent exception swallowing in `BuildV2CapturePlanInitialization`.** The catch-and-continue pattern will make debugging harder when this code is wired into production. Should be replaced with a diagnostic before Slice 4.

- **`FormatDiagnostic` returns `"(no v2 diagnostic)"` for null `BlockReason`.** If somehow called with a non-blocked decision, this string would surface as a user-visible error containing implementation noise. The caller now gates on `BlockReason is not null` before calling `FormatDiagnostic`, which prevents this, but the defensive guard in `FormatDiagnostic` itself is misleading (it implies null-BlockReason is a valid codepath for the method).

---

## History

_First review run — 2026-04-04._
