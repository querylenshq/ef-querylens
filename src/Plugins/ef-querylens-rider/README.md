# EF QueryLens Rider Plugin

This project is a starter JetBrains Rider plugin that will run `EFQueryLens.Lsp` via Rider LSP support.

## Status

MVP LSP wrapper in place.

## Local dev

From `src/Plugins/ef-querylens-rider`:

1. `./gradlew build`
2. `./gradlew runIde`

Current plugin behavior:

- Registers an LSP support provider for `*.cs` files and starts `EFQueryLens.Lsp.dll` when a C# file is opened.
- **Hover SQL Preview:** Hover over a LINQ/EF query to view generated SQL and use the actions: **copy sql**, **open sql**, and **refresh**.

## Debugging (logs)

To see why **copy sql / open sql / refresh** links or the **hover highlight** might not work:

1. **Open the log:** **Help → Diagnostic Tools → Debug Log Settings** (or **Open log in Explorer**), or run Rider from a terminal and watch stdout.
2. **Enable EF QueryLens logs:** In Debug Log Settings, add a logger for `efquerylens` (or the plugin’s package). Then reproduce: open a C# file with a LINQ query, hover to open SQL preview, then click "copy sql" or "open sql".
3. **What to look for:**
   - **`[EFQueryLens] resolveLink called: url=...`** — If this never appears when you click a link, the IDE is not passing doc popup link clicks to our handler (links may be disabled or handled elsewhere).
   - **`[EFQueryLens] Intercepted link: ...`** — Our handler accepted the link; next line should be command execution or **`[EFQueryLens] Failed to handle link:`** with an error.
   - **`[EFQueryLens] applyHighlights: N entries`** — Confirms highlights are applied; if N is 0 for that file, the blue hover highlight won’t show.

## Troubleshooting: file lock / "another process has locked" warnings

When running Rider via `runIde`, you may see warnings like:

- `FileBasedIndexImpl` or `CachedFileContent`: "The process cannot access the file because it is being used by another process"
- Paths under `.intellijPlatform/sandbox/RD-2025.3/` (config, system caches, etc.)

These come from the IDE’s index and caches, not from the EF QueryLens plugin. They usually mean the same sandbox is in use by more than one process (e.g. two Rider runs, or Rider plus a leftover backend).

**Workaround:**

1. Close all Rider windows and, if needed, end any leftover Rider/Java processes (Task Manager).
2. Delete the sandbox once so the next run starts clean:
   ```powershell
   Remove-Item -Recurse -Force ".\\.intellijPlatform\\sandbox"
   ```
3. Start a single Rider from the plugin:
   ```powershell
   .\gradlew runIde --no-daemon
   ```
4. Avoid running `runIde` again until you’ve closed the Rider instance that’s already using the sandbox.

## Prerequisites

- Build the packaged runtime inputs first so the Rider plugin can bundle and launch them:
  - `dotnet build src/EFQueryLens.Lsp/EFQueryLens.Lsp.csproj`
  - `dotnet build src/EFQueryLens.Daemon/EFQueryLens.Daemon.csproj`
