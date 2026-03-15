using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFQueryLens.Lsp.Parsing;

public static partial class MethodQueryInliner
{
    private sealed class ParameterSubstitutionRewriter : CSharpSyntaxRewriter
    {
        private readonly IReadOnlyDictionary<string, ExpressionSyntax> _map;
        private readonly Stack<HashSet<string>> _shadowedNames = new();

        public ParameterSubstitutionRewriter(IReadOnlyDictionary<string, ExpressionSyntax> map)
        {
            _map = map;
        }

        public override SyntaxNode? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
        {
            _shadowedNames.Push(new HashSet<string>(StringComparer.Ordinal)
            {
                node.Parameter.Identifier.ValueText
            });

            var visited = base.VisitSimpleLambdaExpression(node);
            _shadowedNames.Pop();
            return visited;
        }

        public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
        {
            var scoped = new HashSet<string>(
                node.ParameterList.Parameters.Select(p => p.Identifier.ValueText),
                StringComparer.Ordinal);
            _shadowedNames.Push(scoped);

            var visited = base.VisitParenthesizedLambdaExpression(node);
            _shadowedNames.Pop();
            return visited;
        }

        public override SyntaxNode? VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
        {
            var scoped = new HashSet<string>(StringComparer.Ordinal);
            if (node.ParameterList != null)
            {
                foreach (var parameter in node.ParameterList.Parameters)
                {
                    scoped.Add(parameter.Identifier.ValueText);
                }
            }

            _shadowedNames.Push(scoped);
            var visited = base.VisitAnonymousMethodExpression(node);
            _shadowedNames.Pop();
            return visited;
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            var name = node.Identifier.ValueText;
            if (!_map.TryGetValue(name, out var replacement) || IsShadowed(name))
            {
                return base.VisitIdentifierName(node);
            }

            return replacement.WithTriviaFrom(node);
        }

        private bool IsShadowed(string name)
        {
            foreach (var scope in _shadowedNames)
            {
                if (scope.Contains(name))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private sealed class ProjectionTypeSanitizer : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            var visited = (ObjectCreationExpressionSyntax)base.VisitObjectCreationExpression(node)!;

            // Only sanitize object-initializer-style DTO projections; keep constructor-based
            // creations unchanged because they may rely on positional constructor semantics.
            if (visited.Initializer is null)
            {
                return visited;
            }

            if (visited.ArgumentList is { Arguments.Count: > 0 })
            {
                return visited;
            }

            var members = new List<string>();
            foreach (var initExpression in visited.Initializer.Expressions)
            {
                if (initExpression is AssignmentExpressionSyntax assignment)
                {
                    var memberName = assignment.Left switch
                    {
                        IdentifierNameSyntax id => id.Identifier.ValueText,
                        SimpleNameSyntax simple => simple.Identifier.ValueText,
                        MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText,
                        _ => null,
                    };

                    if (!string.IsNullOrWhiteSpace(memberName))
                    {
                        members.Add($"{memberName} = {assignment.Right}");
                        continue;
                    }
                }

                members.Add(initExpression.ToString());
            }

            var anonymousText = members.Count == 0
                ? "new { __ql = 1 }"
                : $"new {{ {string.Join(", ", members)} }}";

            return SyntaxFactory.ParseExpression(anonymousText)
                .WithTriviaFrom(node);
        }
    }
}
