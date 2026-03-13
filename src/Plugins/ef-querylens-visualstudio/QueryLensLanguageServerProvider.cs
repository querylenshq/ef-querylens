using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipelines;
using System.Reflection;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Editor;
using Microsoft.VisualStudio.Extensibility.LanguageServer;
using Microsoft.VisualStudio.RpcContracts.LanguageServerProvider;

namespace EFQueryLens.VisualStudio;

#pragma warning disable VSEXTPREVIEW_LSP
#pragma warning disable VSEXTPREVIEW_SETTINGS
[VisualStudioContribution]
internal sealed class QueryLensLanguageServerProvider : LanguageServerProvider
{
    private const string ServerDirectoryName = "server";
    private const string ServerAssemblyName = "EFQueryLens.Lsp.dll";
    private const int DefaultMaxCodeLensPerDocument = 50;
    private const int DefaultCodeLensDebounceMilliseconds = 250;
    private const bool DefaultUseModelFilter = false;
    private const bool DefaultEnableVerboseLogs = true;

    [VisualStudioContribution]
    public static DocumentTypeConfiguration QueryLensCSharpDocumentType => new("querylens-csharp")
    {
        FileExtensions = [".cs"],
        BaseDocumentType = LanguageServerBaseDocumentType,
    };

    public QueryLensLanguageServerProvider(ExtensionCore container, VisualStudioExtensibility extensibility)
        : base(container, extensibility)
    {
        QueryLensLogger.Info($"provider-ctor logFile={QueryLensLogger.LogFilePath}");
    }

    public override LanguageServerProviderConfiguration LanguageServerProviderConfiguration => new(
        "%EFQueryLens.LanguageServer.DisplayName%",
        [
            // Built-in CSharp is required so existing .cs documents activate this provider.
            DocumentFilter.FromDocumentType("CSharp"),
            DocumentFilter.FromDocumentType(QueryLensCSharpDocumentType),
        ]);

    public override async Task<IDuplexPipe?> CreateServerConnectionAsync(CancellationToken cancellationToken)
    {
        QueryLensLogger.Info("create-server-connection-start");
        var settingsSnapshot = await ReadSettingsSnapshotAsync(cancellationToken);
        QueryLensLogger.Info($"settings maxCodeLens={settingsSnapshot.MaxCodeLensPerDocument} debounceMs={settingsSnapshot.CodeLensDebounceMilliseconds} useModelFilter={settingsSnapshot.UseModelFilter} verboseLogs={settingsSnapshot.EnableVerboseLogs}");

        string extensionDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to resolve extension assembly directory.");
        QueryLensLogger.Info($"extension-directory path={extensionDirectory}");

        string serverPath = ResolveServerPath(extensionDirectory);
        if (!File.Exists(serverPath))
        {
            QueryLensLogger.Info($"server-missing path={serverPath}");
            throw new FileNotFoundException("Could not find the QueryLens language server assembly.", serverPath);
        }
        QueryLensLogger.Info($"server-path path={serverPath}");

        var processStartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{serverPath}\"",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        processStartInfo.Environment["QUERYLENS_MAX_CODELENS_PER_DOCUMENT"] = settingsSnapshot.MaxCodeLensPerDocument.ToString(CultureInfo.InvariantCulture);
        processStartInfo.Environment["QUERYLENS_CODELENS_DEBOUNCE_MS"] = settingsSnapshot.CodeLensDebounceMilliseconds.ToString(CultureInfo.InvariantCulture);
        processStartInfo.Environment["QUERYLENS_CODELENS_USE_MODEL_FILTER"] = settingsSnapshot.UseModelFilter ? "1" : "0";
        processStartInfo.Environment["QUERYLENS_DEBUG"] = settingsSnapshot.EnableVerboseLogs ? "1" : "0";
        processStartInfo.Environment["QUERYLENS_CLIENT"] = "vs";

#pragma warning disable CA2000
        var process = new Process { StartInfo = processStartInfo };
#pragma warning restore CA2000

        if (!process.Start())
        {
            QueryLensLogger.Info("server-process-start-failed");
            return null;
        }

        QueryLensLogger.Info($"server-process-started pid={process.Id}");
        _ = PumpServerErrorStreamAsync(process, cancellationToken);

        return new SimpleDuplexPipe(
            PipeReader.Create(process.StandardOutput.BaseStream),
            PipeWriter.Create(process.StandardInput.BaseStream));
    }

