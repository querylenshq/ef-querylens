using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace EFQueryLens.VisualStudio;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration("EF QueryLens", "Preview EF Core LINQ SQL in Visual Studio", "0.1.17")]
[Guid(CommandGuids.PackageString)]
internal sealed class QueryLensPackage : AsyncPackage
{
    protected override Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        QueryLensLogger.Info("querylens-package-initialized");
        return Task.CompletedTask;
    }
}
