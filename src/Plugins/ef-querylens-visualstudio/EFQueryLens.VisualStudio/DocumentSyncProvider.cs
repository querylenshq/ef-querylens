// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace EFQueryLens.VisualStudio;

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

/// <summary>
/// Listens to text buffer events and sends document sync notifications to the LSP server.
/// This ensures that the LSP server's DocumentManager always contains the latest source text,
/// so local variable type extraction works correctly regardless of file encoding or line endings.
/// </summary>
[Export(typeof(IWpfTextViewCreationListener))]
[ContentType("CSharp")]
[TextViewRole(PredefinedTextViewRoles.Document)]
internal sealed class DocumentSyncProvider : IWpfTextViewCreationListener
{
    private readonly Dictionary<string, DocumentSyncBuffer> _syncedBuffers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object SyncLock = new();

    public void TextViewCreated(IWpfTextView textView)
    {
        if (textView?.TextBuffer is null)
            return;

        var buffer = textView.TextBuffer;
        if (!buffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument document))
            return;

        var filePath = document.FilePath;
        if (string.IsNullOrWhiteSpace(filePath) || !filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            return;

        lock (SyncLock)
        {
            if (_syncedBuffers.ContainsKey(filePath))
                return;

            var syncBuffer = new DocumentSyncBuffer(filePath, buffer);
            _syncedBuffers[filePath] = syncBuffer;

            // Send initial didOpen notification with current buffer content
            var sourceText = buffer.CurrentSnapshot.GetText();
            QueryLensLanguageClient.NotifyDocumentOpened(filePath, sourceText);
        }
    }

    private sealed class DocumentSyncBuffer : IDisposable
    {
        private readonly string _filePath;
        private readonly ITextBuffer _buffer;
        private bool _disposed;

        internal DocumentSyncBuffer(string filePath, ITextBuffer buffer)
        {
            _filePath = filePath;
            _buffer = buffer;

            _buffer.ChangedHighPriority += OnTextBufferChanged;
            _buffer.Properties.AddProperty(typeof(DocumentSyncBuffer), this);
        }

        private void OnTextBufferChanged(object? sender, TextContentChangedEventArgs e)
        {
            if (_disposed)
                return;

            // When the buffer content changes, send didChange notification with full content
            var sourceText = _buffer.CurrentSnapshot.GetText();
            QueryLensLanguageClient.NotifyDocumentChanged(_filePath, sourceText);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _buffer.ChangedHighPriority -= OnTextBufferChanged;
            _buffer.Properties.RemoveProperty(typeof(DocumentSyncBuffer));

            // Send didClose notification to drain LSP server cache
            QueryLensLanguageClient.NotifyDocumentClosed(_filePath);
        }
    }
}
