using EFQueryLens.Core.Scaffolding;
using EFQueryLens.Lsp.Handlers;
using EFQueryLens.Lsp.Protocol;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

namespace EFQueryLens.Lsp.Hosting;

internal sealed partial class LanguageServerHandler
{
    [JsonRpcMethod("efquerylens/warmup", UseSingleObjectParameterDeserialization = true)]
    public Task<WarmupResponse> WarmupAsync(TextDocumentPositionParams request, CancellationToken ct)
    {
        if (_debugEnabled) Console.Error.WriteLine("[QL-LSP] request method=efquerylens/warmup");
        return _warmup.HandleAsync(request, ct);
    }

    [JsonRpcMethod("efquerylens/daemon/restart", UseSingleObjectParameterDeserialization = true)]
    public Task<DaemonRestartResponse> RestartDaemonAsync(JToken? _ = null, CancellationToken ct = default)
    {
        if (_debugEnabled) Console.Error.WriteLine("[QL-LSP] request method=efquerylens/daemon/restart");
        return _daemonControl.RestartAsync(ct);
    }

    [JsonRpcMethod("efquerylens/preview/recalculate", UseSingleObjectParameterDeserialization = true)]
    public async Task<JObject> RecalculatePreviewAsync(TextDocumentPositionParams request, CancellationToken ct)
    {
        if (_debugEnabled) Console.Error.WriteLine("[QL-LSP] request method=efquerylens/preview/recalculate");

        var invalidateResponse = await _daemonControl.InvalidateQueryCachesAsync(ct);
        if (!invalidateResponse.Success)
        {
            return new JObject
            {
                ["success"] = false,
                ["message"] = invalidateResponse.Message,
                ["removedCachedResults"] = invalidateResponse.RemovedCachedResults,
                ["removedInflightJobs"] = invalidateResponse.RemovedInflightJobs,
            };
        }

        _hover.InvalidateForManualRecalculate();
        var hover = await _hover.HandleStructuredAsync(request, ct);

        return new JObject
        {
            ["success"] = true,
            ["message"] = "Preview cache invalidated and query recalculated.",
            ["removedCachedResults"] = invalidateResponse.RemovedCachedResults,
            ["removedInflightJobs"] = invalidateResponse.RemovedInflightJobs,
            ["hover"] = hover is null ? null : JObject.FromObject(hover),
        };
    }

    [JsonRpcMethod("efquerylens/setup/detect", UseSingleObjectParameterDeserialization = true)]
    public Task<JObject> SetupDetectAsync(TextDocumentPositionParams request, CancellationToken ct)
    {
        if (_debugEnabled) Console.Error.WriteLine("[QL-LSP] request method=efquerylens/setup/detect");

        var filePath = DocumentPathResolver.Resolve(request.TextDocument.Uri);
        var detect = _daemonControl.DetectSetupHosts(filePath);
        return Task.FromResult(ToSetupDetectJObject(detect));
    }

    [JsonRpcMethod("efquerylens/setup/apply", UseSingleObjectParameterDeserialization = true)]
    public async Task<JObject> SetupApplyAsync(JObject request, CancellationToken ct)
    {
        if (_debugEnabled) Console.Error.WriteLine("[QL-LSP] request method=efquerylens/setup/apply");

        var textDocumentUri = request["textDocument"]?["uri"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(textDocumentUri))
        {
            return new JObject
            {
                ["success"] = false,
                ["message"] = "Missing textDocument.uri for setup apply.",
                ["action"] = SetupAction.NotBuilt.ToString(),
            };
        }

        var filePath = DocumentPathResolver.Resolve(new Uri(textDocumentUri));
        var applyRequest = new SetupApplyRequest
        {
            HostProjectPath = request["hostProjectPath"]?.Value<string>(),
            ProviderOverride = ParseProviderKind(request["provider"]?.Value<string>()),
            Force = request["force"]?.Value<bool>() ?? false,
        };

        var response = await _daemonControl.SetupApplyAsync(filePath, applyRequest, ct);
        if (response.Success)
        {
            _hover.InvalidateForManualRecalculate();
        }

        return ToSetupResponseJObject(response);
    }

