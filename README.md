<div align="center">
  <img src="docs/assets/icons/ef-querylens_128x128.png" alt="EF QueryLens" width="96" />
  <h1>EF QueryLens</h1>
  <p><strong>See the SQL behind every LINQ expression — right in your editor.</strong></p>
  <p>
    <a href="https://marketplace.visualstudio.com/items?itemName=EFQueryLens.EFQueryLensVS"><img src="https://vsmarketplacebadges.dev/version/EFQueryLens.EFQueryLensVS.svg" alt="Visual Studio" /></a>
    <a href="https://marketplace.visualstudio.com/items?itemName=EFQueryLens.ef-querylens-vscode"><img src="https://vsmarketplacebadges.dev/version-short/EFQueryLens.ef-querylens-vscode.svg" alt="VS Code" /></a>
    <a href="https://plugins.jetbrains.com/plugin/30753-ef-querylens"><img src="https://img.shields.io/jetbrains/plugin/v/dev.efquerylens?label=Rider" alt="Rider" /></a>
    <img src="https://img.shields.io/badge/License-MIT-green.svg" alt="MIT License" />
  </p>
</div>

<br/>

<div align="center">
  <img src="docs/assets/query_lens.gif" alt="EF QueryLens in action" width="800" />
</div>

<br/>

EF QueryLens translates EF Core LINQ to SQL at hover time. A shared daemon does the work — no database connection needed.

<div align="center">
  <a href="https://marketplace.visualstudio.com/items?itemName=EFQueryLens.ef-querylens-vscode"><img src="https://img.shields.io/badge/VS%20Code-007ACC?style=flat-square&logo=visualstudiocode&logoColor=white" alt="VS Code" /></a>
  &nbsp;
  <a href="https://plugins.jetbrains.com/plugin/30753-ef-querylens"><img src="https://img.shields.io/badge/Rider-FF318C?style=flat-square&logo=rider&logoColor=white" alt="Rider" /></a>
  &nbsp;
  <a href="https://marketplace.visualstudio.com/items?itemName=EFQueryLens.EFQueryLensVS"><img src="https://img.shields.io/badge/Visual%20Studio-5C2D91?style=flat-square&logo=visualstudio&logoColor=white" alt="Visual Studio" /></a>
</div>

<br/>

## Get started in four steps

```
1. Install the extension for your IDE
2. Run "EF QueryLens: Setup" to generate your offline DbContext factory
3. Build your solution
4. Hover any LINQ query → SQL appears
```

That's it. Setup scans your `AddDbContext` registrations and auto-generates everything it needs.

<br/>

## Features

<table>
  <tr>
    <td width="50%" valign="top">
      <h3>🔍 View SQL on Hover</h3>
      Hover any EF Core LINQ expression to see the generated SQL inline. No context switching, no logging, no guessing.
    </td>
    <td width="50%" valign="top">
      <h3>📄 Open SQL Panel</h3>
      Open the full SQL in a dedicated editor panel. Copy to clipboard or inspect split-query statements with per-split labels.
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <h3>⚡ Auto Setup</h3>
      Run <strong>EF QueryLens: Setup</strong> once. QueryLens scans your <code>AddDbContext</code> registrations, generates a gitignored factory, and is ready to go — no manual wiring.
    </td>
    <td width="50%" valign="top">
      <h3>✈️ Works Offline</h3>
      SQL is generated from your compiled assembly, not a live database. Works anywhere your code builds — no connection string required at runtime.
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <h3>🖥️ Every Major .NET IDE</h3>
      One daemon, consistent results across <strong>VS Code</strong>, <strong>JetBrains Rider</strong>, and <strong>Visual Studio</strong>.
    </td>
    <td width="50%" valign="top">
      <h3>🗄️ Provider-Aware</h3>
      SQL Server, PostgreSQL, and MySQL are each formatted with their correct dialect — what you see is what EF Core actually sends.
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <h3>🤖 MCP Server</h3>
      Expose LINQ-to-SQL translation to AI agents and automation via a built-in MCP server. Query your data model programmatically.
    </td>
    <td width="50%" valign="top">
      <h3>🔀 Split-Query Support</h3>
      Multi-statement split queries are rendered with per-statement labels so you see exactly how EF Core breaks up the load.
    </td>
  </tr>
</table>

<br/>

## Setup

**1. Install the extension**

| IDE | Link |
|---|---|
| VS Code | [Visual Studio Marketplace](https://marketplace.visualstudio.com/items?itemName=EFQueryLens.ef-querylens-vscode) |
| JetBrains Rider | [JetBrains Marketplace](https://plugins.jetbrains.com/plugin/30753-ef-querylens) |
| Visual Studio | [Visual Studio Marketplace](https://marketplace.visualstudio.com/items?itemName=EFQueryLens.EFQueryLensVS) |

**2. Set up your offline DbContext factory**

EF QueryLens needs a lightweight offline DbContext to generate SQL without a live database. Run **EF QueryLens: Setup** from the command palette — QueryLens inspects your `AddDbContext` registrations and generates the factory for you, gitignored and ready to go.

You can also trigger setup by hovering any LINQ query and clicking **Set up QueryLens for this project**.

> For multiple DbContexts, custom options, or manual factory authoring, see [Factory Setup](docs/factory-setup.md).

**3. Build your solution**

```bash
dotnet build
```

This compiles the generated factory so QueryLens can load it.

**4. Hover any LINQ expression**

SQL appears in the hover popup. Use the inline actions to **copy**, **open** the full SQL panel, or **refresh**.

<br/>

## Works everywhere

<details>
<summary>IDE screenshots</summary>

<br/>

**VS Code**

<img src="docs/assets/vs_code_plugin_single_query.png" alt="VS Code" />

<br/>

**JetBrains Rider**

<img src="docs/assets/rider_plugin_single_query.png" alt="Rider" />

<br/>

**Visual Studio**

<img src="docs/assets/vs_extension_single_query.png" alt="Visual Studio" />

</details>

<br/>

## Documentation

- [Getting Started](docs/getting-started.md)
- [Factory Setup](docs/factory-setup.md)
- [IDE Support](docs/ide-support.md)
- [Providers](docs/providers.md)
- [MCP Server](docs/mcp-server.md)
- [CLI Reference](docs/cli-reference.md)
- [Architecture](docs/architecture.md)

<br/>

## Build & Test

<details>
<summary>Build commands</summary>

```bash
# .NET solution
dotnet build EFQueryLens.slnx
dotnet test EFQueryLens.slnx

# VS Code plugin
npm ci --prefix src/Plugins/ef-querylens-vscode
npm run compile --prefix src/Plugins/ef-querylens-vscode

# Rider plugin
cd src/Plugins/ef-querylens-rider
./gradlew build
```

</details>

<br/>

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## Security

See [SECURITY.md](SECURITY.md).

## License

MIT — see [LICENSE](LICENSE).
