namespace EFQueryLens.Lsp.Parsing;

// Terminal expression rewriting was removed. The engine now handles terminal calls
// (Count, ToList, FirstOrDefaultAsync, ExecuteDeleteAsync, etc.) natively — SQL is
// captured before the expression executes, so the LSP sends the raw expression
// unchanged and developers see the exact SQL their application will run.
public static partial class LspSyntaxHelper;