    private async Task<SettingsSnapshot> ReadSettingsSnapshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            var settingValues = await this.Extensibility.Settings().ReadEffectiveValuesAsync(
                [
                    QueryLensSettings.MaxCodeLensPerDocument,
                    QueryLensSettings.CodeLensDebounceMilliseconds,
                    QueryLensSettings.UseModelFilter,
                    QueryLensSettings.EnableVerboseLogs,
                ],
                cancellationToken);

            var maxCodeLens = Clamp(
                settingValues.ValueOrDefault(QueryLensSettings.MaxCodeLensPerDocument, DefaultMaxCodeLensPerDocument),
                min: 1,
                max: 500);

            var debounceMs = Clamp(
                settingValues.ValueOrDefault(QueryLensSettings.CodeLensDebounceMilliseconds, DefaultCodeLensDebounceMilliseconds),
                min: 0,
                max: 5000);

            var useModelFilter = settingValues.ValueOrDefault(QueryLensSettings.UseModelFilter, DefaultUseModelFilter);
            var enableVerboseLogs = settingValues.ValueOrDefault(QueryLensSettings.EnableVerboseLogs, DefaultEnableVerboseLogs);

            return new SettingsSnapshot(maxCodeLens, debounceMs, useModelFilter, enableVerboseLogs);
        }
        catch
        {
            // If settings are unavailable, fall back to previous defaults.
            QueryLensLogger.Info("settings-read-failed using-defaults");
            return new SettingsSnapshot(
                DefaultMaxCodeLensPerDocument,
                DefaultCodeLensDebounceMilliseconds,
                DefaultUseModelFilter,
                DefaultEnableVerboseLogs);
        }
    }

    private static async Task PumpServerErrorStreamAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!process.HasExited)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await process.StandardError.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                if (line.Length > 0)
                {
                    QueryLensLogger.Info($"lsp-stderr pid={process.Id} {line}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellation during shutdown.
        }
        catch (Exception ex)
        {
            QueryLensLogger.Error("lsp-stderr-pump-failed", ex);
        }
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }

    private static string ResolveServerPath(string extensionDirectory)
    {
        var serverFolderPath = Path.Combine(extensionDirectory, ServerDirectoryName, ServerAssemblyName);
        if (File.Exists(serverFolderPath))
        {
            return serverFolderPath;
        }

        var rootPath = Path.Combine(extensionDirectory, ServerAssemblyName);
        if (File.Exists(rootPath))
        {
            return rootPath;
        }

        return serverFolderPath;
    }

    public override Task OnServerInitializationResultAsync(
        ServerInitializationResult serverInitializationResult,
        LanguageServerInitializationFailureInfo? initializationFailureInfo,
        CancellationToken cancellationToken)
    {
        QueryLensLogger.Info($"server-initialization-result result={serverInitializationResult} failureInfo={(initializationFailureInfo?.ToString() ?? "<null>")}");
        if (serverInitializationResult == ServerInitializationResult.Failed)
        {
            this.Enabled = false;
            QueryLensLogger.Info("provider-disabled-after-failure");
        }

        return base.OnServerInitializationResultAsync(serverInitializationResult, initializationFailureInfo, cancellationToken);
    }

    private sealed class SimpleDuplexPipe(PipeReader input, PipeWriter output) : IDuplexPipe
    {
        public PipeReader Input { get; } = input;

        public PipeWriter Output { get; } = output;
    }

    private sealed record SettingsSnapshot(
        int MaxCodeLensPerDocument,
        int CodeLensDebounceMilliseconds,
        bool UseModelFilter,
        bool EnableVerboseLogs);
}
#pragma warning restore VSEXTPREVIEW_LSP
#pragma warning restore VSEXTPREVIEW_SETTINGS
