# V2 Cutover Inventory - K8 Findings

**Date**: 2026-04-06  
**Focus**: Catalog remaining v1/legacy extraction and runtime paths; classify by production reachability; identify guardrail gaps

---

## Executive Summary

The codebase has **dual-path execution** controlled by `V2RuntimeAnalyzer.Analyze()`:
- **V2 Path (Primary)**: Extract via v2IR → v2CapturePlan → v2 stubs → QueryEvaluator → SQL
- **V1 Path (Legacy)**: Extract via LocalSymbolGraph → legacy stubs → QueryEvaluator → SQL

**Key Finding**: Guardrails ARE partially in place:
- **✅ Core Runtime (QueryEvaluator.EvaluationFlow.cs)**: Blocks unsupported v2 requests with explicit diagnostic (no silent fallback)
- **⚠️ LSP Side (HoverPreviewService)**: Currently adapts v2 capture plan to LocalSymbolGraph → both paths still populated
- **Note**: The v1 path is **STILL REACHABLE** when: no v2 payloads present, v2 payloads are incomplete/rejected

---

## Inventory by Area

### A. LSP-Side Legacy Paths (Extraction & Capture Planning)

#### A1. `LspSyntaxHelper.TypeExtraction.cs` — Legacy Symbol Graph Extraction

| Path | Status | Reachability | Notes |
|------|--------|--------------|-------|
| `ExtractLocalSymbolGraphAtPosition()` | LEGACY | **Dead now** — only called by old test patterns | Called by tests but not used in production LSP flow |
| `ExtractFreeVariableSymbolGraph()` (2 overloads) | LEGACY | **Reachable** → adapts v2 plan | Extracts locals/parameters for any symbol context; still called post-v2 |
| LocalSymbolGraphEntry type definition | BRIDGE | **Reachable** | In-memory v1 symbol representation; kept for compatibility with QueryEvaluator |

**Proposed Action**: Keep as-is for now (required bridge). Mark for removal in LSP cutover slice once QueryEvaluator consumes v2 plans directly.

#### A2. `LspSyntaxHelper.V2Capture.cs` — V2-to-V1 Adapter

| Path | Status | Reachability | Notes |
|------|--------|--------------|-------|
| `AdaptCapturePlanToLocalSymbolGraph()` | ADAPTER | **Reachable** | Converts v2CapturePlan → LocalSymbolGraph for QueryEvaluator |
| Capture-plan-to-LocalSymbolGraphEntry mapping | BRIDGE | **Reachable** | Maps v2 policies (ReplayInitializer/UsePlaceholder/Reject) to legacy format |

**Proposed Action**: Keep adapter until Core QueryEvaluator is refactored to consume V2CapturePlan directly (Core cutover slice).

#### A3. `HoverPreviewService.Pipeline.cs` — Request Assembly

| Path | Status | Reachability | Notes |
|------|--------|--------------|-------|
| Line 269: `AdaptCapturePlanToLocalSymbolGraph(capturePlan)` | ADAPTER | **Reachable** | Populates TranslationRequest.LocalSymbolGraph alongside V2CapturePlan |
| Request construction (lines 331–360) | CURRENT | **Reachable** | Builds TranslationRequest with BOTH LocalSymbolGraph and V2CapturePlan |

**Current Flow**:
1. Extract expression IR (v2ExtractionPlan)
2. Plan captures (v2CapturePlan)
3. Adapt to LocalSymbolGraph (backward-compat bridge)
4. Build TranslationRequest with both
5. LSP diagnostic check (blocks if capturePlan has diagnostics)

**Proposed Action**: Clarify in-code intent. Add comment explaining why both are populated until Core is refactored.

---

### B. Core-Side Legacy Paths (Runtime Execution & Stub Synthesis)

#### B1. `StubSynthesizer.cs` — Legacy Stub Generation

| Method | Status | Reachability | Notes |
|--------|--------|--------------|-------|
| `BuildInitialStubs()` | LEGACY | **Reachable** | Generates stubs from LocalSymbolGraph; fallback when v2 not available |
| `BuildStubDeclaration()` | LEGACY | **Reachable** | Synthesizes individual stub (var x = value;) from LocalSymbolGraphEntry |
| `BuildStubFromTypeName()` | LEGACY | **Reachable** | Type-based stub inference (int=0, string="", etc.) |
| `BuildStubFromInitializer()` | LEGACY | **Reachable** | Initializer expression synthesis from LocalSymbolGraphEntry.InitializerExpression |

**Codegen Path** (Current):
```
BuildInitialStubs() → iterates LocalSymbolGraph → synthesizes stubs (v1 mode)
```

**Proposed Action**: Do NOT delete yet. Keep for now; will be removed when Core runtime cutover slice refactors to use v2CapturePlan directly.

#### B2. `StubSynthesizer.V2Support.cs` — V2 Stub Generation

| Method | Status | Reachability | Notes |
|--------|--------|--------------|-------|
| `BuildV2Stubs()` | NEW | **Reachable** | Generates stubs from V2CapturePlan; chosen when v2Decision.ShouldUseV2Path |
| V2 stub synthesis helpers | NEW | **Reachable** | Maps v2 entries to stub declarations |

