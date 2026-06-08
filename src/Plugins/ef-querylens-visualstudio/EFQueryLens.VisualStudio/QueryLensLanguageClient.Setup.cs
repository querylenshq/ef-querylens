// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace EFQueryLens.VisualStudio;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

internal sealed partial class QueryLensLanguageClient
{
    internal sealed class SetupHostCandidate
    {
        internal SetupHostCandidate(string projectPath, string displayName, string? assemblyPath)
        {
            ProjectPath = projectPath;
            DisplayName = displayName;
            AssemblyPath = assemblyPath;
        }

        internal string ProjectPath { get; }

        internal string DisplayName { get; }

        internal string? AssemblyPath { get; }
    }

    internal sealed class SetupDetectResult
    {
        internal SetupDetectResult(
            bool requiresHostSelection,
            string? defaultHostProjectPath,
            IReadOnlyList<SetupHostCandidate> hosts,
            string? message)
        {
            RequiresHostSelection = requiresHostSelection;
            DefaultHostProjectPath = defaultHostProjectPath;
            Hosts = hosts;
            Message = message;
        }

        internal bool RequiresHostSelection { get; }

        internal string? DefaultHostProjectPath { get; }

        internal IReadOnlyList<SetupHostCandidate> Hosts { get; }

        internal string? Message { get; }
    }

    internal sealed class SetupApplyResult
    {
        internal SetupApplyResult(
            bool success,
            string message,
            string? action,
            string? generatedFilePath,
            bool requiresReview)
        {
            Success = success;
            Message = message;
            Action = action;
            GeneratedFilePath = generatedFilePath;
            RequiresReview = requiresReview;
        }

        internal bool Success { get; }

        internal string Message { get; }

        internal string? Action { get; }

        internal string? GeneratedFilePath { get; }

        internal bool RequiresReview { get; }
    }

    internal static async Task<(bool Success, SetupDetectResult? Result, string Message)> RequestSetupDetectAsync(
        string documentUri,
        int line,
        int character,
        CancellationToken cancellationToken)
    {
        var client = Current;
        if (client?.rpc is not JsonRpc languageServerRpc)
        {
            return (false, null, "Language server RPC channel is not ready yet.");
        }

        try
        {
            var response = await languageServerRpc.InvokeWithParameterObjectAsync<JToken?>(
                "efquerylens/setup/detect",
                new JObject
                {
                    ["textDocument"] = new JObject { ["uri"] = documentUri },
                    ["position"] = new JObject
                    {
                        ["line"] = line,
                        ["character"] = character,
                    },
                },
                cancellationToken);

            if (response is null || response.Type == JTokenType.Null)
            {
                return (false, null, "Setup host detection returned no response.");
            }

            return (true, ParseSetupDetectResponse(response), "Setup host detection completed.");
        }
        catch (Exception ex)
        {
            Log($"setup-detect-request-failed type={ex.GetType().Name} message={ex.Message}");
            return (false, null, $"Setup host detection failed: {ex.Message}");
        }
    }

    internal static async Task<(bool Success, SetupApplyResult? Result, string Message)> RequestSetupApplyAsync(
        string documentUri,
        string? hostProjectPath,
        string? provider,
        bool force,
        CancellationToken cancellationToken)
    {
        var client = Current;
        if (client?.rpc is not JsonRpc languageServerRpc)
        {
            return (false, null, "Language server RPC channel is not ready yet.");
        }

        try
        {
            var request = new JObject
            {
                ["textDocument"] = new JObject { ["uri"] = documentUri },
                ["force"] = force,
            };

            if (!string.IsNullOrWhiteSpace(hostProjectPath))
            {
                request["hostProjectPath"] = hostProjectPath;
            }

            if (!string.IsNullOrWhiteSpace(provider))
            {
                request["provider"] = provider;
            }

            var response = await languageServerRpc.InvokeWithParameterObjectAsync<JToken?>(
                "efquerylens/setup/apply",
                request,
                cancellationToken);

            if (response is null || response.Type == JTokenType.Null)
            {
                return (false, null, "Setup apply returned no response.");
            }

            var result = ParseSetupApplyResponse(response);
            return (true, result, result.Message);
        }
        catch (Exception ex)
        {
            Log($"setup-apply-request-failed type={ex.GetType().Name} message={ex.Message}");
            return (false, null, $"Setup apply failed: {ex.Message}");
        }
    }

    private static SetupDetectResult ParseSetupDetectResponse(JToken response)
    {
        var hosts = new List<SetupHostCandidate>();
        var rawHosts = response["hosts"] ?? response["Hosts"];
        if (rawHosts is JArray hostArray)
        {
            foreach (var host in hostArray.OfType<JObject>())
            {
                var projectPath = ReadStringField(host, "projectPath");
                if (string.IsNullOrWhiteSpace(projectPath))
                {
                    continue;
                }

                hosts.Add(new SetupHostCandidate(
                    projectPath,
                    ReadStringField(host, "displayName") ?? "Host project",
                    ReadStringField(host, "assemblyPath")));
            }
        }

        return new SetupDetectResult(
            ReadBoolField(response, "requiresHostSelection"),
            ReadStringField(response, "defaultHostProjectPath"),
            hosts,
            ReadStringField(response, "message"));
    }

    private static SetupApplyResult ParseSetupApplyResponse(JToken response)
    {
        var success = ReadBoolField(response, "success");
        var message = ReadStringField(response, "message")
            ?? (success ? "QueryLens factory generated." : "Set up QueryLens did not complete.");

        return new SetupApplyResult(
            success,
            message,
            ReadStringField(response, "action"),
            ReadStringField(response, "generatedFilePath"),
            ReadBoolField(response, "requiresReview"));
    }

    private static string? ReadStringField(JToken payload, string camelKey)
    {
        var pascalKey = char.ToUpperInvariant(camelKey[0]) + camelKey.Substring(1);
        var value = payload[camelKey] ?? payload[pascalKey];
        return value?.Type == JTokenType.String ? value.Value<string>() : null;
    }

    private static bool ReadBoolField(JToken payload, string camelKey)
    {
        var pascalKey = char.ToUpperInvariant(camelKey[0]) + camelKey.Substring(1);
        var value = payload[camelKey] ?? payload[pascalKey];
        return value?.Type == JTokenType.Boolean && value.Value<bool>();
    }
}
