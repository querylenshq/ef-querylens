using System;
using EFQueryLens.Core;
using EFQueryLens.Core.Common;
using EFQueryLens.Lsp;
using EFQueryLens.Lsp.Hosting;

Console.SetError(new TimestampedTextWriter(Console.Error));
var lspLogWriter = LspProgramHelpers.ConfigureLspLogWriter();

try
{
    if (LspProgramHelpers.TryRunCacheStatusCommand(args))
    {
        return;
    }

    if (LspProgramHelpers.TryRunCacheCleanupCommand(args))
    {
        return;
    }

    if (LspProgramHelpers.TryRunCacheClearCommand(args))
    {
        return;
    }

    var debugEnabled = LspEnvironment.ReadBool("QUERYLENS_DEBUG", fallback: false);
    if (debugEnabled)
    {
        Console.Error.WriteLine("[QL-LSP] startup");
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Console.Error.WriteLine($"[QL-LSP] unhandled-exception terminating={args.IsTerminating} exception={args.ExceptionObject}");
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Console.Error.WriteLine($"[QL-LSP] unobserved-task-exception exception={args.Exception}");
        };
    }

    await MicrosoftLspHost.RunAsync(_ => LspProgramHelpers.CreateEngineAsync(debugEnabled));
}
catch (Exception ex)
{
    var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture);
    var path = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        $"querylens-crash-{timestamp}-pid{Environment.ProcessId}.log");
    System.IO.File.WriteAllText(path, ex.ToString());
    Console.Error.WriteLine($"[QL-LSP] fatal crashLog={path} type={ex.GetType().Name} message={ex.Message}");
    throw;
}
finally
{
    lspLogWriter?.Dispose();
}