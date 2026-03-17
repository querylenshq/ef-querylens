namespace EFQueryLens.Core.Grpc;

using Domain = EFQueryLens.Core;

public static partial class GrpcMappingExtensions
{
    public static TranslationMetadata ToProto(this Contracts.TranslationMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var proto = new TranslationMetadata
        {
            DbContextType = metadata.DbContextType ?? string.Empty,
            EfCoreVersion = metadata.EfCoreVersion ?? string.Empty,
            ProviderName = metadata.ProviderName ?? string.Empty,
            TranslationTimeTicks = metadata.TranslationTime.Ticks,
            HasClientEvaluation = metadata.HasClientEvaluation,
            CreationStrategy = metadata.CreationStrategy ?? "unknown",
        };

        if (metadata.ContextResolutionTime is { } contextResolution)
        {
            proto.ContextResolutionTimeTicks = contextResolution.Ticks;
        }

        if (metadata.DbContextCreationTime is { } dbContextCreation)
        {
            proto.DbContextCreationTimeTicks = dbContextCreation.Ticks;
        }

        if (metadata.MetadataReferenceBuildTime is { } metadataReferenceBuild)
        {
            proto.MetadataReferenceBuildTimeTicks = metadataReferenceBuild.Ticks;
        }

        if (metadata.RoslynCompilationTime is { } roslynCompilation)
        {
            proto.RoslynCompilationTimeTicks = roslynCompilation.Ticks;
        }

        if (metadata.CompilationRetryCount is { } retryCount)
        {
            proto.CompilationRetryCount = retryCount;
        }

        if (metadata.EvalAssemblyLoadTime is { } evalAssemblyLoad)
        {
            proto.EvalAssemblyLoadTimeTicks = evalAssemblyLoad.Ticks;
        }

        if (metadata.RunnerExecutionTime is { } runnerExecution)
        {
            proto.RunnerExecutionTimeTicks = runnerExecution.Ticks;
        }

        if (metadata.ToQueryStringFallbackTime is { } fallbackTime)
        {
            proto.ToQueryStringFallbackTimeTicks = fallbackTime.Ticks;
        }

        return proto;
    }

    public static Contracts.TranslationMetadata ToDomain(this TranslationMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        return new Contracts.TranslationMetadata
        {
            DbContextType = metadata.DbContextType,
            EfCoreVersion = metadata.EfCoreVersion,
            ProviderName = metadata.ProviderName,
            TranslationTime = TimeSpan.FromTicks(metadata.TranslationTimeTicks),
            ContextResolutionTime = metadata.HasContextResolutionTimeTicks
                ? TimeSpan.FromTicks(metadata.ContextResolutionTimeTicks)
                : null,
            DbContextCreationTime = metadata.HasDbContextCreationTimeTicks
                ? TimeSpan.FromTicks(metadata.DbContextCreationTimeTicks)
                : null,
            MetadataReferenceBuildTime = metadata.HasMetadataReferenceBuildTimeTicks
                ? TimeSpan.FromTicks(metadata.MetadataReferenceBuildTimeTicks)
                : null,
            RoslynCompilationTime = metadata.HasRoslynCompilationTimeTicks
                ? TimeSpan.FromTicks(metadata.RoslynCompilationTimeTicks)
                : null,
            CompilationRetryCount = metadata.HasCompilationRetryCount
                ? metadata.CompilationRetryCount
                : null,
            EvalAssemblyLoadTime = metadata.HasEvalAssemblyLoadTimeTicks
                ? TimeSpan.FromTicks(metadata.EvalAssemblyLoadTimeTicks)
                : null,
            RunnerExecutionTime = metadata.HasRunnerExecutionTimeTicks
                ? TimeSpan.FromTicks(metadata.RunnerExecutionTimeTicks)
                : null,
            ToQueryStringFallbackTime = metadata.HasToQueryStringFallbackTimeTicks
                ? TimeSpan.FromTicks(metadata.ToQueryStringFallbackTimeTicks)
                : null,
            HasClientEvaluation = metadata.HasClientEvaluation,
            CreationStrategy = metadata.CreationStrategy,
        };
    }
}
