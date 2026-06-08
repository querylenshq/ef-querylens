namespace EFQueryLens.Core.Scaffolding;

/// <summary>Joins assembly-discovered DbContext full names with source registrations.</summary>
public static class ContextRegistrationMatcher
{
    public static DbContextRegistration? FindRegistration(
        string contextFullName,
        IReadOnlyList<DbContextRegistration> registrations)
    {
        foreach (var registration in registrations)
        {
            if (Matches(contextFullName, registration.ContextTypeName))
            {
                return registration;
            }
        }

        return null;
    }

    public static IReadOnlyList<ContextRenderPlan> BuildRenderPlans(
        IReadOnlyList<string> contextFullNames,
        IReadOnlyList<DbContextRegistration> registrations,
        ProviderDetector.Result detection,
        ProviderKind providerOverride)
    {
        var plans = new List<ContextRenderPlan>();

        foreach (var contextFullName in contextFullNames)
        {
            var registration = FindRegistration(contextFullName, registrations);
            if (registration is not null)
            {
                var rewritten = OfflineChainRewriter.Rewrite(registration);
                plans.Add(new ContextRenderPlan
                {
                    ContextFullName = contextFullName,
                    Provider = rewritten.Provider,
                    UseProjectables = rewritten.UseProjectables,
                    UseSplitQuery = rewritten.UseSplitQuery,
                    OfflineOptionsChain = rewritten.OfflineChain.StartsWith(".", StringComparison.Ordinal)
                        ? rewritten.OfflineChain
                        : "." + rewritten.OfflineChain,
                    MatchedRegistration = true,
                    ExtraUsings = rewritten.RequiredUsings,
                });
                continue;
            }

            var provider = providerOverride != ProviderKind.Unknown
                ? providerOverride
                : detection.Provider;

            plans.Add(new ContextRenderPlan
            {
                ContextFullName = contextFullName,
                Provider = provider,
                UseProjectables = detection.UseProjectables,
                UseSplitQuery = true,
                OfflineOptionsChain = null,
                MatchedRegistration = false,
                ExtraUsings = [],
            });
        }

        return plans;
    }

    internal static bool Matches(string contextFullName, string registrationTypeName)
    {
        if (string.Equals(contextFullName, registrationTypeName, StringComparison.Ordinal))
        {
            return true;
        }

        if (contextFullName.EndsWith("." + registrationTypeName, StringComparison.Ordinal))
        {
            return true;
        }

        var contextSimple = SimpleName(contextFullName);
        var registrationSimple = SimpleName(registrationTypeName);
        return string.Equals(contextSimple, registrationSimple, StringComparison.Ordinal);
    }

    private static string SimpleName(string fullName)
    {
        var lastDot = fullName.LastIndexOf('.');
        var name = lastDot >= 0 ? fullName[(lastDot + 1)..] : fullName;
        var plus = name.LastIndexOf('+');
        return plus >= 0 ? name[(plus + 1)..] : name;
    }
}