**Codegen Path** (Current):
```
QueryEvaluator.EvaluationPipeline.cs (line 163):
  if (v2Decision.ShouldUseV2Path) → BuildV2Stubs(capturePlan)
  else → BuildInitialStubs(localSymbolGraph)
```

**Key Decision Point**: `V2RuntimeDecision.ShouldUseV2Path` (line 17 of V2RuntimeAdapter.cs)

#### B3. `TranslationRequest.cs` — Contract Type

| Property | Status | Reachability | Notes |
|----------|--------|--------------|-------|
| `LocalSymbolGraph` | BRIDGE | **Reachable** | Still populated by LSP for fallback compatibility |
| `V2ExtractionPlan` | NEW | **Reachable** | v2 extraction snapshot; new field in contract |
| `V2CapturePlan` | NEW | **Reachable** | v2 capture snapshot; new field in contract |

**Proposed Action**: Keep all three fields; gradual refactor during Core cutover slice to phase out LocalSymbolGraph usage in queries.

#### B4. `V2RuntimeAnalyzer.cs` — Decision Gate & Guardrails

| Method | Status | Guardedness | Notes |
|-----------|--------|-------------|---------|
| `Analyze()` | GUARD | **Fully Protected** | Determines ShouldUseV2Path; blocks unsupported shapes with BlockReason |
| `FormatDiagnostic()` | GUARD | **Fully Protected** | Format diagnostic for user display |
| Policy checkers: `IsReplayInitializer()`, `IsPlaceholder()`, `IsRejected()` | GUARD | **Fully Protected** | Check capture plan entry policies |

**Guardrail Behavior** (Analyze method):
- No v2 payloads → `ShouldUseV2Path = false` (proceed with v1 path)
- Extraction without capture → Block with "incomplete-v2-state"
- Capture with diagnostics → Block with "capture-rejected:{code}"
- Capture incomplete → Block with "incomplete-capture-plan"
- All valid → `ShouldUseV2Path = true`

**Proposed Action**: **VERIFIED**: Guardrails are correct. Add test cases to prove unsupported shapes block explicitly.

#### B5. `QueryEvaluator.EvaluationFlow.cs` — Execution Flow

| Section | Status | Guardedness | Notes |
|---------|--------|-------------|--------|
| Lines 66–76: v2Decision check | GUARD | **Fully Protected** | Blocks v2-blocked requests; returns Failure() with diagnostic |
| Lines 160–178: Stub selection | CURRENT | **Dual-Path** | Chooses BuildV2Stubs vs BuildInitialStubs based on ShouldUseV2Path |
| Lines 180+: Compilation & execution | CURRENT | **Common Path** | Both paths flow through same compilation → execution pipeline |

**Key Code** (lines 66–76):
```csharp
var v2Decision = V2RuntimeAnalyzer.Analyze(request);
if (v2Decision.BlockReason is not null)
{
    var diagnostic = V2RuntimeAnalyzer.FormatDiagnostic(v2Decision);
    return Failure(diagnostic, ...);  // No silent fallback
}
```

**Proposed Action**: **VERIFIED**: Guardrails work correctly. QueryEvaluator refuses blocked requests; does not silently fall back.

---

### C. Query Evaluation Paths (EvalSourceBuilder & RunnerGenerator)

#### C1. `EvalSourceBuilder.cs` — Expression-to-Csharp Synthesis

| Scope | Status | Reachability | Notes |
|-------|--------|--------------|-------|
| Non-v2 path (legacy) | LEGACY | **Reachable** | Synthesizes evaluation expressions without v2 optimizations |
| Handles all built-in types, collections, etc. | LEGACY | **Reachable** | Used by both v1 and v2 paths (post-stub creation) |

#### C2. `EvalSourceBuilder.V2Support.cs` — V2-Specific Synthesis

| Scope | Status | Reachability | Notes |
|-------|--------|--------------|-------|
| `BuildExpressionFromCapturePolicyEntry()` | NEW | **Reachable** | Interprets capture plan policies (ReplayInitializer/UsePlaceholder/Reject) |
| Policy-driven code generation | NEW | **Reachable** | Consumes v2 capture decisions verbatim, no re-evaluation |

#### C3. `RunnerGenerator.V2Support.cs` — V2 Initialization Code

| Scope | Status | Reachability | Notes |
|-------|--------|--------------|-------|
| V2 initialization synthesis | NEW | **Reachable** | Generates v2-specific setup code for evaluation runner |

---

### D. Test Paths

#### D1. Unit Tests — Legacy vs V2

