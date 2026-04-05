using EFQueryLens.Core.Contracts;
using EFQueryLens.Core.Scripting.Compilation;
using Xunit;

namespace EFQueryLens.Core.Tests.Scripting;

/// <summary>
/// Unit tests for EvalSourceBuilder V2 capture-plan support (Slice 3b step 2).
/// Tests policy-driven code generation for symbol initialization.
/// </summary>
public class EvalSourceBuilderV2Tests
{
    [Fact]
    public void BuildV2CaptureInitializationCode_ReplayInitializer_EmitsReplayCode()
    {
        var entry = new V2CapturePlanEntry
        {
            Name = "user",
            TypeName = "User",
            CapturePolicy = LocalSymbolReplayPolicies.ReplayInitializer,
            InitializerExpression = "new User { Id = 1 }",
        };

        var code = EvalSourceBuilder.BuildV2CaptureInitializationCode(entry);

        Assert.NotNull(code);
        Assert.Contains("var user =", code);
        Assert.Contains("new User { Id = 1 }", code);
    }

    [Fact]
    public void BuildV2CaptureInitializationCode_ReplayWithoutExpression_EmitsDefault()
    {
        var entry = new V2CapturePlanEntry
        {
            Name = "user",
            TypeName = "User",
            CapturePolicy = LocalSymbolReplayPolicies.ReplayInitializer,
            // InitializerExpression is null
        };

        var code = EvalSourceBuilder.BuildV2CaptureInitializationCode(entry);

        Assert.NotNull(code);
        Assert.Contains("var user =", code);
        Assert.Contains("default(User)", code);
    }

    [Fact]
    public void BuildV2CaptureInitializationCode_UsePlaceholder_Int_EmitsCanonicalValue()
    {
        var entry = new V2CapturePlanEntry
        {
            Name = "pageSize",
            TypeName = "int",
            CapturePolicy = LocalSymbolReplayPolicies.UsePlaceholder,
        };

        var code = EvalSourceBuilder.BuildV2CaptureInitializationCode(entry);

        Assert.NotNull(code);
        Assert.Contains("var pageSize =", code);
        Assert.Contains("1", code);
    }

    [Fact]
    public void BuildV2CaptureInitializationCode_UsePlaceholder_String_EmitsSentinelValue()
    {
        var entry = new V2CapturePlanEntry
        {
            Name = "term",
            TypeName = "string",
            CapturePolicy = LocalSymbolReplayPolicies.UsePlaceholder,
        };

        var code = EvalSourceBuilder.BuildV2CaptureInitializationCode(entry);

        Assert.NotNull(code);
        Assert.Contains("var term =", code);
        Assert.Contains("\"qlstub0\"", code);
    }

    [Fact]
    public void BuildV2CaptureInitializationCode_UsePlaceholder_Guid_EmitsDeterministicGuid()
    {
        var entry = new V2CapturePlanEntry
        {
            Name = "id",
            TypeName = "Guid",
            CapturePolicy = LocalSymbolReplayPolicies.UsePlaceholder,
        };

        var code = EvalSourceBuilder.BuildV2CaptureInitializationCode(entry);

        Assert.NotNull(code);
        Assert.Contains("Guid.Parse", code);
        Assert.Contains("11111111-1111-1111-1111-111111111111", code);
    }

    [Fact]
    public void BuildV2CaptureInitializationCode_UsePlaceholder_DateTime_EmitsUtcNow()
    {
        var entry = new V2CapturePlanEntry
        {
            Name = "createdBefore",
            TypeName = "DateTime",
            CapturePolicy = LocalSymbolReplayPolicies.UsePlaceholder,
        };

        var code = EvalSourceBuilder.BuildV2CaptureInitializationCode(entry);

        Assert.NotNull(code);
        Assert.Contains("global::System.DateTime.UtcNow", code);
    }

    [Fact]
    public void BuildV2CaptureInitializationCode_UsePlaceholder_DateOnly_EmitsTodayFromUtcNow()
    {
        var entry = new V2CapturePlanEntry
        {
            Name = "createdOn",
            TypeName = "DateOnly",
            CapturePolicy = LocalSymbolReplayPolicies.UsePlaceholder,
        };

        var code = EvalSourceBuilder.BuildV2CaptureInitializationCode(entry);

        Assert.NotNull(code);
        Assert.Contains("global::System.DateOnly.FromDateTime(global::System.DateTime.UtcNow)", code);
    }

