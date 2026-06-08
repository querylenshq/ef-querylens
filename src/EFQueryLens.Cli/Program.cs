using System.CommandLine;
using EFQueryLens.Core.Scaffolding;

var projectPathArgument = new Argument<string>("projectPath")
{
    Description = "Path to a .csproj, project directory, or .sln/.slnx file.",
};

var hostOption = new Option<string?>("--host")
{
    Description = "Executable host .csproj when the project path is a solution or class library.",
};

var providerOption = new Option<string?>("--provider")
{
    Description = "Override provider auto-detection: SqlServer, Npgsql, MySql, or Sqlite.",
};

var forceOption = new Option<bool>("--force")
{
    Description = "Overwrite a hand-edited generated factory file.",
};

var setupCommand = new Command("setup", "Generate the offline QueryLens DbContext factory for a project.")
{
    projectPathArgument,
    hostOption,
    providerOption,
    forceOption,
};

setupCommand.SetAction(parseResult =>
{
    var projectPath = parseResult.GetValue(projectPathArgument)!;
    var hostPath = parseResult.GetValue(hostOption);
    var providerName = parseResult.GetValue(providerOption);
    var force = parseResult.GetValue(forceOption);

    var providerOverride = SetupHostResolver.ParseProvider(providerName);
    if (!string.IsNullOrWhiteSpace(providerName) && providerOverride == ProviderKind.Unknown)
    {
        Console.Error.WriteLine(
            $"Unknown provider '{providerName}'. Use SqlServer, Npgsql, MySql, or Sqlite.");
        return 2;
    }

    SetupHostResolver.ResolvedHost? host;
    if (!string.IsNullOrWhiteSpace(hostPath))
    {
        host = SetupHostResolver.ResolveHost(projectPath, hostPath);
        if (host is null)
        {
            Console.Error.WriteLine($"Could not resolve executable host from '{hostPath}'.");
            return 1;
        }
    }
    else
    {
        var normalized = Path.GetFullPath(projectPath);
        if (normalized.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            var hosts = SetupHostResolver.ListExecutableHosts(normalized)
                .Where(candidate => candidate.AssemblyPath is not null)
                .ToList();

            if (hosts.Count == 0)
            {
                Console.Error.WriteLine("No built executable host projects were found in the solution.");
                Console.Error.WriteLine("Build the solution, then re-run setup with --host <csprojPath>.");
                return 1;
            }

            if (hosts.Count > 1)
            {
                Console.Error.WriteLine("Multiple executable hosts were found. Re-run with --host <csprojPath>:");
                foreach (var candidate in hosts)
                {
                    Console.Error.WriteLine($"  {candidate.CsprojPath}");
                }

                return 1;
            }

            host = hosts[0];
        }
        else
        {
            host = SetupHostResolver.ResolveHost(projectPath);
            if (host is null)
            {
                Console.Error.WriteLine(
                    "Could not resolve an executable host project. Build the project and pass --host <csprojPath> when needed.");
                return 1;
            }
        }
    }

    if (string.IsNullOrWhiteSpace(host.AssemblyPath) || !File.Exists(host.AssemblyPath))
    {
        Console.Error.WriteLine("Could not locate the compiled executable assembly.");
        Console.Error.WriteLine("Build the host project, then run setup again.");
        return 1;
    }

    var result = FactoryScaffolder.Run(new SetupRequest
    {
        AssemblyPath = host.AssemblyPath,
        ProjectDirectory = host.ProjectDirectory,
        ProviderOverride = providerOverride,
        Force = force,
    });

    Console.WriteLine(result.Message);
    if (!string.IsNullOrWhiteSpace(result.GeneratedFilePath))
    {
        Console.WriteLine($"Generated: {result.GeneratedFilePath}");
    }

    if (result.Contexts.Count > 0)
    {
        Console.WriteLine($"Contexts: {string.Join(", ", result.Contexts)}");
    }

    return result.Succeeded ? 0 : 1;
});

var rootCommand = new RootCommand("EF QueryLens command-line tools.")
{
    setupCommand,
};

return rootCommand.Parse(args).Invoke();
