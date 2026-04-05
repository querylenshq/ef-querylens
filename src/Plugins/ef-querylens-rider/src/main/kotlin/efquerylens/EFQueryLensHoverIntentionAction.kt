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
        if (editor == null) return false

        val virtualFile = element.containingFile?.virtualFile ?: return false
        if (!virtualFile.extension.equals("cs", ignoreCase = true)) return false

        // Only offer the action at positions where Rider naturally fires hover requests:
        // statement-anchor tokens (var, =, ;, return, await) or inside an invocation chain.
        // This keeps Alt+Enter from flooding every token in a .cs file while still
        // covering the declaration shapes that the LSP server now handles correctly.
        return isAtStatementAnchorOrInvocation(element)
    }

    private fun isAtStatementAnchorOrInvocation(element: PsiElement): Boolean {
        val leafText = element.text

        // Test doubles and some PSI edge nodes can surface null/blank leaf text.
        // Fall back to enabled so availability does not become token-shape brittle.
        if (leafText.isNullOrBlank()) return true

        // Exact tokens where Rider hover fires on declaration/assignment/return lines.
        if (leafText in STATEMENT_ANCHOR_TOKENS) return true

        // Element is an identifier — accept it when a parent within look-up depth
        // is a declaration, assignment, or return context. This covers the variable
        // name in "var pagedOrders = ..." and "return GetQuery(...)".
        var current: PsiElement? = element.parent
        repeat(6) {
            val typeStr = current?.node?.elementType?.toString() ?: return@repeat
            if (STATEMENT_CONTEXT_TYPE_FRAGMENTS.any { typeStr.contains(it, ignoreCase = true) }) return true
            current = current?.parent
        }

        return false
    }

    companion object {
        // Leaf token texts that correspond to statement-boundary positions.
        private val STATEMENT_ANCHOR_TOKENS = setOf("var", "=", ";", "return", "await", "=>")

        // Fragments of PSI element-type names that indicate a statement context.
        // These match JetBrains' internal C# PSI node type strings without importing
        // ReSharper APIs (which are not available in the platform LSP plugin layer).
        private val STATEMENT_CONTEXT_TYPE_FRAGMENTS = listOf(
            "LOCAL_VARIABLE",
            "LOCAL_DECLARATION",
            "ASSIGNMENT",
            "RETURN_STATEMENT",
            "EXPRESSION_STATEMENT",
            "AWAIT_EXPRESSION",
        )
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

class EFQueryLensPreviewSqlIntentionAction : EFQueryLensBaseIntentionAction("EF QueryLens: Preview SQL", "showsqlpopup")

class EFQueryLensOpenSqlIntentionAction : EFQueryLensBaseIntentionAction("EF QueryLens: Open SQL", "opensqleditor")

class EFQueryLensReanalyzeIntentionAction : EFQueryLensBaseIntentionAction("EF QueryLens: Reanalyze", "recalculate")
