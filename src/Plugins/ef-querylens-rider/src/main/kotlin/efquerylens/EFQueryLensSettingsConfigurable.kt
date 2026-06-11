package efquerylens

import com.intellij.openapi.options.Configurable
import com.intellij.openapi.options.ConfigurableProvider
import com.intellij.openapi.project.Project
import com.intellij.openapi.project.ProjectManager
import com.intellij.ui.components.JBCheckBox
import com.intellij.ui.components.JBLabel
import com.intellij.util.ui.FormBuilder
import javax.swing.JComponent
import javax.swing.JPanel
import javax.swing.JSpinner
import javax.swing.SpinnerNumberModel

class EFQueryLensSettingsConfigurable(
    private val project: Project,
) : Configurable {
    private var panel: JPanel? = null
    private var notifyWhenSqlReadyCheckBox: JBCheckBox? = null
    private var showStatusBarCheckBox: JBCheckBox? = null
    private var hoverWaitSpinner: JSpinner? = null

    override fun getDisplayName(): String = "EF QueryLens"

    override fun createComponent(): JComponent {
        val settings = EFQueryLensSettingsService.getInstance(project)
        val notifyCheckBox = JBCheckBox("Notify when SQL is ready", settings.notifyWhenSqlReady)
        val statusBarCheckBox = JBCheckBox("Show status in Rider status bar", settings.showStatusBar)
        val hoverWait =
            JSpinner(
                SpinnerNumberModel(settings.hoverWaitWhenWarmMs, 0, 30_000, 500),
            )

        notifyWhenSqlReadyCheckBox = notifyCheckBox
        showStatusBarCheckBox = statusBarCheckBox
        hoverWaitSpinner = hoverWait

        val built =
            FormBuilder
                .createFormBuilder()
                .addComponent(notifyCheckBox)
                .addComponent(statusBarCheckBox)
                .addLabeledComponent(JBLabel("Hover wait when warm (ms):"), hoverWait)
                .addComponentFillVertically(JPanel(), 0)
                .panel

        panel = built
        return built
    }

    override fun isModified(): Boolean {
        val settings = EFQueryLensSettingsService.getInstance(project)
        val notifyBox = notifyWhenSqlReadyCheckBox ?: return false
        val statusBarBox = showStatusBarCheckBox ?: return false
        val spinner = hoverWaitSpinner ?: return false
        return notifyBox.isSelected != settings.notifyWhenSqlReady ||
            statusBarBox.isSelected != settings.showStatusBar ||
            (spinner.value as Int) != settings.hoverWaitWhenWarmMs
    }

    override fun apply() {
        val settings = EFQueryLensSettingsService.getInstance(project)
        notifyWhenSqlReadyCheckBox?.let { settings.notifyWhenSqlReady = it.isSelected }
        showStatusBarCheckBox?.let { settings.showStatusBar = it.isSelected }
        hoverWaitSpinner?.let { settings.hoverWaitWhenWarmMs = it.value as Int }
    }

    override fun reset() {
        val settings = EFQueryLensSettingsService.getInstance(project)
        notifyWhenSqlReadyCheckBox?.isSelected = settings.notifyWhenSqlReady
        showStatusBarCheckBox?.isSelected = settings.showStatusBar
        hoverWaitSpinner?.value = settings.hoverWaitWhenWarmMs
    }

    override fun disposeUIResources() {
        panel = null
        notifyWhenSqlReadyCheckBox = null
        showStatusBarCheckBox = null
        hoverWaitSpinner = null
    }
}

class EFQueryLensSettingsConfigurableProvider : ConfigurableProvider() {
    override fun createConfigurable(): Configurable? {
        val project = ProjectManager.getInstance().openProjects.firstOrNull() ?: return null
        return EFQueryLensSettingsConfigurable(project)
    }

    override fun canCreateConfigurable(): Boolean = ProjectManager.getInstance().openProjects.isNotEmpty()
}
