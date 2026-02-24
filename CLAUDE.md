# QueryLens — EF Core SQL Preview Toolkit

## Project Overview

A .NET library + CLI tool + MCP server that translates EF Core LINQ expressions to SQL without running the app. MySQL/Pomelo provider first. See architecture in `/docs/DESIGN.md`.

## Tech Stack

- .NET 10, C# 12
- `Pomelo.EntityFrameworkCore.MySql` (MySQL provider)
- `Microsoft.EntityFrameworkCore` 9.0.x (pinned to match Pomelo 9.x upper bound)
- `Microsoft.CodeAnalysis.CSharp.Scripting` (Roslyn scripting)
- `ModelContextProtocol` SDK for .NET (MCP server)
- `System.CommandLine` (CLI)
- xUnit + TestContainers (tests)

## Build Commands

```bash
dotnet build
dotnet test
dotnet run --project src/QueryLens.Cli -- translate --help
```

## Project Structure

```
src/
  QueryLens.Core/          ← engine interfaces & records (no provider refs)
  QueryLens.MySql/         ← Pomelo bootstrap + MySQL explain parser
  QueryLens.Postgres/      ← stub (Phase 2)
  QueryLens.SqlServer/     ← stub (Phase 2)
  QueryLens.Cli/           ← dotnet global tool (System.CommandLine)
  QueryLens.Mcp/           ← MCP server (ModelContextProtocol SDK)
  QueryLens.Analyzer/      ← Roslyn analyzer (ships as NuGet to user projects)
tests/
  QueryLens.Core.Tests/
  QueryLens.MySql.Tests/
  QueryLens.Integration.Tests/   ← TestContainers, real MySQL
samples/
  SampleApp/               ← dogfood EF Core project for testing
docs/
  Design.md                ← full architecture document
```

## Architecture Decisions

- Each project assembly loads into its own **isolated, collectible AssemblyLoadContext** — prevents EF Core version conflicts between the tool and user projects
- `ToQueryString()` is the **only** public EF API we depend on — no internals
- MCP server, CLI, and analyzer are thin hosts over `QueryLens.Core`
- All transport-agnostic output flows through the `QueryTranslationResult` record
- `QueryLens.Analyzer` communicates with the engine over a named pipe — it NEVER loads EF Core/Pomelo directly (runs inside VS/Rider process)

## Current Phase

**Phase 1 (active):** `QueryLens.Core` contracts + `QueryLens.MySql` stub
- Target: `ToQueryString()` working against SampleApp's DbContext
- Next up (Session 2): Implement `AssemblyLoadContext` loading in `QueryLens.Core`

## Progress

| Session | Status | What was done |
|---------|--------|---------------|
| 1 | ✅ Done | Solution scaffold, all Phase 1 contracts defined (`IQueryLensEngine`, request/result records, provider interfaces) |
| 2 | ⬜ Next | Implement `ProjectAssemblyContext` — isolated collectible ALC loading user assembly + dependencies |
| 3 | ⬜ | Implement Roslyn scripting sandbox + `ScriptState` cache |
| 4 | ⬜ | Wire up CLI (`translate` command) |
| 5 | ⬜ | Wire up MCP server (`ef_translate` tool) |

## Key Constraints — DO NOT Violate

- **No EF Core internals** — only `ToQueryString()`, never internal query translators or expression visitors
- **No cross-boundary refs** — `QueryLens.Analyzer` must NOT reference `QueryLens.Core` (different process)
- **No provider code in Core** — `QueryLens.Core` stays provider-agnostic
- **ALC isolation is mandatory** — user assemblies always load into their own isolated ALC

## Test MySQL (Docker)

```bash
docker run -d --name querylens-mysql -p 3306:3306 \
  -e MYSQL_ROOT_PASSWORD=querylens \
  -e MYSQL_DATABASE=querylens_test \
  mysql:8.0
```

## Session Prompts (for continuing work)

**Session 2 — AssemblyLoadContext:**
> Implement `ProjectAssemblyContext` in `QueryLens.Core`. It should load a given assembly path + all its dependencies from the same directory into an isolated collectible ALC. Add unit tests. Use the SampleApp in /samples as the test subject.

**Session 3 — Roslyn Sandbox:**
> Implement the Roslyn scripting sandbox in `QueryLens.Core`. It should take a DbContext instance and evaluate a LINQ expression string against it, returning the IQueryable result for `ToQueryString()`. Cache warm `ScriptState` per assembly path.
