# IDE Support

EF QueryLens currently ships active IDE plugins for VS Code, Rider, and Visual Studio, all backed by the same LSP and daemon runtime.

## VS Code

- Plugin path: `src/Plugins/ef-querylens-vscode`
- Command/config prefix: `efquerylens.*`
- Key actions: Show SQL Preview, Copy SQL, Open SQL, Refresh

Screenshot:

![VS Code SQL Preview](assets/vs_code_plugin_single_query.png)

## Rider

- Plugin path: `src/Plugins/ef-querylens-rider`
- Key actions: copy sql, open sql, refresh
- Hover preview and highlight support enabled through Rider LSP APIs

Screenshot:

![Rider SQL Preview](assets/rider_plugin_single_query.png)

## Visual Studio

- Plugin path: `src/Plugins/ef-querylens-visualstudio`
- Key actions: copy sql, open sql, refresh
- Structured split-query rendering support

Screenshots:

![Visual Studio SQL Preview](assets/vs_extension_single_query.png)

![Visual Studio Multi Query](assets/vs_extension_multi_query.png)

## Shared Backend

All three IDE clients use:

- `EFQueryLens.Lsp` for request/response orchestration
- `EFQueryLens.Daemon` for runtime query translation services
- `EFQueryLens.Core` for transport-agnostic engine contracts

This shared architecture keeps behavior consistent across IDEs and reduces feature drift.
