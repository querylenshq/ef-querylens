using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Extensibility;

namespace EFQueryLens.VisualStudio;

[VisualStudioContribution]
internal sealed class QueryLensExtension : Extension
{
    public override ExtensionConfiguration ExtensionConfiguration => new()
    {
        Metadata = new(
            id: "EFQueryLens.VisualStudio.9f5a9a8e-f220-4ef0-be7c-a3cff5c767a5",
            version: this.ExtensionAssemblyVersion,
            publisherName: "EF QueryLens Contributors",
            displayName: "EF QueryLens",
            description: "Preview EF Core LINQ SQL in Visual Studio using the QueryLens language server.")
        {
            DotnetTargetVersions = [DotnetTarget.Custom("net8.0")],
        },
    };

    protected override void InitializeServices(IServiceCollection serviceCollection)
    {
        base.InitializeServices(serviceCollection);
        QueryLensLogger.Info($"extension-initialize version={this.ExtensionAssemblyVersion}");
    }
}
