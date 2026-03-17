# Getting Started

This guide gets EF QueryLens running against a local EF Core project.

## Prerequisites

- .NET SDK 10+
- A C# project that uses EF Core
- One supported IDE plugin:
  - VS Code plugin (`src/Plugins/ef-querylens-vscode`)
  - Rider plugin (`src/Plugins/ef-querylens-rider`)
  - Visual Studio plugin (`src/Plugins/ef-querylens-visualstudio`)

## 1) Build QueryLens

```bash
dotnet build EFQueryLens.slnx
```

## 2) Build your selected IDE plugin

### VS Code

```bash
npm ci --prefix src/Plugins/ef-querylens-vscode
npm run compile --prefix src/Plugins/ef-querylens-vscode
```

### Rider

```bash
cd src/Plugins/ef-querylens-rider
./gradlew build
```

### Visual Studio

```bash
dotnet build src/Plugins/ef-querylens-visualstudio/EFQueryLens.VisualStudio/EFQueryLens.VisualStudio.csproj -c Debug
```

## 3) Add a QueryLens DbContext factory

Add `IQueryLensDbContextFactory<TContext>` in your executable startup project so QueryLens can construct your DbContext reliably.

```csharp
using EFQueryLens.Core;

public sealed class QueryLensDbContextFactory : IQueryLensDbContextFactory<AppDbContext>
{
	public AppDbContext Create()
	{
		var options = new DbContextOptionsBuilder<AppDbContext>()
			.UseSqlServer("<connection-string>")
			.Options;

		return new AppDbContext(options);
	}
}
```

See [factory-setup.md](factory-setup.md) for placement rules and multi-DbContext guidance.

## 4) Verify first hover

1. Open a `.cs` file with an EF Core LINQ query.
2. Hover a query expression.
3. Confirm SQL preview appears.
4. Try copy sql and open sql actions.

## Troubleshooting

- If no preview appears, rebuild your solution and reopen the file.
- If actions fail, inspect plugin logs (`efquerylens` category).
- If provider SQL looks wrong, validate your project uses a supported EF Core provider.

## Performance Tuning

- Shadow cache location:
	- Set `QUERYLENS_SHADOW_ROOT` if you want the shadow assembly cache on a faster or larger drive.

- DbContext pool concurrency:
	- Start with `QUERYLENS_DBCONTEXT_POOL_SIZE=1`.
	- Increase to `2`, then `4`, only after validating stable SQL output under concurrent hover requests.
	- If behavior becomes inconsistent, revert to `1` and review DbContext factory statefulness.
