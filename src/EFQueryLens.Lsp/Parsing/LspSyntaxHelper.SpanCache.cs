using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFQueryLens.Lsp.Parsing;

public static partial class LspSyntaxHelper
{
    /// <summary>
    /// When the cursor sits inside a lambda (or similar) passed as a call argument — e.g.
    /// <c>GetApplicationByIdAsync(id, a => new { ... }, ct)</c> — returns that argument's span
    /// so the hover layer can map any position in the projection to one semantic cache key.
    /// </summary>
    internal static bool TryGetEnclosingCallArgumentSpan(
        string sourceText,
        int line,
        int character,
        out int spanStart,
        out int spanEnd)
    {
        spanStart = 0;
        spanEnd = 0;

        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return false;
        }

        var tree = CSharpSyntaxTree.ParseText(sourceText);
        var text = tree.GetText();
        if (line >= text.Lines.Count)
        {
            return false;
        }

        var lineSpan = text.Lines[line];
        if (character > lineSpan.Span.Length)
        {
            return false;
        }

        var position = lineSpan.Start + character;
        var node = tree.GetRoot().FindToken(position).Parent;
        if (node is null)
        {
            return false;
        }

        // Prefer the outermost containing argument (largest span). Nested LINQ such as
        // `a => new { X = a.Items.Where(w => w.Id == id) }` must map every inner cursor
        // to the same projection-lambda span as the composed query cache key.
        ArgumentSyntax? widestArgument = null;
        foreach (var invocation in node.AncestorsAndSelf().OfType<InvocationExpressionSyntax>())
        {
            foreach (var argument in invocation.ArgumentList.Arguments)
            {
                if (!argument.Span.Contains(position))
                {
                    continue;
                }

                if (widestArgument is null || argument.Span.Length > widestArgument.Span.Length)
                {
                    widestArgument = argument;
                }
            }
        }

        if (widestArgument is null)
        {
            return false;
        }

        spanStart = widestArgument.SpanStart;
        spanEnd = widestArgument.Span.End;
        return true;
    }

    internal static bool TryGetAbsolutePosition(
        string sourceText,
        int line,
        int character,
        out int absolutePosition)
    {
        absolutePosition = 0;
        var tree = CSharpSyntaxTree.ParseText(sourceText);
        var text = tree.GetText();
        if (line >= text.Lines.Count)
        {
            return false;
        }

        var lineSpan = text.Lines[line];
        if (character > lineSpan.Span.Length)
        {
            return false;
        }

        absolutePosition = lineSpan.Start + character;
        return true;
    }

    /// <summary>
    /// Outermost invocation span containing the cursor — e.g. the full
    /// <c>GetApplicationByIdAsync(applicationId, a => ..., ct)</c> call.
    /// </summary>
    internal static bool TryGetEnclosingInvocationSpan(
        string sourceText,
        int line,
        int character,
        out int spanStart,
        out int spanEnd)
    {
        spanStart = 0;
        spanEnd = 0;

        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return false;
        }

        var tree = CSharpSyntaxTree.ParseText(sourceText);
        var text = tree.GetText();
        if (line >= text.Lines.Count)
        {
            return false;
        }

        var lineSpan = text.Lines[line];
        if (character > lineSpan.Span.Length)
        {
            return false;
        }

        var position = lineSpan.Start + character;
        var node = tree.GetRoot().FindToken(position).Parent;
        if (node is null)
        {
            return false;
        }

        InvocationExpressionSyntax? widestInvocation = null;
        var widestLength = 0;
        foreach (var invocation in node.AncestorsAndSelf().OfType<InvocationExpressionSyntax>())
        {
            if (!invocation.Span.Contains(position))
            {
                continue;
            }

            if (invocation.Span.Length > widestLength)
            {
                widestInvocation = invocation;
                widestLength = invocation.Span.Length;
            }
        }

        if (widestInvocation is null)
        {
            return false;
        }

        spanStart = widestInvocation.SpanStart;
        spanEnd = widestInvocation.Span.End;
        return true;
    }

    /// <summary>
    /// Full <see cref="LinqChainInfo"/> statement span so hovers on <c>await</c> or
    /// closing parens on direct <c>dbContext</c> chains map to the same semantic cache key.
    /// </summary>
    internal static bool TryGetEnclosingLinqStatementSpan(
        string sourceText,
        int line,
        int character,
        out int spanStart,
        out int spanEnd)
    {
        spanStart = 0;
        spanEnd = 0;

        foreach (var chain in FindAllLinqChains(sourceText))
        {
            if (!IsPositionWithinLinqStatement(chain, line, character))
            {
                continue;
            }

            if (!TryGetAbsolutePosition(sourceText, chain.StatementStartLine, chain.StatementStartCharacter, out spanStart)
                || !TryGetAbsolutePosition(sourceText, chain.StatementEndLine, chain.StatementEndCharacter, out var endPosition))
            {
                return false;
            }

            spanEnd = endPosition + 1;
            return true;
        }

        return false;
    }

    private static bool IsPositionWithinLinqStatement(LinqChainInfo chain, int line, int character)
    {
        if (line < chain.StatementStartLine || line > chain.StatementEndLine)
        {
            return false;
        }

        if (chain.StatementStartLine == chain.StatementEndLine)
        {
            return character >= chain.StatementStartCharacter
                   && character <= chain.StatementEndCharacter;
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
}
