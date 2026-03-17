using System.Linq;
using EFQueryLens.Core.Contracts;

namespace EFQueryLens.Core.Grpc;

using Domain = EFQueryLens.Core;

public static partial class GrpcMappingExtensions
{
    public static QuerySqlCommand ToProto(this Contracts.QuerySqlCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var proto = new QuerySqlCommand
        {
            Sql = command.Sql ?? string.Empty,
        };
        proto.Parameters.AddRange(command.Parameters.Select(ToProto));
        return proto;
    }

    public static Contracts.QuerySqlCommand ToDomain(this QuerySqlCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return new Contracts.QuerySqlCommand
        {
            Sql = command.Sql,
            Parameters = command.Parameters.Select(ToDomain).ToArray(),
        };
    }

    public static QueryParameter ToProto(this Contracts.QueryParameter parameter)
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

    public static Contracts.QueryParameter ToDomain(this QueryParameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        return new Contracts.QueryParameter
        {
            Name = parameter.Name,
            ClrType = parameter.ClrType,
            InferredValue = parameter.HasInferredValue ? parameter.InferredValue : null,
        };
    }

    public static QueryWarning ToProto(this Contracts.QueryWarning warning)
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

    public static Contracts.QueryWarning ToDomain(this QueryWarning warning)
    {
        ArgumentNullException.ThrowIfNull(warning);

        return new Contracts.QueryWarning
        {
            Severity = warning.Severity.ToDomain(),
            Code = warning.Code,
            Message = warning.Message,
            Suggestion = warning.HasSuggestion ? warning.Suggestion : null,
        };
    }

    public static TranslationStatus ToProto(this QueryTranslationStatus status) =>
        status switch
        {
            QueryTranslationStatus.Ready => TranslationStatus.Ready,
            QueryTranslationStatus.InQueue => TranslationStatus.InQueue,
            QueryTranslationStatus.Starting => TranslationStatus.Starting,
            _ => TranslationStatus.Unreachable,
        };

    public static QueryTranslationStatus ToDomain(this TranslationStatus status) =>
        status switch
        {
            TranslationStatus.Ready => QueryTranslationStatus.Ready,
            TranslationStatus.InQueue => QueryTranslationStatus.InQueue,
            TranslationStatus.Starting => QueryTranslationStatus.Starting,
            _ => QueryTranslationStatus.DaemonUnavailable,
        };

    public static WarningSeverity ToProto(this Contracts.WarningSeverity severity) =>
        severity switch
        {
            Contracts.WarningSeverity.Info => WarningSeverity.Info,
            Contracts.WarningSeverity.Warning => WarningSeverity.Warning,
            _ => WarningSeverity.Critical,
        };

    public static Contracts.WarningSeverity ToDomain(this WarningSeverity severity) =>
        severity switch
        {
            WarningSeverity.Info => Contracts.WarningSeverity.Info,
            WarningSeverity.Warning => Contracts.WarningSeverity.Warning,
            _ => Contracts.WarningSeverity.Critical,
        };
}
