# Rider Query Preview Parity Stabilization

## Overview

Stabilize Rider SQL preview behavior so it is consistent with Visual Studio and VS Code when hovering or invoking actions on query-producing statements. The feature goal is query-triggered extraction parity: if a statement contains a LINQ chain or a helper invocation that resolves to a LINQ chain, QueryLens should extract and execute the outer SQL shape regardless of token-level cursor variance.

## Scope Boundaries

**Feature context**: Slice 1 of cross-IDE query preview parity

**This slice delivers**:

- Server-side cursor canonicalization and extraction relevance updates for statement-level query triggers
- Rider split interaction model: hover is preview-only, while Alt+Enter intention menu exposes all query actions
- Deterministic hover/action feedback for extraction failures (no silent Ready-with-empty-preview ambiguity)
- Regression coverage for helper-invocation declarations and direct LINQ chains across common cursor anchors

**Out of scope / deferred**:

- New user settings for cursor behavior
- Broad UX redesign of tool windows or popup layouts
- Performance optimization beyond parity/stability correctness

**Depends on**: Existing v2 extraction/runtime work (`v2-production-wiring-p9`, `v2-parity-extraction-k4`) remains in progress and must stay behavior-compatible

## Requirements

- [ ] Query-triggered extraction contract: if cursor is within a statement that contains a query-producing invocation, extraction resolves and executes the outer SQL expression
- [ ] Cursor normalization must be IDE-agnostic and not rely on Rider-only token assumptions
- [ ] Rider hover must remain preview-only (no action links)
- [ ] Rider intention menu must expose the full action set for query statements (preview popup, open SQL, copy SQL, reanalyze)
- [ ] Hover/action behavior must match VS/VS Code outcomes for equivalent query statements
- [ ] Extraction failures must return explicit reason text/diagnostics instead of silent no-preview success
- [ ] Existing direct LINQ chain scenarios must not regress

## Design Decisions

### Decision 1: Query-Triggered Contract Over Token-Triggered Contract

**Choice:** Treat statement query presence as the trigger condition, not exact cursor token kind.

**Rationale:** Rider, VS, and VS Code can place hover/action requests on different tokens for the same user intent. Token-specific contracts cause avoidable cross-IDE drift.

**Alternatives considered:**

- Token whitelist as mandatory gate (`var`, identifier, `=`, `return`, `;`, method token) — rejected because it is brittle and does not represent user intent
- Invocation-span-only requirement — rejected because Rider commonly anchors outside invocation spans

### Decision 2: Split Surface in Rider (Preview on Hover, Actions in Intentions)

**Choice:** Keep hover for SQL preview only and expose all actionable operations through Alt+Enter intentions.

**Rationale:** This keeps Rider behavior predictable and uncluttered: passive hover for reading SQL, explicit intention menu for operations.

**Alternatives considered:**

- Dual-surface actions (hover links + intentions) — rejected per product direction to avoid duplicate action entry points in Rider
- Hover-only actions — rejected because intention menu is the primary Rider command UX

### Decision 3: Server Owns Canonicalization

**Choice:** Canonical cursor-to-query normalization is implemented in shared LSP logic, not Rider plugin heuristics.

**Rationale:** Shared backend logic keeps parity guarantees uniform across IDE clients and reduces plugin-specific behavior drift.

**Alternatives considered:**

- Rider-only pre-normalization in plugin code — rejected because it duplicates extraction policy and diverges from VS/VS Code

## Implementation Plan

1. Define canonical query-anchor resolution contract in hover request context and semantic context fallback paths.
2. Update extraction relevance logic to accept statement-level anchors for helper invocations and declaration/assignment/return forms.
3. Add explicit failure messaging path for extraction-not-found outcomes to avoid ambiguous successful-empty states.
4. Keep Rider hover output preview-only and remove/suppress action links for Rider client rendering.
5. Align Rider intention availability with statement/query intent while exposing full action menu (preview popup, open SQL, copy SQL, reanalyze).
6. Add regression tests for helper-based and direct-chain queries at representative anchor positions (without token-list gating semantics).
7. Validate parity manually across Rider, VS Code, and Visual Studio on the same sample queries.

