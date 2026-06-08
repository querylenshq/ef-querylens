// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace EFQueryLens.VisualStudio;

using System;
using System.IO;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

internal sealed class QueryLensActiveEditorContext
{
    internal QueryLensActiveEditorContext(string documentUri, int line, int character)
    {
        DocumentUri = documentUri;
        Line = line;
        Character = character;
    }

    internal string DocumentUri { get; }

    internal int Line { get; }

    internal int Character { get; }
}

internal static class QueryLensEditorContext
{
    internal static bool TryGetActiveCSharpContext(out QueryLensActiveEditorContext? context, out string? errorMessage)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        context = null;
        errorMessage = null;

        if (Package.GetGlobalService(typeof(EnvDTE.DTE)) is not EnvDTE.DTE dte)
        {
            errorMessage = "Unable to access the active editor.";
            return false;
        }

        if (dte.ActiveDocument is not EnvDTE.Document activeDocument
            || string.IsNullOrWhiteSpace(activeDocument.FullName)
            || !activeDocument.FullName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "Open the C# file with the EF query, then run Set up QueryLens.";
            return false;
        }

        var filePath = activeDocument.FullName;
        var line = 0;
        var character = 0;

        if (activeDocument.Selection is EnvDTE.TextSelection selection)
        {
            line = Math.Max(0, selection.ActivePoint.Line - 1);
            character = Math.Max(0, selection.ActivePoint.DisplayColumn - 1);
        }

        try
        {
            var documentUri = new Uri(Path.GetFullPath(filePath)).AbsoluteUri;
            context = new QueryLensActiveEditorContext(documentUri, line, character);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Unable to resolve the active document path: {ex.Message}";
            return false;
        }
    }
}
