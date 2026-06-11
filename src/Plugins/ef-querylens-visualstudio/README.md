# EF QueryLens for Visual Studio

Preview your EF Core SQL in real time, without leaving your IDE.

EF QueryLens for Visual Studio provides hover-based SQL preview and quick SQL actions for Entity Framework Core LINQ queries.

## Features

- Hover SQL preview for LINQ queries
- Copy SQL action
- Open SQL action in dedicated viewer
- Refresh analysis action
- Structured split-query rendering

## Screenshots

![EF QueryLens Visual Studio Single Query](https://raw.githubusercontent.com/querylenshq/ef-querylens/main/docs/assets/vs_extension_single_query.png)

![EF QueryLens Visual Studio Multi Query](https://raw.githubusercontent.com/querylenshq/ef-querylens/main/docs/assets/vs_extension_multi_query.png)

## Requirements

- Visual Studio 2022 (17.14+)
- .NET 8 or later runtime (10 recommended; the extension uses RollForward so 8 / 9 / 10 all work)
- .NET 10 SDK (only for local development/build)
- EF Core project

## Local Build

The VS extension is strong-named with `build/sgKey.snk` (required for local/CI VSIX builds). The in-proc host uses wire DTOs under `Host/Contracts/` and does **not** reference `EFQueryLens.Lsp.Protocol`; the LSP server bundle under `server/` still includes Protocol for the out-of-proc process.

```bash
dotnet clean src/Plugins/ef-querylens-visualstudio/EFQueryLens.VisualStudio/EFQueryLens.VisualStudio.csproj
dotnet build src/Plugins/ef-querylens-visualstudio/EFQueryLens.VisualStudio/EFQueryLens.VisualStudio.csproj -c Debug
```

For F5 debugging, use **Debug → Reset the Visual Studio Experimental Instance** after large host changes so stale extension files are not reused.

## More

- Repository: https://github.com/querylenshq/ef-querylens
- Docs: https://github.com/querylenshq/ef-querylens/tree/main/docs
