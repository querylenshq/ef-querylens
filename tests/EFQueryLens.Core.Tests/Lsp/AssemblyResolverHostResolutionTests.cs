using EFQueryLens.Lsp.Parsing;

namespace EFQueryLens.Core.Tests.Lsp;

public sealed class AssemblyResolverHostResolutionTests
{
    [Fact]
    public void TryGetTargetAssembly_ClassLibrary_ResolvesHostWithColocatedDllViaSlnx()
    {
        using var workspace = new HostResolutionWorkspace();
        workspace.WriteSlnx("""
            <Solution>
              <Project Path="Apps/QueryApi/QueryApi.csproj" />
              <Project Path="ReadProjection.QueryHandlers/ReadProjection.QueryHandlers.csproj" />
            </Solution>
            """);
        workspace.WriteExecutableCsproj("Apps/QueryApi/QueryApi.csproj", "QueryApi");
        workspace.WriteLibraryCsproj("ReadProjection.QueryHandlers/ReadProjection.QueryHandlers.csproj", "ReadProjection.QueryHandlers");
        workspace.WriteDll("Apps/QueryApi/bin/Debug/net10.0/QueryApi.dll");
        workspace.WriteDll("Apps/QueryApi/bin/Debug/net10.0/ReadProjection.QueryHandlers.dll");
        workspace.WriteSourceFile("ReadProjection.QueryHandlers/QueryHandler.cs");

        var resolved = AssemblyResolver.TryGetTargetAssembly(workspace.SourceFilePath);

        Assert.NotNull(resolved);
        Assert.EndsWith("QueryApi.dll", resolved!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"{Path.DirectorySeparatorChar}net10.0{Path.DirectorySeparatorChar}", resolved!.Replace('/', Path.DirectorySeparatorChar));
    }

