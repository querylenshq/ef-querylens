using System.Diagnostics;
using System.Text;

namespace EFQueryLens.VisualStudio;

internal static class QueryLensLogger
{
    private static readonly object Sync = new();

    public static string LogFilePath { get; } = Path.Combine(
        Path.GetTempPath(),
        "EFQueryLens.VisualStudio.log");

    public static void Info(string message)
    {
        Write("INFO", message, exception: null);
    }

    public static void Error(string message, Exception exception)
    {
        Write("ERROR", message, exception);
    }

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            var line = BuildLine(level, message, exception);
            lock (Sync)
            {
                File.AppendAllText(LogFilePath, line, Encoding.UTF8);
            }

            Debug.Write(line);
            Console.Error.Write(line);
        }
        catch
        {
            // Best-effort logging only. Never throw from logger.
        }
    }

    private static string BuildLine(string level, string message, Exception? exception)
    {
        var processId = Environment.ProcessId;
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
        var builder = new StringBuilder();
        builder.Append(now)
            .Append("Z [")
            .Append(level)
            .Append("] pid=")
            .Append(processId)
            .Append(" ")
            .Append(message);

        if (exception is not null)
        {
            builder.Append(" | ex=")
                .Append(exception.GetType().Name)
                .Append(": ")
                .Append(exception.Message)
                .AppendLine()
                .Append(exception.StackTrace);
        }

        builder.AppendLine();
        return builder.ToString();
    }
}
