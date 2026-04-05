using EFQueryLens.Core.Contracts;
using EFQueryLens.Lsp.Parsing;

namespace EFQueryLens.Core.Tests.Lsp;

public partial class LspSyntaxHelperTests
{
    [Fact]
    public void TryBuildV2CapturePlan_DirectChainLocalValue_UsesReplayInitializer()
    {
        var source = """
            class Demo
            {
                void Run(CancellationToken ct)
                {
                    var minId = 5;
                    _ = dbContext.Users.Where(u => u.Id > minId).ToListAsync(ct);
                }
            }
            """;

        var capturePlan = BuildCapturePlanAtMarker(source, "ToListAsync");

        var minId = Assert.Single(capturePlan.Entries, e => string.Equals(e.Name, "minId", StringComparison.Ordinal));
        Assert.Equal(LocalSymbolReplayPolicies.ReplayInitializer, minId.CapturePolicy);
        Assert.Equal("5", minId.InitializerExpression);
        Assert.Empty(minId.Dependencies);
        Assert.Empty(capturePlan.Diagnostics);
    }

    [Fact]
    public void TryBuildV2CapturePlan_QueryExpressionParameter_UsesPlaceholder()
    {
        var source = """
            class Demo
            {
                void Run(Guid customerId, CancellationToken ct)
                {
                    _ = (from u in dbContext.Users
                         where u.CustomerId == customerId
                         select u)
                        .ToListAsync(ct);
                }
            }
            """;

        var capturePlan = BuildCapturePlanAtMarker(source, "where u.CustomerId == customerId");

        var customerId = Assert.Single(capturePlan.Entries, e => string.Equals(e.Name, "customerId", StringComparison.Ordinal));
        Assert.Equal(LocalSymbolReplayPolicies.UsePlaceholder, customerId.CapturePolicy);
        Assert.Null(customerId.InitializerExpression);
        Assert.Empty(customerId.Dependencies);
        Assert.Empty(capturePlan.Diagnostics);
    }

    [Fact]
    public void TryBuildV2CapturePlan_HelperComposedExpressionParameter_CapturesDeterministically()
    {
        var source = """
            class Demo
            {
                IQueryable<User> ApplyFilter(Expression<Func<User, bool>> whereExpression)
                {
                    return dbContext.Users.Where(whereExpression);
                }

                void Run(CancellationToken ct)
                {
                    Expression<Func<User, bool>> whereExpression = u => u.IsActive;
                    _ = ApplyFilter(whereExpression).ToListAsync(ct);
                }
            }
            """;

        var capturePlan = BuildCapturePlanAtMarker(source, "ApplyFilter(whereExpression)");

        Assert.Empty(capturePlan.Diagnostics);
        Assert.True(capturePlan.IsComplete);
    }

    [Fact]
    public void TryBuildV2CapturePlan_StringSearchTerm_UsesTypedPlaceholder()
    {
        var source = """
            class Demo
            {
                void Run(string term, CancellationToken ct)
                {
                    _ = dbContext.Customers
                        .Where(c => c.Name.Contains(term) || c.Email.StartsWith(term))
                        .ToListAsync(ct);
                }
            }
            """;

        var capturePlan = BuildCapturePlanAtMarker(source, "ToListAsync");

        var term = Assert.Single(capturePlan.Entries, e => string.Equals(e.Name, "term", StringComparison.Ordinal));
        Assert.Equal(LocalSymbolReplayPolicies.UsePlaceholder, term.CapturePolicy);
        Assert.True(
            string.Equals(term.TypeName, "string", StringComparison.OrdinalIgnoreCase)
            || term.TypeName.Contains("System.String", StringComparison.Ordinal),
            $"Expected string capture type for 'term' but got '{term.TypeName}'.");
        Assert.Empty(capturePlan.Diagnostics);
        Assert.True(capturePlan.IsComplete);
    }

    [Fact]
    public void BuildV2CapturePlanFromGraph_MissingDependency_RejectedWithDiagnostic()
    {
        var graph = new[]
        {
            new LocalSymbolGraphEntry
            {
                Name = "baseValue",
                TypeName = "global::System.Int32",
                Kind = "local",
                InitializerExpression = "10",
                DeclarationOrder = 1,
                Dependencies = [],
                ReplayPolicy = LocalSymbolReplayPolicies.ReplayInitializer,
            },
            new LocalSymbolGraphEntry
            {
                Name = "threshold",
                TypeName = "global::System.Int32",
                Kind = "local",
                InitializerExpression = "baseValue + missingVar",
                DeclarationOrder = 2,
                Dependencies = ["baseValue", "missingVar"],
                ReplayPolicy = LocalSymbolReplayPolicies.ReplayInitializer,
            },
        };

        var capturePlan = LspSyntaxHelper.BuildV2CapturePlanFromGraph(
            "dbContext.Users.Where(u => u.Id > threshold).ToListAsync(ct)",
            graph);

        Assert.False(capturePlan.IsComplete);
        Assert.Contains(capturePlan.Diagnostics, d => d.Code == "QLV2_CAPTURE_MISSING_DEPENDENCY" && d.SymbolName == "threshold");
        Assert.Contains(
            capturePlan.Entries,
            e => string.Equals(e.Name, "threshold", StringComparison.Ordinal)
                && string.Equals(e.CapturePolicy, LocalSymbolReplayPolicies.Reject, StringComparison.Ordinal));
    }

    private static V2CapturePlanSnapshot BuildCapturePlanAtMarker(string source, string marker)
    {
        var (line, character) = FindPosition(source, marker);

        var extractionSuccess = LspSyntaxHelper.TryBuildV2ExtractionPlan(
            source,
            @"c:\repo\Demo.cs",
            line,
            character,
            out var extractionPlan,
            out var extractionDiagnostics);

        Assert.True(extractionSuccess);
        Assert.NotNull(extractionPlan);
        Assert.Empty(extractionDiagnostics);

        var helperSubstitutionApplied = string.Equals(extractionPlan!.Origin.Scope, "helper-method", StringComparison.Ordinal);

        var captureSuccess = LspSyntaxHelper.TryBuildV2CapturePlan(
            extractionPlan.Expression,
            extractionPlan.ContextVariableName,
            source,
            extractionPlan.Origin.Line,
            extractionPlan.Origin.Character,
            targetAssemblyPath: null,
            out var capturePlan,
            secondarySourceText: helperSubstitutionApplied ? source : null,
            secondaryLine: helperSubstitutionApplied ? line : null,
            secondaryCharacter: helperSubstitutionApplied ? character : null,
            dbContextTypeName: null);

        Assert.NotNull(capturePlan);
        if (!captureSuccess)
        {
            // Explicitly returning the plan on failure allows reject-path tests to assert diagnostics.
            return capturePlan;
        }

        return capturePlan;
    }
}
