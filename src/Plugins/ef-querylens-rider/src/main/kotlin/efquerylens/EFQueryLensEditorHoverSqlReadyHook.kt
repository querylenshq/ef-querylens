package efquerylens

import com.intellij.openapi.diagnostic.thisLogger
import com.intellij.openapi.editor.Editor
import com.intellij.openapi.editor.EditorFactory
import com.intellij.openapi.editor.LogicalPosition
import com.intellij.openapi.editor.event.EditorFactoryEvent
import com.intellij.openapi.editor.event.EditorFactoryListener
import com.intellij.openapi.editor.event.EditorMouseEvent
import com.intellij.openapi.editor.event.EditorMouseMotionListener
import com.intellij.openapi.project.Project
import java.util.concurrent.ConcurrentHashMap
import javax.swing.Timer

internal object EFQueryLensEditorHoverSqlReadyHook {
    private const val MOUSE_IDLE_PROBE_DELAY_MS = 900

    private val registeredProjects = ConcurrentHashMap.newKeySet<Project>()
    private val mouseListeners = ConcurrentHashMap<Editor, EditorMouseMotionListener>()
    private val pendingMousePositions = ConcurrentHashMap<Editor, LogicalPosition>()
    private val mouseProbeTimers = ConcurrentHashMap<Editor, Timer>()

    fun register(project: Project) {
        if (!registeredProjects.add(project)) {
            return
        }

        val editorFactory = EditorFactory.getInstance()
        editorFactory.addEditorFactoryListener(
            object : EditorFactoryListener {
                override fun editorCreated(event: EditorFactoryEvent) {
                    attach(project, event.editor)
                }

                override fun editorReleased(event: EditorFactoryEvent) {
                    detach(event.editor)
                }
            },
            project,
        )

        editorFactory.allEditors.forEach { attach(project, it) }
        thisLogger().info("[EFQueryLens] editor sql-ready hover hook registered")
    }

    private fun attach(
        project: Project,
        editor: Editor,
    ) {
        if (project.isDisposed || editor.project != project || !isCSharpEditor(editor)) {
            return
        }

        val mouseListener =
            object : EditorMouseMotionListener {
                override fun mouseMoved(event: EditorMouseEvent) {
                    val position = editor.xyToLogicalPosition(event.mouseEvent.point)
                    scheduleMouseIdleProbe(project, editor, position)
                }

                override fun mouseDragged(event: EditorMouseEvent) = Unit
            }

        if (mouseListeners.putIfAbsent(editor, mouseListener) == null) {
            editor.addEditorMouseMotionListener(mouseListener)
        }
    }

    private fun detach(editor: Editor) {
        val listener = mouseListeners.remove(editor) ?: return
        editor.removeEditorMouseMotionListener(listener)
        pendingMousePositions.remove(editor)
        mouseProbeTimers.remove(editor)?.stop()
    }

    private fun isCSharpEditor(editor: Editor): Boolean =
        editor.virtualFile?.extension.equals("cs", ignoreCase = true)

    internal fun scheduleMouseIdleProbe(
        project: Project,
        editor: Editor,
        position: LogicalPosition,
    ) {
        pendingMousePositions[editor] = position
        val existing = mouseProbeTimers[editor]
        if (existing != null) {
            existing.restart()
            return
        }

        val timer =
            Timer(MOUSE_IDLE_PROBE_DELAY_MS) {
                val latestPosition = pendingMousePositions.remove(editor) ?: return@Timer
                mouseProbeTimers.remove(editor)
                if (!project.isDisposed && !editor.isDisposed) {
                    EFQueryLensHoverProbe.probeAtLine(project, editor, latestPosition)
                }
            }.apply {
                isRepeats = false
            }

        mouseProbeTimers[editor] = timer
        timer.start()
    }
}
