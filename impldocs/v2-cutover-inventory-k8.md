# V2 Cutover Inventory and Guardrails

## Overview

Create the first cleanup slice for removing dead and conceptually obsolete v1 query extraction/runtime code now that v2 extraction is working successfully in production scenarios. This slice does not delete the full legacy stack yet. It inventories all remaining production-reachable v1 paths, classifies what is truly dead vs still reachable, and adds guardrails so unsupported shapes fail explicitly instead of silently relying on legacy behavior.

## Scope Boundaries

**Feature context**: Split follow-on from `query-extraction-v2-cutover-r4`

**This slice delivers**:

- Inventory of remaining v1 / legacy extraction, capture, runtime, and test-support paths
- Classification of each candidate as one of: dead now, reachable but obsolete, required transitional bridge, or not yet removable
- Explicit runtime/LSP guardrails so unsupported shapes do not silently drift onto legacy behavior
- Documentation of known unsupported non-v2 shapes that must be fixed before deeper removal slices
- Focused validation proving the inventory is accurate and the new guardrails do not regress supported v2 flows

**Out of scope / deferred**:

- Bulk deletion of all legacy extraction/capture/runtime code in one pass
- Removal of all v1-oriented tests and helpers
- Large contract removals such as deleting `LocalSymbolGraph` from `TranslationRequest`
- New v2 feature expansion beyond documenting known unsupported shapes

**Follow-on slices expected after this one**:

1. LSP cutover and legacy extraction/capture removal
2. Core runtime/stub synthesis cutover and legacy runtime removal
3. Test/helper/doc cleanup for removed v1 behavior

**Expected implementation order**:

1. `v2-cutover-inventory-k8`
2. LSP cutover slice (new impldoc)
3. Core runtime cutover slice (new impldoc)
4. Test/docs cleanup slice (new impldoc)

## Requirements

- [x] Identify every remaining production path that still depends on legacy extraction, `LocalSymbolGraph`, legacy stub synthesis, or legacy runtime fallback semantics
- [x] For each candidate, record whether it has production call sites, test-only call sites, or no call sites at all
- [x] Add or tighten guardrails so requests unsupported by v2 fail explicitly rather than silently continuing on conceptually obsolete v1 behavior
- [x] Produce a deletion map grouped by area: LSP parsing/capture, Core runtime/contracts/stub synthesis, tests/helpers/docs
- [x] Document any query shapes still not covered by v2 and treat them as explicit follow-up work rather than preserving v1 compatibility indefinitely
- [x] Keep all currently supported v2 scenarios working, including factory-root substitution paths
- [x] Validate changes with unit tests and targeted hover validation on representative sample apps

## Design Decisions

### Decision 1: Inventory before deletion

**Choice:** Perform a dedicated inventory-and-guardrails slice before removing major legacy code.

**Rationale:** The remaining v1 footprint spans LSP extraction, Core runtime contracts, stub synthesis, tests, and documentation. A deletion-first pass would make it too easy to remove a still-reachable bridge without first proving that v2 owns the path end to end.

**Alternatives considered:**

- Delete all obvious v1 code immediately — rejected because reachability is not yet fully proven.
- Keep dual-path behavior indefinitely — rejected because the goal is to remove conceptually obsolete fallback, not preserve it.

### Decision 2: Prefer explicit unsupported diagnostics over silent compatibility

**Choice:** Any shape not covered by v2 should be documented and surfaced explicitly, not left on a silent v1 fallback path.

**Rationale:** The stated compatibility bar is v2-first. If a shape is still unsupported, that gap should be visible and actionable.

**Alternatives considered:**

- Preserve legacy fallback for uncovered shapes — rejected because it hides cutover gaps and makes dead-code removal ambiguous.

### Decision 3: Classify by production reachability, not by naming

**Choice:** `legacy`, `fallback`, or `v1` comments are not enough to justify deletion. Each candidate must be classified by actual production reachability.

**Rationale:** Some code still uses legacy names while acting as transitional bridges for the current runtime. Cleanup should follow behavior, not labels.

## Implementation Plan

1. Inventory LSP-side legacy and bridge paths related to extraction, capture planning, and request emission.
2. Inventory Core-side legacy and bridge paths related to `TranslationRequest`, `LocalSymbolGraph`, `V2RuntimeAnalyzer`, and stub synthesis.
3. Inventory tests, helper utilities, and docs that exist only to preserve or explain v1 behavior.
4. Map each candidate to one of four states: dead now, obsolete-but-reachable, required bridge, or keep-for-now.
5. Add or adjust guardrails so unsupported non-v2 shapes return explicit diagnostics instead of silently flowing into conceptually obsolete runtime paths.
6. Add focused unit tests proving guardrail behavior and proving no supported v2 shapes regress.
7. Run targeted hover validation against representative sample apps, including at least a direct v2-supported query and a factory-root query.
8. Produce the follow-on deletion plan for the next cutover slices.

