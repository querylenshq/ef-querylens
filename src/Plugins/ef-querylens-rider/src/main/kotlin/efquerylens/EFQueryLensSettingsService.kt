package efquerylens

import com.intellij.openapi.components.PersistentStateComponent
import com.intellij.openapi.components.Service
import com.intellij.openapi.components.State
import com.intellij.openapi.components.Storage
import com.intellij.openapi.project.Project

@Service(Service.Level.PROJECT)
@State(name = "EFQueryLensSettings", storages = [Storage("efquerylens.xml")])
class EFQueryLensSettingsService(
    private val project: Project,
) : PersistentStateComponent<EFQueryLensSettingsService.State> {
    data class State(
        var notifyWhenSqlReady: Boolean = true,
        var hoverWaitWhenWarmMs: Int = 0,
        var showStatusBar: Boolean = true,
    )

    private var state = State()

    var notifyWhenSqlReady: Boolean
        get() = state.notifyWhenSqlReady
        set(value) {
            state.notifyWhenSqlReady = value
            EFQueryLensLspConfiguration.pushRuntimeConfiguration(project)
        }

    var hoverWaitWhenWarmMs: Int
        get() = state.hoverWaitWhenWarmMs
        set(value) {
            state.hoverWaitWhenWarmMs = value.coerceIn(0, 30_000)
            EFQueryLensLspConfiguration.pushRuntimeConfiguration(project)
        }

    var showStatusBar: Boolean
        get() = state.showStatusBar
        set(value) {
            state.showStatusBar = value
            EFQueryLensStatusBarWidget.refresh(project)
        }

    override fun getState(): State = state

    override fun loadState(state: State) {
        this.state = state
    }

    companion object {
        fun getInstance(project: Project): EFQueryLensSettingsService =
            project.getService(EFQueryLensSettingsService::class.java)
    }
}
