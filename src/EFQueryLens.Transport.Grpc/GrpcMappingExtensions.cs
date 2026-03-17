using System.Collections.Generic;
using System.Linq;
using EFQueryLens.Core.Contracts;

namespace EFQueryLens.Core.Grpc;

using Domain = EFQueryLens.Core;

public static partial class GrpcMappingExtensions
{
    public static TranslationRequestPayload ToProto(this TranslationRequest request)
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

    public static TranslationRequest ToDomain(this TranslationRequestPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        return new TranslationRequest
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

    public static TranslationResult ToProto(this QueryTranslationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var proto = new TranslationResult
        {
            Success = result.Success,
            Metadata = (result.Metadata ?? new Contracts.TranslationMetadata
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

    public static QueryTranslationResult ToDomain(this TranslationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new QueryTranslationResult
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

}