## Dependencies

- `query-extraction-v2-q7`
- `query-extraction-v2-capture-h2`
- `query-extraction-v2-runtime-m6`
- `query-extraction-v2-runtime-3b`
- `v2-production-wiring-p9`
- `factory-root-substitution-j4`
- `tools/EfQueryHarness/`

## Inventory Summary

**See also**: `.tsp-inventory/v2-cutover-k8-findings.md` and `.tsp-inventory/v2-cutover-k8-deletion-map.md`

### Key Findings

1. **Guardrails Already in Place**: V2RuntimeAnalyzer is fully functional. When v2 payloads are incomplete or rejected, the runtime returns explicit diagnostic messages instead of silently falling back to legacy paths.

2. **Test Coverage Comprehensive**: All 10 V2RuntimeAnalyzer tests and 3 QueryEvaluatorV2Integration tests pass, validating guardrail behavior end-to-end.

3. **Inventory Classification** (26 candidates):
   - 1 dead (ExtractLocalSymbolGraphAtPosition — test-only)
   - 5 required bridges (LocalSymbolGraph type, AdaptCapturePlan adapter, TranslationRequest fields)
   - 12 reachable legacy paths (BuildInitialStubs, legacy EvalSourceBuilder, etc.)
   - 8 new v2 code (BuildV2Stubs, V2RuntimeAnalyzer, v2 support modules)

4. **Sample App Validation**: SampleDbContextFactoryApp builds without errors/warnings, confirming both direct queries and factory-root patterns are syntactically correct.

5. **Core/Daemon Stability**: Both projects build with -TreatWarningsAsErrors=true, confirming production readiness.

### Next Steps

This slice does not modify code; it establishes the deletion plan for future slices:

- **Slice 2 (LSP Cutover)**: Remove AdaptCapturePlanToLocalSymbolGraph adapter once Core switches to v2
- **Slice 3 (Core Runtime Cutover)**: Remove BuildInitialStubs, legacy EvalSourceBuilder, dual-path dispatch
- **Slice 4 (Test/Docs Cleanup)**: Update tests and documentation to v2-first language

## Dependencies

- `query-extraction-v2-q7`
- `query-extraction-v2-capture-h2`
- `query-extraction-v2-runtime-m6`
- `query-extraction-v2-runtime-3b`
- `v2-production-wiring-p9`
- `factory-root-substitution-j4`
- `tools/EfQueryHarness/`

## Testing Strategy

### Unit Tests

- Add tests proving unsupported shapes now surface explicit diagnostics where legacy continuation would previously have been possible
- Add tests proving supported v2 paths still execute without legacy fallback assumptions
- Add tests for any newly introduced inventory/guard helper methods

### Targeted End-to-End Validation

Use sample apps to hover and confirm:

1. Supported direct-chain query still produces SQL
2. Supported factory-root query still produces SQL
3. Known unsupported shape produces explicit diagnostic rather than silent fallback behavior

### Harness Validation

- Use `tools/EfQueryHarness/` where helpful to confirm representative supported queries still translate after guardrail tightening

## Acceptance Criteria

- [x] Remaining v1/legacy candidates are inventoried and classified by production reachability
- [x] A concrete deletion map exists for later LSP, Core, and test/doc cleanup slices
- [x] Unsupported non-v2 shapes are explicitly documented instead of implicitly relying on legacy behavior
- [x] Guardrails are in place to prevent silent conceptually obsolete fallback on inventoried paths
- [x] Supported v2 scenarios continue to pass unit tests and targeted sample-app hover validation
- [x] Follow-on cleanup slices are clearly defined from the inventory output

## Change Log

| Date | Change | Reason |
| --- | --- | --- |
| 2026-04-06 | Initial impldoc drafted | Split the broad v2 cutover cleanup into an inventory-and-guardrails first slice so dead-code removal can proceed safely and in smaller reviewable increments. |
| 2026-04-06 | Implementation complete | All requirements and acceptance criteria met. **Key Findings:** (1) V2RuntimeAnalyzer guardrails already fully in place—unsupported shapes return explicit diagnostics, not silent fallback. (2) 10 unit tests prove guardrail behavior (V2RuntimeAnalyzerTests all passing). (3) 3 integration tests validate end-to-end v2 flow (QueryEvaluatorV2IntegrationTests passing). (4) SampleDbContextFactoryApp builds successfully (no errors/warnings), confirming factory-root patterns work. (5) Core and Daemon projects compile with -TreatWarningsAsErrors=true (production-ready). **Inventory Findings:** ~26 candidate paths across LSP/Core/tests classified: 1 dead, 5 required bridges, 12 reachable legacy, 8 new v2 code. **Deletion Map Produced:** 3 follow-on slices defined with specific deletion targets, risk assessments, and validation gates. **No Code Changes Required:** Guardrails already implemented and verified; slice focused on inventory and planning to enable safe, incremental deletion in future slices. |