# Contributing

Thanks for contributing to EF QueryLens.

## Prerequisites

- .NET SDK 10
- Node.js 20+
- npm 10+
- JDK 21+ (for Rider plugin builds)

## Build (root)

```bash
dotnet build EFQueryLens.slnx
```

## Test

```bash
dotnet test EFQueryLens.slnx
```

## VS Code Plugin

```bash
npm ci --prefix src/Plugins/ef-querylens-vscode
npm run compile --prefix src/Plugins/ef-querylens-vscode
```

## Rider Plugin

```bash
cd src/Plugins/ef-querylens-rider
./gradlew build
```

Run sandbox IDE for local plugin debugging:

```bash
./gradlew runIde --no-daemon
```

## Visual Studio Plugin

```bash
dotnet build src/Plugins/ef-querylens-visualstudio/EFQueryLens.VisualStudio/EFQueryLens.VisualStudio.csproj -c Debug
```

## Full Validation Sequence

```bash
dotnet build EFQueryLens.slnx
dotnet test EFQueryLens.slnx
npm run compile --prefix src/Plugins/ef-querylens-vscode
cd src/Plugins/ef-querylens-rider && ./gradlew compileKotlin
```

## Pull Requests

- Keep changes scoped and cohesive
- Add or update tests when behavior changes
- Update docs/README for user-visible changes
- Keep command and config naming under `efquerylens.*`
- Keep plugin metadata (publisher/version/description) consistent across VS Code, Rider, and Visual Studio manifests
