using System.Diagnostics;
using System.Reflection;
using EFQueryLens.Core.Contracts;
using EFQueryLens.Core.Scripting.Contracts;

namespace EFQueryLens.Core.Scripting.Evaluation;

internal static partial class RunnerExecutor
{
    private static async Task<(object? Queryable, string? CaptureSkipReason, string? CaptureError, IReadOnlyList<QuerySqlCommand> Commands)>
        InvokeRunMethodAsync(
            QueryEvaluator.AsyncRunnerInvoker runAsync,
            object dbInstance,
            CancellationToken ct)
    {
        var payload = await runAsync(dbInstance, ct).ConfigureAwait(false);
        return ParseExecutionPayload(payload);
    }

    internal static QueryEvaluator.SyncRunnerInvoker CreateSyncRunnerInvoker(Type runType)
    {
        var runMethod = runType.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find Run method in __QueryLensRunner__.");

        try
        {
            return (QueryEvaluator.SyncRunnerInvoker)Delegate.CreateDelegate(typeof(QueryEvaluator.SyncRunnerInvoker), runMethod);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Could not bind sync runner delegate for __QueryLensRunner__.Run.", ex);
        }
    }

    internal static QueryEvaluator.AsyncRunnerInvoker CreateAsyncRunnerInvoker(Type runType)
    {
        var runMethod = runType.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("Could not find RunAsync method in __QueryLensRunner__.");

        try
        {
            return (QueryEvaluator.AsyncRunnerInvoker)Delegate.CreateDelegate(typeof(QueryEvaluator.AsyncRunnerInvoker), runMethod);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Could not bind async runner delegate for __QueryLensRunner__.RunAsync.", ex);
        }
    }

    private static (object? Queryable, string? CaptureSkipReason, string? CaptureError, IReadOnlyList<QuerySqlCommand> Commands)
        ParseExecutionPayload(object? payload)
    {
        if (payload is null)
            return (null, "Generated runner returned null payload.", null, []);

        if (payload is not IQueryLensExecutionPayload typedPayload)
        {
            throw new InvalidOperationException(
                $"Payload contract mismatch: generated runner returned '{payload.GetType().FullName}', " +
                $"which does not implement {nameof(IQueryLensExecutionPayload)}.");
        }

        if (typedPayload.PayloadContractVersion != QueryLensGeneratedPayloadContract.Version)
        {
            throw new InvalidOperationException(
                $"Payload contract version mismatch: expected {QueryLensGeneratedPayloadContract.Version} " +
                $"but runner returned {typedPayload.PayloadContractVersion}.");
        }

        var commands = typedPayload.Commands
            .Where(static command => !string.IsNullOrWhiteSpace(command.Sql))
            .Select(static command => new QuerySqlCommand
            {
                Sql = command.Sql,
                Parameters = command.Parameters
                    .Where(static parameter => parameter is not null)
                    .Select(static parameter => new QueryParameter
                    {
                        Name = string.IsNullOrWhiteSpace(parameter.Name) ? "@p" : parameter.Name,
                        ClrType = string.IsNullOrWhiteSpace(parameter.ClrType)
                            ? (string.IsNullOrWhiteSpace(parameter.DbTypeName) ? "object" : parameter.DbTypeName)
                            : parameter.ClrType,
                        InferredValue = parameter.InferredValue,
                    })
                    .ToList(),
            })
            .ToList();

        return (
            typedPayload.Queryable,
            typedPayload.CaptureSkipReason,
            typedPayload.CaptureError,
            commands);
    }

    internal static async Task<(IReadOnlyList<QuerySqlCommand> Commands, List<QueryWarning> Warnings, string? FailureReason, TimeSpan RunnerExecutionTime)> ExecuteRunnerAndCaptureAsync(
        bool useAsyncRunner,
        QueryEvaluator.AsyncRunnerInvoker? asyncRunner,
        QueryEvaluator.SyncRunnerInvoker? syncRunner,
        object dbInstance,
        CancellationToken ct)
    {
        var runnerExecutionWatch = Stopwatch.StartNew();
        var (queryable, captureSkipReason, captureError, capturedCommands) = useAsyncRunner
            ? await InvokeRunMethodAsync(
                asyncRunner ?? throw new InvalidOperationException("Async runner delegate was not initialized."),
                dbInstance,
                ct)
            : ParseExecutionPayload(
                (syncRunner ?? throw new InvalidOperationException("Sync runner delegate was not initialized."))(dbInstance));
        runnerExecutionWatch.Stop();

        if (capturedCommands.Count == 0)
        {
            return ([], [], captureSkipReason ?? captureError ?? "Offline capture produced no SQL commands.", runnerExecutionWatch.Elapsed);
        }

        var warnings = new List<QueryWarning>();
        if (!string.IsNullOrWhiteSpace(captureError))
        {
            warnings.Add(new QueryWarning
            {
                Severity = WarningSeverity.Warning,
                Code = "QL_CAPTURE_PARTIAL",
                Message = "Captured SQL commands, but query materialization failed in offline mode.",
                Suggestion = captureError,
            });
        }

        return (capturedCommands, warnings, null, runnerExecutionWatch.Elapsed);
    }
}

