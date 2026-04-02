using System.Text.Json;
using CSharpier.Core;
using CSharpier.Core.CSharp;

var input = await Console.In.ReadToEndAsync();
if (string.IsNullOrWhiteSpace(input))
{
    await WriteResponseAsync(new FormatResponse(false, string.Empty, ["Empty input."]));
    return;
}

FormatRequest? request;
try
{
    request = JsonSerializer.Deserialize<FormatRequest>(input);
}
catch (Exception ex)
{
    await WriteResponseAsync(new FormatResponse(false, string.Empty, [$"Invalid JSON request: {ex.Message}"]));
    return;
}

if (request is null || string.IsNullOrWhiteSpace(request.Code))
{
    await WriteResponseAsync(new FormatResponse(false, string.Empty, ["Missing code."]));
    return;
}

try
{
    var result = CSharpFormatter.Format(request.Code, new CodeFormatterOptions());
    var errors = result.CompilationErrors.Select(x => x.ToString()).ToArray();
    var success = errors.Length == 0;
    await WriteResponseAsync(new FormatResponse(success, result.Code, errors));
}
catch (Exception ex)
{
    await WriteResponseAsync(new FormatResponse(false, string.Empty, [$"Formatter error: {ex.Message}"]));
}

static async Task WriteResponseAsync(FormatResponse response)
{
    var json = JsonSerializer.Serialize(response);
    await Console.Out.WriteAsync(json);
}

internal sealed record FormatRequest(string Code);

internal sealed record FormatResponse(bool Success, string Code, IReadOnlyList<string> Errors);
