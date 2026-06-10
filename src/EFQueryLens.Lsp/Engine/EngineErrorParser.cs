using System.Text.Json;
using EFQueryLens.Core.AssemblyContext;
using EFQueryLens.Core.Contracts;

namespace EFQueryLens.Lsp.Engine;

internal static class EngineErrorParser
{
    internal static Exception? TryParseException(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            var error = JsonSerializer.Deserialize<EngineErrorResponse>(responseBody, EngineJsonOptions.Default);
            if (error is null)
            {
                return null;
            }

            if (string.Equals(error.ErrorType, nameof(DbContextDiscoveryException), StringComparison.Ordinal)
                && Enum.TryParse<DbContextDiscoveryFailureKind>(error.FailureKind, out var failureKind))
            {
                return new DbContextDiscoveryException(
                    failureKind,
                    error.Message ?? "DbContext discovery failed.");
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}
