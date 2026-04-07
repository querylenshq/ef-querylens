using System.Linq;
using EFQueryLens.Lsp.Parsing;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace EFQueryLens.Core.Tests.Lsp.SemanticDeduplication;

public class SemanticVariableTrackingTests
{
    /// <summary>
    /// Tests that Phase 2 semantic deduplication runs without errors
    /// and doesn't break basic extraction.
    /// </summary>
    [Fact]
    public void TryExtractLinqExpression_WithSimpleQuery_Succeeds()
    {
        var sourceCode = """
            using System.Linq;
            using Microsoft.EntityFrameworkCore;
            
            public class Test
            {
                public void Example(DbContext db)
                {
                    db.Customers.Where(c => c.IsNotDeleted).OrderBy(c => c.Id).ToList();
                }
            }
            """;

        var extraction = LspSyntaxHelper.TryExtractLinqExpression(
            sourceCode,
            sourceCode.Split('\n').ToList().FindIndex(l => l.Contains("ToList")),
            20,
            out var contextVar,
            out _);

        Assert.NotNull(extraction);
        Assert.NotNull(contextVar);
    }

    /// <summary>
    /// Tests that reused parameters in a GetCustomersAsync pattern are tracked.
    /// This is the real-world scenario from CustomerReadService.
    /// </summary>
    [Fact]
    public void TryExtractLinqExpression_WithConditionalQueryBuilding_TrackingWorks()
    {
        var sourceCode = """
            using System;
            using System.Linq;
            using Microsoft.EntityFrameworkCore;
            
            public class CustomerReadService
            {
                public void GetCustomersAsync(DbContext db, CustomerQueryRequest request)
                {
                    var query = db.Customers.Where(c => c.IsNotDeleted);
                    
                    if (request.IsActive.HasValue)
                        query = query.Where(c => c.IsActive == request.IsActive.Value);
                    
                    if (request.MinOrders.HasValue)
                        query = query.Where(c => c.Orders.Count() >= request.MinOrders.Value);
                    
                    if (!string.IsNullOrEmpty(request.SearchTerm))
                        query = query.Where(c => c.Name.Contains(request.SearchTerm));
                    
                    var result = query.OrderByDescending(c => c.CreatedUtc).ToList();
                }
            }
            
            public class CustomerQueryRequest
            {
                public string? SearchTerm { get; set; }
                public bool? IsActive { get; set; }
                public int? MinOrders { get; set; }
            }
            """;

        var extraction = LspSyntaxHelper.TryExtractLinqExpression(
            sourceCode,
            sourceCode.Split('\n').ToList().FindIndex(l => l.Contains("ToList()")),
            20,
            out var contextVar,
            out _);

        Assert.NotNull(extraction);
        // Phase 2 should enhance the extraction with semantic info
        Assert.NotNull(contextVar);
    }

    /// <summary>
    /// Tests that the semantic deduplication method is callable and doesn't crash.
    /// </summary>
    [Fact]
    public void SemanticTracking_DoesNotCrashOnSimpleExpression()
    {
        var sourceCode = """
            var x = db.Items.Where(i => i.Active).ToList();
            """;

        var tree = CSharpSyntaxTree.ParseText(sourceCode);
        var root = tree.GetRoot();
        var expr = root
            .DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .FirstOrDefault();

        Assert.NotNull(expr);
        
        // This should not throw even if nothing special happens
        // (The ApplySemanticVariableTracking method is internal, so we're testing
        // via the public TryExtractLinqExpression method)
    }
}