    [JsonRpcMethod("efquerylens/setup", UseSingleObjectParameterDeserialization = true)]
    public async Task<JObject> SetupAsync(TextDocumentPositionParams request, CancellationToken ct)
    {
        if (_debugEnabled) Console.Error.WriteLine("[QL-LSP] request method=efquerylens/setup");

        var filePath = DocumentPathResolver.Resolve(request.TextDocument.Uri);
        var response = await _daemonControl.SetupApplyAsync(filePath, new SetupApplyRequest(), ct);

        if (response.Success)
        {
            _hover.InvalidateForManualRecalculate();
        }

        return ToSetupResponseJObject(response);
    }

    private static JObject ToSetupResponseJObject(SetupResponse response)
        => new()
        {
            ["success"] = response.Success,
            ["message"] = response.Message,
            ["action"] = response.Action,
            ["generatedFilePath"] = response.GeneratedFilePath,
            ["provider"] = response.Provider.ToString(),
            ["requiresReview"] = response.RequiresReview,
        };

    private static JObject ToSetupDetectJObject(SetupDetectResult detect)
    {
        var hosts = new JArray(
            detect.Hosts.Select(host => new JObject
            {
                ["projectPath"] = host.ProjectPath,
                ["displayName"] = host.DisplayName,
                ["assemblyPath"] = host.AssemblyPath,
                ["projectDirectory"] = host.ProjectDirectory,
                ["isDefault"] = host.IsDefault,
            }));

        return new JObject
        {
            ["requiresHostSelection"] = detect.RequiresHostSelection,
            ["defaultHostProjectPath"] = detect.DefaultHostProjectPath,
            ["hosts"] = hosts,
            ["message"] = detect.Message,
        };
    }

    private static ProviderKind ParseProviderKind(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return ProviderKind.Unknown;
        }

