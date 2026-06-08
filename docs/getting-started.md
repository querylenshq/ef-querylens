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

## 3) Set up QueryLens for your project

Build your EF Core solution first, then run **Set up QueryLens**. QueryLens scans your `AddDbContext` registrations, copies the provider configuration into a gitignored generated factory, and updates `.gitignore` so nothing is committed.

### In the IDE (recommended)

1. Open a `.cs` file in your executable host project (API, worker, or console app).
2. Hover an EF Core LINQ query.
3. If setup is needed, click **Set up QueryLens for this project** in the hover, or run the command **EF QueryLens: Setup** from the command palette.
4. Rebuild the host project so the generated factory is compiled locally.

### From the CLI

```bash
dotnet run --project src/EFQueryLens.Cli -- setup path/to/YourHost.csproj
```

If your solution has multiple executable hosts, pass `--host path/to/Host.csproj`. See [cli-reference.md](cli-reference.md) for full options.

See [factory-setup.md](factory-setup.md) for placement rules, multi-DbContext behavior, and manual factory authoring when you need full control.

## 4) Verify first hover

1. Open a `.cs` file with an EF Core LINQ query.
2. Hover a query expression.
3. Confirm SQL preview appears.
4. Try copy sql and open sql actions.

## Troubleshooting

- If no preview appears, rebuild your solution and reopen the file.
- If setup fails with "build the project first", compile the executable host and run setup again.
- If actions fail, inspect plugin logs (`efquerylens` category).
- If provider SQL looks wrong, re-run setup with `--provider` or validate your `AddDbContext` configuration.

## Performance Tuning

- Shadow cache location:
	- Set `QUERYLENS_SHADOW_ROOT` if you want the shadow assembly cache on a faster or larger drive.

- DbContext pool concurrency:
	- Start with `QUERYLENS_DBCONTEXT_POOL_SIZE=1`.
	- Increase to `2`, then `4`, only after validating stable SQL output under concurrent hover requests.
	- If behavior becomes inconsistent, revert to `1` and review DbContext factory statefulness.
