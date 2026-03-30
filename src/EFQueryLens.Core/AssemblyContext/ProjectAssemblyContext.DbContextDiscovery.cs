using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFQueryLens.Core.AssemblyContext;

public sealed partial class ProjectAssemblyContext
{
    /// <summary>
    /// Returns all concrete (non-abstract) DbContext subclasses found across
    /// <b>all assemblies</b> currently loaded into this context (including any
    /// additional assemblies preloaded via <see cref="LoadAdditionalAssembly"/>).
    /// Walks the full inheritance chain by type name, because the DbContext type
    /// in the user's ALC is a different runtime instance than the one in the
    /// tool's default load context.
    /// </summary>
    public IReadOnlyList<Type> FindDbContextTypes()
    {
        EnsureNotDisposed();

        var results = new List<Type>();

        // Scan ALL loaded assemblies - not just the primary target - so that
        // projects which place DbContext in a class library dependency are
        // discovered correctly after LoadAdditionalAssembly() is called.
        foreach (var asm in LoadedAssemblies)
        {
            IEnumerable<Type> candidates;
            try
            {
                candidates = asm.GetExportedTypes();
            }
            catch (ReflectionTypeLoadException rtle)
            {
                // Some types in the assembly couldn't be loaded (e.g. a factory
                // class that references a design-time interface whose assembly is
                // not in the probe paths). The successfully loaded types are still
                // in rtle.Types - use them so that DbContext types aren't missed.
                candidates = rtle.Types.Where(t => t is not null)!;
            }
            catch
            {
                // Truly broken assembly - skip entirely.
                continue;
            }

            foreach (var type in candidates)
            {
                try
                {
                    if (!type.IsAbstract && IsDbContextSubclass(type))
                        results.Add(type);
                }
                catch
                {
                    // IsDbContextSubclass may throw for partially-loaded types.
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Resolves a single DbContext type from the loaded assembly.
    /// </summary>
    /// <param name="typeName">
    ///   Simple name ("AppDbContext") or fully qualified name
    ///   ("SampleApp.AppDbContext"). Pass null to auto-discover when exactly
    ///   one DbContext exists in the assembly.
    /// </param>
    /// <param name="expressionHint"></param>
    /// <exception cref="InvalidOperationException">
    ///   No DbContext found; multiple found with null typeName; or no match
    ///   for the provided typeName.
    /// </exception>
    public Type FindDbContextType(string? typeName = null, string? expressionHint = null)
    {
        EnsureNotDisposed();

        var all = FindDbContextTypes();

        if (all.Count == 0)
            throw new DbContextDiscoveryException(
                DbContextDiscoveryFailureKind.NoDbContextFound,
                $"No DbContext subclass found in '{Path.GetFileName(AssemblyPath)}'.");

        if (typeName is null)
        {
            if (all.Count == 1)
                return all[0];

            // Auto-disambiguate using the LINQ expression: extract the first property
            // access (e.g. "AppWorkflows" from "dbContext.AppWorkflows.Include(...)") and
            // find which DbContext owns a DbSet/IQueryable property with that name.
            if (expressionHint is not null)
            {
                var dbSetName = ExtractFirstPropertyAccess(expressionHint);
                if (dbSetName is not null)
                {
                    var match = all.FirstOrDefault(t =>
                        t.GetProperties().Any(p =>
                            string.Equals(p.Name, dbSetName, StringComparison.Ordinal)));

                    if (match is not null)
                        return match;
                }
            }

            // Fallback: filter out obvious test/utility DbContexts.
            var filtered = all.Where(t =>
            {
                var name = t.Name;
                return !name.Contains("Test", StringComparison.OrdinalIgnoreCase) &&
                       !name.Contains("Empty", StringComparison.OrdinalIgnoreCase) &&
                       !name.Contains("Mock", StringComparison.OrdinalIgnoreCase);
            }).ToList();

            if (filtered.Count == 1)
                return filtered[0];

            var candidates = filtered.Count > 1 ? filtered : all;
            throw new DbContextDiscoveryException(
                DbContextDiscoveryFailureKind.MultipleDbContextsFound,
                $"Multiple DbContext types found in '{Path.GetFileName(AssemblyPath)}': " +
                $"{string.Join(", ", candidates.Select(t => t.FullName))}. " +
                "Specify --context to disambiguate.");
        }

        // Match on concrete DbContext simple/FQ name first for backwards compatibility.
        var nameMatch = all.FirstOrDefault(t =>
            string.Equals(t.Name, typeName, StringComparison.Ordinal) ||
            string.Equals(t.FullName, typeName, StringComparison.Ordinal));

        if (nameMatch is not null)
            return nameMatch;

        // If no concrete type matched, treat the provided name as a potential interface
        // implemented by one or more DbContext classes (common with DI abstractions).
        var interfaceMatches = all.Where(t =>
                t.GetInterfaces().Any(i =>
                    string.Equals(i.Name, typeName, StringComparison.Ordinal) ||
                    string.Equals(i.FullName, typeName, StringComparison.Ordinal)))
            .ToList();

        if (interfaceMatches.Count == 1)
            return interfaceMatches[0];

        if (interfaceMatches.Count > 1)
        {
            if (expressionHint is not null)
            {
                var dbSetName = ExtractFirstPropertyAccess(expressionHint);
                if (dbSetName is not null)
                {
                    var hintMatch = interfaceMatches.FirstOrDefault(t =>
                        t.GetProperties().Any(p => string.Equals(p.Name, dbSetName, StringComparison.Ordinal)));

                    if (hintMatch is not null)
                        return hintMatch;
                }
            }

            throw new DbContextDiscoveryException(
                DbContextDiscoveryFailureKind.MultipleDbContextsFound,
                $"Multiple DbContext types implement interface '{typeName}' in '{Path.GetFileName(AssemblyPath)}': " +
                $"{string.Join(", ", interfaceMatches.Select(t => t.FullName))}. " +
                "Specify --context with a concrete DbContext type name to disambiguate.");
        }

        throw new InvalidOperationException(
            $"DbContext type '{typeName}' not found in '{Path.GetFileName(AssemblyPath)}'. " +
            $"Available: {string.Join(", ", all.Select(t => t.FullName))}");
    }

    /// <summary>
    /// Extracts the first member access from a LINQ expression.
    /// e.g. "dbContext.AppWorkflows.Include(...)" -> "AppWorkflows"
    ///      "db.Orders.Where(...)" -> "Orders"
    /// </summary>
    private static string? ExtractFirstPropertyAccess(string expression)
    {
        var tree = CSharpSyntaxTree.ParseText(
            expression,
            CSharpParseOptions.Default.WithKind(SourceCodeKind.Script));

        var root = tree.GetRoot();

        var memberAccess = root.DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .FirstOrDefault(static m => IsRootContextExpression(m.Expression));
        if (memberAccess is not null)
        {
            return memberAccess.Name.Identifier.Text;
        }

        var conditionalAccess = root.DescendantNodes()
            .OfType<ConditionalAccessExpressionSyntax>()
            .FirstOrDefault(static c => IsRootContextExpression(c.Expression));
        if (conditionalAccess?.WhenNotNull is MemberBindingExpressionSyntax memberBinding)
        {
            return memberBinding.Name.Identifier.Text;
        }

        var fallbackMemberBinding = root.DescendantNodes()
            .OfType<MemberBindingExpressionSyntax>()
            .FirstOrDefault();
        return fallbackMemberBinding?.Name.Identifier.Text;

        static bool IsRootContextExpression(ExpressionSyntax expression) => expression switch
        {
            IdentifierNameSyntax => true,
            ParenthesizedExpressionSyntax { Expression: IdentifierNameSyntax } => true,
            _ => false,
        };
    }

    /// <summary>
    /// Walks the base-type chain by FullName to detect DbContext subclasses.
    /// Name-based comparison is required because the DbContext type loaded
    /// inside the ALC is a different runtime type than typeof(DbContext) in
    /// the host process - even though they are semantically the same class.
    /// </summary>
    private static bool IsDbContextSubclass(Type type)
    {
        const string dbContextFullName = "Microsoft.EntityFrameworkCore.DbContext";
        var current = type.BaseType;
        while (current is not null)
        {
            if (current.FullName == dbContextFullName)
                return true;
            current = current.BaseType;
        }

        return false;
    }
}
