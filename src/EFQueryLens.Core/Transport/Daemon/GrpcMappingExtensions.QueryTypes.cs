using System.Linq;

namespace EFQueryLens.Core.Grpc;

using Domain = EFQueryLens.Core;

public static partial class GrpcMappingExtensions
{
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
}