    [Fact]
    public void TryGetTargetAssembly_UsesSlnxProjectsWhenBothFormatsExist()
    {
        using var workspace = new HostResolutionWorkspace();
        workspace.WriteSln("""
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "ReadProjection.QueryHandlers", "ReadProjection.QueryHandlers\ReadProjection.QueryHandlers.csproj", "{11111111-1111-1111-1111-111111111111}"
            EndProject
            """);
        workspace.WriteSlnx("""
            <Solution>
              <Project Path="Apps/QueryApi/QueryApi.csproj" />
              <Project Path="ReadProjection.QueryHandlers/ReadProjection.QueryHandlers.csproj" />
            </Solution>
            """);
        workspace.WriteExecutableCsproj("Apps/QueryApi/QueryApi.csproj", "QueryApi");
        workspace.WriteLibraryCsproj("ReadProjection.QueryHandlers/ReadProjection.QueryHandlers.csproj", "ReadProjection.QueryHandlers");
        workspace.WriteDll("Apps/QueryApi/bin/Debug/net10.0/QueryApi.dll");
        workspace.WriteDll("Apps/QueryApi/bin/Debug/net10.0/ReadProjection.QueryHandlers.dll");
        workspace.WriteSourceFile("ReadProjection.QueryHandlers/QueryHandler.cs");

        var resolved = AssemblyResolver.TryGetTargetAssembly(workspace.SourceFilePath);

        Assert.NotNull(resolved);
        Assert.EndsWith("QueryApi.dll", resolved!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryGetTargetAssembly_FailsWhenLibraryDllMissingFromHostOutput()
    {
        using var workspace = new HostResolutionWorkspace();
        workspace.WriteSlnx("""
            <Solution>
              <Project Path="Apps/QueryApi/QueryApi.csproj" />
              <Project Path="ReadProjection.QueryHandlers/ReadProjection.QueryHandlers.csproj" />
            </Solution>
            """);
        workspace.WriteExecutableCsproj("Apps/QueryApi/QueryApi.csproj", "QueryApi");
        workspace.WriteLibraryCsproj("ReadProjection.QueryHandlers/ReadProjection.QueryHandlers.csproj", "ReadProjection.QueryHandlers");
        workspace.WriteDll("Apps/QueryApi/bin/Debug/net10.0/QueryApi.dll");
        workspace.WriteSourceFile("ReadProjection.QueryHandlers/QueryHandler.cs");

        var resolved = AssemblyResolver.TryGetTargetAssembly(workspace.SourceFilePath);

        Assert.NotNull(resolved);
        Assert.StartsWith("DEBUG_FAIL", resolved!, StringComparison.Ordinal);
        Assert.Contains("library DLL not alongside", resolved!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryGetTargetAssembly_PrefersMatchingTfmOverStaleHostOutput()
    {
        using var workspace = new HostResolutionWorkspace();
        workspace.WriteSlnx("""
            <Solution>
              <Project Path="Apps/QueryApi/QueryApi.csproj" />
              <Project Path="ReadProjection.QueryHandlers/ReadProjection.QueryHandlers.csproj" />
            </Solution>
            """);
        workspace.WriteExecutableCsproj("Apps/QueryApi/QueryApi.csproj", "QueryApi");
        workspace.WriteLibraryCsproj("ReadProjection.QueryHandlers/ReadProjection.QueryHandlers.csproj", "ReadProjection.QueryHandlers");
        workspace.WriteDll("ReadProjection.QueryHandlers/bin/Debug/net10.0/ReadProjection.QueryHandlers.dll");
        workspace.WriteDll("Apps/QueryApi/bin/Debug/net9.0/QueryApi.dll", utcTimestamp: DateTime.UtcNow.AddHours(1));
        workspace.WriteDll("Apps/QueryApi/bin/Debug/net9.0/ReadProjection.QueryHandlers.dll", utcTimestamp: DateTime.UtcNow.AddHours(1));
        workspace.WriteDll("Apps/QueryApi/bin/Debug/net10.0/QueryApi.dll", utcTimestamp: DateTime.UtcNow.AddHours(-1));
        workspace.WriteDll("Apps/QueryApi/bin/Debug/net10.0/ReadProjection.QueryHandlers.dll", utcTimestamp: DateTime.UtcNow.AddHours(-1));
        workspace.WriteSourceFile("ReadProjection.QueryHandlers/QueryHandler.cs");

        var resolved = AssemblyResolver.TryGetTargetAssembly(workspace.SourceFilePath);

        Assert.NotNull(resolved);
        Assert.Contains($"{Path.DirectorySeparatorChar}net10.0{Path.DirectorySeparatorChar}", resolved!.Replace('/', Path.DirectorySeparatorChar));
    }

    [Theory]
    [InlineData("  -> EXCEPTION: No .slnx or .sln file found.\n", "Could not locate a .slnx or .sln file above this project.")]
    [InlineData("  -> EXCEPTION: No executable project references this library.\n", "No executable host project was found in the solution.")]
    [InlineData("library DLL not alongside", "its build output does not include this library's DLL")]
    public void FormatTargetAssemblyFailureMessage_SurfacesActionableText(string debugFragment, string expectedFragment)
    {
        var message = AssemblyResolver.FormatTargetAssemblyFailureMessage($"DEBUG_FAIL:\n{debugFragment}");

        Assert.Contains(expectedFragment, message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class HostResolutionWorkspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "efql-host-test-" + Guid.NewGuid());

        public string SourceFilePath { get; private set; } = string.Empty;

        public HostResolutionWorkspace()
        {
            Directory.CreateDirectory(Root);
        }

        public void WriteSlnx(string content) => WriteFile("Backend.slnx", content);

        public void WriteSln(string content) => WriteFile("Backend.sln", content);

        public void WriteExecutableCsproj(string relativePath, string assemblyName)
        {
            WriteFile(relativePath, $"""
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <AssemblyName>{assemblyName}</AssemblyName>
                  </PropertyGroup>
                </Project>
                """);
        }

        public void WriteLibraryCsproj(string relativePath, string assemblyName)
        {
            WriteFile(relativePath, $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <AssemblyName>{assemblyName}</AssemblyName>
                  </PropertyGroup>
                </Project>
                """);
        }

        public void WriteSourceFile(string relativePath)
        {
            SourceFilePath = GetPath(relativePath);
            WriteFile(relativePath, "namespace Demo; public static class QueryHandler { }");
        }

        public void WriteDll(string relativePath, DateTime? utcTimestamp = null)
        {
            var fullPath = GetPath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllBytes(fullPath, [0x4D, 0x5A, 0x90, 0x00]);
            if (utcTimestamp is not null)
            {
                File.SetLastWriteTimeUtc(fullPath, utcTimestamp.Value);
            }
        }

        private string GetPath(string relativePath) => Path.Combine(Root, relativePath);

        private void WriteFile(string relativePath, string content)
        {
            var fullPath = GetPath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
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
