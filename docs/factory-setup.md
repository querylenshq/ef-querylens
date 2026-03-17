# Factory Setup

EF QueryLens discovers DbContext construction through `IQueryLensDbContextFactory<TContext>`.

## Placement Rule

Place QueryLens factory implementations in an executable startup project:

- API host
- Worker service
- Console app

Do not place QueryLens factories only in a class library if the executable is elsewhere.

## Why

QueryLens resolves dependencies from executable output boundaries and expects startup-level resolution context.

## Multiple DbContexts

You can support multiple DbContexts using either pattern:

1. One factory class per DbContext type.
2. One class implementing multiple `IQueryLensDbContextFactory<TContext>` interfaces.

## Example

```csharp
using EFQueryLens.Core;

public sealed class QueryLensFactory :
    IQueryLensDbContextFactory<AppDbContext>,
    IQueryLensDbContextFactory<AuditDbContext>
{
    AppDbContext IQueryLensDbContextFactory<AppDbContext>.Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("<connection-string>")
            .Options;

        return new AppDbContext(options);
    }

    AuditDbContext IQueryLensDbContextFactory<AuditDbContext>.Create()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlServer("<connection-string>")
            .Options;

        return new AuditDbContext(options);
    }
}
```

If QueryLens cannot disambiguate context type from the query location, specify context explicitly in command-host flows where supported.
