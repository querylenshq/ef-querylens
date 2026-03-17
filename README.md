# EF QueryLens

<!-- logo: add official brand mark here -->

[![CI](https://github.com/nemina47/ef-querylens/actions/workflows/ci.yml/badge.svg)](https://github.com/nemina47/ef-querylens/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

Preview your EF Core SQL in real time, without leaving your IDE.

EF QueryLens is a hover-first SQL preview and analysis toolkit for Entity Framework Core. It runs a shared LSP backend and ships IDE plugins for VS Code, Rider, and Visual Studio so you can inspect generated SQL where you already work.

## Screenshots

![VS Code Single Query](docs/assets/vs_code_plugin_single_query.png)
![Rider Single Query](docs/assets/rider_plugin_single_query.png)
![Visual Studio Single Query](docs/assets/vs_extension_single_query.png)
![Visual Studio Multi Query](docs/assets/vs_extension_multi_query.png)

## Highlights

- Hover SQL preview for EF Core LINQ expressions
- Copy SQL action from hover
- Open SQL action in dedicated preview dialog
- Split-query rendering with per-split labels
- Shared backend across all IDE clients for parity and predictable behavior
- Provider-aware SQL formatting controls

## Supported IDEs

| IDE | Status | Install |
|---|---|---|
| VS Code | Active | Local install from `src/Plugins/ef-querylens-vscode` |
| JetBrains Rider | Active | Local install from `src/Plugins/ef-querylens-rider` |
| Visual Studio 2022 | Active | Local install from `src/Plugins/ef-querylens-visualstudio` |

## Quick Start

1. Build the repo.
2. Install one of the IDE plugins.
3. Open a C# project with EF Core queries.
4. Hover a LINQ query to inspect generated SQL.
5. Use copy sql, open sql, or refresh directly from the hover UI.

## Build From Source

<details>
<summary>Commands</summary>

```bash
dotnet build EFQueryLens.slnx
dotnet test EFQueryLens.slnx

npm ci --prefix src/Plugins/ef-querylens-vscode
npm run compile --prefix src/Plugins/ef-querylens-vscode

cd src/Plugins/ef-querylens-rider
./gradlew build
```

</details>

## Documentation

- [Getting Started](docs/getting-started.md)
- [IDE Support](docs/ide-support.md)
- [Architecture](docs/architecture.md)
- [Providers](docs/providers.md)
- [CLI Reference](docs/cli-reference.md)
- [MCP Server](docs/mcp-server.md)

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## Security

See [SECURITY.md](SECURITY.md).

## License

MIT, see [LICENSE](LICENSE).
