// EvalSourceBuilderDiagnosticContext.cs — thread-safe context for collecting diagnostics during
// v2 capture initialization code generation. Enables structured recording of placeholder failures,
// type resolution issues, and policy-driven rejections without changing build method signatures.
using System;
using System.Collections.Generic;
using EFQueryLens.Core.Contracts;

namespace EFQueryLens.Core.Scripting.Compilation;

/// <summary>
/// Session-scoped context for accumulating diagnostics during v2 capture code generation.
/// Enables factory methods (EvalSourceBuilder, etc.) to emit diagnostics without requiring
/// async/return-value changes to existing public APIs.
/// </summary>
internal sealed class EvalSourceBuilderDiagnosticContext
{
    private readonly List<V2CaptureDiagnostic> _diagnostics = new();
    private readonly object _lock = new object();

    /// <summary>
    /// All diagnostics recorded in this context.
    /// </summary>
    public IReadOnlyList<V2CaptureDiagnostic> Diagnostics
    {
        get
        {
            lock (_lock)
            {
                return _diagnostics.AsReadOnly();
            }
        }
    }

    /// <summary>
    /// Emit a structured diagnostic.
    /// </summary>
    public void Emit(V2CaptureDiagnostic diagnostic)
    {
        lock (_lock)
        {
            _diagnostics.Add(diagnostic);
        }
    }

    /// <summary>
    /// Emit multiple diagnostics.
    /// </summary>
    public void EmitMany(IEnumerable<V2CaptureDiagnostic> diagnostics)
    {
        lock (_lock)
        {
            _diagnostics.AddRange(diagnostics);
        }
    }

    /// <summary>
    /// Clear all diagnostics.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _diagnostics.Clear();
        }
    }

    /// <summary>
    /// Get a snapshot of currently recorded diagnostics and optionally clear.
    /// </summary>
    public IReadOnlyList<V2CaptureDiagnostic> FlushDiagnostics()
    {
        lock (_lock)
        {
            var snapshot = new List<V2CaptureDiagnostic>(_diagnostics);
            _diagnostics.Clear();
            return snapshot.AsReadOnly();
        }
    }
}

/// <summary>
/// Static accessor for thread-local diagnostic context during build operations.
/// Allows injection-free diagnostic collection without threading context through all method signatures.
/// </summary>
internal static class EvalSourceBuilderDiagnosticContextHolder
{
    [ThreadStatic]
    private static EvalSourceBuilderDiagnosticContext? _context;

    public static EvalSourceBuilderDiagnosticContext Current =>
        _context ??= new EvalSourceBuilderDiagnosticContext();

    public static void SetContext(EvalSourceBuilderDiagnosticContext? context)
    {
        _context = context;
    }

    public static void ClearContext()
    {
        _context = null;
    }
}
