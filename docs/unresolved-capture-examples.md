# Unresolved Capture Examples

This guide explains query capture shapes that can be difficult to resolve during SQL preview extraction.
The default policy is resolution-first: try hard to infer, evaluate, and bind values before rejecting.

For each case:

- The example shows a LINQ query context
- The reason explains what can block resolution
- The expected behavior describes how EF QueryLens should attempt resolution first, then log and reject only if impossible

## Resolution-First Policy

Before classifying a capture as unresolved, the engine should try these steps in order:

1. Infer expected type from the query expression (for example, the right side of `==`, `Contains`, `StartsWith`, or comparison operators).
2. Reconstruct symbol path and evaluate simple member chains without side effects.
3. Run symbolic evaluation for branch/loop-dependent locals when the control flow can be reduced deterministically.
4. Evaluate only side-effect-free, synchronous initializers when inputs are resolvable.
5. For async/runtime-dependent values, bind predetermined typed defaults that preserve SQL predicate semantics.
6. Reject with clear diagnostics only when no safe/accurate resolution path exists.

## Universal Variable Rule

For any captured variable used in a query expression:

1. Identify the variable and infer expected type from query usage.
2. Try concrete resolution first when it is safe and deterministic.
3. If concrete resolution is unavailable, bind a predetermined typed default.
4. Use defaults that preserve predicate semantics (for example, avoid null defaults that collapse conditions unexpectedly).
5. Reject only when neither concrete value nor predetermined typed default can preserve intended query semantics.

## Predetermined Default Catalog Policy

Use this order when synthesizing variable values:

1. Core-type catalog first:
    - `string`, `bool`, `byte`, `short`, `int`, `long`, `float`, `double`, `decimal`
    - `DateTime`, `DateOnly`, `TimeOnly`, `Guid`
    - enums and all nullable variants of core types
2. For non-catalog types, infer deterministic typed defaults from expected expression type shape.
3. For collections (`T[]`, `List<T>`, `IEnumerable<T>`, set-like collections), seed at least two deterministic items.

Why two items for collections:

- Reduces translation distortion for `Contains`, `Any`, `All`, and count-sensitive predicates.
- Avoids empty/singleton artifacts that can produce unrealistic SQL behavior.

## Canonical Default Values (v1)

Use these canonical defaults unless expected-type inference requires a safer equivalent.

| Type | Canonical default | Notes |
| --- | --- | --- |
| `string` | `"qlstub0"` | Non-null sentinel to avoid null-collapsed predicates. |
| `bool` | `true` | Avoids always-false guard artifacts in many filters. |
| `byte` | `1` | Deterministic non-zero scalar. |
| `short` | `1` | Deterministic non-zero scalar. |
| `int` | `1` | Deterministic non-zero scalar. |
| `long` | `1L` | Deterministic non-zero scalar. |
| `float` | `1.0f` | Deterministic non-zero scalar. |
| `double` | `1.0d` | Deterministic non-zero scalar. |
| `decimal` | `1.0m` | Deterministic non-zero scalar. |
| `DateTime` | `DateTime.UtcNow` | Uses current UTC time for date-sensitive predicates. |
| `DateOnly` | `DateOnly.FromDateTime(DateTime.UtcNow)` | Uses current UTC date (`Today`-style). |
| `TimeOnly` | `TimeOnly.FromDateTime(DateTime.UtcNow)` | Uses current UTC time-of-day (`Now`-style). |
| `Guid` | `Guid.Parse("11111111-1111-1111-1111-111111111111")` | Deterministic non-empty GUID. |
| `enum` | First declared enum member | Deterministic enum seed. |
| `Nullable<T>` | Non-null canonical default of `T` when semantic-safe | Prefer non-null for predicate fidelity. |
| `T[]` | Two-item array seed | Example: for `int[]` use `[1, 2]`; for `string[]` use `["qlstub0", "qlstub1"]`. |
| `List<T>` | Two-item list seed | Same seed strategy as arrays. |
| `IEnumerable<T>` | Two-item enumerable seed | Materialize as deterministic array/list then expose enumerable. |
| Set-like collections | Two-item unique seed | Ensure item uniqueness when required by set semantics. |

Fallback for non-catalog types:

- Infer target type from expression usage and build deterministic instance with safe defaults.
- If object construction is not safe/possible, emit unresolved diagnostic with category and reason.

## 1. Closure Chain Not Materializable

Category: `closure-chain-not-materializable`

```csharp
public IQueryable<Order> Build(AppDbContext db, Request req)
{
    return db.Orders.Where(o => o.TenantId == req.AuthContext.CurrentTenant.Id);
}
```

Why unresolved:

- The nested closure path may include runtime-only objects or lazy members that cannot be safely materialized in extraction context.

Expected behavior:

- First try to resolve the full member chain and infer expected type from `o.TenantId`.
- If chain evaluation succeeds, synthesize a typed local value and continue translation.
- If chain evaluation fails, emit unresolved diagnostic with symbol path `req.AuthContext.CurrentTenant.Id` and concrete failure reason.

## 2. Non-Deterministic Source

Category: `nondeterministic-source`

```csharp
var now = DateTime.UtcNow;
var q = db.Orders.Where(o => o.CreatedAt <= now);
```

Why unresolved:

- `DateTime.UtcNow` is volatile across repeated evaluations, but it is still usable as a captured point-in-time value.

Expected behavior:

- Resolve once per evaluation request and bind as a typed local value.
- Mark capture metadata as `volatile-time` for cache/debug visibility.
- Reject only if the volatile value cannot be safely represented in generated stubs.

