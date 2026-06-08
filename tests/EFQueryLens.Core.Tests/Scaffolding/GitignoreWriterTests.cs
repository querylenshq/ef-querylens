using EFQueryLens.Core.Scaffolding;

namespace EFQueryLens.Core.Tests.Scaffolding;

public sealed class GitignoreWriterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ql-gitignore-{Guid.NewGuid():N}");

    public GitignoreWriterTests() => Directory.CreateDirectory(_dir);

    private const string Rule = "Properties/QueryLens/";

    [Fact]
    public void EnsureRule_NoFile_CreatesItWithRule()
    {
        var changed = GitignoreWriter.EnsureRule(_dir, Rule);

        Assert.True(changed);
        var content = File.ReadAllText(Path.Combine(_dir, ".gitignore"));
        Assert.Contains(Rule, content, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureRule_ExistingFileWithoutRule_AppendsOnce()
    {
        var path = Path.Combine(_dir, ".gitignore");
        File.WriteAllText(path, "bin/\nobj/\n");

        var changed = GitignoreWriter.EnsureRule(_dir, Rule);

        Assert.True(changed);
        var content = File.ReadAllText(path);
        Assert.Contains("bin/", content, StringComparison.Ordinal);
        Assert.Contains(Rule, content, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureRule_RuleAlreadyPresent_IsNoOp()
    {
        var path = Path.Combine(_dir, ".gitignore");
        File.WriteAllText(path, "obj/\n" + Rule + "\n");

        var changed = GitignoreWriter.EnsureRule(_dir, Rule);

        Assert.False(changed);
        var occurrences = File.ReadAllText(path).Split(Rule).Length - 1;
        Assert.Equal(1, occurrences);
    }

    [Fact]
    public void EnsureRule_Idempotent_AcrossRepeatedCalls()
    {
        GitignoreWriter.EnsureRule(_dir, Rule);
        GitignoreWriter.EnsureRule(_dir, Rule);
        GitignoreWriter.EnsureRule(_dir, Rule);

        var occurrences = File.ReadAllText(Path.Combine(_dir, ".gitignore")).Split(Rule).Length - 1;
        Assert.Equal(1, occurrences);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }
}
