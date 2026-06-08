using EFQueryLens.Core.Contracts;
using EFQueryLens.Lsp.Parsing;

namespace EFQueryLens.Lsp.Handlers;

internal sealed partial class HoverHandler
{
    /// <summary>
    /// Builds the primary hover cache key.
    ///
    /// The key is scoped by <b>assembly fingerprint</b> (path + size + last-write timestamp)
    /// rather than a hash of the full source text. This means:
    /// <list type="bullet">
    ///   <item>Edits anywhere in the file do <em>not</em> bust cached hover entries.</item>
    ///   <item>A rebuild (new .dll on disk) automatically invalidates all entries because
    ///         the fingerprint changes.</item>
    /// </list>
    /// </summary>
    private string BuildHoverCacheKey(
        string filePath,
        int requestLine,
        int requestCharacter,
        SemanticHoverContext? semanticContext)
    {
        var fingerprint = AssemblyResolver.TryGetAssemblyFingerprint(filePath)
                          ?? $"no-assembly|{Path.GetFullPath(filePath)}";

        if (semanticContext is not null)
        {
            return $"{fingerprint}|semantic|{semanticContext.SemanticKey}|{semanticContext.EffectiveLine}|{semanticContext.EffectiveCharacter}";
        }

        return $"{fingerprint}|cursor|{requestLine}|{requestCharacter}";
    }

    private static bool TryResolveSemanticHoverContext(
        string filePath,
        string sourceText,
        int line,
        int character,
        out SemanticHoverContext? semanticContext)
    {
        semanticContext = null;

        if (TryFindContainingChain(sourceText, line, character, out var containingChain))
        {
            semanticContext = CreateSemanticHoverContext(
                filePath,
                sourceText,
                containingChain.Expression,
                containingChain.ContextVariableName,
                containingChain.Line,
                containingChain.Character);
            return semanticContext is not null;
        }

        var siblingRoots = ProjectSourceHelper.GetSiblingRoots(filePath);
        var expression = LspSyntaxHelper.TryExtractLinqExpression(sourceText, line, character, out var contextVariableName, siblingRoots);

        if (string.IsNullOrWhiteSpace(expression) || string.IsNullOrWhiteSpace(contextVariableName))
        {
            return false;
        }

        semanticContext = CreateSemanticHoverContext(
            filePath,
            sourceText,
            expression,
            contextVariableName,
            line,
            character);
        return semanticContext is not null;
    }

    /// <summary>
    /// Single source of truth for the semantic (translation-request) cache key.
    /// Uses the same canonical hash as the daemon so locals, usings, and aliases
    /// all participate in cache identity.
    /// </summary>
    internal static string BuildSemanticKey(TranslationRequest request)
        => TranslationRequestBuilder.BuildSemanticCacheKey(request);

    /// <summary>
    /// Builds the semantic key for every chain in a document. Used by the prewarm service
    /// to decide which chains are already analysed.
    /// </summary>
    internal IReadOnlyList<string> BuildChainSemanticKeys(
        string filePath,
        string sourceText,
        IReadOnlyList<LinqChainInfo> chains)
    {
        var keys = new string[chains.Count];
        for (var i = 0; i < chains.Count; i++)
        {
            var chain = chains[i];
            var request = TranslationRequestBuilder.TryBuild(
                filePath,
                sourceText,
                chain.Expression,
                chain.ContextVariableName,
                chain.Line,
                chain.Character);
            keys[i] = request is not null
                ? BuildSemanticKey(request)
                : $"unresolved|{chain.Line}|{chain.Character}|{NormalizeWhitespace(chain.Expression)}";
        }

        return keys;
    }

    private static SemanticHoverContext? CreateSemanticHoverContext(
        string filePath,
        string sourceText,
        string expression,
        string contextVariableName,
        int effectiveLine,
        int effectiveCharacter)
    {
        var request = TranslationRequestBuilder.TryBuild(
            filePath,
            sourceText,
            expression,
            contextVariableName,
            effectiveLine,
            effectiveCharacter);
        if (request is null)
        {
            return null;
        }

        return new SemanticHoverContext(
            SemanticKey: BuildSemanticKey(request),
            EffectiveLine: effectiveLine,
            EffectiveCharacter: effectiveCharacter);
    }

    private static bool TryFindContainingChain(string sourceText, int line, int character, out LinqChainInfo containingChain)
    {
        containingChain = null!;

        foreach (var chain in LspSyntaxHelper.FindAllLinqChains(sourceText))
        {
            if (!IsWithinStatementRange(chain, line, character))
            {
                continue;
            }

            containingChain = chain;
            return true;
        }

        return false;
    }

    private static bool IsWithinStatementRange(LinqChainInfo chain, int line, int character)
    {
        if (line < chain.StatementStartLine || line > chain.StatementEndLine)
        {
            return false;
        }

        if (chain.StatementStartLine == chain.StatementEndLine)
        {
            return character >= chain.StatementStartCharacter && character <= chain.StatementEndCharacter;
        }

        if (line == chain.StatementStartLine)
        {
            return character >= chain.StatementStartCharacter;
        }

        if (line == chain.StatementEndLine)
        {
            return character <= chain.StatementEndCharacter;
        }

        return true;
    }

    internal static string NormalizeWhitespace(string value)
    {
        var buffer = new char[value.Length];
        var index = 0;
        var previousWasWhitespace = false;

        foreach (var current in value)
        {
            if (char.IsWhiteSpace(current))
            {
                if (previousWasWhitespace)
                {
                    continue;
                }

                buffer[index++] = ' ';
                previousWasWhitespace = true;
            }
            else
            {
                buffer[index++] = current;
                previousWasWhitespace = false;
            }
        }

        return new string(buffer, 0, index).Trim();
    }

    private sealed record SemanticHoverContext(string SemanticKey, int EffectiveLine, int EffectiveCharacter);
}
