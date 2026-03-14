using System.ComponentModel.Composition;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

namespace EFQueryLens.VisualStudio;

[Export(typeof(ILanguageClient))]
[ContentType("CSharp")]
[Name("EF QueryLens")]
internal sealed class QueryLensLanguageClient : ILanguageClient, ILanguageClientCustomMessage2
{
    private static readonly object Sync = new();
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<int, byte>> KnownQueryLinesByFile =
        new(StringComparer.OrdinalIgnoreCase);
    private const int DefaultMaxCodeLensPerDocument = 50;
    private const int DefaultCodeLensDebounceMilliseconds = 250;
    private static readonly Regex SqlBlockRegex = new("```sql\\s*([\\s\\S]*?)```", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private Process? _serverProcess;
    private JsonRpc? _rpc;

    internal static QueryLensLanguageClient? Current { get; private set; }

    public QueryLensLanguageClient()
    {
        lock (Sync)
        {
            Current = this;
        }
    }

    public string Name => "EF QueryLens";

    public IEnumerable<string> ConfigurationSections => [];

    public object? InitializationOptions => null;

    public IEnumerable<string> FilesToWatch => [];

    public bool ShowNotificationOnInitializeFailed => true;

    public object CustomMessageTarget => null!;

    public object MiddleLayer => null!;

#pragma warning disable CS0067
    public event AsyncEventHandler<EventArgs>? StartAsync;

    public event AsyncEventHandler<EventArgs>? StopAsync;
#pragma warning restore CS0067

    public async Task<Connection?> ActivateAsync(CancellationToken token)
    {
        var extensionDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to resolve extension assembly directory.");

        var serverPath = ResolveServerPath(extensionDirectory);
        if (!File.Exists(serverPath))
        {
            throw new FileNotFoundException("Could not find the QueryLens language server assembly.", serverPath);
        }

        var processStartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{serverPath}\"",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Environment.CurrentDirectory,
        };

        processStartInfo.Environment["QUERYLENS_MAX_CODELENS_PER_DOCUMENT"] = DefaultMaxCodeLensPerDocument.ToString(System.Globalization.CultureInfo.InvariantCulture);
        processStartInfo.Environment["QUERYLENS_CODELENS_DEBOUNCE_MS"] = DefaultCodeLensDebounceMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        processStartInfo.Environment["QUERYLENS_CODELENS_USE_MODEL_FILTER"] = "0";
        processStartInfo.Environment["QUERYLENS_DEBUG"] = "0";
        processStartInfo.Environment["QUERYLENS_CLIENT"] = "vs";
        processStartInfo.Environment["QUERYLENS_DAEMON_SHUTDOWN_ON_DISPOSE"] = "1";

        _serverProcess = new Process { StartInfo = processStartInfo };
        if (!_serverProcess.Start())
        {
            throw new InvalidOperationException("Failed to start QueryLens language server process.");
        }

        QueryLensLogger.Info($"lsp-process-started pid={_serverProcess.Id} path={serverPath}");
        _ = PumpServerErrorStreamAsync(_serverProcess, token);

        await Task.Yield();
        return new Connection(
            _serverProcess.StandardOutput.BaseStream,
            _serverProcess.StandardInput.BaseStream);
    }

    public Task OnLoadedAsync()
    {
        QueryLensLogger.Info("language-client-loaded");
        return Task.CompletedTask;
    }

    public Task OnServerInitializedAsync()
    {
        QueryLensLogger.Info("language-server-initialized");
        _ = RequestDaemonRestartAsync();
        return Task.CompletedTask;
    }

    public Task<InitializationFailureContext?> OnServerInitializeFailedAsync(ILanguageClientInitializationInfo initializationState)
    {
        QueryLensLogger.Info($"language-server-init-failed state={initializationState}");
        return Task.FromResult<InitializationFailureContext?>(null);
    }

    public Task AttachForCustomMessageAsync(JsonRpc rpc)
    {
        _rpc = rpc;
        QueryLensLogger.Info("custom-message-rpc-attached");
        return Task.CompletedTask;
    }

    internal static async Task<(bool Success, string Message)> RequestDaemonRestartAsync(CancellationToken cancellationToken)
    {
        var client = Current;
        if (client is null)
        {
            return (false, "Language client is not active yet.");
        }

        var rpc = client._rpc;
        if (rpc is null)
        {
            return (false, "Language server RPC channel is not ready yet.");
        }

        try
        {
            var response = await rpc.InvokeWithParameterObjectAsync<JToken?>(
                "efquerylens/daemon/restart",
                new JObject(),
                cancellationToken);

            var success = response?["success"]?.Value<bool>() == true;
            var message = response?["message"]?.Value<string>()
                ?? (success ? "Daemon restarted." : "Daemon restart did not complete.");

            return (success, message);
        }
        catch (Exception ex)
        {
            QueryLensLogger.Error("daemon-restart-request-failed", ex);
            return (false, $"Daemon restart failed: {ex.Message}");
        }
    }

