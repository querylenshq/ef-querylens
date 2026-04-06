// Unit tests for factory-root receiver substitution in LINQ expression capture.
using EFQueryLens.Lsp.Parsing;
using Xunit;

namespace EFQueryLens.Core.Tests.Lsp;

public class FactoryRootSubstitutionTests
{
    // --- Positive cases (detection and rewrite should succeed) ---

    [Fact]
    public void TrySubstituteFactoryRoot_AsyncWithChain_SampleAppPattern_ReplacesReceiver()
    {
        // This is the exact pattern that appears in SampleDbContextFactoryApp.
        var expression = "(await _contextFactory.CreateDbContextAsync(ct)).Rationales\n            .AsNoTracking()\n            .OrderBy(x => x.Title)\n            .ToListAsync(ct)";
        var candidates = new[] { "ApplicationDbContext" };

        var (rewritten, applied, contextType) = LspSyntaxHelper.TrySubstituteFactoryRoot(
            expression, "dbContext", candidates);

        Assert.True(applied, "Factory root should be detected in the sample app pattern");
        Assert.Contains("__qlFactoryContext", rewritten);
        Assert.DoesNotContain("CreateDbContextAsync", rewritten);
        Assert.DoesNotContain("_contextFactory", rewritten);
        Assert.Equal("ApplicationDbContext", contextType);
    }

    [Fact]
    public void TrySubstituteFactoryRoot_AsyncWithChain_PreservesDownstreamOperations()
    {
        var expression = "(await _contextFactory.CreateDbContextAsync(ct)).DbSet<User>().Where(u => u.Active).OrderBy(u => u.Name).ToListAsync(ct)";
        var candidates = new[] { "ApplicationDbContext" };

        var (rewritten, applied, _) = LspSyntaxHelper.TrySubstituteFactoryRoot(
            expression, "dbContext", candidates);

        Assert.True(applied);
        Assert.Contains("__qlFactoryContext", rewritten);
        Assert.DoesNotContain("CreateDbContextAsync", rewritten);
        // Chain operations must be preserved
        Assert.Contains("Where", rewritten);
        Assert.Contains("OrderBy", rewritten);
        Assert.Contains("ToListAsync", rewritten);
    }

    [Fact]
    public void TrySubstituteFactoryRoot_AsyncWithoutParens_SimpleAwait_ReplacesReceiver()
    {
        // Simpler pattern: await factory.CreateDbContextAsync(ct) without (...)
        // (less common in real code, but should still work)
        var expression = "await _contextFactory.CreateDbContextAsync(ct)";
        var candidates = new[] { "ApplicationDbContext" };

        var (rewritten, applied, _) = LspSyntaxHelper.TrySubstituteFactoryRoot(
            expression, "dbContext", candidates);

        Assert.True(applied);
        Assert.Contains("__qlFactoryContext", rewritten);
    }

    [Fact]
    public void TrySubstituteFactoryRoot_SyncWithChain_ReplacesReceiver()
    {
        var expression = "_contextFactory.CreateDbContext().Set<User>().AsNoTracking().ToList()";
        var candidates = new[] { "ApplicationDbContext" };

        var (rewritten, applied, _) = LspSyntaxHelper.TrySubstituteFactoryRoot(
            expression, "dbContext", candidates);

        Assert.True(applied, "Sync pattern should be detected");
        Assert.Contains("__qlFactoryContext", rewritten);
        Assert.DoesNotContain("CreateDbContext()", rewritten);
    }

    [Fact]
    public void TrySubstituteFactoryRoot_NullCandidates_StillDetectsPattern()
    {
        // When candidates list is null/empty, ambiguity gate doesn't apply.
        var expression = "(await _contextFactory.CreateDbContextAsync(ct)).Users.ToListAsync(ct)";

        var (rewritten, applied, _) = LspSyntaxHelper.TrySubstituteFactoryRoot(
            expression, "dbContext", null);

        Assert.True(applied, "Factory pattern should be detected even with null candidates");
        Assert.Contains("__qlFactoryContext", rewritten);
    }

    // --- Negative cases (no detection) ---

    [Fact]
    public void TrySubstituteFactoryRoot_NoFactoryPattern_ReturnsOriginal()
    {
        var expression = "users.Where(u => u.Id == 1)";
        var candidates = new[] { "ApplicationDbContext" };

        var (rewritten, applied, contextType) = LspSyntaxHelper.TrySubstituteFactoryRoot(
            expression, "users", candidates);

        Assert.False(applied, "No factory pattern should be detected");
        Assert.Equal(expression, rewritten);
        Assert.Null(contextType);
    }

    [Fact]
    public void TrySubstituteFactoryRoot_MultipleCandidates_SkipsSubstitution()
    {
        // Ambiguity gate: when multiple factory candidates exist without a clear winner,
        // don't rewrite to avoid cross-DbContext execution.
        var expression = "(await _contextFactory.CreateDbContextAsync(ct)).Users.ToListAsync(ct)";
        var candidates = new[] { "ApplicationDbContext", "CatalogDbContext" };

        var (rewritten, applied, _) = LspSyntaxHelper.TrySubstituteFactoryRoot(
            expression, "dbContext", candidates);

        Assert.False(applied, "Substitution should be skipped when multiple factory candidates exist");
        Assert.Equal(expression, rewritten);
    }

    [Fact]
    public void TrySubstituteFactoryRoot_EmptyExpression_ReturnsEmpty()
    {
        var (rewritten, applied, _) = LspSyntaxHelper.TrySubstituteFactoryRoot(
            "", "dbContext", ["ApplicationDbContext"]);

        Assert.False(applied);
        Assert.Equal("", rewritten);
    }

    [Fact]
    public void TrySubstituteFactoryRoot_NormalDbContextVariable_ReturnsOriginal()
    {
        // Direct DbContext variable usage — must not be rewritten.
        var expression = "_db.Users.Where(u => u.Active).ToList()";
        var candidates = new[] { "ApplicationDbContext" };

        var (rewritten, applied, _) = LspSyntaxHelper.TrySubstituteFactoryRoot(
            expression, "_db", candidates);

        Assert.False(applied, "Direct DbContext variable should not be detected as factory pattern");
        Assert.Equal(expression, rewritten);
    }

    [Fact]
    public void TrySubstituteFactoryRoot_RobustToMalformedExpression_DoesNotCrash()
    {
        var (rewritten, applied, _) = LspSyntaxHelper.TrySubstituteFactoryRoot(
            "ctx)", "context", ["ApplicationDbContext"]);

        Assert.False(applied, "Malformed expression should not crash");
    }
}
