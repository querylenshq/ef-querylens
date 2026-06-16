using EFQueryLens.Core.Contracts;
using EFQueryLens.Lsp;
using EFQueryLens.Lsp.Handlers;
using EFQueryLens.Lsp.Hosting;
using EFQueryLens.Lsp.Services;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Newtonsoft.Json.Linq;

namespace EFQueryLens.Core.Tests.Lsp;

public sealed class LanguageServerHandlerCodeLensTests
{
    private const string SourceWithQuery = """
        public sealed class Demo
        {
            public void Run(AppDbContext db)
            {
                var query = db.Orders.Where(o => o.Id > 0).ToList();
            }
        }
        """;

    [Fact]
    public void GetCodeLens_WhenQueryLensIsNotSetup_ShowsOnlySetupAction()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), "efql-codelens-" + Guid.NewGuid(), "Demo.cs");
        var codeLens = GetCodeLens(sourcePath, SourceWithQuery);

        var commands = codeLens.Select(lens => lens.Command).ToArray();

        Assert.Single(commands);
        Assert.Equal("Set up QueryLens", commands[0]?.Title);
        Assert.Equal("efquerylens.setup", commands[0]?.CommandIdentifier);
    }

    [Fact]
    public void GetCodeLens_WhenQueryLensFactorySourceExists_ShowsSqlActions()
    {
        using var workspace = new SetupWorkspace();
        var codeLens = GetCodeLens(workspace.SourcePath, SourceWithQuery);

        var commands = codeLens.Select(lens => lens.Command!.CommandIdentifier).ToArray();

        Assert.Equal(
            [
                "efquerylens.showsqlpopup",
                "efquerylens.opensqleditor",
                "efquerylens.copysql",
                "efquerylens.reanalyze",
            ],
            commands);
    }

    [Fact]
    public void Initialize_AdvertisesSetupCommand()
    {
        var handler = CreateHandler(new DocumentManager());

        var result = handler.Initialize();
        var commands = result["capabilities"]?["executeCommandProvider"]?["commands"] as JArray;

        Assert.NotNull(commands);
        Assert.Contains(commands!.Values<string>(), command => command == "efquerylens.setup");
    }

    [Fact]
    public void CreateRiderSetupPayload_IncludesTextDocumentPosition()
    {
        var uri = new Uri("file:///repo/Demo.cs");
        var payload =
            LanguageServerHandler.CreateRiderSetupPayload(
                new TextDocumentPositionParams
                {
                    TextDocument = new TextDocumentIdentifier { Uri = uri },
                    Position = new Position
                    {
                        Line = 12,
                        Character = 34,
                    },
                });

        Assert.Equal(uri.ToString(), payload["fileUri"]?.Value<string>());
        Assert.Equal(12, payload["line"]?.Value<int>());
        Assert.Equal(34, payload["character"]?.Value<int>());
    }

    private static CodeLens[] GetCodeLens(string sourcePath, string sourceText)
    {
        var documentManager = new DocumentManager();
        var handler = CreateHandler(documentManager);
        var uri = new Uri(sourcePath).AbsoluteUri;
        documentManager.UpdateDocument(uri, sourceText);

        return handler.GetCodeLens(
            new CodeLensParams
            {
                TextDocument = new TextDocumentIdentifier
                {
                    Uri = new Uri(uri),
                },
            });
    }

    private static LanguageServerHandler CreateHandler(DocumentManager documentManager)
    {
        var engine = new NoOpQueryLensEngine();
        var hover = new HoverHandler(documentManager, new HoverPreviewService(engine), engine);
        var warmup = new WarmupHandler(documentManager, engine);
        var daemon = new DaemonControlHandler(engine);
        var sync = new TextDocumentSyncHandler(documentManager);
        return new LanguageServerHandler(hover, warmup, daemon, sync);
    }

    private sealed class SetupWorkspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "efql-codelens-" + Guid.NewGuid());

        public string SourcePath => Path.Combine(Root, "Demo.cs");

        public SetupWorkspace()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Path.Combine(Root, "bin", "Debug", "net10.0"));
            Directory.CreateDirectory(Path.Combine(Root, "Properties", "QueryLens"));

            File.WriteAllText(
                Path.Combine(Root, "DemoApp.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <OutputType>Exe</OutputType>
                  </PropertyGroup>
                </Project>
                """);

            File.WriteAllText(Path.Combine(Root, "bin", "Debug", "net10.0", "DemoApp.dll"), string.Empty);
            File.WriteAllText(
                Path.Combine(Root, "Properties", "QueryLens", "QueryLensDbContextFactory.g.cs"),
                "internal sealed class QueryLensFactory : IQueryLensDbContextFactory<AppDbContext> { }");
            File.WriteAllText(SourcePath, SourceWithQuery);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch
            {
                // Best effort cleanup for Windows test runners.
            }
        }
    }

    private sealed class NoOpQueryLensEngine : IQueryLensEngine
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task<QueryTranslationResult> TranslateAsync(TranslationRequest request, CancellationToken ct = default)
            => Task.FromResult(new QueryTranslationResult());

        public Task<ModelSnapshot> InspectModelAsync(ModelInspectionRequest request, CancellationToken ct = default)
            => Task.FromResult(new ModelSnapshot { DbContextType = string.Empty });

        public Task InvalidateAssemblyCachesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
