// EvalSourceBuilderDiagnostics.cs — factory for emitting structured diagnostics when variables
// cannot be resolved or placeholders cannot be generated. Implements the diagnostic taxonomy from
// docs/unresolved-capture-examples.md with consistent code formatting, categorization, and remediation guidance.
using System;
using System.Collections.Generic;
using EFQueryLens.Core.Contracts;

namespace EFQueryLens.Core.Scripting.Compilation;

/// <summary>
/// Factory for building structured diagnostics when v2 capture initialization fails or falls back to defaults.
/// </summary>
internal static class EvalSourceBuilderDiagnostics
{
    private static int _diagnosticSequence;

    /// <summary>
    /// Diagnostic when a placeholder (UsePlaceholder policy) cannot be generated for a type.
    /// </summary>
    internal static V2CaptureDiagnostic DetailedPlaceholderUnsupported(
        string symbolName,
        string typeName,
        string? reason = null)
    {
        return new V2CaptureDiagnostic
        {
            Code = $"QLDIAG_PLACEHOLDER_UNSUPPORTED_{NextSequence()}",
            Category = "placeholder-unsupported",
            SymbolName = symbolName,
            Reason = reason ?? "no-canonical-default-available",
            Message = $"Cannot synthesize a placeholder value for symbol '{symbolName}' of type '{typeName}'. " +
                      "The type is not in the canonical default catalog and cannot be reliably reconstructed at extraction time.",
            SuggestedFix = "Consider moving this variable outside the query or using a simpler type (e.g., string, int, bool) that can be stubbed.",
        };
    }

    /// <summary>
    /// Diagnostic for when closure chain member resolution fails.
    /// </summary>
    internal static V2CaptureDiagnostic ClosureChainNotMaterializableAsync(
        string symbolName,
        string symbolPath,
        string? failureReason = null)
    {
        return new V2CaptureDiagnostic
        {
            Code = $"QLDIAG_CLOSURE_CHAIN_{NextSequence()}",
            Category = "closure-chain-not-materializable",
            SymbolName = symbolName,
            SymbolPath = symbolPath,
            Reason = failureReason ?? "member-chain-evaluation-failed",
            Message = $"Cannot materialize the closure chain '{symbolPath}' at extraction time. " +
                      "The chain may contain runtime-only objects, async interactions, or lazy evaluation.",
            SuggestedFix = "Extract the value into a local variable before the query expression.",
        };
    }

    /// <summary>
    /// Diagnostic for non-deterministic sources like DateTime.UtcNow (when logged as unresolved).
    /// </summary>
    internal static V2CaptureDiagnostic NonDeterministicSource(
        string symbolName,
        string reason)
    {
        return new V2CaptureDiagnostic
        {
            Code = $"QLDIAG_NONDETERMINISTIC_SRC_{NextSequence()}",
            Category = "nondeterministic-source",
            SymbolName = symbolName,
            Reason = reason,
            Message = $"Variable '{symbolName}' is non-deterministic (e.g., current time, random values) and may vary across evaluations.",
            SuggestedFix = "This is expected for time-sensitive or random values. QueryLens will bind a snapshot at query extraction time.",
        };
    }

    /// <summary>
    /// Diagnostic for side-effectful initializers.
    /// </summary>
    internal static V2CaptureDiagnostic SideEffectfulInitializer(
        string symbolName,
        string methodName)
    {
        return new V2CaptureDiagnostic
        {
            Code = $"QLDIAG_SIDE_EFFECT_{NextSequence()}",
            Category = "side-effectful-initializer",
            SymbolName = symbolName,
            Reason = "unsafe-evaluation-side-effects",
            Message = $"Variable '{symbolName}' is initialized by a method call ('{methodName}') that may have side effects. " +
                      "QueryLens cannot safely evaluate this in the extraction context.",
            SuggestedFix = "Move the initialization outside the query or use a value that is computed without side effects.",
        };
    }

    /// <summary>
    /// Diagnostic for unsupported expression forms (dynamic, etc.).
    /// </summary>
    internal static V2CaptureDiagnostic UnsupportedExpressionForm(
        string symbolName,
        string expressionForm)
    {
        return new V2CaptureDiagnostic
        {
            Code = $"QLDIAG_UNSUPPORTED_EXPR_{NextSequence()}",
            Category = "unsupported-expression-form",
            SymbolName = symbolName,
            Reason = expressionForm,
            Message = $"Variable '{symbolName}' uses an unsupported expression form: '{expressionForm}'. " +
                      "QueryLens cannot safely resolve this expression in the extraction context.",
            SuggestedFix = "Simplify the expression or use a value computed with a supported language feature.",
        };
    }

