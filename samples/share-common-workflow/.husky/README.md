# Husky Configuration

This directory contains centralized git hooks and CSX validation scripts.

## Contents

- **Hooks**: `commit-msg`, `pre-commit`, `pre-push`
- **Validation Scripts**: `csx/` folder containing CSX lint and validation scripts
- **Task Runner**: `task-runner.json` for Husky.Net task definitions
- **Support**: `_/` folder with Husky runtime files

## Distribution

The entire `.husky/` folder is copied directly from this repo to all consuming repos using:

```powershell
# From readme/setup/
.\sync-all-husky-scripts.ps1 -Action init    # First time
.\sync-all-husky-scripts.ps1 -Action update  # Updates
.\sync-all-husky-scripts.ps1 -Action status  # Check sync status
```

## Making Changes

1. Edit hooks or CSX scripts in this directory
2. Commit changes: `git add .husky/ && git commit -m "..."`
3. Distribute to all repos: `cd ..\..\readme\setup && .\sync-all-husky-scripts.ps1 -Action update`

## Running Validation Locally

```powershell
# Commit message validation
dotnet script .husky/csx/commit-lint.csx -- <commit-msg-file>

# Branch name validation
dotnet script .husky/csx/branch-lint.csx

# Production release validation
dotnet script .husky/csx/prod-rel-validate.csx

# Dev release validation
dotnet script .husky/csx/dev-relase-lint.csx
```

## Task Runner

Uses Husky.Net task definitions in `task-runner.json`. Tasks can be viewed and executed via the Husky.Net CLI.

For more info, see [readme/setup/README-global-scripts.md](../../readme/setup/README-global-scripts.md).
