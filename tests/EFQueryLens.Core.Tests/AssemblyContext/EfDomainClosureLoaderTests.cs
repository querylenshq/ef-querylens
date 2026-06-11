using EFQueryLens.Core.AssemblyContext;
using EFQueryLens.Core.Scaffolding;

namespace EFQueryLens.Core.Tests.AssemblyContext;

public class EfDomainClosureLoaderTests
{
    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite", true)]
    [InlineData("Microsoft.EntityFrameworkCore", true)]
    [InlineData("Npgsql.EntityFrameworkCore.PostgreSQL", true)]
    [InlineData("EntityFrameworkCore.Projectables", true)]
    [InlineData("Vendor.DocGen", false)]
    [InlineData("Swashbuckle.AspNetCore", false)]
    public void EfPackageRegistry_IsEfEcosystemPackage_ExpectedPolicy(string packageName, bool expected)
    {
        Assert.Equal(expected, EfPackageRegistry.IsEfEcosystemPackage(packageName));
    }

    [Fact]
    public void TryBuildClosure_IncludesProjectRefsAndEfPackages_ExcludesHostOnlyNuGet()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "efql-closure-" + Guid.NewGuid().ToString("N"));
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
                          "SampleCore": "1.0.0",
                          "Vendor.DocGen": "1.0.0",
                          "Microsoft.EntityFrameworkCore.Sqlite": "8.0.0"
                        },
                        "runtime": { "SampleHost.dll": {} }
                      },
                      "SampleCore/1.0.0": {
                        "dependencies": {
                          "Ardalis.Specification": "8.0.0"
                        },
                        "runtime": { "SampleCore.dll": {} }
                      },
                      "Ardalis.Specification/8.0.0": {
                        "runtime": {
                          "lib/net8.0/Ardalis.Specification.dll": {}
                        }
                      },
                      "Vendor.DocGen/1.0.0": {
                        "runtime": { "lib/net8.0/Vendor.DocGen.dll": {} }
                      },
                      "Microsoft.EntityFrameworkCore.Sqlite/8.0.0": {
                        "dependencies": {
                          "Microsoft.EntityFrameworkCore": "8.0.0",
                          "Microsoft.Data.Sqlite.Core": "8.0.0"
                        },
                        "runtime": {
                          "lib/net8.0/Microsoft.EntityFrameworkCore.Sqlite.dll": {}
                        }
                      },
                      "Microsoft.EntityFrameworkCore/8.0.0": {
                        "runtime": {
                          "lib/net8.0/Microsoft.EntityFrameworkCore.dll": {}
                        }
                      },
                      "Microsoft.Data.Sqlite.Core/8.0.0": {
                        "runtime": {
                          "lib/net8.0/Microsoft.Data.Sqlite.Core.dll": {}
                        }
                      }
                    }
                  },
                  "libraries": {
                    "SampleHost/1.0.0": { "type": "project", "serviceable": false },
                    "SampleCore/1.0.0": { "type": "project", "serviceable": false },
                    "Ardalis.Specification/8.0.0": { "type": "package", "serviceable": true },
                    "Vendor.DocGen/1.0.0": { "type": "package", "serviceable": true },
                    "Microsoft.EntityFrameworkCore.Sqlite/8.0.0": { "type": "package", "serviceable": true },
                    "Microsoft.EntityFrameworkCore/8.0.0": { "type": "package", "serviceable": true },
                    "Microsoft.Data.Sqlite.Core/8.0.0": { "type": "package", "serviceable": true }
                  }
                }
                """;
            File.WriteAllText(Path.ChangeExtension(hostDll, ".deps.json"), depsJson);
            File.WriteAllText(Path.ChangeExtension(hostDll, ".runtimeconfig.json"), "{}");

            Assert.True(EfDomainClosureLoader.TryBuildClosure(hostDll, null, out var closure));

            Assert.Contains("SampleHost", closure.AssemblySimpleNames);
            Assert.Contains("SampleCore", closure.AssemblySimpleNames);
            Assert.Contains("Ardalis.Specification", closure.AssemblySimpleNames);
            Assert.Contains("Microsoft.EntityFrameworkCore.Sqlite", closure.AssemblySimpleNames);
            Assert.Contains("Microsoft.EntityFrameworkCore", closure.AssemblySimpleNames);
            Assert.Contains("Microsoft.Data.Sqlite.Core", closure.AssemblySimpleNames);
            Assert.DoesNotContain("Vendor.DocGen", closure.AssemblySimpleNames);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
