using EFQueryLens.Lsp.Parsing;

namespace EFQueryLens.Lsp.Services;

/// <summary>Badge = where "Preview SQL" is drawn. Anchor = position for opening doc. Binding = full statement span where hover doc is shown.</summary>
internal sealed record CodeLensAnchor(
    int BadgeLine,
    int BadgeCharacter,
    int AnchorLine,
    int AnchorCharacter,
    int BindingStartLine,
    int BindingStartCharacter,
    int BindingEndLine,
    int BindingEndCharacter);

internal sealed class CodeLensPreviewService
{
    public IReadOnlyList<CodeLensAnchor> ComputeAnchors(string filePath, string sourceText, int maxCodeLens)
    {
        var chainInfos = LspSyntaxHelper.FindAllLinqChains(sourceText);
        if (chainInfos.Count == 0)
        {
            return [];
        }

        var targetAssembly = AssemblyResolver.TryGetTargetAssembly(filePath);
        if (string.IsNullOrWhiteSpace(targetAssembly)
            || targetAssembly.StartsWith("DEBUG_FAIL", StringComparison.Ordinal)
            || !File.Exists(targetAssembly))
        {
            return [];
        }

        var anchors = new List<CodeLensAnchor>(Math.Min(chainInfos.Count, maxCodeLens));
        foreach (var chain in chainInfos)
        {
            anchors.Add(new CodeLensAnchor(
                chain.BadgeLine,
                chain.BadgeCharacter,
                chain.Line,
                chain.Character,
                chain.StatementStartLine,
                chain.StatementStartCharacter,
                chain.StatementEndLine,
                chain.StatementEndCharacter));
            if (anchors.Count >= maxCodeLens)
            {
                break;
            }
        }

        return anchors;
    }
}
