using System.Diagnostics;
using System.Text.Json;
using EFQueryLens.Core.Daemon;
using EFQueryLens.DaemonClient;

namespace EFQueryLens.Core.Tests;

public class DaemonLocatorTests
{
    [Fact]
    public void TryGetPipeName_CamelCasePidFile_ReturnsPipeName()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "querylens-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);

        var pidFilePath = DaemonWorkspaceIdentity.BuildPidFilePath(workspacePath);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(pidFilePath)!);

            var payload = new
            {
                processId = Process.GetCurrentProcess().Id,
                pipeName = "querylens-test-pipe",
                workspacePath = DaemonWorkspaceIdentity.NormalizeWorkspacePath(workspacePath),
            };

            File.WriteAllText(pidFilePath, JsonSerializer.Serialize(payload));

            var pipeName = DaemonLocator.TryGetPipeName(workspacePath);
            Assert.Equal("querylens-test-pipe", pipeName);
        }
        finally
        {
            TryDeleteFile(pidFilePath);
            TryDeleteDirectory(workspacePath);
        }
    }

    [Fact]
    public void TryGetPipeName_WorkspaceMismatchInPidFile_ReturnsNull()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "querylens-workspace", Guid.NewGuid().ToString("N"));
        var otherWorkspacePath = Path.Combine(Path.GetTempPath(), "querylens-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);
        Directory.CreateDirectory(otherWorkspacePath);

        var pidFilePath = DaemonWorkspaceIdentity.BuildPidFilePath(workspacePath);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(pidFilePath)!);

            var payload = new
            {
                processId = Process.GetCurrentProcess().Id,
                pipeName = "querylens-test-pipe",
                workspacePath = DaemonWorkspaceIdentity.NormalizeWorkspacePath(otherWorkspacePath),
            };

            File.WriteAllText(pidFilePath, JsonSerializer.Serialize(payload));

            var pipeName = DaemonLocator.TryGetPipeName(workspacePath);
            Assert.Null(pipeName);
        }
        finally
        {
            TryDeleteFile(pidFilePath);
            TryDeleteDirectory(workspacePath);
            TryDeleteDirectory(otherWorkspacePath);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup for test artifacts.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup for test artifacts.
        }
    }
}