    [Fact]
    public void BuildV2CaptureInitializationCode_UsePlaceholder_TimeOnly_EmitsNowFromUtcNow()
    {
        var entry = new V2CapturePlanEntry
        {
            Name = "createdAt",
            TypeName = "TimeOnly",
            CapturePolicy = LocalSymbolReplayPolicies.UsePlaceholder,
        };

        var code = EvalSourceBuilder.BuildV2CaptureInitializationCode(entry);

        Assert.NotNull(code);
        Assert.Contains("global::System.TimeOnly.FromDateTime(global::System.DateTime.UtcNow)", code);
    }

    [Fact]
    public void BuildV2CaptureInitializationCode_UsePlaceholder_Array_SeedsTwoDeterministicItems()
    {
        var entry = new V2CapturePlanEntry
        {
            Name = "ids",
            TypeName = "int[]",
            CapturePolicy = LocalSymbolReplayPolicies.UsePlaceholder,
        };

        var code = EvalSourceBuilder.BuildV2CaptureInitializationCode(entry);

        Assert.NotNull(code);
        Assert.Contains("new int[]", code);
        Assert.Contains("{ 1, 2 }", code);
    }

    [Fact]
    public void BuildV2CaptureInitializationCode_UsePlaceholder_List_SeedsTwoDeterministicItems()
    {
        var entry = new V2CapturePlanEntry
        {
            Name = "terms",
            TypeName = "List<string>",
            CapturePolicy = LocalSymbolReplayPolicies.UsePlaceholder,
        };

        var code = EvalSourceBuilder.BuildV2CaptureInitializationCode(entry);

        Assert.NotNull(code);
        Assert.Contains("new global::System.Collections.Generic.List<string>", code);
        Assert.Contains("\"qlstub0\"", code);
        Assert.Contains("\"qlstub1\"", code);
    }

    [Fact]
    public void BuildV2CaptureInitializationCode_UsePlaceholder_IEnumerable_SeedsTwoDeterministicItems()
    {
        var entry = new V2CapturePlanEntry
        {
            Name = "ids",
            TypeName = "IEnumerable<int>",
            CapturePolicy = LocalSymbolReplayPolicies.UsePlaceholder,
        };

        var code = EvalSourceBuilder.BuildV2CaptureInitializationCode(entry);

        Assert.NotNull(code);
        Assert.Contains("new int[]", code);
        Assert.Contains("{ 1, 2 }", code);
    }

    [Fact]
    public void BuildV2CaptureInitializationCode_UsePlaceholder_HashSet_SeedsTwoDeterministicItems()
    {
        var entry = new V2CapturePlanEntry
        {
            Name = "ids",
            TypeName = "HashSet<int>",
            CapturePolicy = LocalSymbolReplayPolicies.UsePlaceholder,
        };

        var code = EvalSourceBuilder.BuildV2CaptureInitializationCode(entry);

        Assert.NotNull(code);
        Assert.Contains("new global::System.Collections.Generic.HashSet<int>", code);
        Assert.Contains("{ 1, 2 }", code);
    }

    [Fact]
    public void BuildV2CaptureInitializationCode_Reject_ReturnsNull()
    {
        var entry = new V2CapturePlanEntry
        {
            Name = "rejected",
            TypeName = "object",
            CapturePolicy = LocalSymbolReplayPolicies.Reject,
        };

        var code = EvalSourceBuilder.BuildV2CaptureInitializationCode(entry);

        Assert.Null(code);
    }

    [Theory]
    [InlineData(LocalSymbolReplayPolicies.ReplayInitializer, true)]
    [InlineData(LocalSymbolReplayPolicies.UsePlaceholder, true)]
    [InlineData(LocalSymbolReplayPolicies.Reject, false)]
    public void BuildV2CaptureInitializationCode_AllPolicies_ProducesCorrectOutput(
        string policy, bool shouldProduceCode)
    {
        var entry = new V2CapturePlanEntry
        {
            Name = "x",
            TypeName = "int",
            CapturePolicy = policy,
        };

        var code = EvalSourceBuilder.BuildV2CaptureInitializationCode(entry);

        if (shouldProduceCode)
        {
            Assert.NotNull(code);
            Assert.Contains("var x =", code);
        }
        else
        {
            Assert.Null(code);
        }
    }
}