## 3. Side-Effectful Initializer

Category: `side-effectful-initializer`

```csharp
var token = tokenService.GetFreshToken();
var q = db.Users.Where(u => u.Token == token);
```

Why unresolved:

- Evaluating initializer requires calling external code with possible side effects.

Expected behavior:

- Try pure-call detection first.
- If initializer is proven pure and inputs are resolvable, evaluate and bind `token` as typed local value.
- If purity cannot be proven, emit unresolved diagnostic for method-call initializer capture with reason `unsafe-evaluation-side-effects`.

## 4. Unsupported Expression Form

Category: `unsupported-expression-form`

```csharp
dynamic d = GetDynamicFilter();
var q = db.Orders.Where(o => o.Status == d.StatusValue);
```

Why unresolved:

- Dynamic binding cannot be safely resolved through static semantic analysis.

Expected behavior:

- Infer expected type from `o.Status` and try to materialize `d.StatusValue` through safe runtime binding.
- If safe binding succeeds, synthesize typed local value and continue.
- If binding remains ambiguous, emit unresolved diagnostic tagged `dynamic-binding` with node/symbol details.

## 5. Generic Type Ambiguity

Category: `generic-type-ambiguity`

```csharp
T value = ResolveGeneric<T>();
var q = db.Set<Entity>().Where(e => e.Payload == value);
```

Why unresolved:

- Runtime generic instantiation may not be known or safely reconstructible at extraction time.

Expected behavior:

- Infer expected type from `e.Payload` and attempt generic substitution from available semantic context.
- If substitution succeeds, bind typed local and continue translation.
- If type remains ambiguous, emit unresolved diagnostic with unresolved generic argument list and concrete type gap.

## 6. Control-Flow Dependent Value

Category: `control-flow-dependent-value`

```csharp
string term;
if (request.UseShort)
{
    term = request.ShortTerm;
}
else
{
    term = request.LongTerm;
}

var q = db.Customers.Where(c => c.Name.Contains(term));
```

Why unresolved:

- Value depends on branch path and current symbolic evaluator may not prove a single stable value.

Expected behavior:

- Run path-sensitive symbolic evaluation using the same inputs available at extraction time.
- If a concrete branch is determinable, use that value.
- If branch cannot be determined, try a typed default only when semantic intent remains valid for translation.
- Otherwise emit unresolved diagnostic tagged `branch-merge-unresolved` with branch source details.

## 7. Async Continuation State Dependency

Category: `async-state-dependent`

```csharp
var term = await searchService.GetTermAsync(ct);
var q = db.Customers.Where(c => c.Email.StartsWith(term));
```

Why unresolved:

- Value is produced by async execution state that is not replayed in extraction-only context.

Expected behavior:

- Identify `term` as a captured variable and infer expected type from `c.Email.StartsWith(term)`.
- Do not execute async sources in extraction path.
- Bind a predetermined typed default value for the variable (for example, non-null string sentinel for string predicates).
- Reject only when no predetermined typed value can preserve intended predicate semantics.

## 8. Reflection or Dynamic Invocation

Category: `reflection-or-dynamic-invocation`

```csharp
var prop = typeof(Customer).GetProperty(request.FieldName)!;
var value = prop.GetValue(request.Filter)!;
var q = db.Customers.Where(c => c.Name == (string)value);
```

Why unresolved:

- Reflection-driven member/value resolution depends on runtime metadata and object state.

Expected behavior:

- If reflection target and member name are determinable and safe, resolve value and bind typed local.
- If reflection input is non-deterministic or unsafe, emit unresolved diagnostic for reflection-based capture.
- Log unresolved reflection target/member details.

## 9. Cross-Assembly Visibility Limit

Category: `cross-assembly-visibility-limit`

```csharp
var term = ExternalLib.HiddenFilters.BuildTerm(request);
var q = db.Customers.Where(c => c.Name.Contains(term));
```

Why unresolved:

- Required initializer internals are inaccessible (missing source, restricted metadata, or internal visibility).

Expected behavior:

- Attempt full-solution and metadata-based resolution first.
- If symbol body remains inaccessible after all supported lookup paths, emit unresolved diagnostic with assembly/symbol references.
- Log reason `initializer-not-observable`.

## 10. Translation-Semantics Unverifiable

Category: `translation-semantics-unverifiable`

```csharp
var comparer = StringComparer.CurrentCultureIgnoreCase;
var q = db.Customers.Where(c => comparer.Equals(c.Name, request.Name));
```

Why unresolved:

- Capture can be obtained, but the rewritten expression semantics cannot be verified as equivalent for translation.

Expected behavior:

- Attempt semantic rewrite to translatable form first.
- If equivalence cannot be proven, emit deterministic rejection diagnostic with semantic-verification tag.
- Log provider context and unsupported semantic construct for follow-up implementation.

## Suggested Diagnostic Fields

When a capture is unresolved, include these fields in logs and diagnostics:

- `code`: stable diagnostic code
- `category`: one of the categories above
- `symbolName`: captured symbol identifier
- `symbolPath`: full member path when available
- `sourceLocation`: file, line, and character
- `reason`: short machine-readable reason
- `message`: human-readable explanation
- `suggestedFix`: optional actionable guidance
- `provider`: optional provider context for translation-risk cases

## Quick Interpretation Guide

- If category is deterministic and common, add binder support with tests.
- For any variable capture, prefer concrete value when safe; otherwise bind predetermined typed default.
- For collection defaults, always use at least two deterministic items.
- If category is unsafe due to side effects or unverifiable semantics, keep deterministic rejection with explicit reason.
