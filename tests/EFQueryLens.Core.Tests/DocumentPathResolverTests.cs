using EFQueryLens.Lsp;

namespace EFQueryLens.Core.Tests;

public class DocumentPathResolverTests
{
    [Fact]
    public void Resolve_EncodedWindowsFileUri_DoesNotDuplicateDrivePrefix()
    {
        var uri = new Uri("file:///c%3A/tsp/plus-web/src/Sla.Plus.Application/Services/CaseService.cs");

        var path = DocumentPathResolver.Resolve(uri);

        Assert.DoesNotContain("c:\\c:\\", path, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(
            "Sla.Plus.Application" + Path.DirectorySeparatorChar + "Services" + Path.DirectorySeparatorChar + "CaseService.cs",
            path,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_RegularFileUri_ReturnsFullPath()
    {
        var uri = new Uri("file:///C:/repo/app/File.cs");

        var path = DocumentPathResolver.Resolve(uri);

        var expectedSuffix = "repo" + Path.DirectorySeparatorChar + "app" + Path.DirectorySeparatorChar + "File.cs";
        Assert.EndsWith(expectedSuffix, path, StringComparison.OrdinalIgnoreCase);
    }
}
