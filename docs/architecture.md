# EF QueryLens Architecture

This page is the public architecture overview. Detailed internal design notes remain in `docs/Design.md`.

## Runtime Topology

EF QueryLens uses a layered architecture:

1. `EFQueryLens.Core`
	- Contracts, request/response models, and engine abstractions
	- No IDE-specific or transport-specific dependencies

2. `EFQueryLens.Daemon`
	- Hosts translation workloads
	- Executes query translation and status lifecycle handling

3. `EFQueryLens.Lsp`
	- Language-server protocol surface used by all IDE clients
	- Maps editor positions to query preview operations

4. IDE plugins
	- VS Code plugin (`ef-querylens-vscode`)
	- Rider plugin (`ef-querylens-rider`)
	- Visual Studio plugin (`ef-querylens-visualstudio`)
	- Each plugin stays thin and delegates translation work to the shared backend

## Why This Layout

- Consistent behavior across all IDEs
- Faster feature rollout (backend once, UI wrappers per IDE)
- Reduced duplication and easier diagnostics
- Clear separation between translation engine and presentation

## Isolation Strategy

QueryLens isolates user-project loading and query execution boundaries to avoid dependency conflicts between plugin runtime and target project runtime.

## Design Principles

- Keep `EFQueryLens.Core` provider-agnostic
- Keep IDE hosts thin and backend-driven
- Keep command/config naming explicit and stable
- Favor forward-compatible protocol contracts
