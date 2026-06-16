package efquerylens

import com.intellij.openapi.actionSystem.ActionManager
import com.intellij.openapi.actionSystem.AnAction
import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.actionSystem.CommonDataKeys
import com.intellij.openapi.actionSystem.DataContext
import com.intellij.openapi.actionSystem.ex.AnActionListener
import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.diagnostic.thisLogger
import com.intellij.openapi.editor.Editor
import com.intellij.openapi.project.Project
import java.util.concurrent.ConcurrentHashMap

internal object EFQueryLensQuickDocSqlReadyHook {
    private val registeredProjects = ConcurrentHashMap.newKeySet<Project>()
    private const val STATUS_READY = 0
    private const val STATUS_IN_QUEUE = 1

    fun register(project: Project) {
        if (!registeredProjects.add(project)) {
            return
        }

        project.messageBus
            .connect(project)
            .subscribe(
                AnActionListener.TOPIC,
                object : AnActionListener {
                    override fun beforeActionPerformed(
                        action: AnAction,
                        event: AnActionEvent,
                    ) {
                        if (isQuickDocumentationAction(action)) {
                            event.getData(CommonDataKeys.EDITOR)?.let {
                                EFQueryLensHoverProbe.probeAtCaret(project, it)
                            }
                        }
                    }

                    @Suppress("DEPRECATION", "OVERRIDE_DEPRECATION")
                    override fun afterActionPerformed(
                        action: AnAction,
                        dataContext: DataContext,
                        event: AnActionEvent,
                    ) {
                        if (isQuickDocumentationAction(action)) {
                            CommonDataKeys.EDITOR.getData(dataContext)?.let { editor ->
                                scheduleProbeAtCaret(project, editor, delayMs = 0)
                                scheduleProbeAtCaret(project, editor, delayMs = 200)
                            }
                        }
                    }
                },
            )

        thisLogger().info("[EFQueryLens] quick-doc sql-ready action hook registered")
    }

    internal fun handleDocumentationTextForTests(
        project: Project,
        fileUri: String,
        line: Int,
        character: Int,
        text: String?,
    ) {
        if (project.isDisposed || text.isNullOrBlank() || !QueryLensHoverMarkers.isQueryLensHover(text)) {
            return
        }

        when {
            QueryLensHoverMarkers.isProcessing(text) ->
                EFQueryLensSqlReadyHoverWatcher.watchIfQueued(
                    project,
                    EFQueryLensHoverProbe.normalizeFileUri(fileUri),
                    line,
                    character,
                    STATUS_IN_QUEUE,
                )

            QueryLensHoverMarkers.isReady(text) ->
                EFQueryLensSqlReadyHoverWatcher.cancelWatch(
                    EFQueryLensHoverProbe.normalizeFileUri(fileUri),
                    line,
                    character,
                )
        }
    }

    private fun scheduleProbeAtCaret(
        project: Project,
        editor: Editor,
        delayMs: Long,
    ) {
        ApplicationManager.getApplication().executeOnPooledThread {
            if (delayMs > 0) {
                Thread.sleep(delayMs)
            }
            EFQueryLensHoverProbe.probeAtCaret(project, editor, force = true)
        }
    }

    private fun isQuickDocumentationAction(action: AnAction): Boolean {
        val actionId = ActionManager.getInstance().getId(action)
        return actionId == "QuickJavaDoc" ||
            actionId == "QuickDoc" ||
            action.templateText?.contains("Quick Documentation", ignoreCase = true) == true
    }
}
