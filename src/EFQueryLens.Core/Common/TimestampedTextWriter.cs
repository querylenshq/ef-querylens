using System.Text;

namespace EFQueryLens.Core.Common;

/// <summary>
/// A text writer that prepends a standard date-time stamp to written lines.
/// Used to wrap standard output streams (e.g. Console.Error) to ensure all logs
/// have consistent timing without needing to manually add the stamp at each log site.
/// </summary>
public sealed class TimestampedTextWriter(TextWriter inner) : TextWriter
{
    private readonly TextWriter _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public override Encoding Encoding => _inner.Encoding;

    public override void Write(char value)
    {
        _inner.Write(value);
    }

    public override void WriteLine(string? value)
    {
        _inner.WriteLine(Format(value));
    }

    public override Task WriteLineAsync(string? value)
    {
        return _inner.WriteLineAsync(Format(value));
    }

    private static string Format(string? value)
    {
        return $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {value}";
    }
}
