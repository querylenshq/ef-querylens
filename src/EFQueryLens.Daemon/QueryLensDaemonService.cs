using System.Collections.Concurrent;
using EFQueryLens.Core;
using EFQueryLens.Core.Contracts;
using EFQueryLens.Core.Grpc;
using Microsoft.Extensions.Hosting;

namespace EFQueryLens.Daemon;

internal sealed partial class QueryLensDaemonService(
    IQueryLensEngine engine,
    SqlTranslationQueue queue,
    TranslationMetrics metrics,
    ConcurrentDictionary<string, DaemonWarmState> contextStates,
    DaemonEventStreamBroker eventStreamBroker,
    IHostApplicationLifetime hostLifetime)
    : DaemonService.DaemonServiceBase
{
    private readonly bool _debugEnabled = ReadBoolEnvironmentVariable("QUERYLENS_DEBUG", fallback: false);
    private readonly DateTime _startedUtc = DateTime.UtcNow;
    private readonly ConcurrentDictionary<string, string> _contextAssemblyPaths = new(StringComparer.Ordinal);
}
