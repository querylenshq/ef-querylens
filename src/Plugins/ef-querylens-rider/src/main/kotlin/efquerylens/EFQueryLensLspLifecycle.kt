package efquerylens

import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.fileEditor.FileEditorManager
import com.intellij.openapi.project.Project
import com.intellij.openapi.vfs.VirtualFile

internal object EFQueryLensLspLifecycle {
    fun onServerInitialized(project: Project) {
        EFQueryLensQuickDocSqlReadyHook.register(project)
        EFQueryLensEditorHoverSqlReadyHook.register(project)
        EFQueryLensLspRequests.refreshStatus(project)
        runStartupWarmup(project)
    }

    private fun runStartupWarmup(project: Project) {
        ApplicationManager.getApplication().executeOnPooledThread {
            val editor = FileEditorManager.getInstance(project).selectedTextEditor ?: return@executeOnPooledThread
            val file: VirtualFile = editor.virtualFile ?: return@executeOnPooledThread
            if (!file.extension.equals("cs", ignoreCase = true)) {
                return@executeOnPooledThread
            }

            val caret = editor.caretModel.currentCaret
            val line = caret.logicalPosition.line
            val character = caret.logicalPosition.column
            EFQueryLensLspRequests.requestWarmup(project, EFQueryLensHoverProbe.fileUri(file), line, character)
            EFQueryLensLspRequests.refreshStatus(project)
        }
    }
}
