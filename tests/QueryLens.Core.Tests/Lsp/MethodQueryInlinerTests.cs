using QueryLens.Lsp.Parsing;

namespace QueryLens.Core.Tests.Lsp;

public class MethodQueryInlinerTests
{
    [Fact]
    public void TryInlineTopLevelInvocation_ReturnAwaitMethod_InlinesQueryBody()
    {
        var workspaceRoot = CreateWorkspace();

        try
        {
            var apiFile = Path.Combine(workspaceRoot, "Api", "GetWorkflowByType.cs");
            var coreFile = Path.Combine(workspaceRoot, "Core", "WorkflowService.cs");

            Directory.CreateDirectory(Path.GetDirectoryName(apiFile)!);
            Directory.CreateDirectory(Path.GetDirectoryName(coreFile)!);
            File.WriteAllText(Path.Combine(workspaceRoot, "Sample.sln"), "");

            File.WriteAllText(coreFile, """
                using System.Linq.Expressions;
                using System.Threading;
                using System.Threading.Tasks;

                public sealed class WorkflowService
                {
                    public async Task<TResult?> GetWorkflowByTypeAsync<TResult>(
                        WorkflowType workflowType,
                        Expression<Func<Workflow, TResult>> expression,
                        CancellationToken ct)
                    {
                        return await dbContext
                            .Workflows.Where(w => w.WorkflowType == workflowType && w.IsNotDeleted)
                            .Select(expression)
                            .SingleOrDefaultAsync(ct);
                    }
                }
                """);

            var endpointSource = """
                var data = await service.GetWorkflowByTypeAsync(
                    req.WorkflowType,
                    w => new WorkflowResponse { WorkflowType = w.WorkflowType },
                    ct);
                """;
            File.WriteAllText(apiFile, endpointSource);

            var expression = "service.GetWorkflowByTypeAsync(req.WorkflowType, w => new WorkflowResponse { WorkflowType = w.WorkflowType }, ct)";

            var success = MethodQueryInliner.TryInlineTopLevelInvocation(
                endpointSource,
                apiFile,
                expression,
                out var inlined,
                out var contextVariable,
                out var reason);

            Assert.True(success, reason);
            Assert.Equal("dbContext", contextVariable);
            Assert.Contains("dbContext.Workflows", inlined, StringComparison.Ordinal);
            Assert.Contains("req.WorkflowType", inlined, StringComparison.Ordinal);
            Assert.Contains("Select(w => new WorkflowResponse", inlined, StringComparison.Ordinal);
            Assert.DoesNotContain("SingleOrDefaultAsync", inlined, StringComparison.Ordinal);
        }
        finally
        {
            DeleteWorkspace(workspaceRoot);
        }
    }

    [Fact]
    public void TryInlineTopLevelInvocation_ExpressionBodiedMethod_InlinesQueryBody()
    {
        var workspaceRoot = CreateWorkspace();

        try
        {
            var apiFile = Path.Combine(workspaceRoot, "Api", "CreateApplication.cs");
            var coreFile = Path.Combine(workspaceRoot, "Core", "WorkflowService.cs");

            Directory.CreateDirectory(Path.GetDirectoryName(apiFile)!);
            Directory.CreateDirectory(Path.GetDirectoryName(coreFile)!);
            File.WriteAllText(Path.Combine(workspaceRoot, "Sample.sln"), "");

            File.WriteAllText(coreFile, """
                using System.Linq.Expressions;
                using System.Threading;
                using System.Threading.Tasks;

                public sealed class WorkflowService
                {
                    public Task<TResult?> GetWorkflowByIdAsync<TResult>(
                        Guid workflowId,
                        Expression<Func<Workflow, TResult>> expression,
                        CancellationToken ct)
                        => dbContext.Workflows
                            .Where(w => w.WorkflowId == workflowId)
                            .Select(expression)
                            .SingleOrDefaultAsync(ct);
                }
                """);

            var endpointSource = """
                var workflowId = await service.GetWorkflowByIdAsync(
                    req.WorkflowId,
                    w => w.WorkflowId,
                    ct);
                """;
            File.WriteAllText(apiFile, endpointSource);

            var expression = "service.GetWorkflowByIdAsync(req.WorkflowId, w => w.WorkflowId, ct)";

            var success = MethodQueryInliner.TryInlineTopLevelInvocation(
                endpointSource,
                apiFile,
                expression,
                out var inlined,
                out var contextVariable,
                out var reason);

            Assert.True(success, reason);
            Assert.Equal("dbContext", contextVariable);
            Assert.Contains("dbContext.Workflows", inlined, StringComparison.Ordinal);
            Assert.Contains("req.WorkflowId", inlined, StringComparison.Ordinal);
            Assert.Contains("Select(w => w.WorkflowId)", inlined, StringComparison.Ordinal);
            Assert.DoesNotContain("SingleOrDefaultAsync", inlined, StringComparison.Ordinal);
        }
        finally
        {
            DeleteWorkspace(workspaceRoot);
        }
    }

    [Fact]
    public void TryInlineTopLevelInvocation_UnknownMethod_ReturnsFalse()
    {
        var workspaceRoot = CreateWorkspace();

        try
        {
            var apiFile = Path.Combine(workspaceRoot, "Api", "GetWorkflowByType.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(apiFile)!);
            File.WriteAllText(Path.Combine(workspaceRoot, "Sample.sln"), "");

            var source = """
                var data = await service.DoesNotExist(req.WorkflowType, w => w.WorkflowType, ct);
                """;
            File.WriteAllText(apiFile, source);

            var expression = "service.DoesNotExist(req.WorkflowType, w => w.WorkflowType, ct)";

            var success = MethodQueryInliner.TryInlineTopLevelInvocation(
                source,
                apiFile,
                expression,
                out var inlined,
                out var contextVariable,
                out var reason);

            Assert.False(success);
            Assert.Equal(expression, inlined);
            Assert.Null(contextVariable);
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }
        finally
        {
            DeleteWorkspace(workspaceRoot);
        }
    }

    private static string CreateWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), "querylens-inliner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteWorkspace(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}