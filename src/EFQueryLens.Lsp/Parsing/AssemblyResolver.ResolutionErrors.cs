namespace EFQueryLens.Lsp.Parsing;

public static partial class AssemblyResolver
{
    internal static string FormatTargetAssemblyFailureMessage(string? resolved)
    {
        const string fallback =
            "Could not locate compiled target assembly for this file. Build the project and try again.";

        if (string.IsNullOrWhiteSpace(resolved))
        {
            return fallback;
        }

        if (!resolved.StartsWith("DEBUG_FAIL", StringComparison.Ordinal))
        {
            return $"{fallback} Expected assembly at '{resolved}' but the file was not found.";
        }

        if (resolved.Contains("Walked to root, no csproj found", StringComparison.Ordinal))
        {
            return "Could not locate a .csproj for this file. Save the file inside a C# project and try again.";
        }

        if (resolved.Contains("No .slnx or .sln file found", StringComparison.Ordinal)
            || resolved.Contains("No .sln file found", StringComparison.Ordinal))
        {
            return "Could not locate a .slnx or .sln file above this project. Open the solution folder in your IDE and try again.";
        }

        if (resolved.Contains("Found 0 other projects in solution", StringComparison.Ordinal))
        {
            var solutionName = TryExtractSolutionName(resolved);
            return solutionName is null
                ? "The solution file contains no other projects. Add the executable host project to the solution."
                : $"The solution '{solutionName}' contains no other projects. Add the executable host project and rebuild.";
        }

        if (resolved.Contains("No executable project", StringComparison.Ordinal))
        {
            return "No executable host project was found in the solution. Add an API, worker, or console project that references this library.";
        }

        if (resolved.Contains("library DLL not alongside", StringComparison.Ordinal)
            || resolved.Contains("No candidate host project has a built bin folder containing the library", StringComparison.Ordinal))
        {
            return "A host project was found, but its build output does not include this library's DLL. Rebuild the executable host project, then try again.";
        }

        if (resolved.Contains("not found in bin dir", StringComparison.Ordinal)
            || resolved.Contains("bin dir does not exist", StringComparison.Ordinal))
        {
            return "The project has not been built yet. Build the solution, then try again.";
        }

        return fallback;
    }

    private static string? TryExtractSolutionName(string debugFail)
    {
        const string prefix = "Found solution: ";
        var start = debugFail.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += prefix.Length;
        var end = debugFail.IndexOf('\n', start);
        if (end < 0)
        {
            end = debugFail.Length;
        }

        var name = debugFail[start..end].Trim();
        return name.Length > 0 ? name : null;
    }
}
