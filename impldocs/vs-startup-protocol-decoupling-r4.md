# Visual Studio Startup Protocol Decoupling

## Overview

Decouple the Visual Studio host startup path from direct references to EFQueryLens.Lsp.Protocol so package load does not fail when protocol assembly binding fails. This slice keeps LSP wire payloads and server contracts unchanged, focusing only on Visual Studio in-proc startup resilience.

## Scope Boundaries

**Feature context**: Slice 1 of Visual Studio Host/Protocol Boundary Hardening

**This slice delivers**:

- Visual Studio package initialization no longer requires EFQueryLens.Lsp.Protocol types.
- Initialization options construction in Visual Studio host is protocol-free while preserving the exact current JSON wire shape.
- Startup/status UI path in Visual Studio host is protocol-free and degrades gracefully on server unavailability.

**Out of scope / deferred**:

- Refactoring all Visual Studio notification/message handling away from protocol DTOs _(planned for impldoc vs-protocol-boundary-hardening-<suffix>)_.
- Any protocol schema changes, message name changes, or server-side contract changes.
- Changes to VS Code and Rider host implementations.

**Depends on**: None

## Requirements

- [ ] Visual Studio package startup succeeds without direct EFQueryLens.Lsp.Protocol type loads in the package initialization path.
- [ ] Visual Studio initialization options sent to the LSP server are wire-compatible with the current payload.
- [ ] Visual Studio status bar startup text and transitions continue to work when server connection succeeds.
- [ ] Visual Studio host degrades gracefully when server/protocol issues occur (no package load failure).
- [ ] No code changes are required in VS Code, Rider, or server projects for this slice.

## Design Decisions

### Decision 1: Keep wire compatibility, change host-side model only

**Choice:** Replace protocol-typed startup models in the Visual Studio host startup path with local host-side DTO/anonymous payload construction that serializes to the same JSON contract.

**Rationale:** This removes fragile in-proc assembly coupling while avoiding cross-IDE/server regressions.

**Alternatives considered:**

- Keep direct protocol references and add more packaging/binding rules — rejected because package startup remains fragile and future refactors can reintroduce load-order failures.
- Change shared protocol schema to better match host internals — rejected because it risks VS Code/Rider/server compatibility and expands scope.

### Decision 2: Isolate startup state from protocol DTOs

**Choice:** Introduce host-local startup/status state mapping used by package init and status UI before/without protocol assembly availability.

**Rationale:** Package initialization should only rely on always-available host code. Protocol objects can remain in later runtime paths until follow-on slices complete.

**Alternatives considered:**

- Continue using protocol status snapshot in status manager — rejected due to eager load risk during package initialization.
- Remove startup status text entirely — rejected due to UX regression and reduced diagnostics visibility.

## Implementation Plan

1. Identify and document all Visual Studio package-startup call paths that currently touch EFQueryLens.Lsp.Protocol types.
2. Add host-local startup options/state representation in Visual Studio plugin code with serialization parity to current initialize payload JSON.
3. Refactor package initialization and status startup path to depend only on host-local types.
4. Keep language-client/server interaction payload names and fields unchanged; adapt with local mapping layer where needed.
5. Add/adjust unit tests in Visual Studio plugin test surface (or nearest feasible tests) to verify payload shape parity and startup-state mapping.
6. Build Visual Studio plugin and validate package startup in experimental instance with ActivityLog verification.
7. Record deferred wider decoupling work as a follow-on impldoc candidate.

## Dependencies

- Existing Visual Studio plugin project and package initialization flow.
- Existing LSP server initialize contract shape (must remain unchanged).
- No new external packages required.

## Testing Strategy

### Unit Tests

- Startup options mapper produces expected JSON property names and values.
- Startup/status mapping handles null/missing data without exceptions.
- Package startup helper path does not require protocol types.

### Integration Tests

- Visual Studio plugin build and VSIX generation still succeed.
- LSP initialize handshake still succeeds with unchanged server contract.

### Manual Smoke Tests

Steps for end-to-end-testing.md:

1. Launch Visual Studio experimental instance with extension installed — expected result: package loads without SetSite FileNotFound errors.
2. Open a C# file with LINQ query and trigger extension activation — expected result: no EFQueryLens.Lsp.Protocol FileNotFound entries in ActivityLog.
3. Run Setup/Restart commands from the extension menu — expected result: commands execute and status text updates.

## Acceptance Criteria

- [ ] ActivityLog in experimental instance shows no EFQueryLens.Lsp.Protocol FileNotFound during package startup.
- [ ] Visual Studio package SetSite/Initialize completes successfully.
- [ ] LSP initialize payload remains wire-compatible (field names/structure unchanged).
- [ ] Setup and restart commands continue to function.
- [ ] VS Code and Rider require no changes for this slice.
- [ ] All existing tests pass.
- [ ] New or updated tests for startup decoupling logic pass.
- [ ] Documentation updated (features.md, INDEX.md, roadmap.md, todos.md, end-to-end-testing.md).

## Review Findings

_Populated by the implementer after code review, security review, and quality analysis. Only findings that resulted in code changes are recorded here. Deferred items go to todos.md. Valid sources: code-reviewer, red-team, query-analyzer, trivy, sonarqube._

| Source | Finding | Resolution |
| --- | --- | --- |

## Quality Report

_Populated by the implementer after all scans complete. Captures the final quality snapshot for the permanent record._

### Security Scan (Trivy)

| Targets | Vulnerabilities | Secrets | Misconfigurations |
| --- | --- | --- | --- |

### Code Quality (SonarQube)

**Quality Gate**: Skipped

| Metric | Value | Threshold | Status |
| --- | --- | --- | --- |

#### Issues Summary

| Type | Count | Top Finding |
| --- | --- | --- |
| Bugs (reliability) | | |
| Vulnerabilities | | |
| Code Smells (maintainability) | | |
| Security Hotspots | | |

## Change Log

| Date | Change | Reason |
| --- | --- | --- |
| 2026-06-11 | Initial impldoc draft created | Define a minimal, wire-compatible decoupling slice for Visual Studio startup resilience |
