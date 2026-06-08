using System.Security.Cryptography;
using System.Text;

namespace EFQueryLens.Core.Contracts;

/// <summary>
/// Canonical content-addressed cache key for query translations. Shared by the daemon
/// (in-memory + durable SQLite) and the LSP semantic hover cache so both layers agree
/// on when two <see cref="TranslationRequest"/> values represent the same translation.
/// </summary>
public static class TranslationCacheKey
{
    /// <summary>
    /// Returns the first 16 hex characters of the SHA256 of all
    /// <see cref="TranslationRequest"/> fields that affect the compiled eval assembly
    /// or its stub declarations.
    /// </summary>
    public static string Compute(TranslationRequest request)
    {
        var sb = new StringBuilder();
        sb.Append(request.Expression).Append('\0');
        sb.Append(request.AssemblyPath ?? string.Empty).Append('\0');
        sb.Append(ComputeAssemblyFingerprint(request.AssemblyPath)).Append('\0');
        sb.Append(request.DbContextTypeName ?? string.Empty).Append('\0');
        sb.Append(request.ContextVariableName).Append('\0');
        foreach (var ns in request.AdditionalImports.OrderBy(x => x, StringComparer.Ordinal))
        {
            sb.Append(ns).Append('\0');
        }

        foreach (var kv in request.UsingAliases.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            sb.Append(kv.Key).Append(':').Append(kv.Value).Append('\0');
        }

        foreach (var st in request.UsingStaticTypes.OrderBy(x => x, StringComparer.Ordinal))
        {
            sb.Append(st).Append('\0');
        }

        foreach (var kv in request.LocalVariableTypes.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            sb.Append(kv.Key).Append(':').Append(kv.Value).Append('\0');
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    /// <summary>
    /// Returns the assembly fingerprint (size + last-write timestamp) for the given path,
    /// or a sentinel when the file is missing/unreadable. Mirrors the LSP-side
    /// <c>AssemblyResolver.TryGetAssemblyFingerprint</c> format (path|size|ticks) is not
    /// used here — only size|ticks — matching the daemon's historical cache key shape.
    /// </summary>
    public static string ComputeAssemblyFingerprint(string? assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            return "no-assembly";
        }

        try
        {
            var info = new FileInfo(assemblyPath);
            return info.Exists ? $"{info.Length}|{info.LastWriteTimeUtc.Ticks}" : "missing";
        }
        catch
        {
            return "error";
        }
    }
}
