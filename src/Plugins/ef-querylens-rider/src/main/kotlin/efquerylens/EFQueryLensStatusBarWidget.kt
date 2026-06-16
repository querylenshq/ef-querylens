package efquerylens

import com.intellij.openapi.project.Project
import com.intellij.openapi.wm.StatusBar
import com.intellij.openapi.wm.StatusBarWidget
import com.intellij.openapi.wm.StatusBarWidgetFactory
import com.intellij.openapi.wm.ToolWindowManager
import com.intellij.openapi.wm.WindowManager
import com.intellij.util.Consumer
import java.awt.event.MouseEvent

internal class EFQueryLensStatusBarWidget(
    private val project: Project,
) : StatusBarWidget {
    private val refreshListener = Runnable { refreshWidget() }

    private val presentation =
        object : StatusBarWidget.TextPresentation {
            override fun getText(): String = EFQueryLensHostStatus.displayText

            override fun getTooltipText(): String = EFQueryLensHostStatus.tooltipText

            override fun getAlignment(): Float = 0f

            override fun getClickConsumer(): Consumer<MouseEvent> =
                Consumer {
                    ToolWindowManager
                        .getInstance(project)
                        .getToolWindow(EFQueryLensLogToolWindowFactory.TOOL_WINDOW_ID)
                        ?.activate(null)
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

    override fun getPresentation(): StatusBarWidget.WidgetPresentation = presentation

    private fun refreshWidget() {
        if (project.isDisposed) {
            return
        }

        WindowManager.getInstance().getStatusBar(project)?.updateWidget(WIDGET_ID)
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
