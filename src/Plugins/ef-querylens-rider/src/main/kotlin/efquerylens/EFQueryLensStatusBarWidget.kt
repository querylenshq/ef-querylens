package efquerylens

import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.project.Project
import com.intellij.openapi.wm.CustomStatusBarWidget
import com.intellij.openapi.wm.StatusBar
import com.intellij.openapi.wm.StatusBarWidget
import com.intellij.openapi.wm.StatusBarWidgetFactory
import com.intellij.openapi.wm.ToolWindowManager
import com.intellij.openapi.wm.WindowManager
import java.awt.event.MouseAdapter
import java.awt.event.MouseEvent
import javax.swing.JComponent
import javax.swing.JLabel

internal class EFQueryLensStatusBarWidget(
    private val project: Project,
) : CustomStatusBarWidget {
    private val refreshListener = Runnable { refreshWidget() }
    private val component =
        lazy {
            JLabel(EFQueryLensHostStatus.displayText).apply {
                toolTipText = EFQueryLensHostStatus.tooltipText
                addMouseListener(
                    object : MouseAdapter() {
                        override fun mouseClicked(event: MouseEvent) {
                            openToolWindow()
                        }
                    },
                )
            }
        }

    override fun ID(): String = WIDGET_ID

    override fun install(statusBar: StatusBar) {
        EFQueryLensHostStatus.addListener(refreshListener)
        EFQueryLensLspRequests.refreshStatus(project)
        refreshWidget()
    }

    override fun dispose() {
        EFQueryLensHostStatus.removeListener(refreshListener)
    }

    override fun getComponent(): JComponent = component.value

    private fun refreshWidget() {
        if (project.isDisposed) {
            return
        }

        ApplicationManager.getApplication().invokeLater {
            if (project.isDisposed) {
                return@invokeLater
            }

            if (component.isInitialized()) {
                component.value.text = EFQueryLensHostStatus.displayText
                component.value.toolTipText = EFQueryLensHostStatus.tooltipText
            }

            WindowManager.getInstance().getStatusBar(project)?.updateWidget(WIDGET_ID)
        }
    }

    private fun openToolWindow() {
        ToolWindowManager
            .getInstance(project)
            .getToolWindow(EFQueryLensLogToolWindowFactory.TOOL_WINDOW_ID)
            ?.activate(null)
    }

    companion object {
        const val WIDGET_ID = "EFQueryLensStatusBar"

        fun refresh(project: Project) {
            if (project.isDisposed) {
                return
            }

            WindowManager.getInstance().getStatusBar(project)?.updateWidget(WIDGET_ID)
        }
    }
}

internal class EFQueryLensStatusBarWidgetFactory : StatusBarWidgetFactory {
    override fun getId(): String = EFQueryLensStatusBarWidget.WIDGET_ID

    override fun getDisplayName(): String = "EF QueryLens"

    override fun isAvailable(project: Project): Boolean =
        !project.isDisposed && EFQueryLensSettingsService.getInstance(project).showStatusBar

    override fun createWidget(project: Project): StatusBarWidget = EFQueryLensStatusBarWidget(project)

    override fun disposeWidget(widget: StatusBarWidget) {
        (widget as? EFQueryLensStatusBarWidget)?.dispose()
    }

    override fun canBeEnabledOn(statusBar: StatusBar): Boolean = true
}
