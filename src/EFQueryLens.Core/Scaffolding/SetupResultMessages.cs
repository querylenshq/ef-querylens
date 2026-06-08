namespace EFQueryLens.Core.Scaffolding;

internal static class SetupResultMessages
{
    internal static string BuildGeneratedMessage(
        int contextCount,
        ProviderKind provider,
        bool requiresReview,
        IReadOnlyList<string> contextsNeedingReview)
    {
        var message =
            $"Generated QueryLens factory for {contextCount} DbContext(s) using {provider}. "
            + "Rebuild the project to enable SQL preview. "
            + "Please review Properties/QueryLens/QueryLensDbContextFactory.g.cs and confirm each "
            + "CreateOfflineContext() matches your AddDbContext configuration.";

        if (!requiresReview || contextsNeedingReview.Count == 0)
        {
            return message;
        }

        var reviewTargets = FormatReviewTargets(contextsNeedingReview);
        return message
               + " QueryLens used best-effort defaults for "
               + reviewTargets
               + " because no matching AddDbContext registration was found.";
    }

    private static string FormatReviewTargets(IReadOnlyList<string> contextsNeedingReview)
    {
        if (contextsNeedingReview.Count == 0)
        {
            return "one or more DbContexts";
        }

        if (contextsNeedingReview.Count == 1)
        {
            return SimpleName(contextsNeedingReview[0]);
        }

        if (contextsNeedingReview.Count <= 3)
        {
            return string.Join(", ", contextsNeedingReview.Select(SimpleName));
        }

        return $"{contextsNeedingReview.Count} DbContexts";
    }

    private static string SimpleName(string fullName)
    {
        var lastDot = fullName.LastIndexOf('.');
        var name = lastDot >= 0 ? fullName[(lastDot + 1)..] : fullName;
        var plus = name.LastIndexOf('+');
        return plus >= 0 ? name[(plus + 1)..] : name;
    }
}
