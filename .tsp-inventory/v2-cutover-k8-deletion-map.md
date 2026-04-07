# V2 Cutover Deletion Map — K8 Planning Document

**Generated**: 2026-04-06  
**Purpose**: Define specific deletion targets for follow-on cleanup slices (2, 3, 4) based on v2-cutover-inventory-k8 findings

---

## Deletion Slice 2: LSP Extraction/Capture Cutover

**Scope**: Remove LSP-side legacy extraction/capture code once v2 owns all extraction paths.

**Estimated Effort**: Small (LSP is thin layer)  
**Estimated Risk**: Low (v2 path already in production)

### Files to Delete/Modify

#### Delete Entirely

None — Keep LSP extraction adapters for now.

#### Modify (Reduce Scope)

| File | Line Range | Change | Notes |
|------|-----------|--------|-------|
| `src/EFQueryLens.Lsp/Parsing/LspSyntaxHelper.TypeExtraction.cs` | All | **DEFER to future** | Contains only legacy `ExtractLocalSymbolGraphAtPosition()` which is dead; keep in place as a historical record but mark with TODOs for removal after Core cutover |
| `src/EFQueryLens.Lsp/Parsing/LspSyntaxHelper.V2Capture.cs` | 159–200 | Replace `AdaptCapturePlanToLocalSymbolGraph()` when Core calls v2 plan directly | Currently required bridge; becomes dead after Core cutover |
| `src/EFQueryLens.Lsp/Services/HoverPreviewService.Pipeline.cs` | 269, 338 | Remove `AdaptCapturePlanToLocalSymbolGraph()` call and `LocalSymbolGraph` population | Once Core consumes V2CapturePlan directly, LSP no longer needs to build legacy adapter |

#### Test Modifications Required

| File | Action | Notes |
|------|--------|-------|
| `tests/EFQueryLens.Core.Tests/Lsp/LspSyntaxHelperTests.V2CaptureSlice2.cs` | Update/reduce tests | Tests verify v2 capture → legacy adapter works; reduce to just v2 capture tests after Core cutover |
| `tests/EFQueryLens.Core.Tests/Lsp/HoverPreviewServiceFormattingTests.cs` | Update/reduce tests | Tests verify dual-path request assembly; reduce to just v2-path tests after Core cutover |

### Interdependencies

- **Blocked by**: Completion of Core Runtime Cutover Slice — cannot remove LSP adapter until Core QueryEvaluator consumes V2CapturePlan directly
- **Enables**: Simpler LSP codebase with clear v2-first design

### Validation for Slice 2

- [ ] All v2 extraction paths work without AdaptCapturePlanToLocalSymbolGraph adapter
- [ ] LSP → Daemon → Core pipeline produces correct SQL for v2-supported queries
- [ ] Unsupported shapes return explicit diagnostics (already validated in K8)

---

## Deletion Slice 3: Core Runtime/Stub Synthesis Cutover

**Scope**: Remove Core-side legacy stub synthesis and runtime fallback logic.

**Estimated Effort**: Medium (Core runtime is complex)  
**Estimated Risk**: Medium (core execution path; requires extensive validation)

### Files to Delete/Modify

#### Delete Entirely

| File | Reason | Tests Impacted |
|------|--------|----------------|
| `src/EFQueryLens.Core/Scripting/StubSynthesis/StubSynthesizer.cs` | Entire legacy stub synthesis | Replace with v2 path only; `BuildInitialStubs()` becomes dead code |
| `src/EFQueryLens.Core/Scripting/Compilation/EvalSourceBuilder.cs` (non-v2 parts) | Legacy expression synthesis | Keep only v2-aware code paths; remove legacy heuristics |
| `src/EFQueryLens.Core/Contracts/TranslationRequest.cs` (partial) | Fields no longer needed | Can be deleted: `LocalSymbolGraph` property (line 65) after v2 phase-in complete |
| `src/EFQueryLens.Core/Scripting/Evaluation/QueryEvaluator.EvaluationPipeline.cs` (lines 160–180) | Dual-path decision logic | Remove fallback to `BuildInitialStubs()`; use only V2 path |

#### Modify (Keep Scoped V2 Code)

