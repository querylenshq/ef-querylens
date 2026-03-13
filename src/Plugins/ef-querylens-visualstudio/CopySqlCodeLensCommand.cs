using System.Diagnostics;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;

namespace EFQueryLens.VisualStudio;

[VisualStudioContribution]
internal sealed class CopySqlCodeLensCommand : Command
{
    public override CommandConfiguration CommandConfiguration => new("%EFQueryLens.Command.CopySql.DisplayName%");

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        QueryLensLogger.Info("copy-sql-command-invoked");

        _ = ShowSqlCodeLensCommand.TryExtractSqlPreviewData(
            context,
            out var sqlPreview,
            out var translationError,
            out _);

        if (string.IsNullOrWhiteSpace(sqlPreview))
        {
            if (!string.IsNullOrWhiteSpace(translationError))
            {
                await this.Extensibility.Shell().ShowPromptAsync(
                    $"QueryLens could not generate SQL for this query.\n\n{translationError}",
                    PromptOptions.OK,
                    cancellationToken);
                return;
            }

            await this.Extensibility.Shell().ShowPromptAsync(
                "QueryLens did not receive SQL content for this action.",
                PromptOptions.OK,
                cancellationToken);
            return;
        }

        var copied = await TryCopyToClipboardAsync(sqlPreview, cancellationToken);
        if (!copied)
        {
            await this.Extensibility.Shell().ShowPromptAsync(
                "QueryLens failed to copy SQL to clipboard.",
                PromptOptions.OK,
                cancellationToken);
            return;
        }

        await this.Extensibility.Shell().ShowPromptAsync(
            "QueryLens copied SQL to clipboard.",
            PromptOptions.OK,
            cancellationToken);
    }

    private static async Task<bool> TryCopyToClipboardAsync(string text, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "clip.exe",
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                return false;
            }

            await process.StandardInput.WriteAsync(text.AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
            process.StandardInput.Close();

            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            QueryLensLogger.Error("copy-sql-command-clipboard-failed", ex);
            return false;
        }
    }
}
