using System.Text.Json;
using EFQueryLens.Core.AssemblyContext;
using EFQueryLens.Lsp.Engine;

namespace EFQueryLens.Core.Tests.Lsp;

public sealed class EngineErrorParserTests
{
    [Fact]
    public void TryParseException_ParsesDbContextDiscoveryBody()
    {
        var body = JsonSerializer.Serialize(new
        {
            errorType = nameof(DbContextDiscoveryException),
            failureKind = nameof(DbContextDiscoveryFailureKind.MultipleDbContextsFound),
            message = "Multiple DbContext types found in 'App.dll': A, B.",
        });

        var parsed = EngineErrorParser.TryParseException(body);

        var discovery = Assert.IsType<DbContextDiscoveryException>(parsed);
        Assert.Equal(DbContextDiscoveryFailureKind.MultipleDbContextsFound, discovery.FailureKind);
        Assert.Contains("Multiple DbContext types found", discovery.Message, StringComparison.Ordinal);
    }
}
