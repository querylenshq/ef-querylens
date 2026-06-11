using EFQueryLens.Core.AssemblyContext;
using FluentValidation.Results;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace EFQueryLens.Core.Tests.AssemblyContext;

public class HostBinMetadataTests
{
    [Fact]
    public void DepsJsonAssemblyIndex_IncludesHostDirectPackages_ExcludedFromEfClosure()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "efql-deps-index-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var hostDll = Path.Combine(tempDir, "SampleHost.dll");
            File.WriteAllBytes(hostDll, [0]);

            var depsJson = """
                {
                  "runtimeTarget": { "name": ".NETCoreApp,Version=v8.0" },
                  "targets": {
                    ".NETCoreApp,Version=v8.0": {
                      "SampleHost/1.0.0": {
                        "dependencies": {
                          "FluentValidation": "11.0.0"
                        },
                        "runtime": { "SampleHost.dll": {} }
                      },
                      "FluentValidation/11.0.0": {
                        "runtime": { "lib/net8.0/FluentValidation.dll": {} }
                      }
                    }
                  },
                  "libraries": {
                    "SampleHost/1.0.0": { "type": "project", "serviceable": false },
                    "FluentValidation/11.0.0": { "type": "package", "serviceable": true }
                  }
                }
                """;
            File.WriteAllText(Path.ChangeExtension(hostDll, ".deps.json"), depsJson);

            Assert.True(DepsJsonAssemblyIndex.TryGetAllRuntimeAssemblySimpleNames(hostDll, out var names));
            Assert.Contains("FluentValidation", names);
            Assert.Contains("SampleHost", names);

            Assert.True(EfDomainClosureLoader.TryBuildClosure(hostDll, null, out var closure));
            Assert.DoesNotContain("FluentValidation", closure.AssemblySimpleNames);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void HostBinDlls_ProvideRoslynMetadata_ForValidationFailure()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "efql-host-bin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var hostDll = Path.Combine(tempDir, "SampleHost.dll");
            File.WriteAllBytes(hostDll, [0]);

            var fluentPath = typeof(ValidationFailure).Assembly.Location;
            Assert.True(File.Exists(fluentPath));
            File.Copy(fluentPath, Path.Combine(tempDir, "FluentValidation.dll"), overwrite: true);

            var dllPaths = HostBinAssemblyCatalog.EnumerateHostBinDllPaths(hostDll);
            Assert.Contains(dllPaths, path =>
                string.Equals(Path.GetFileName(path), "FluentValidation.dll", StringComparison.OrdinalIgnoreCase));

            var refs = dllPaths
                .Where(path => !string.Equals(
                    Path.GetFileName(path),
                    "SampleHost.dll",
                    StringComparison.OrdinalIgnoreCase))
                .Select(path => MetadataReference.CreateFromFile(path))
                .Append(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .ToList<MetadataReference>();

            var tree = CSharpSyntaxTree.ParseText(
                """
                using System.Collections.Generic;
                using FluentValidation.Results;
                public static class Stub {
                    public static List<ValidationFailure> Items = new();
                }
                """);

            var compilation = CSharpCompilation.Create(
                "HostBinMetadataTest",
                [tree],
                refs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var errors = compilation.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToList();

            Assert.Empty(errors);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TypeDefiningAssemblyLocator_FindsFluentValidation_ForValidationFailure()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "efql-type-loc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var hostDll = Path.Combine(tempDir, "SampleHost.dll");
            File.WriteAllBytes(hostDll, [0]);

            var fluentPath = typeof(ValidationFailure).Assembly.Location;
            File.Copy(fluentPath, Path.Combine(tempDir, "FluentValidation.dll"), overwrite: true);

            var assemblyName = TypeDefiningAssemblyLocator.TryFindAssemblySimpleName(
                "ValidationFailure",
                hostDll);

            Assert.Equal("FluentValidation", assemblyName);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("NSwag.Core", true)]
    [InlineData("Swashbuckle.AspNetCore.SwaggerGen", true)]
    [InlineData("FluentValidation", false)]
    public void AssemblyReflection_SkipsOptionalReflectionScan_ForOpenApiTooling(string assemblyName, bool expected)
    {
        Assert.Equal(expected, AssemblyReflection.ShouldSkipOptionalReflectionScanName(assemblyName));
    }
}
