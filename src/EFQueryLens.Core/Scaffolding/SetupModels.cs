namespace EFQueryLens.Core.Scaffolding;

/// <summary>The EF Core provider a generated factory should configure.</summary>
public enum ProviderKind
{
    Unknown = 0,
    SqlServer,
    Npgsql,
    MySql,
    Sqlite,
}

/// <summary>Outcome of a setup/scaffold run.</summary>
public enum SetupAction
{
    /// <summary>A new factory file was written (or an out-of-date one regenerated).</summary>
    Generated,

    /// <summary>A hand-written factory already exists — nothing to do.</summary>
    SkippedExistingFactory,

    /// <summary>The generated file already matches the current project — no write needed.</summary>
    SkippedUpToDate,

    /// <summary>The generated file was edited by hand; refused to overwrite without force.</summary>
    RefusedEdited,

    /// <summary>The provider could not be detected; the caller must supply one.</summary>
    NeedProvider,

    /// <summary>The project hasn't been built, so DbContext types can't be discovered.</summary>
    NotBuilt,

    /// <summary>No DbContext subclass was found in the assembly.</summary>
    NoDbContext,
}

/// <summary>A DbContext registration discovered from an <c>AddDbContext</c> call in source.</summary>
public sealed record DbContextRegistration
{
    /// <summary>Context type as written in source (may be simple or qualified).</summary>
    public required string ContextTypeName { get; init; }

    /// <summary>Lambda parameter name for the options builder (e.g. <c>options</c>).</summary>
    public required string BuilderParameterName { get; init; }

    /// <summary>Fluent chain text starting with <c>.UseXxx(...)</c>.</summary>
    public required string OptionsChain { get; init; }

    /// <summary>Using directives from the registration source file.</summary>
    public IReadOnlyList<string> Usings { get; init; } = [];

    /// <summary>Absolute path to the file containing the registration.</summary>
    public string? SourceFilePath { get; init; }
}

/// <summary>Per-context render plan after joining assembly discovery with registrations.</summary>
public sealed record ContextRenderPlan
{
    public required string ContextFullName { get; init; }
    public ProviderKind Provider { get; init; } = ProviderKind.Unknown;
    public bool UseProjectables { get; init; }
    public bool UseSplitQuery { get; init; }

    /// <summary>Offline options chain starting with <c>.UseXxx(...)</c>; null when using template fallback.</summary>
    public string? OfflineOptionsChain { get; init; }

    /// <summary>True when options were copied from a matched <c>AddDbContext</c> registration.</summary>
    public bool MatchedRegistration { get; init; }

    public IReadOnlyList<string> ExtraUsings { get; init; } = [];
}

/// <summary>Inputs for a setup/scaffold run.</summary>
public sealed record SetupRequest
{
    /// <summary>Absolute path to the executable project's compiled .dll.</summary>
    public required string AssemblyPath { get; init; }

    /// <summary>Directory of the executable project (where the factory + .gitignore live).</summary>
    public required string ProjectDirectory { get; init; }

    /// <summary>Overrides provider auto-detection when not <see cref="ProviderKind.Unknown"/>.</summary>
    public ProviderKind ProviderOverride { get; init; } = ProviderKind.Unknown;

    /// <summary>Overwrite a hand-edited generated file.</summary>
    public bool Force { get; init; }

    /// <summary>Append the ignore rule to .gitignore (default true).</summary>
    public bool UpdateGitignore { get; init; } = true;
}

/// <summary>Result of a setup/scaffold run.</summary>
public sealed record SetupResult
{
    public required SetupAction Action { get; init; }
    public ProviderKind Provider { get; init; } = ProviderKind.Unknown;
    public IReadOnlyList<string> Contexts { get; init; } = [];
    public string? GeneratedFilePath { get; init; }
    public bool GitignoreUpdated { get; init; }
    public required string Message { get; init; }

    /// <summary>
    /// True when a new factory was generated — callers should always prompt the user to review it.
    /// </summary>
    public bool RequiresReview { get; init; }

    /// <summary>
    /// True when one or more DbContexts used template defaults instead of a matched registration.
    /// </summary>
    public bool UsedBestEffortDefaults { get; init; }

    /// <summary>Full names of DbContexts that used template defaults.</summary>
    public IReadOnlyList<string> ContextsNeedingReview { get; init; } = [];

    public bool Succeeded => Action is SetupAction.Generated or SetupAction.SkippedUpToDate or SetupAction.SkippedExistingFactory;
}

/// <summary>Candidate executable host for setup when the current file is in a class library.</summary>
public sealed record SetupHostCandidate
{
    public required string ProjectPath { get; init; }
    public required string DisplayName { get; init; }
    public string? AssemblyPath { get; init; }
    public string? ProjectDirectory { get; init; }
    public bool IsDefault { get; init; }
}

/// <summary>Result of setup host detection before applying scaffold.</summary>
public sealed record SetupDetectResult
{
    public bool RequiresHostSelection { get; init; }
    public string? DefaultHostProjectPath { get; init; }
    public IReadOnlyList<SetupHostCandidate> Hosts { get; init; } = [];
    public string? Message { get; init; }
}
