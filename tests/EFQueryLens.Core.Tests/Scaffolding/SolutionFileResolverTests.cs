using EFQueryLens.Core.Scaffolding;

namespace EFQueryLens.Core.Tests.Scaffolding;

public sealed class SolutionFileResolverTests
{
    [Fact]
    public void FindSolutionFile_PrefersSlnxOverSlnInSameDirectory()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("Backend.sln", "Microsoft Visual Studio Solution File, Format Version 12.00");
        workspace.WriteFile("Backend.slnx", """
            <Solution>
              <Project Path="Apps/QueryApi/QueryApi.csproj" />
            </Solution>
            """);

        var found = SolutionFileResolver.FindSolutionFile(workspace.GetPath("Apps/QueryApi"));

        Assert.NotNull(found);
        Assert.EndsWith("Backend.slnx", found, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseSolutionProjects_SlnxIncludesNestedFolderProjects()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("Backend.slnx", """
            <Solution>
              <Folder Name="/src/">
                <Project Path="src/Apps/QueryApi/QueryApi.csproj" />
                <Project Path="src/ReadProjection.QueryHandlers/ReadProjection.QueryHandlers.csproj" />
              </Folder>
            </Solution>
            """);
        workspace.WriteCsproj("src/Apps/QueryApi/QueryApi.csproj", executable: true);
        workspace.WriteCsproj("src/ReadProjection.QueryHandlers/ReadProjection.QueryHandlers.csproj", executable: false);

        var projects = SolutionFileResolver.ParseSolutionProjects(workspace.GetPath("Backend.slnx"));

        Assert.Equal(2, projects.Count);
        Assert.Contains(projects, path => path.EndsWith("QueryApi.csproj", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projects, path => path.EndsWith("ReadProjection.QueryHandlers.csproj", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ParseSolutionProjects_LegacySlnStillWorks()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteCsproj("Host/Host.csproj", executable: true);
        workspace.WriteCsproj("Library/Library.csproj", executable: false);
        workspace.WriteFile("Backend.sln", """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Host", "Host\Host.csproj", "{11111111-1111-1111-1111-111111111111}"
            EndProject
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Library", "Library\Library.csproj", "{22222222-2222-2222-2222-222222222222}"
            EndProject
            """);

        var projects = SolutionFileResolver.ParseSolutionProjects(workspace.GetPath("Backend.sln"));

        Assert.Equal(2, projects.Count);
        Assert.Contains(projects, path => path.EndsWith("Host.csproj", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projects, path => path.EndsWith("Library.csproj", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FindSolutionFile_PrefersDirectoryNameMatchWhenMultipleCandidatesExist()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("Other.slnx", "<Solution />");
        workspace.WriteFile("Backend.slnx", "<Solution />");

        var found = SolutionFileResolver.SelectSolutionFileInDirectory(workspace.Root);

        Assert.NotNull(found);
        Assert.EndsWith("Backend.slnx", found, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "efql-slnx-test-" + Guid.NewGuid());

        public TempWorkspace()
        {
            Directory.CreateDirectory(Root);
        }

        public string GetPath(string relativePath) => Path.Combine(Root, relativePath);

        public void WriteFile(string relativePath, string content)
        {
            var fullPath = GetPath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
        }

        public void WriteCsproj(string relativePath, bool executable)
        {
            var sdk = executable ? "Microsoft.NET.Sdk.Web" : "Microsoft.NET.Sdk";
            var outputType = executable ? "<OutputType>Exe</OutputType>" : string.Empty;
            WriteFile(relativePath, $"""
                <Project Sdk="{sdk}">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    {outputType}
                  </PropertyGroup>
                </Project>
                """);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch
            {
                // Best effort cleanup for temp test directories.
            }
        }
    }
}