| File | Keep | Delete | Notes |
|------|------|--------|-------|
| `src/EFQueryLens.Core/Scripting/Compilation/EvalSourceBuilder.cs` | Non-v2-specific code (type mapping, etc.) | Legacy symbol graph iteration | Core type synthesis logic remains; only v2-specific code paths used |
| `src/EFQueryLens.Core/Contracts/LocalSymbolGraphEntry` | Legacy definition (struct) | Usage in active code paths | Type definition remains in history; direct usage can be removed |
| `src/EFQueryLens.Core/Contracts/LocalSymbolReplayPolicies` | Keep enum values | Usage in legacy stubs | Policies remain defined; only v2 uses them |

#### Core Logic Changes

| Component | Current | Post-Cutover |
|-----------|---------|--------------|
| `QueryEvaluator.EvaluationPipeline` line 163 | `if (v2Decision.ShouldUseV2Path) ... else BuildInitialStubs()` | `// v2 path only; no fallback` |
| Stub generation | `BuildV2Stubs()` when v2, else `BuildInitialStubs()` | `BuildV2Stubs()` always |
| Compilation pipeline | Handles both v2 and legacy stubs | Handles only v2 stubs |

### Test Modifications Required

| File | Action | Effort | Notes |
|------|--------|--------|-------|
| `tests/EFQueryLens.Core.Tests/Scripting/QueryEvaluatorTests.cs` | Reduce BuildSymbolGraph helper | Small | Legacy test helper can be removed; all tests should use v2 payloads |
| `tests/EFQueryLens.Core.Tests/Scripting/QueryEvaluatorTests.*.cs` (all) | Convert all tests to v2 payloads | Large | 200+ tests currently populate LocalSymbolGraph; refactor to populate V2CapturePlan instead |
| `tests/EFQueryLens.Core.Tests/Scripting/StubSynthesizerTests.cs` | Delete entirely | Medium | Tests legacy stub syntax; no longer needed post-cutover |
| `tests/EFQueryLens.Core.Tests/Daemon/DaemonRuntimeTests.cs` | Update request builders | Medium | Remove LocalSymbolGraph population from test helpers |

### Interdependencies

- **Prerequisite**: LSP Cutover Slice must be complete (no adapter generation needed)
- **Enables**: Simpler, more maintainable Core runtime architecture
- **Risk Level**: HIGH — extensive test refactoring required; must validate no regressions

### Validation for Slice 3

- [ ] All 779 Core tests pass (currently 771 pass, 7 fail pre-existing, 1 skipped)
- [ ] All v2-routed queries produce correct SQL
- [ ] All v2-blocked queries return explicit diagnostics
- [ ] Performance: no regression vs dual-path evaluation
- [ ] Integration: LSP → Core → SQL pipeline end-to-end validated

---

## Deletion Slice 4: Test/Docs/Cleanup

**Scope**: Clean up test fixtures, documentation, and helpers specific to v1 mode.

**Estimated Effort**: Large (documentation-heavy, but low risk)  
**Estimated Risk**: Low (mostly docs and test infrastructure)

### Files to Delete/Modify

#### Delete Entirely

| File | Reason |
|------|--------|
| `tests/EFQueryLens.Core.Tests/Scripting/StubSynthesizerTests.cs` | Tests legacy stub generation; covered by integration tests |
| Legacy test builder methods (TBD in Slice 3) | Helpers specific to LocalSymbolGraph construction |
| Old LSP extraction docs (TBD) | Any docs referencing old extraction flavor |

#### Modify (Update Documentation)

| File | Change | Notes |
|------|--------|-------|
| `README.md` | Update extraction flow diagram | Change from dual-path to v2-first |
| `architecture.md` | Complete the extraction/runtime section | (Currently noted as scaffolding in TSP instructions) |
| `features.md` | Mark v1 features as "archived" | Add "Superseded by v2 in Slice 3" notes |
| `CHANGELOG.md` | Add "v2 cutover complete" entry | Reference all 4 cleanup slices |
| Test documentation | Update test strategy docs | Reference v2-validation pattern |

#### Comments & Code Markers

