# Hover Generation — Speed & Robustness (first cut)

## Context

SQL generation works end-to-end (LSP detects a LINQ chain → `TranslationRequestBuilder` →
daemon `/translate` → engine compiles & captures SQL → multi-layer cache). Two classes of
problem remain:

- **Speed/UX**: the first hover on an un-warmed query always returns a "computing…" placeholder
  and the user must hover again — the adaptive re-check runs only in the background task, not in
  the hover response path.
- **Robustness**: the daemon passes `CancellationToken.None` to the engine (a pathological query
  can wedge a DbContext-pool slot with no timeout), and only `Success` results are cached, so a
  query that always fails is fully recompiled on every hover and every warm sweep. A warm storm
  (file opened with many queries) can also spawn unbounded concurrent compilations.

This plan covers the agreed "first cut": daemon timeout/cancellation, negative-result caching,
interactive-vs-warm concurrency fairness, and a bounded synchronous hover wait. Generation logic
itself is untouched.

## Scope / non-goals

- In scope: daemon request handling (`Program.cs`), hover response path (`HoverHandler`), small
  config knobs, tests.
- Out of scope: changing the engine's compile/capture pipeline, batch-warm endpoint, document
  versioning, stale-build detection (tracked separately as follow-ups).

## Work items

### 1. Daemon translation timeout + real cancellation  *(robustness)*
- File: `src/EFQueryLens.Daemon/Program.cs` (`/translate`, `/translate/warm`).
- Replace `CancellationToken.None` with a token from a `CancellationTokenSource` created per
  request, cancelled after `QUERYLENS_TRANSLATE_TIMEOUT_MS` (default 15000, min 1000, max 120000).
- On timeout: stop awaiting, remove the key from `inflight`, return a failure result with a clear
  "translation timed out" message and `DaemonUnavailable` status. Do **not** cache as success.
- Note: the engine may not honor cancellation internally; the wrapper still returns promptly via
  `Task.WhenAny(lazy.Value, timeoutTask)`. Pair with item 3 so an orphaned slow task can't starve
  interactive work.
- Tests: a fake engine that delays beyond the timeout → daemon returns a timeout failure quickly;
  a fast engine → unaffected.

### 2. Negative-result caching  *(robustness + speed)*
- File: `src/EFQueryLens.Daemon/Program.cs`.
- When `!result.Success`, cache in-memory only (never SQLite) with a short TTL
  `QUERYLENS_NEGATIVE_CACHE_TTL_MS` (default 5000, min 0 = disabled, max 60000).
- Keep the existing 60s success TTL + SQLite persistence for successes.
- Short TTL so a transient failure recovers; fingerprint is already in the key so a rebuild busts it.
- Tests: fake engine returns failure → second call within TTL hits cache (engine invoked once);
  after TTL (or with TTL=0) it re-invokes.

### 3. Interactive-vs-warm concurrency fairness  *(speed under load + robustness)*
- File: `src/EFQueryLens.Daemon/Program.cs`.
- Global engine gate `SemaphoreSlim(QUERYLENS_MAX_CONCURRENT_TRANSLATIONS)` (default
  `max(2, ProcessorCount/2)`) around every engine translation.
- Warm path additionally acquires a smaller warm gate
  `SemaphoreSlim(QUERYLENS_MAX_CONCURRENT_WARM)` (default 2) **before** the engine gate, leaving
  headroom so interactive `/translate` is never blocked behind a warm storm.
- Interactive `/translate` takes only the engine gate.
- Tests: with a controllable engine that records peak concurrency, assert warm never exceeds the
  warm cap; assert an interactive request proceeds while warm work is saturated.

### 4. Bounded synchronous hover wait  *(speed/UX — the visible win)*
- File: `src/EFQueryLens.Lsp/Handlers/HoverHandler.MarkdownHover.cs`.
- On a cache miss, after kicking the background compute, `await` it up to
  `QUERYLENS_HOVER_WAIT_BUDGET_MS` (default 500, min 0 = current behavior, max 5000). If it
  finishes `Ready` within budget, return the real SQL hover; otherwise return the InQueue
  placeholder as today.
- Keep the `TryCacheEntryInQueue` race guard so only one task is started; concurrent hovers join.
- Tests: fake preview service completing under budget → first hover returns Ready SQL; completing
  over budget → returns InQueue.

### 5. (Optional) Lightweight counters  *(measurement)*
- Cache hit/miss counts (LSP + daemon + SQLite), translate p50/p95, warm vs interactive counts.
- Minimal: increment counters + a `/stats` daemon endpoint (or debug log line). Lets us confirm
  the above actually move the numbers. Do last; skip if time-boxed.

## Config knobs added
| Env var | Default | Item |
|---|---|---|
| `QUERYLENS_TRANSLATE_TIMEOUT_MS` | 15000 | 1 |
| `QUERYLENS_NEGATIVE_CACHE_TTL_MS` | 5000 | 2 |
| `QUERYLENS_MAX_CONCURRENT_TRANSLATIONS` | max(2, cores/2) | 3 |
| `QUERYLENS_MAX_CONCURRENT_WARM` | 2 | 3 |
| `QUERYLENS_HOVER_WAIT_BUDGET_MS` | 500 | 4 |

## Verification
- Unit tests per item (daemon items are self-contained and testable with a fake `IQueryLensEngine`;
  hover item with a fake `HoverPreviewService`/engine).
- Full `dotnet test EFQueryLens.Core.Tests` green.
- Manual: open a query-dense sample file, confirm first hover shows SQL within budget, confirm a
  deliberately-broken query doesn't recompile on every hover (negative cache), and that opening a
  big file doesn't stall an immediate hover (fairness).

## Sequencing
Daemon items 1 → 2 → 3 first (self-contained, same file, easy to test together), then LSP item 4,
then optional item 5. Build + test after each.
