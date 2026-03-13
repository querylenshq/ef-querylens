# EF QueryLens Visual Studio Extension

This project hosts the QueryLens language server in a Visual Studio extension using the VisualStudio.Extensibility SDK.

## Scope

- Target Visual Studio 2026+ with out-of-process extensibility
- Launch `EFQueryLens.Lsp.dll` over stdio via `LanguageServerProvider`
- Reuse LSP features from the shared server (hover, CodeLens, diagnostics)

## Status

Phase 1 implementation in progress:

- .NET 10 extension project wired to VisualStudio.Extensibility
- Language server provider implemented for `.cs` files
- LSP server artifacts copied into the extension output (`bin/.../server/`)
- VSIX builds successfully
