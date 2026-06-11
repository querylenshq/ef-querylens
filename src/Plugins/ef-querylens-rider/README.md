# EF QueryLens for Rider

Preview your EF Core SQL in real time, without leaving your IDE.

EF QueryLens for Rider integrates with the QueryLens LSP backend and gives you SQL visibility directly from your LINQ queries.

## Features

- Hover SQL preview for LINQ/EF queries (Quick Documentation, Ctrl+Q)
- Copy SQL action from hover
- Open SQL action in a dedicated preview dialog
- Refresh analysis action
- Structured split-query rendering
- SQL-ready dialog with **Go to Query** / **Open SQL** when background translation completes
- Status bar indicator (`QueryLens: Ready` / `Computing…`) — matches VS Code `showStatusBar`

## Settings (VS Code parity)

Open **Settings → EF QueryLens** (or search “EF QueryLens” in Rider settings):

| Setting | VS Code | Rider default | Notes |
|---------|---------|---------------|-------|
| Notify when SQL is ready | `efquerylens.notifyWhenSqlReady` | **on** | Same LSP `efquerylens/sqlReady` notification |
| Hover wait when warm (ms) | `efquerylens.hoverWaitWhenWarmMs` | **8000** | How long sync hover waits for SQL before returning InQueue |
| Show status in status bar | `efquerylens.showStatusBar` | **on** | Right-side status text; click opens EF QueryLens tool window |

### When the SQL-ready popup appears

The notification is **not** shown on every successful hover — same as VS Code:

1. **Cold / slow hover:** Quick Doc shows “InQueue” → translation runs in background → dialog appears with **Go to Query** / **Open SQL**.
2. **Warm hover within wait budget:** SQL appears in Quick Doc on first hover → **no** popup (sync path clears pending notification).

To see SQL in Quick Doc on first hover more often, increase **Hover wait when warm** (e.g. keep at 8000 ms or higher). Set to **0** to always use the InQueue + background-dialog pattern.

### Rider vs VS Code presentation

- **SQL preview:** Rider uses Quick Documentation; VS Code uses the editor hover tooltip. Same LSP markdown, different surface.
- **SQL ready:** Rider uses a choose dialog; VS Code uses a bottom information message. Same actions and labels.
- **CodeLens:** VS Code disables inline SQL badges (`QUERYLENS_MAX_CODELENS_PER_DOCUMENT=0`). Rider may still show LSP CodeLens if the server advertises them.

## Screenshot

![EF QueryLens Rider](https://raw.githubusercontent.com/querylenshq/ef-querylens/main/docs/assets/rider_plugin_single_query.png)

## Requirements

- JetBrains Rider (2025.2+)
- .NET 10 Runtime + ASP.NET Core Runtime (required to run the bundled QueryLens backend)
- .NET 10 SDK (only for local development/build)
- EF Core project

## Development

From `src/Plugins/ef-querylens-rider`:

1. Use **JDK 21** for Gradle. JetBrains Runtime or Eclipse Temurin work well.
   Microsoft Build of OpenJDK can fail `instrumentCode` with `Packages does not exist`
   unless you create `%JAVA_HOME%\Packages` manually (requires admin under `Program Files`).
   This project disables `instrumentCode` because the plugin has no UI forms.
2. `./gradlew build`
3. `./gradlew runIde`

Build backend runtime inputs first so Rider can bundle and launch them:

- `dotnet build src/EFQueryLens.Lsp/EFQueryLens.Lsp.csproj`
- `dotnet build src/EFQueryLens.Daemon/EFQueryLens.Daemon.csproj`

## Debugging (logs)

To investigate **copy sql / open sql / refresh** behavior or hover highlighting:

1. Open Rider logs (Help > Diagnostic Tools > Debug Log Settings, or open log in explorer).
2. Add logger category `efquerylens`.
3. Reproduce with a C# file containing EF Core queries.

Useful log entries:

- `[EFQueryLens] URL opener command=... uri=...` for action-link dispatch.
- `[EFQueryLens] URL opener failed for command=...` for command handling failures.
- `[EFQueryLens] applyHighlights: N entries` for hover highlight coverage.

## Troubleshooting file lock warnings

When running `runIde`, Rider sandbox lock warnings can appear if multiple IDE instances reuse the same sandbox.

Workaround:

1. Close all Rider windows and stop leftover Rider/Java processes.
2. Delete sandbox:

   ```powershell
   Remove-Item -Recurse -Force ".\\.intellijPlatform\\sandbox"
   ```

3. Start a single IDE instance:

   ```powershell
   .\gradlew runIde --no-daemon
   ```

4. Do not start another `runIde` session before closing the first one.
