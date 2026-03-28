package efquerylens

import com.intellij.codeInsight.intention.PsiElementBaseIntentionAction
import com.intellij.openapi.editor.Editor
import com.intellij.openapi.project.Project
import com.intellij.psi.PsiElement

abstract class EFQueryLensBaseIntentionAction(
    private val text: String,
    private val actionType: String,
) : PsiElementBaseIntentionAction() {
    override fun getText(): String = text

    override fun getFamilyName(): String = "EF QueryLens"

    override fun isAvailable(
        project: Project,
        editor: Editor?,
        element: PsiElement,
    ): Boolean {
        if (editor == null) {
            return false
        }

        val virtualFile = element.containingFile?.virtualFile ?: return false
        return virtualFile.extension.equals("cs", ignoreCase = true)
    }

    override fun invoke(
        project: Project,
        editor: Editor?,
        element: PsiElement,
    ) {
        val currentEditor = editor ?: return
        val virtualFile = element.containingFile?.virtualFile ?: return

        val fileUri =
            runCatching { virtualFile.toNioPath().toUri().toString() }
                .getOrElse { virtualFile.url }

        val line = currentEditor.caretModel.logicalPosition.line
        val character = currentEditor.caretModel.logicalPosition.column
        EFQueryLensUrlOpener().executeAction(actionType, project, fileUri, line, character)
    }

    override fun startInWriteAction(): Boolean = false
}

class EFQueryLensCopySqlIntentionAction : EFQueryLensBaseIntentionAction("EF QueryLens: Copy SQL", "copysql")

class EFQueryLensOpenSqlIntentionAction : EFQueryLensBaseIntentionAction("EF QueryLens: Open SQL", "opensqleditor")

class EFQueryLensReanalyzeIntentionAction : EFQueryLensBaseIntentionAction("EF QueryLens: Reanalyze", "recalculate")
