using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace EFQueryLens.Core.Scripting;

public sealed partial class QueryEvaluator
{
    private static string BuildEvalSource(
        Type dbContextType,
        TranslationRequest request,
        IReadOnlyList<string> stubs,
        IReadOnlySet<string> knownNamespaces,
        IReadOnlySet<string> knownTypes,
        IReadOnlyCollection<string> synthesizedUsingStaticTypes)
    {
        var sb = new StringBuilder();

        AppendBaseUsings(sb);
        AppendRequestUsings(sb, request, knownNamespaces, knownTypes, synthesizedUsingStaticTypes);
        AppendCapturedTypes(sb);
        AppendOfflineDbConnection(sb);
        AppendFakeDbDataReader(sb);
        AppendSqlCaptureScope(sb);
        AppendOfflineCapture(sb);
        AppendRunner(sb, dbContextType, request, stubs);

        return sb.ToString();
    }

    private static CSharpCompilation BuildCompilation(string source, MetadataReference[] refs)
    {
        var tree = CSharpSyntaxTree.ParseText(source, SParseOptions);
        return CSharpCompilation.Create(
            $"__QueryLensEval_{Guid.NewGuid():N}",
            syntaxTrees: [tree],
            references: refs,
            options: SCompilationOptions);
    }
}