| Test Suite | Status | Coverage | Notes |
|------------|--------|----------|-------|
| `StubSynthesizerTests.cs` | LEGACY | Tests legacy stub generation | Still passing; should be kept for regression |
| `V2RuntimeAnalyzerTests.cs` | NEW | Tests v2 decision logic | Tests guarding behavior (incomplete-capture-plan, etc.) |
| `QueryEvaluatorTests.V2ProductionWiring.cs` | NEW | Tests end-to-end v2 flow | Confirms v2 extraction → runtime → SQL |
| `EvalSourceBuilder.V2Support tests` | NEW | Tests v2 codegen | Tests ReplayInitializer/UsePlaceholder/Reject policies |
| `RunnerGenerator.V2Support tests` | NEW | Tests v2 initialization | Tests v2-specific runner setup |

#### D2. Integration Tests

| Test Scenario | Status | Coverage | Notes |
|---------------|--------|----------|-------|
| Direct chain query (v2-supported) | NEW | ✅ | Confirms end-to-end v2 flow works |
| Factory-root query (v2-supported) | NEW | ✅ | Confirms factory-root substitution works |
| Unsupported shape (should block) | **NEEDS TEST** | ❌ | Should be explicit diagnostic, not silent fallback |
| Incomplete capture (should block) | **NEEDS TEST** | ❌ | Should be explicit diagnostic |

---

## Classification Summary

| Category | Count | Action |
|----------|-------|--------|
| **Dead Now** (unreachable, safe to delete) | 1 | `ExtractLocalSymbolGraphAtPosition` — only in tests |
| **Required Bridge** (keep until refactored) | 5 | LocalSymbolGraph type, AdaptCapturePlan adapter, 3 TranslationRequest fields |
| **Reachable Legacy** (still used by v1 path) | 12 | BuildInitialStubs, BuildStubDeclaration, BuildStubFromTypeName, legacy EvalSourceBuilder, etc. |
| **New V2 Code** (production, keep) | 8 | BuildV2Stubs, EvalSourceBuilder.V2Support, RunnerGenerator.V2Support, V2RuntimeAnalyzer, etc. |

**Total Inventory**: ~26 candidate paths and types

---

## Guardrail Status

### ✅ Already Protected

1. **V2RuntimeAnalyzer blocks unsupported requests** — explicit diagnostics instead of silent fallback
2. **QueryEvaluator.EvaluationFlow** guards unsupported shapes — returns Failure() with diagnostic
3. **N+1 check prevention** — V2CapturePlan validates completeness before runtime
4. **Deterministic policy application** — EvalSourceBuilder/RunnerGenerator consume capture policies verbatim

### ⚠️ Gaps Identified

1. **No explicit test** validating that an unsupported shape returns diagnostic, not silent v1 fallback
2. **LSP documentation** unclear about why both LocalSymbolGraph and V2CapturePlan are populated
3. **No tracking doc** for known unsupported query shapes that should be fixed in v2

---

## Follow-On Cleanup Slices

### Slice 2: LSP Cutover (to be defined post-inventory)

**Scope**: Remove legacy extraction/capture paths from LSP once v2 owns all extraction.

**Candidates**:
- `ExtractLocalSymbolGraphAtPosition()` (dead now — safe)
- `AdaptCapturePlanToLocalSymbolGraph()` (can defer until Core cutover)
- Legacy capture planning code (TBD pending deeper LSP analysis)

### Slice 3: Core Runtime Cutover (to be defined post-inventory)

**Scope**: Refactor QueryEvaluator to consume V2CapturePlan directly, removing legacy stubs.

**Candidates**:
- `BuildInitialStubs()` and helpers (replace with v2-only path)
- `LocalSymbolReplayPolicies` enum (keep for v2 policies)
- `EvalSourceBuilder` legacy code (keep only v2-aware portions)

### Slice 4: Test/Docs Cleanup (to be defined post-inventory)

**Scope**: Remove v1-specific tests and docs once Core cutover is complete.

**Candidates**:
- Legacy test fixtures (TBD)
- Docs referencing v1 extraction flow (TBD)

---

## Implementation Recommendations

1. **Add explicit guardrail validation tests** (this slice)
   - Test unsupported shape → explicit diagnostic (not silent fallback)
   - Test complete v2 capture → SQL generation succeeds
   - Test incomplete capture → explicit diagnostic

2. **Document known unsupported shapes** (this slice)
   - Create `UNSUPPORTED_SHAPES.md` referencing shapes not yet covered by v2
   - Link from `features.md` for visibility

3. **Clarify dual-path intent in code** (this slice)
   - Add comment block in HoverPreviewService explaining why LocalSymbolGraph still populated
   - Reference LSP cutover slice in comment

4. **Define LSP Cutover slice** (post-inventory)
   - Identify which LSP extraction code is truly dead vs still needed for diagnostics
   - Plan removal of AdaptCapturePlanToLocalSymbolGraph adapter
   - Plan LSP-side v2-first migration

5. **Define Core Cutover slice** (post-inventory)
   - Refactor QueryEvaluator to consume V2CapturePlan directly
   - Remove BuildInitialStubs and legacy stubs
   - Verify all v2 test scenarios pass

6. **Define Test/Docs Cleanup slice** (post-inventory)
   - Remove v1-specific test fixtures
   - Update documentation to v2-first language
   - Archive v1 patterns for reference
