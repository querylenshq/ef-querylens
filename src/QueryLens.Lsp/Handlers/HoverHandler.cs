using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using QueryLens.Core;
using QueryLens.Lsp.Parsing;

namespace QueryLens.Lsp.Handlers;

internal sealed class HoverHandler : HoverHandlerBase
{
    private readonly IQueryLensEngine _engine;
    private readonly DocumentManager _documentManager;

    public HoverHandler(ILanguageServerFacade server, IQueryLensEngine engine, DocumentManager documentManager)
    {
        _engine = engine;
        _documentManager = documentManager;
    }

    public override async Task<Hover?> Handle(HoverParams request, CancellationToken cancellationToken)
    {
        // 1. Get the document path and live text
        var filePath = request.TextDocument.Uri.GetFileSystemPath();

        var sourceText = _documentManager.GetDocumentText(request.TextDocument.Uri);
        if (sourceText == null) return null;

        // 2. Parse the text dynamically and find the LINQ expression under the cursor
        var expression = LspSyntaxHelper.TryExtractLinqExpression(
            sourceText,
            request.Position.Line,
            request.Position.Character,
            out var contextVariableName);

        if (expression == null || contextVariableName == null)
        {
            // The user isn't hovering over a valid queryable member access chain
            return null;
        }

        if (MethodQueryInliner.TryInlineTopLevelInvocation(
                sourceText,
                filePath,
                expression,
                out var inlinedExpression,
                out var inlinedContextVariable,
                out _))
        {
            expression = inlinedExpression;
            if (!string.IsNullOrWhiteSpace(inlinedContextVariable))
            {
                contextVariableName = inlinedContextVariable;
            }
        }

        var targetAssembly = AssemblyResolver.TryGetTargetAssembly(filePath);

        // Debug fallback
        if (!string.IsNullOrEmpty(targetAssembly) && targetAssembly.StartsWith("DEBUG_FAIL"))
        {
            return new Hover
            {
                Contents = new MarkedStringsOrMarkupContent(new MarkupContent
                {
                    Kind = MarkupKind.Markdown,
                    Value = $"⚠️ *QueryLens AssemblyResolver Failed*\n```text\n{targetAssembly}\n```"
                })
            };
        }

        // Let's protect against the scenario where the assembly isn't built yet
        if (string.IsNullOrEmpty(targetAssembly) || !File.Exists(targetAssembly))
        {
            return new Hover
            {
                Contents = new MarkedStringsOrMarkupContent(new MarkupContent
                {
                    Kind = MarkupKind.Markdown,
                    Value =
                        $"⚠️ *QueryLens: Target assembly `{Path.GetFileName(targetAssembly)}` not found. Please build the project.*"
                })
            };
        }

        try
        {
            var translation = await _engine.TranslateAsync(new TranslationRequest
            {
                AssemblyPath = targetAssembly,
                Expression = expression,
                ContextVariableName = contextVariableName
            }, cancellationToken);

            if (translation.Success)
            {
                var commands = translation.Commands.Count > 0
                    ? translation.Commands
                    : translation.Sql is null
                        ? []
                        : [new QuerySqlCommand { Sql = translation.Sql, Parameters = translation.Parameters }];

                if (commands.Count == 0)
                {
                    return new Hover
                    {
                        Contents = new MarkedStringsOrMarkupContent(new MarkupContent
                        {
                            Kind = MarkupKind.Markdown,
                            Value = "**QueryLens Error**\n```text\nNo SQL was produced for this expression.\n```"
                        })
                    };
                }

                return new Hover
                {
                    Contents = new MarkedStringsOrMarkupContent(new MarkupContent
                    {
                        Kind = MarkupKind.Markdown,
                        Value = BuildSqlPreview(commands)
                    })
                };
            }

            return new Hover
            {
                Contents = new MarkedStringsOrMarkupContent(new MarkupContent
                {
                    Kind = MarkupKind.Markdown,
                    Value =
                        $"**QueryLens Error**\n```text\n{translation.ErrorMessage}\n```\n\n*Assembly: `{targetAssembly}`*\n*Expression: `{expression}`*"
                })
            };
        }
        catch (Exception ex)
        {
            return new Hover
            {
                Contents = new MarkedStringsOrMarkupContent(new MarkupContent
                {
                    Kind = MarkupKind.Markdown,
                    Value =
                        $"**QueryLens Exception**\n```text\n{ex.GetType().Name}: {ex.Message}\n{ex.InnerException?.Message ?? ""}\n```\n\n*Assembly: `{targetAssembly}`*\n*Expression: `{expression}`*"
                })
            };
        }
    }

    protected override HoverRegistrationOptions CreateRegistrationOptions(HoverCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new HoverRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("csharp")
        };
    }

    private static string BuildSqlPreview(IReadOnlyList<QuerySqlCommand> commands)
    {
        if (commands.Count == 1)
        {
            return $"**QueryLens SQL Preview**\n```sql\n{commands[0].Sql}\n```";
        }

        var blocks = new List<string>
        {
            $"**QueryLens SQL Preview ({commands.Count} statements)**"
        };

        for (var i = 0; i < commands.Count; i++)
        {
            blocks.Add($"**Statement {i + 1}**\n```sql\n{commands[i].Sql}\n```");
        }

        return string.Join("\n\n", blocks);
    }
}
