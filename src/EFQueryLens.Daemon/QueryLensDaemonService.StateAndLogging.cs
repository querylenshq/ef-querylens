using EFQueryLens.Core.Grpc;

namespace EFQueryLens.Daemon;

internal sealed partial class QueryLensDaemonService
{
    private void TrackState(string contextName, DaemonWarmState state)
    {
        if (string.IsNullOrWhiteSpace(contextName))
        {
            return;
        }

        if (contextStates.TryGetValue(contextName, out var existing) && existing == state)
        {
            return;
        }

        contextStates[contextName] = state;
        eventStreamBroker.PublishStateChanged(contextName, state);
    }

    private void TrackAssemblyPath(string contextName, string? assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(contextName) || string.IsNullOrWhiteSpace(assemblyPath))
        {
            return;
        }

        if (_contextAssemblyPaths.TryGetValue(contextName, out var existing)
            && string.Equals(existing, assemblyPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _contextAssemblyPaths[contextName] = assemblyPath;
        eventStreamBroker.PublishAssemblyChanged(contextName, assemblyPath);
    }

    private static bool ReadBoolEnvironmentVariable(string variableName, bool fallback)
    {
        var raw = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        if (bool.TryParse(raw, out var parsed))
        {
            return parsed;
        }

        return raw.Equals("1", StringComparison.OrdinalIgnoreCase)
               || raw.Equals("yes", StringComparison.OrdinalIgnoreCase)
               || raw.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    private void LogDebug(string message)
    {
        if (!_debugEnabled)
        {
            return;
        }

        Console.Error.WriteLine($"[QL-DAEMON] {message}");
    }
}
