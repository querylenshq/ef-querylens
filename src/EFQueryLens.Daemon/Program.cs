using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using EFQueryLens.Core;
using EFQueryLens.Core.Daemon;
using EFQueryLens.Core.Grpc;
using EFQueryLens.Daemon;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using EFQueryLens.Core.Common;
using EFQueryLens.Core.Contracts;
using EFQueryLens.Core.Engine;

Console.SetError(new TimestampedTextWriter(Console.Error));

if (!TryParseArgs(args, out var workspacePath, out var requestedPort))
{
    Console.Error.WriteLine("Usage: querylens-daemon --workspace <absolute-or-relative-path> [--port <tcp-port>] ");
    return 1;
}

workspacePath = Path.GetFullPath(workspacePath);
var listenPort = requestedPort ?? 0;

Console.Error.WriteLine($"[QL-Daemon] startup workspace={workspacePath} requestedPort={listenPort}");

AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
{
    Console.Error.WriteLine($"[QL-Daemon] unhandled-exception terminating={eventArgs.IsTerminating} exception={eventArgs.ExceptionObject}");
};
TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
{
    Console.Error.WriteLine($"[QL-Daemon] unobserved-task-exception exception={eventArgs.Exception}");
};

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders()
    .AddConsole()
    .SetMinimumLevel(LogLevel.Critical);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(IPAddress.Loopback, listenPort, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http2;
    });
});

builder.Services.AddGrpc();
builder.Services.AddSingleton<IQueryLensEngine, QueryLensEngine>();
builder.Services.AddSingleton<TranslationMetrics>();
builder.Services.AddSingleton<SqlTranslationQueue>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SqlTranslationQueue>());
builder.Services.AddSingleton(new ConcurrentDictionary<string, DaemonWarmState>(StringComparer.Ordinal));
builder.Services.AddSingleton<DaemonEventStreamBroker>();
builder.Services.AddSingleton(new DaemonWorkspaceOptions(workspacePath));
builder.Services.AddHostedService<QueryLensConfigWatcher>();

try
{
    var app = builder.Build();
    app.MapGrpcService<QueryLensDaemonService>();

    await app.StartAsync();
    var boundPort = ResolveBoundPort(app, requestedPort);

    using var pidManager = new PidManager(workspacePath, boundPort);
    Console.Error.WriteLine($"[QL-Daemon] listening workspace={workspacePath} port={boundPort}");

    await app.WaitForShutdownAsync();
    await app.StopAsync();
    return 0;
}
catch (Exception ex)
{
    var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
    var crashPath = Path.Combine(Path.GetTempPath(), $"querylens-daemon-crash-{timestamp}-pid{Environment.ProcessId}.log");
    File.WriteAllText(crashPath, ex.ToString());
    Console.Error.WriteLine($"[QL-Daemon] fatal-exit crashLog={crashPath} type={ex.GetType().Name} message={ex.Message}");
    throw;
}

static int ResolveBoundPort(WebApplication app, int? requestedPort)
{
    if (requestedPort is > 0)
    {
        return requestedPort.Value;
    }

    var server = app.Services.GetRequiredService<IServer>();
    var addressesFeature = server.Features.Get<IServerAddressesFeature>();
    var addresses = addressesFeature?.Addresses ?? [];

    foreach (var address in addresses)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            continue;
        }

        if (uri.Port > 0)
        {
            return uri.Port;
        }
    }

    throw new InvalidOperationException("Kestrel started but no bound gRPC port was discovered.");
}

static bool TryParseArgs(string[] args, out string workspacePath, out int? port)
{
    workspacePath = string.Empty;
    port = null;

    for (var i = 0; i < args.Length; i++)
    {
        var current = args[i];
        if (current.Equals("--workspace", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        {
            workspacePath = args[++i];
            continue;
        }

        if (current.Equals("--port", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        {
            if (int.TryParse(args[++i], out var parsedPort) && parsedPort is >= 0 and <= 65535)
            {
                port = parsedPort;
            }
        }
    }

    return !string.IsNullOrWhiteSpace(workspacePath);
}
