package efquerylens

import com.intellij.openapi.actionSystem.AnAction
import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.actionSystem.CommonDataKeys
import com.intellij.openapi.project.Project

class EFQueryLensSetupAction : AnAction() {
    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        val editor = e.getData(CommonDataKeys.EDITOR) ?: return
        val virtualFile = e.getData(CommonDataKeys.VIRTUAL_FILE) ?: return
        if (!virtualFile.extension.equals("cs", ignoreCase = true)) {
            return
        }

        val fileUri =
            runCatching { virtualFile.toNioPath().toUri().toString() }
                .getOrElse { virtualFile.url }

        val line = editor.caretModel.logicalPosition.line
        val character = editor.caretModel.logicalPosition.column
        EFQueryLensSetupService.run(project, fileUri, line, character)
    }

    override fun update(e: AnActionEvent) {
        val project: Project? = e.project
        val virtualFile = e.getData(CommonDataKeys.VIRTUAL_FILE)
        e.presentation.isEnabledAndVisible =
            project != null &&
            virtualFile?.extension.equals("cs", ignoreCase = true)
    }
}
