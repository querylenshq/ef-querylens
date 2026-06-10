using EFQueryLens.Lsp.Parsing;

namespace EFQueryLens.Core.Tests.Lsp;

public sealed class TranslationRequestBuilderTests
{
    [Fact]
    public void ResolveDbContextTypeName_MapsReadOnlyInterfaceToConcreteFactoryType()
    {
        var result = TranslationRequestBuilder.ResolveDbContextTypeName(
            "Infrastructure.Interfaces.IReadOnlyMedicsInsightsDbContext",
            ["Share.Medics.Insights.Core.Infrastructure.Services.ReadOnlyMedicsInsightsDbContext"]);

        Assert.Equal(
            "Share.Medics.Insights.Core.Infrastructure.Services.ReadOnlyMedicsInsightsDbContext",
            result);
    }

    [Fact]
    public void ResolveDbContextTypeName_PrefersVariableTypeWhenNoFactoryMatch()
    {
        var result = TranslationRequestBuilder.ResolveDbContextTypeName(
            "App.MyDbContext",
            ["Other.AppDbContext"]);

        Assert.Equal("App.MyDbContext", result);
    }

    [Fact]
    public void ResolveDbContextTypeName_UsesSingleFactoryWhenVariableMissing()
    {
        var result = TranslationRequestBuilder.ResolveDbContextTypeName(
            null,
            ["Sample.AppDbContext"]);

        Assert.Equal("Sample.AppDbContext", result);
    }

    [Fact]
    public void ResolveDbContextTypeName_MatchesInferredFactoryDbContextType()
    {
        var result = TranslationRequestBuilder.ResolveDbContextTypeName(
            "MedicsInsightsDbContext",
            [
                "Share.Medics.Insights.Core.Infrastructure.Services.MedicsInsightsDbContext",
                "Share.Medics.Insights.Core.Infrastructure.Services.ReadOnlyMedicsInsightsDbContext",
            ]);

        Assert.Equal(
            "Share.Medics.Insights.Core.Infrastructure.Services.MedicsInsightsDbContext",
            result);
    }

    [Fact]
    public void ResolveDbContextTypeName_TreatsVarAsUnresolved()
    {
        var result = TranslationRequestBuilder.ResolveDbContextTypeName(
            "var",
            [
                "Share.Medics.Insights.Core.Infrastructure.Services.MedicsInsightsDbContext",
                "Share.Medics.Insights.Core.Infrastructure.Services.ReadOnlyMedicsInsightsDbContext",
            ]);

        Assert.Null(result);
    }
}
