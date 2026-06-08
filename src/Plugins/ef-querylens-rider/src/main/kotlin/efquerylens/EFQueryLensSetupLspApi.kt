package efquerylens

import org.eclipse.lsp4j.jsonrpc.services.JsonRequest
import org.eclipse.lsp4j.services.LanguageServer
import java.util.concurrent.CompletableFuture

interface EFQueryLensLspServer : LanguageServer {
    @JsonRequest("efquerylens/setup/detect")
    fun setupDetect(params: Map<String, Any?>): CompletableFuture<Any?>

    @JsonRequest("efquerylens/setup/apply")
    fun setupApply(params: Map<String, Any?>): CompletableFuture<Any?>
}
