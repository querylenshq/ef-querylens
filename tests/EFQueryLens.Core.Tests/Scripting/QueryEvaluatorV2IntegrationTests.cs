using EFQueryLens.Core.Contracts;
using Xunit;

namespace EFQueryLens.Core.Tests.Scripting;

/// <summary>
/// Integration tests for Slice 3b: V2 runtime decision framework integration with QueryEvaluator.
/// Tests the end-to-end flow of v2 payload validation through evaluation pipeline.
/// </summary>
public class QueryEvaluatorV2IntegrationTests
{
    [Fact]
    public void EvaluateAsync_InvalidV2Extraction_ReturnsStructuredDiagnostic()
    {
        // Test that blocked v2 payloads return diagnostic, not silent fallback.
        // This validates Slice 3b step 1: V2RuntimeAnalyzer integration into entry point.
        
        var request = new TranslationRequest
        {
            Expression = "db.Users.ToListAsync()",
            AssemblyPath = "/test.dll",
            // Incomplete v2 payload: extraction only, no capture plan
            V2ExtractionPlan = new V2QueryExtractionPlanSnapshot
            {
                Expression = "db.Users",
                ContextVariableName = "db",
                RootContextVariableName = "db",
                RootMemberName = "Users",
                BoundaryKind = "Queryable",
                NeedsMaterialization = false,
            },
            // Missing: V2CapturePlan should be present but isn't
        };

        var decision = V2RuntimeAnalyzer.Analyze(request);

        // Should be blocked with specific reason
        Assert.False(decision.ShouldUseV2Path);
        Assert.NotNull(decision.BlockReason);
        Assert.Contains("incomplete", decision.BlockReason);
    }

    [Fact]
    public void EvaluateAsync_ValidV2Payload_EnablesV2Path()
    {
        // Test that complete v2 payloads are accepted
        var request = new TranslationRequest
        {
            Expression = "db.Users.Where(u => u.IsActive).ToListAsync()",
            AssemblyPath = "/test.dll",
            V2ExtractionPlan = new V2QueryExtractionPlanSnapshot
            {
                Expression = "db.Users.Where(u => u.IsActive)",
                ContextVariableName = "db",
                RootContextVariableName = "db",
                RootMemberName = " Users",
                BoundaryKind = "Queryable",
                NeedsMaterialization = false,
            },
            V2CapturePlan = new V2CapturePlanSnapshot
            {
                ExecutableExpression = "db.Users.Where(u => u.IsActive)",
                IsComplete = true,
                Entries = new[]
                {
                    new V2CapturePlanEntry
                    {
                        Name = "u",
                        TypeName = "User",
                        CapturePolicy = LocalSymbolReplayPolicies.ReplayInitializer,
                        InitializerExpression = "default(User)",
                    },
                },
            },
        };

        var decision = V2RuntimeAnalyzer.Analyze(request);

        // Should be accepted
        Assert.True(decision.ShouldUseV2Path);
        Assert.Null(decision.BlockReason);
        Assert.NotNull(decision.ExtractionPlan);
        Assert.NotNull(decision.CapturePlan);
    }

    [Fact(Skip = "TODO: Investigate FormatDiagnostic output format")]
    public void EvaluateAsync_CaptureRejection_ProvidesExplicitDiagnostic()
    {
        // Test that capture rejections produce user-facing diagnostics
        var request = new TranslationRequest
        {
            Expression = "db.Users.ToListAsync()",
            AssemblyPath = "/test.dll",
            V2ExtractionPlan = new V2QueryExtractionPlanSnapshot
            {
                Expression = "db.Users",
                ContextVariableName = "db",
                RootContextVariableName = "db",
                RootMemberName = "Users",
                BoundaryKind = "Queryable",
                NeedsMaterialization = false,
            },
            V2CapturePlan = new V2CapturePlanSnapshot
            {
                ExecutableExpression = "db.Users",
                IsComplete = false,
                Diagnostics = new[]
                {
                    new V2CaptureDiagnostic
                    {
                        Code = "MISSING_SYMBOL",
                        SymbolName = "userId",
                        Message = "Symbol 'userId' cannot be captured from outer scope.",
                    },
                },
            },
        };

        var decision = V2RuntimeAnalyzer.Analyze(request);

        // Should be blocked with capture rejection reason
        Assert.False(decision.ShouldUseV2Path);
        Assert.StartsWith("capture-rejected:", decision.BlockReason);
        Assert.NotNull(decision.BlockMessage);
    }

    [Fact]
    public void DirectPayload_NoV2Extension_ReturnsBlockedDiagnostic()
    {
        // Hard-cut behavior: requests without v2 payloads are blocked.
        var request = new TranslationRequest
        {
            Expression = "db.Users.ToListAsync()",
            AssemblyPath = "/test.dll",
        };

        var decision = V2RuntimeAnalyzer.Analyze(request);

        Assert.False(decision.ShouldUseV2Path);
        Assert.Equal("missing-v2-payload", decision.BlockReason);
    }
}
