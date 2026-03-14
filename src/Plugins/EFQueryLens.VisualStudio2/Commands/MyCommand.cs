namespace EFQueryLens.VisualStudio2
{
    [Command(PackageIds.MyCommand)]
    internal sealed class MyCommand : BaseCommand<MyCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await VS.MessageBox.ShowWarningAsync("EFQueryLens.VisualStudio2", "Button clicked");
        }
    }
}
