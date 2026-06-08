using EFQueryLens.Core.AssemblyContext;

namespace EFQueryLens.Core.Scaffolding;

/// <summary>
/// Generates an offline <c>IQueryLensDbContextFactory&lt;T&gt;</c> factory for a project so users
/// don't have to hand-write and commit one.
/// </summary>
public static class FactoryScaffolder
{
    public static readonly string[] GeneratedRelativePath = ["Properties", "QueryLens", "QueryLensDbContextFactory.g.cs"];
    public const string GitignoreRule = "Properties/QueryLens/";

    public static SetupResult Run(SetupRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.AssemblyPath) || !File.Exists(request.AssemblyPath))
        {
            return new SetupResult
            {
                Action = SetupAction.NotBuilt,
                Message = "Build the project first, then run Set up QueryLens again.",
            };
        }

        var generatedPath = Path.Combine(new[] { request.ProjectDirectory }.Concat(GeneratedRelativePath).ToArray());

        if (HandWrittenFactoryExists(request.ProjectDirectory, generatedPath))
        {
            return new SetupResult
            {
                Action = SetupAction.SkippedExistingFactory,
                Message = "An IQueryLensDbContextFactory<T> already exists in this project.",
            };
        }

        IReadOnlyList<string> contexts;
        try
        {
            using var ctx = ShadowAssemblyContextLoader.Create(request.AssemblyPath);
            contexts = ctx.FindDbContextTypes()
                .Select(t => t.FullName)
                .Where(static n => !string.IsNullOrWhiteSpace(n))
                .Select(static n => n!)
                .Where(static n => !n.StartsWith("Microsoft.", StringComparison.Ordinal)
                                   && !n.StartsWith("System.", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static n => n, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex)
        {
            return new SetupResult
            {
                Action = SetupAction.NotBuilt,
                Message = $"Could not load the built assembly to discover DbContext types: {ex.Message}",
            };
        }

        if (contexts.Count == 0)
        {
            return new SetupResult
            {
                Action = SetupAction.NoDbContext,
                Message = "No DbContext was found in the executable assembly.",
            };
        }

        var registrations = RegistrationScanner.Scan(request.ProjectDirectory);
        var detection = ProviderDetector.Detect(request.AssemblyPath, request.ProjectDirectory);
        var renderPlans = ContextRegistrationMatcher.BuildRenderPlans(
            contexts,
            registrations,
            detection,
            request.ProviderOverride);

        if (renderPlans.Any(plan => plan.OfflineOptionsChain is null && plan.Provider == ProviderKind.Unknown))
        {
            return new SetupResult
            {
                Action = SetupAction.NeedProvider,
                Contexts = contexts,
                Message = "Could not determine the EF Core provider automatically. Re-run with an explicit provider (SqlServer, Npgsql, MySql, or Sqlite).",
            };
        }

        var primaryProvider = renderPlans
            .Select(plan => plan.Provider)
            .FirstOrDefault(provider => provider != ProviderKind.Unknown, ProviderKind.Unknown);

        var contextsNeedingReview = renderPlans
            .Where(plan => !plan.MatchedRegistration)
            .Select(plan => plan.ContextFullName)
            .ToList();
        var usedBestEffortDefaults = contextsNeedingReview.Count > 0;

        var content = FactoryRenderer.Render(renderPlans);

        if (File.Exists(generatedPath))
        {
            var existing = File.ReadAllText(generatedPath);

            if (!request.Force && WasEditedByHand(existing))
            {
                return new SetupResult
                {
                    Action = SetupAction.RefusedEdited,
                    Provider = primaryProvider,
                    Contexts = contexts,
                    GeneratedFilePath = generatedPath,
                    Message = "The generated factory was edited by hand. Re-run with force to overwrite.",
                };
            }

            if (FactoryRenderer.NormalizeNewlines(existing) == FactoryRenderer.NormalizeNewlines(content))
            {
                var giNoop = request.UpdateGitignore && GitignoreWriter.EnsureRule(request.ProjectDirectory, GitignoreRule);
                return new SetupResult
                {
                    Action = SetupAction.SkippedUpToDate,
                    Provider = primaryProvider,
                    Contexts = contexts,
                    GeneratedFilePath = generatedPath,
                    GitignoreUpdated = giNoop,
                    Message = "QueryLens factory is already up to date.",
                };
            }
        }

        var directory = Path.GetDirectoryName(generatedPath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(generatedPath, content);

        var gitignoreUpdated = request.UpdateGitignore && GitignoreWriter.EnsureRule(request.ProjectDirectory, GitignoreRule);

        return new SetupResult
        {
            Action = SetupAction.Generated,
            Provider = primaryProvider,
            Contexts = contexts,
            GeneratedFilePath = generatedPath,
            GitignoreUpdated = gitignoreUpdated,
            RequiresReview = true,
            UsedBestEffortDefaults = usedBestEffortDefaults,
            ContextsNeedingReview = contextsNeedingReview,
            Message = SetupResultMessages.BuildGeneratedMessage(
                contexts.Count,
                primaryProvider,
                usedBestEffortDefaults,
                contextsNeedingReview),
        };
    }

    private static bool WasEditedByHand(string existingContent)
    {
        if (!FactoryRenderer.TryReadRecordedHash(existingContent, out var recordedHash, out var body))
        {
            return true;
        }

        return !string.Equals(recordedHash, FactoryRenderer.ComputeHash(body), StringComparison.OrdinalIgnoreCase);
    }

    private static bool HandWrittenFactoryExists(string projectDirectory, string generatedPath)
    {
        if (!Directory.Exists(projectDirectory))
        {
            return false;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories);
        }
        catch
        {
            return false;
        }

        var normalizedGenerated = Path.GetFullPath(generatedPath);

        foreach (var file in files)
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(Path.GetFullPath(file), normalizedGenerated, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                if (File.ReadAllText(file).Contains("IQueryLensDbContextFactory<", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            catch { /* ignore unreadable file */ }
        }

        return false;
    }
}
