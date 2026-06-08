# Factory Setup

EF QueryLens discovers DbContext construction through `IQueryLensDbContextFactory<TContext>`.

## Primary path: Set up QueryLens

The default workflow is **Set up QueryLens** (IDE command or CLI `setup`). QueryLens:

1. Scans the solution for `AddDbContext` / `AddDbContextPool` / `AddDbContextFactory` registrations.
2. Copies each context's provider option chain, swapping only the connection string for an offline placeholder.
3. Writes `Properties/QueryLens/QueryLensDbContextFactory.g.cs` in the executable host project.
4. Appends `Properties/QueryLens/` to `.gitignore`.

The generated file declares `IQueryLensDbContextFactory<T>` locally (no NuGet package reference) and uses reflective construction so DbContexts with extra constructor parameters still work offline.

After setup, rebuild the host project. The factory is compiled locally but never committed.

### IDE

- Command palette: **EF QueryLens: Setup**
- Hover link when SQL preview needs setup

When your current file belongs to a class library, QueryLens prompts for the executable host (API, worker, or console) that should own the generated factory.

### CLI

```bash
dotnet run --project src/EFQueryLens.Cli -- setup path/to/Host.csproj [--host path/to/Host.csproj] [--provider MySql] [--force]
```

See [cli-reference.md](cli-reference.md) for details.

## Placement Rule

Place the generated factory in an executable startup project:

- API host
- Worker service
- Console app

Do not place QueryLens factories only in a class library if the executable is elsewhere. Registration scanning may find `AddDbContext` in an infrastructure library, but the generated file always lives in the chosen host.

## Why

QueryLens resolves dependencies from executable output boundaries and expects startup-level resolution context.

## Multiple DbContexts

Setup generates one factory class per detected DbContext in a single gitignored file. Each class implements `IQueryLensDbContextFactory<TContext>` for its context and carries the rewritten options chain from that context's registration.

## Advanced: manual factory authoring

Use a hand-written factory when you need custom offline behavior that setup cannot infer (for example, options built from helpers or environment-specific branches that are not visible in the registration lambda).

Place manual factories in the executable host project using either pattern:

1. One factory class per DbContext type.
2. One class implementing multiple `IQueryLensDbContextFactory<TContext>` interfaces.

Example:

```csharp
using EFQueryLens.Core;

public sealed class QueryLensFactory :
    IQueryLensDbContextFactory<AppDbContext>,
    IQueryLensDbContextFactory<AuditDbContext>
{
    AppDbContext IQueryLensDbContextFactory<AppDbContext>.CreateOfflineContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("<connection-string>")
            .Options;

        return new AppDbContext(options);
    }

    AuditDbContext IQueryLensDbContextFactory<AuditDbContext>.CreateOfflineContext()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlServer("<connection-string>")
            .Options;

        return new AuditDbContext(options);
    }
}
```

If a hand-written factory already exists, setup skips generation. Use `--force` on the CLI only to overwrite the generated file after manual edits to `QueryLensDbContextFactory.g.cs`, not to replace an intentional hand-written factory.

If QueryLens cannot disambiguate context type from the query location, specify context explicitly in command-host flows where supported.
