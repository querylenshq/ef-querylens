# CLI Reference

The CLI host is `EFQueryLens.Cli`.

## Commands

### `setup`

Generate the offline QueryLens DbContext factory for a project. This is the same flow as **Set up QueryLens** in the IDE: scan `AddDbContext` registrations, detect the EF Core provider, write a gitignored factory under `Properties/QueryLens/`, and append the ignore rule to `.gitignore`.

```bash
dotnet run --project src/EFQueryLens.Cli -- setup <projectPath> [--host <csprojPath>] [--provider SqlServer|Npgsql|MySql|Sqlite] [--force]
```

Arguments and options:

| Name | Required | Description |
|------|----------|-------------|
| `projectPath` | yes | Path to a `.csproj`, project directory, or `.sln`/`.slnx` file |
| `--host` | no | Executable host `.csproj` when the path is a solution or class library |
| `--provider` | no | Override provider auto-detection (`SqlServer`, `Npgsql`, `MySql`, `Sqlite`) |
| `--force` | no | Overwrite a hand-edited generated factory file |

Examples:

```bash
# Executable web/API project
dotnet run --project src/EFQueryLens.Cli -- setup samples/SampleMySqlApp/SampleMySqlApp.csproj

# Solution with multiple hosts — list hosts, then pass --host
dotnet run --project src/EFQueryLens.Cli -- setup MyApp.sln --host src/MyApp.Api/MyApp.Api.csproj

# Force regeneration after manual edits to the generated file
dotnet run --project src/EFQueryLens.Cli -- setup src/MyApp.Api --force
```

Exit codes:

- `0` — setup succeeded (`Generated`, `SkippedUpToDate`, or `SkippedExistingFactory`)
- `1` — setup failed (missing build output, ambiguous host, provider unknown, etc.)
- `2` — invalid `--provider` value

Output includes the setup message, generated file path (when applicable), and discovered DbContext type names.

Planned command set:

- `translate`
- `explain`
- `diff`

As command contracts become stable, this page will include complete argument and output schemas for those verbs.

## Runtime Environment Variables

- `QUERYLENS_SHADOW_ROOT`: Optional override for the shadow assembly cache root directory.
	- Default: `%LOCALAPPDATA%/EFQueryLens/shadow` (or platform-equivalent local app data path)
	- Use this when you want cache data on a different drive.
	- Example (Windows): `D:\QueryLensCache\shadow`
	- Example (Linux/macOS): `/data/querylens/shadow`

- `QUERYLENS_DBCONTEXT_POOL_SIZE`: Maximum number of warm DbContext instances per `(assembly path, DbContext type)` pool.
	- Default: `4`
	- Bounds: `1` to `16`
	- Use `1` to force serialized access and minimize shared-state risk.
	- Use `2-4` to improve throughput for concurrent hover/preview requests.

## Pool Rollout Notes

When enabling pooled concurrency in an existing workspace, use a staged rollout:

1. Start with `QUERYLENS_DBCONTEXT_POOL_SIZE=1` for baseline stability.
2. Move to `2` and monitor for behavior drift in generated SQL.
3. Increase to `4` only when your factory and DbContext configuration are verified as stateless per request.

If you observe inconsistent results between identical hover requests, temporarily set pool size back to `1` and check for mutable state in custom factory setup or DbContext configuration hooks.