## Dependencies

- Shared LSP extraction/hover pipeline in `src/EFQueryLens.Lsp`
- Rider plugin action/URL-opener pipeline in `src/Plugins/ef-querylens-rider`
- Existing command handlers in `LanguageServerHandler.Commands`

## Testing Strategy

### Unit Tests

- Extraction relevance: statement-level anchors resolve helper invocation chains
- Hover canonicalization: request-context normalization routes to same resolved expression for equivalent statement anchors
- No-regression tests for direct invocation-span extraction behavior

### Integration Tests

- Rider command path (`showsqlpopup`, `copysql`, `opensqleditor`, `reanalyze`) succeeds from statement-anchor invocation contexts
- Cross-IDE parity checks for equivalent source locations produce equivalent structured hover outcomes

### Manual Smoke Tests

Steps for `end-to-end-testing.md`:

1. Rider: place caret on declaration anchor for helper-invoked query and hover — expected: SQL preview is shown without action links
2. Rider: with same caret position, press Alt+Enter and run each EF QueryLens action (Preview popup, Open SQL, Copy SQL, Reanalyze) — expected: each action succeeds on outer query SQL
3. VS Code and VS: hover equivalent statement/query and compare SQL payload semantics — expected: parity with Rider output

## Acceptance Criteria

- [ ] Rider behavior is closer to VS/VS Code for query-producing statements (helper and direct-chain cases)
- [ ] Rider hover is preview-only and stable for in-scope query statements
- [ ] Rider Alt+Enter intention menu exposes all in-scope query actions and executes them successfully
- [ ] Query-triggered extraction contract is implemented (statement contains qualifying query -> extraction executes outer SQL)
- [ ] No regression in existing extraction/runtime tests
- [ ] New tests covering parity-critical scenarios are added and passing
- [ ] Documentation updated where behavior contract changed

## Review Findings

_Populated by the implementer after code review, security review, and quality analysis. Only findings that resulted in code changes are recorded here. Deferred items go to `todos.md`. Valid sources: `code-reviewer`, `red-team`, `query-analyzer`, `trivy`, `sonarqube`._

| Source | Finding | Resolution |
| --- | --- | --- |

## Quality Report

_Populated by the implementer after all scans complete. Captures the final quality snapshot for the permanent record._

### Security Scan (Trivy)

| Targets | Vulnerabilities | Secrets | Misconfigurations |
| --- | --- | --- | --- |

Or: _"Security scan skipped — Trivy not installed."_

### Code Quality (SonarQube)

**Quality Gate**: _PASSED / FAILED / Skipped_

| Metric | Value | Threshold | Status |
| --- | --- | --- | --- |

#### Issues Summary

| Type | Count | Top Finding |
| --- | --- | --- |
| Bugs (reliability) | | |
| Vulnerabilities | | |
| Code Smells (maintainability) | | |
| Security Hotspots | | |

Or: _"Code quality analysis skipped — SonarQube not configured."_

## Change Log

| Date | Change | Reason |
| --- | --- | --- |
| 2026-04-05 | Disabled Rider top-of-query CodeLens actions by default and added statement-invocation extraction retry fallback for hover positions | Remove duplicate Rider action entry points and unblock preview when hovering non-anchor characters within query statements |
| 2026-04-05 | Implemented core parity wiring: Rider Preview SQL intention action, statement-anchor normalization fallback, and deterministic structured unavailable responses | Align Rider UX with approved split model (hover preview-only, actions via Alt+Enter) while reducing silent extraction failures |
| 2026-04-05 | Refined Rider interaction model to preview-only hover and full Alt+Enter action menu | Developer clarified desired Rider UX split: read SQL on hover, execute actions from intention menu |
| 2026-04-05 | Initial impldoc drafted | Define parity-focused plan for Rider hover/action consistency with VS and VS Code using query-triggered extraction semantics |