    internal static async Task<string?> TryGetSqlPreviewAsync(string filePath, int line, int character, CancellationToken cancellationToken)
    {
        var client = Current;
        if (client is null)
        {
            return null;
        }

        var rpc = client._rpc;
        if (rpc is null)
        {
            return null;
        }

        try
        {
            var uri = new Uri(filePath).AbsoluteUri;
            var response = await rpc.InvokeWithParameterObjectAsync<JToken?>(
                "textDocument/hover",
                new JObject
                {
                    ["textDocument"] = new JObject { ["uri"] = uri },
                    ["position"] = new JObject
                    {
                        ["line"] = Math.Max(0, line),
                        ["character"] = Math.Max(0, character),
                    }
                },
                cancellationToken);

            var hoverText = ExtractHoverText(response);
            if (string.IsNullOrWhiteSpace(hoverText))
            {
                return null;
            }

            var sql = ExtractSqlBlocks(hoverText);
            var preview = string.IsNullOrWhiteSpace(sql) ? hoverText : sql;
            if (!string.IsNullOrWhiteSpace(preview))
            {
                MarkQueryLineKnown(filePath, line);
            }

            return preview;
        }
        catch (Exception ex)
        {
            QueryLensLogger.Error("hover-sql-request-failed", ex);
            return null;
        }
    }

    internal static bool IsQueryLineKnown(string filePath, int line)
    {
        if (string.IsNullOrWhiteSpace(filePath) || line < 0)
        {
            return false;
        }

        var normalizedPath = NormalizePath(filePath);
        if (!KnownQueryLinesByFile.TryGetValue(normalizedPath, out var lines))
        {
            return false;
        }

        return lines.ContainsKey(line);
    }

    private static void MarkQueryLineKnown(string filePath, int line)
    {
        if (string.IsNullOrWhiteSpace(filePath) || line < 0)
        {
            return;
        }

        var normalizedPath = NormalizePath(filePath);
        var lines = KnownQueryLinesByFile.GetOrAdd(normalizedPath, _ => new ConcurrentDictionary<int, byte>());
        lines[line] = 0;
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }

    private static async Task PumpServerErrorStreamAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!process.HasExited && !cancellationToken.IsCancellationRequested)
            {
                var line = await process.StandardError.ReadLineAsync();
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

    private async Task RequestDaemonRestartAsync()
    {
        var restartResult = await RequestDaemonRestartAsync(CancellationToken.None);
        QueryLensLogger.Info($"startup-daemon-restart success={restartResult.Success} message={restartResult.Message}");
    }

    private static string ExtractHoverText(JToken? hover)
    {
        var contents = hover?["contents"];
        if (contents is null)
        {
            return string.Empty;
        }

        if (contents.Type == JTokenType.String)
        {
            return contents.Value<string>() ?? string.Empty;
        }

        if (contents.Type == JTokenType.Array)
        {
            var segments = new List<string>();
            foreach (var item in contents)
            {
                var value = item?["value"]?.Value<string>() ?? item?.Value<string>();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    segments.Add(value!);
                }
            }

            return string.Join("\n\n", segments);
        }

        if (contents.Type == JTokenType.Object)
        {
            return contents["value"]?.Value<string>() ?? string.Empty;
        }

        return string.Empty;
    }

    private static string? ExtractSqlBlocks(string markdown)
    {
        var matches = SqlBlockRegex.Matches(markdown);
        if (matches.Count == 0)
        {
            return null;
        }

        var blocks = new List<string>();
        foreach (Match match in matches)
        {
            if (!match.Success)
            {
                continue;
            }

            var block = match.Groups.Count > 1
                ? match.Groups[1].Value.Trim()
                : string.Empty;
            if (!string.IsNullOrWhiteSpace(block))
            {
                blocks.Add(block);
            }
        }

        if (blocks.Count == 0)
        {
            return null;
        }

        return string.Join("\n\n-- next query --\n\n", blocks);
    }

    private static string ResolveServerPath(string extensionDirectory)
    {
        var serverFolderPath = Path.Combine(extensionDirectory, "server", "EFQueryLens.Lsp.dll");
        if (File.Exists(serverFolderPath))
        {
            return serverFolderPath;
        }

        var rootPath = Path.Combine(extensionDirectory, "EFQueryLens.Lsp.dll");
        if (File.Exists(rootPath))
        {
            return rootPath;
        }

        return serverFolderPath;
    }
}
