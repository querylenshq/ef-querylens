# CLI Reference

The CLI host is `EFQueryLens.Cli`.

Current state:

- Command surface is being stabilized
- IDE integrations are currently the primary production path

Planned command set:

- `translate`
- `explain`
- `diff`

As command contracts become stable, this page will include complete argument and output schemas.

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
