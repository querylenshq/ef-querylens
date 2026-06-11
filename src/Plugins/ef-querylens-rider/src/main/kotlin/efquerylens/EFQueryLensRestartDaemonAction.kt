package efquerylens

import com.intellij.notification.NotificationGroupManager
import com.intellij.notification.NotificationType
import com.intellij.openapi.actionSystem.AnAction
import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.project.Project

class EFQueryLensRestartDaemonAction : AnAction() {
    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        val result = EFQueryLensLspRequests.requestDaemonRestart(project)
        val message =
            result?.get("message")?.toString()
                ?: if (result?.get("success") == true) {
                    "Daemon restarted."
                } else {
                    "Daemon restart did not complete."
                }

        NotificationGroupManager
            .getInstance()
            .getNotificationGroup("EF QueryLens")
            .createNotification(message, if (result?.get("success") == true) NotificationType.INFORMATION else NotificationType.WARNING)
            .notify(project)
    }

    override fun update(e: AnActionEvent) {
        e.presentation.isEnabledAndVisible = e.project != null
    }
}