    /// <summary>
    /// Diagnostic for generic type ambiguity.
    /// </summary>
    internal static V2CaptureDiagnostic GenericTypeAmbiguity(
        string symbolName,
        string typeName)
    {
        return new V2CaptureDiagnostic
        {
            Code = $"QLDIAG_GENERIC_AMBIG_{NextSequence()}",
            Category = "generic-type-ambiguity",
            SymbolName = symbolName,
            Reason = "unresolved-generic-substitution",
            Message = $"Variable '{symbolName}' has generic type '{typeName}' with unresolved type arguments. " +
                      "QueryLens cannot determine the concrete type at extraction time.",
            SuggestedFix = "Use a concrete type instead of a generic type parameter.",
        };
    }

    /// <summary>
    /// Diagnostic for control-flow-dependent values.
    /// </summary>
    internal static V2CaptureDiagnostic ControlFlowDependent(
        string symbolName,
        string? branchContext = null)
    {
        return new V2CaptureDiagnostic
        {
            Code = $"QLDIAG_CONTROL_FLOW_{NextSequence()}",
            Category = "control-flow-dependent-value",
            SymbolName = symbolName,
            Reason = "branch-merge-unresolved",
            Message = $"Variable '{symbolName}' depends on control flow branches that cannot be resolved at extraction time. " +
                      (branchContext != null ? $"Branch context: {branchContext}" : ""),
            SuggestedFix = "Extract the variable computation outside the query or use a value that does not depend on branch decisions.",
        };
    }

    /// <summary>
    /// Diagnostic for async continuation state dependencies.
    /// </summary>
    internal static V2CaptureDiagnostic AsyncStateDependency(
        string symbolName,
        string methodName)
    {
        return new V2CaptureDiagnostic
        {
            Code = $"QLDIAG_ASYNC_STATE_{NextSequence()}",
            Category = "async-state-dependent",
            SymbolName = symbolName,
            Reason = "async-execution-unavailable",
            Message = $"Variable '{symbolName}' is produced by an async call ('{methodName}'). " +
                      "QueryLens cannot execute async code during extraction, so a typed placeholder will be used.",
            SuggestedFix = "If the placeholder value is incorrect, move the variable computation outside the query.",
        };
    }

    /// <summary>
    /// Diagnostic for reflection or dynamic invocation.
    /// </summary>
    internal static V2CaptureDiagnostic ReflectionOrDynamicInvocation(
        string symbolName,
        string? reflectionTarget = null)
    {
        return new V2CaptureDiagnostic
        {
            Code = $"QLDIAG_REFLECTION_{NextSequence()}",
            Category = "reflection-or-dynamic-invocation",
            SymbolName = symbolName,
            Reason = "reflection-based-value-resolution",
            Message = $"Variable '{symbolName}' involves reflection or dynamic method invocation. " +
                      (reflectionTarget != null ? $"Target: {reflectionTarget}. " : "") +
                      "QueryLens cannot reliably resolve reflection-driven values.",
            SuggestedFix = "Use compile-time-known types and methods instead of reflection.",
        };
    }

    /// <summary>
    /// Diagnostic for cross-assembly visibility limits.
    /// </summary>
    internal static V2CaptureDiagnostic CrossAssemblyVisibilityLimit(
        string symbolName,
        string assemblyName,
        string? typeName = null)
    {
        return new V2CaptureDiagnostic
        {
            Code = $"QLDIAG_CROSS_ASSEMBLY_{NextSequence()}",
            Category = "cross-assembly-visibility-limit",
            SymbolName = symbolName,
            Reason = "initializer-not-observable",
            Message = $"Variable '{symbolName}' references external assembly '{assemblyName}'. " +
                      (typeName != null ? $"Type '{typeName}' is not visible or " : "") +
                      "the initializer body is not accessible at extraction time.",
            SuggestedFix = "Ensure the initializer is in a visible assembly or extract the value before the query.",
        };
    }

    /// <summary>
    /// Diagnostic for translation semantics that are unverifiable.
    /// </summary>
    internal static V2CaptureDiagnostic TranslationSemanticsUnverifiable(
        string symbolName,
        string semanticDetail,
        string? provider = null)
    {
        return new V2CaptureDiagnostic
        {
            Code = $"QLDIAG_TRANSLATION_SEM_{NextSequence()}",
            Category = "translation-semantics-unverifiable",
            SymbolName = symbolName,
            Reason = "semantic-equivalence-unproven",
            Message = $"Variable '{symbolName}' uses a semantic construct that cannot be verified for SQL translation equivalence. " +
                      $"Construct: {semanticDetail}. " +
                      (provider != null ? $"Provider: {provider}." : ""),
            SuggestedFix = "Use simpler comparison/filtering operators that are directly translatable to SQL.",
            Provider = provider,
        };
    }

    private static string NextSequence()
    {
        return (++_diagnosticSequence).ToString("000");
    }
}