        return provider.Trim().ToLowerInvariant() switch
        {
            "sqlserver" => ProviderKind.SqlServer,
            "npgsql" or "postgres" or "postgresql" => ProviderKind.Npgsql,
            "mysql" => ProviderKind.MySql,
            "sqlite" => ProviderKind.Sqlite,
            _ => ProviderKind.Unknown,
        };
    }

    [JsonRpcMethod("workspace/executeCommand", UseSingleObjectParameterDeserialization = true)]
    public async Task<JToken?> ExecuteCommandAsync(JObject request, CancellationToken ct)
    {
        var command = request["command"]?.Value<string>();
        if (_debugEnabled) Console.Error.WriteLine($"[QL-LSP] request method=workspace/executeCommand command={command ?? "<null>"}");

        if (string.IsNullOrWhiteSpace(command))
        {
            return new JObject
            {
                ["success"] = false,
                ["message"] = "Missing command.",
            };
        }

        if (command.Equals("efquerylens.warmup", StringComparison.OrdinalIgnoreCase))
        {
            var arguments = request["arguments"] as JArray;
            var warmupRequest = arguments?.Count > 0
                ? arguments[0].ToObject<TextDocumentPositionParams>()
                : null;

            if (warmupRequest is null)
            {
                return new JObject
                {
                    ["success"] = false,
                    ["message"] = "Missing or invalid warmup request payload.",
                };
            }

            var warmupResponse = await _warmup.HandleAsync(warmupRequest, ct);
            return JObject.FromObject(warmupResponse);
        }

        if (command.Equals("efquerylens.daemon.restart", StringComparison.OrdinalIgnoreCase))
        {
            var restartResponse = await _daemonControl.RestartAsync(ct);
            return JObject.FromObject(restartResponse);
        }

        if (command.Equals("efquerylens.preview.recalculate", StringComparison.OrdinalIgnoreCase))
        {
            var arguments = request["arguments"] as JArray;
            var recalculateRequest = arguments?.Count > 0
                ? arguments[0].ToObject<TextDocumentPositionParams>()
                : null;

            if (recalculateRequest is null)
            {
                return new JObject
                {
                    ["success"] = false,
                    ["message"] = "Missing or invalid recalculate request payload.",
                };
            }

            return await RecalculatePreviewAsync(recalculateRequest, ct);
        }

        if (command.Equals("efquerylens.preview.structuredHover", StringComparison.OrdinalIgnoreCase))
        {
            var arguments = request["arguments"] as JArray;
            var structuredHoverRequest = arguments?.Count > 0
                ? arguments[0].ToObject<HoverRequestParams>()
                : null;

            if (structuredHoverRequest is null)
            {
                return new JObject
                {
                    ["success"] = false,
                    ["message"] = "Missing or invalid structured hover request payload.",
                };
            }

            var hover = await _hover.HandleStructuredAsync(
                structuredHoverRequest,
                ct);
            return new JObject
            {
                ["success"] = hover is not null,
                ["message"] = hover?.ErrorMessage,
                ["hover"] = hover is null ? null : JObject.FromObject(hover),
            };
        }

        if (command.Equals("efquerylens.showsqlpopup", StringComparison.OrdinalIgnoreCase))
        {
            var arguments = request["arguments"] as JArray;
            var req = arguments?.Count > 0
                ? arguments[0].ToObject<TextDocumentPositionParams>()
                : null;

            if (req is null) return new JObject { ["success"] = false };

            var hover = await _hover.HandleStructuredAsync(req, ct);
            if (hover is not null)
            {
                _ = JsonRpc?.NotifyAsync("efquerylens/showSqlPopup", new JObject
                {
                    ["hover"] = JObject.FromObject(hover),
                    ["fallbackFileUri"] = req.TextDocument.Uri.ToString(),
                    ["fallbackLine"] = req.Position.Line,
                    ["fallbackCharacter"] = req.Position.Character
                });
            }

            return new JObject { ["success"] = true };
        }

        if (command.Equals("efquerylens.opensqleditor", StringComparison.OrdinalIgnoreCase))
        {
            var arguments = request["arguments"] as JArray;
            var req = arguments?.Count > 0
                ? arguments[0].ToObject<TextDocumentPositionParams>()
                : null;

            if (req is null)
            {
                return new JObject
                {
                    ["success"] = false,
                    ["message"] = "Missing or invalid payload.",
                };
            }

            var hover = await _hover.HandleStructuredAsync(req, ct);
            if (hover is not null)
            {
                var payload = new JObject
                {
                    ["hover"] = JObject.FromObject(hover),
                    ["fallbackFileUri"] = req.TextDocument.Uri.ToString(),
                    ["fallbackLine"] = req.Position.Line,
                    ["fallbackCharacter"] = req.Position.Character
                };
                
                _ = JsonRpc?.NotifyAsync("efquerylens/showSqlPreview", payload);
            }

            return new JObject { ["success"] = true };
        }

        if (command.Equals("efquerylens.copysql", StringComparison.OrdinalIgnoreCase))
        {
            var arguments = request["arguments"] as JArray;
            var req = arguments?.Count > 0
                ? arguments[0].ToObject<TextDocumentPositionParams>()
                : null;

            if (req is null) return new JObject { ["success"] = false };

            var hover = await _hover.HandleStructuredAsync(req, ct);
            if (hover is not null)
            {
                _ = JsonRpc?.NotifyAsync("efquerylens/copySqlToClipboard", new JObject
                {
                    ["sql"] = hover.EnrichedSql ?? string.Join("\n\n", hover.Statements.Select(s => s.Sql))
                });
            }

            return new JObject { ["success"] = true };
        }

        if (command.Equals("efquerylens.reanalyze", StringComparison.OrdinalIgnoreCase))
        {
            var arguments = request["arguments"] as JArray;
            var req = arguments?.Count > 0
                ? arguments[0].ToObject<TextDocumentPositionParams>()
                : null;

            if (req is null) return new JObject { ["success"] = false };

            await RecalculatePreviewAsync(req, ct);
            return new JObject { ["success"] = true };
        }

        if (command.Equals("efquerylens.setup", StringComparison.OrdinalIgnoreCase))
        {
            var arguments = request["arguments"] as JArray;
            var req = arguments?.Count > 0
                ? arguments[0].ToObject<TextDocumentPositionParams>()
                : null;

            if (req is null)
            {
                return new JObject { ["success"] = false, ["message"] = "Missing setup request payload." };
            }

            if (IsRiderClient() && JsonRpc is not null)
            {
                _ = JsonRpc.NotifyAsync("efquerylens/runSetup", CreateRiderSetupPayload(req));

                return new JObject { ["success"] = true, ["message"] = "Setup request sent to Rider." };
            }

            return await SetupAsync(req, ct);
        }

        return new JObject
        {
            ["success"] = false,
            ["message"] = $"Unsupported command '{command}'.",
        };
    }

    private static bool IsRiderClient()
        => string.Equals(
            Environment.GetEnvironmentVariable("QUERYLENS_CLIENT"),
            "rider",
            StringComparison.OrdinalIgnoreCase);

    internal static JObject CreateRiderSetupPayload(TextDocumentPositionParams request)
        => new()
        {
            ["fileUri"] = request.TextDocument.Uri.ToString(),
            ["line"] = request.Position.Line,
            ["character"] = request.Position.Character,
        };
}