- [ ] Add TODO comments in deleted files (archived for reference)
- [ ] Document deprecations in removed methods
- [ ] Create `MIGRATION_GUIDE.md` for future developers on v1→v2 conceptual changes

### Test Modifications Required

| Category | Action |
|----------|--------|
| Integration tests | Add v2-validation smoke tests for critical paths |
| Harness tests | Use EfQueryHarness to validate real SQL for v2 queries |
| Sample apps | Update sample app hover validation to use v2-supported patterns |

### Interdependencies

- **Prerequisite**: Core Runtime Cutover Slice complete
- **No blockers**: Documentation can happen in parallel with final validation rounds

### Validation for Slice 4

- [ ] All documentation reflects v2-first architecture
- [ ] No references to "legacy," "fallback," or v1-specific patterns in active docs
- [ ] Test suite runs clean (779 pass, 0 fail if fixing pre-existing ct issues)
- [ ] Manual smoke test: direct query and factory-root query both produce SQL
- [ ] PR ready for merge with comprehensive changelog

---

## Summary Table: Deletion Targets Across All Slices

| Component | File(s) | Slice 2 | Slice 3 | Slice 4 | Final Status |
|-----------|---------|---------|---------|---------|--------------|
| **LSP Legacy Extraction** | LspSyntaxHelper.TypeExtraction.cs | — | — | Archive | HISTORY |
| **LSP V2 Adapter** | LspSyntaxHelper.V2Capture.cs, AdaptCapturePlanToLocalSymbolGraph | DELETE | — | — | REMOVED |
| **Core Legacy Stubs** | StubSynthesizer.cs, BuildInitialStubs | — | DELETE | — | REMOVED |
| **Legacy Eval Source** | EvalSourceBuilder.cs (non-v2) | — | REDUCE | — | SLIMMED |
| **Legacy Dispatch Logic** | QueryEvaluator.EvaluationPipeline.cs (fallback) | — | DELETE | — | V2-ONLY |
| **LocalSymbolGraphEntry Type** | TranslationRequest.cs | — | REDUCE | — | DEPRECATED |
| **LocalSymbolGraph Field** | TranslationRequest.cs | — | DELETE | — | REMOVED |
| **Dual-Path Tests** | QueryEvaluatorTests.*.cs | — | LARGE REFACTOR | — | V2-ONLY |
| **Legacy Stub Tests** | StubSynthesizerTests.cs | — | DELETE | — | REMOVED |
| **Documentation** | README.md, architecture.md, etc | — | — | UPDATE | V2-FIRST |

---

## Risk Assessment

### Low Risk (Safe to Delete)

- `ExtractLocalSymbolGraphAtPosition()` — only in tests
- `AdaptCapturePlanToLocalSymbolGraph()` adapter — once Core switches to v2
- Legacy LSP capture code — once v2 extraction owns all paths

### Medium Risk (Extensive Testing Required)

- `BuildInitialStubs()` removal — core runtime change; affects all non-v2 queries (which should be zero)
- Dual-path dispatch removal — must ensure all real-world queries can survive v2-blocking

### High Risk (Requires Careful Validation)

- Test refactoring (200+ tests) — must maintain equivalent coverage with v2 payloads
- Performance regression validation — ensure v2-only path is not slower

---

## Next Steps (Per Slice)

### Before Slice 2 Starts

- [ ] Complete K8 validation (targeted hover tests on sample apps)
- [ ] Verify all real-world v2-supported queries work without adapter
- [ ] Identify any query shapes not yet covered by v2; create follow-up issues

### Before Slice 3 Starts

- [ ] Review and approve LSP cutover changes
- [ ] Refresh performance benchmarks with v2-only path
- [ ] Plan extensive test refactoring (200+ tests)

### Before Slice 4 Starts

- [ ] Review and approve Core cutover changes
- [ ] Plan documentation updates
- [ ] Identify any remaining v1 references in comments/docs

---

## Legend

- `(TBD)` — Exact files/lines to be determined during execution of that slice
- `DELETE` — Remove entirely
- `REDUCE` — Keep some code; remove usage in specific contexts
- `MODIFY` — Keep; change implementation or scope
- `ARCHIVE` — Keep as historical reference; mark as deprecated

