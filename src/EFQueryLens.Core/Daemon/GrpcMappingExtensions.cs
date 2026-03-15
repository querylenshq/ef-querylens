using System.Collections.Generic;
using System.Linq;

namespace EFQueryLens.Core.Grpc;

using Domain = EFQueryLens.Core;

public static class GrpcMappingExtensions
{
    public static TranslationRequestPayload ToProto(this Domain.TranslationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = new TranslationRequestPayload
        {
            Expression = request.Expression ?? string.Empty,
            AssemblyPath = request.AssemblyPath ?? string.Empty,
            ContextVariableName = string.IsNullOrWhiteSpace(request.ContextVariableName)
                ? "db"
                : request.ContextVariableName,
        };

        if (!string.IsNullOrWhiteSpace(request.DbContextTypeName))
        {
            payload.DbContextTypeName = request.DbContextTypeName;
        }

        payload.AdditionalImports.AddRange(request.AdditionalImports ?? []);

        if (request.UsingAliases is not null)
        {
            foreach (var pair in request.UsingAliases)
            {
                payload.UsingAliases[pair.Key] = pair.Value;
            }
        }

        payload.UsingStaticTypes.AddRange(request.UsingStaticTypes ?? []);
        return payload;
    }

    public static Domain.TranslationRequest ToDomain(this TranslationRequestPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        return new Domain.TranslationRequest
        {
            Expression = payload.Expression,
            AssemblyPath = payload.AssemblyPath,
            DbContextTypeName = payload.HasDbContextTypeName ? payload.DbContextTypeName : null,
            ContextVariableName = string.IsNullOrWhiteSpace(payload.ContextVariableName)
                ? "db"
                : payload.ContextVariableName,
            AdditionalImports = payload.AdditionalImports.ToArray(),
            UsingAliases = payload.UsingAliases.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal),
            UsingStaticTypes = payload.UsingStaticTypes.ToArray(),
        };
    }

    public static TranslationResult ToProto(this Domain.QueryTranslationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var proto = new TranslationResult
        {
            Success = result.Success,
            Metadata = (result.Metadata ?? new Domain.TranslationMetadata
            {
                DbContextType = string.Empty,
                EfCoreVersion = string.Empty,
                ProviderName = string.Empty,
                CreationStrategy = "unknown",
            }).ToProto(),
        };

        if (result.Sql is not null)
        {
            proto.Sql = result.Sql;
        }

        if (result.ErrorMessage is not null)
        {
            proto.ErrorMessage = result.ErrorMessage;
        }

        proto.Commands.AddRange(result.Commands.Select(ToProto));
        proto.Parameters.AddRange(result.Parameters.Select(ToProto));
        proto.Warnings.AddRange(result.Warnings.Select(ToProto));
        return proto;
    }

    public static Domain.QueryTranslationResult ToDomain(this TranslationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new Domain.QueryTranslationResult
        {
            Success = result.Success,
            Sql = result.HasSql ? result.Sql : null,
            ErrorMessage = result.HasErrorMessage ? result.ErrorMessage : null,
            Commands = result.Commands.Select(ToDomain).ToArray(),
            Parameters = result.Parameters.Select(ToDomain).ToArray(),
            Warnings = result.Warnings.Select(ToDomain).ToArray(),
            Metadata = (result.Metadata ?? new TranslationMetadata
            {
                DbContextType = string.Empty,
                EfCoreVersion = string.Empty,
                ProviderName = string.Empty,
                CreationStrategy = "unknown",
            }).ToDomain(),
        };
    }

    public static QuerySqlCommand ToProto(this Domain.QuerySqlCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var proto = new QuerySqlCommand
        {
            Sql = command.Sql ?? string.Empty,
        };
        proto.Parameters.AddRange(command.Parameters.Select(ToProto));
        return proto;
    }

    public static Domain.QuerySqlCommand ToDomain(this QuerySqlCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return new Domain.QuerySqlCommand
        {
            Sql = command.Sql,
            Parameters = command.Parameters.Select(ToDomain).ToArray(),
        };
    }

    public static QueryParameter ToProto(this Domain.QueryParameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        var proto = new QueryParameter
        {
            Name = parameter.Name ?? string.Empty,
            ClrType = parameter.ClrType ?? string.Empty,
        };

        if (parameter.InferredValue is not null)
        {
            proto.InferredValue = parameter.InferredValue;
        }

        return proto;
    }

    public static Domain.QueryParameter ToDomain(this QueryParameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        return new Domain.QueryParameter
        {
            Name = parameter.Name,
            ClrType = parameter.ClrType,
            InferredValue = parameter.HasInferredValue ? parameter.InferredValue : null,
        };
    }

    public static QueryWarning ToProto(this Domain.QueryWarning warning)
    {
        ArgumentNullException.ThrowIfNull(warning);

        var proto = new QueryWarning
        {
            Severity = warning.Severity.ToProto(),
            Code = warning.Code ?? string.Empty,
            Message = warning.Message ?? string.Empty,
        };

        if (warning.Suggestion is not null)
        {
            proto.Suggestion = warning.Suggestion;
        }

        return proto;
    }

    public static Domain.QueryWarning ToDomain(this QueryWarning warning)
    {
        ArgumentNullException.ThrowIfNull(warning);

        return new Domain.QueryWarning
        {
            Severity = warning.Severity.ToDomain(),
            Code = warning.Code,
            Message = warning.Message,
            Suggestion = warning.HasSuggestion ? warning.Suggestion : null,
        };
    }

    public static TranslationMetadata ToProto(this Domain.TranslationMetadata metadata)
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

    public static Domain.TranslationMetadata ToDomain(this TranslationMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        return new Domain.TranslationMetadata
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

    public static TranslationStatus ToProto(this Domain.QueryTranslationStatus status) =>
        status switch
        {
            Domain.QueryTranslationStatus.Ready => TranslationStatus.Ready,
            Domain.QueryTranslationStatus.InQueue => TranslationStatus.InQueue,
            Domain.QueryTranslationStatus.Starting => TranslationStatus.Starting,
            _ => TranslationStatus.Unreachable,
        };

    public static Domain.QueryTranslationStatus ToDomain(this TranslationStatus status) =>
        status switch
        {
            TranslationStatus.Ready => Domain.QueryTranslationStatus.Ready,
            TranslationStatus.InQueue => Domain.QueryTranslationStatus.InQueue,
            TranslationStatus.Starting => Domain.QueryTranslationStatus.Starting,
            _ => Domain.QueryTranslationStatus.Unreachable,
        };

    public static WarningSeverity ToProto(this Domain.WarningSeverity severity) =>
        severity switch
        {
            Domain.WarningSeverity.Info => WarningSeverity.Info,
            Domain.WarningSeverity.Warning => WarningSeverity.Warning,
            _ => WarningSeverity.Critical,
        };

    public static Domain.WarningSeverity ToDomain(this WarningSeverity severity) =>
        severity switch
        {
            WarningSeverity.Info => Domain.WarningSeverity.Info,
            WarningSeverity.Warning => Domain.WarningSeverity.Warning,
            _ => Domain.WarningSeverity.Critical,
        };

    public static ModelSnapshot ToProto(this Domain.ModelSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var proto = new ModelSnapshot
        {
            DbContextType = snapshot.DbContextType ?? string.Empty,
        };

        proto.DbSetProperties.AddRange(snapshot.DbSetProperties ?? []);
        proto.Entities.AddRange(snapshot.Entities.Select(ToProto));
        return proto;
    }

    public static Domain.ModelSnapshot ToDomain(this ModelSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new Domain.ModelSnapshot
        {
            DbContextType = snapshot.DbContextType,
            DbSetProperties = snapshot.DbSetProperties.ToArray(),
            Entities = snapshot.Entities.Select(ToDomain).ToArray(),
        };
    }

    public static EntitySnapshot ToProto(this Domain.EntitySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var proto = new EntitySnapshot
        {
            ClrType = snapshot.ClrType ?? string.Empty,
            TableName = snapshot.TableName ?? string.Empty,
        };

        proto.Properties.AddRange(snapshot.Properties.Select(ToProto));
        proto.Navigations.AddRange(snapshot.Navigations.Select(ToProto));
        proto.Indexes.AddRange(snapshot.Indexes.Select(ToProto));
        return proto;
    }

    public static Domain.EntitySnapshot ToDomain(this EntitySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new Domain.EntitySnapshot
        {
            ClrType = snapshot.ClrType,
            TableName = snapshot.TableName,
            Properties = snapshot.Properties.Select(ToDomain).ToArray(),
            Navigations = snapshot.Navigations.Select(ToDomain).ToArray(),
            Indexes = snapshot.Indexes.Select(ToDomain).ToArray(),
        };
    }

    public static PropertySnapshot ToProto(this Domain.PropertySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new PropertySnapshot
        {
            Name = snapshot.Name ?? string.Empty,
            ClrType = snapshot.ClrType ?? string.Empty,
            ColumnName = snapshot.ColumnName ?? string.Empty,
            IsKey = snapshot.IsKey,
            IsNullable = snapshot.IsNullable,
        };
    }

    public static Domain.PropertySnapshot ToDomain(this PropertySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new Domain.PropertySnapshot
        {
            Name = snapshot.Name,
            ClrType = snapshot.ClrType,
            ColumnName = snapshot.ColumnName,
            IsKey = snapshot.IsKey,
            IsNullable = snapshot.IsNullable,
        };
    }

    public static NavigationSnapshot ToProto(this Domain.NavigationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var proto = new NavigationSnapshot
        {
            Name = snapshot.Name ?? string.Empty,
            TargetEntity = snapshot.TargetEntity ?? string.Empty,
            IsCollection = snapshot.IsCollection,
        };

        if (snapshot.ForeignKey is not null)
        {
            proto.ForeignKey = snapshot.ForeignKey;
        }

        return proto;
    }

    public static Domain.NavigationSnapshot ToDomain(this NavigationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new Domain.NavigationSnapshot
        {
            Name = snapshot.Name,
            TargetEntity = snapshot.TargetEntity,
            IsCollection = snapshot.IsCollection,
            ForeignKey = snapshot.HasForeignKey ? snapshot.ForeignKey : null,
        };
    }

    public static IndexSnapshot ToProto(this Domain.IndexSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var proto = new IndexSnapshot
        {
            IsUnique = snapshot.IsUnique,
        };

        proto.Columns.AddRange(snapshot.Columns ?? []);

        if (snapshot.Name is not null)
        {
            proto.Name = snapshot.Name;
        }

        return proto;
    }

    public static Domain.IndexSnapshot ToDomain(this IndexSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new Domain.IndexSnapshot
        {
            Columns = snapshot.Columns.ToArray(),
            IsUnique = snapshot.IsUnique,
            Name = snapshot.HasName ? snapshot.Name : null,
        };
    }
}
