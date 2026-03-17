using System.Reflection;
using System.Runtime.CompilerServices;
using EFQueryLens.Core.Contracts;
using EFQueryLens.Core.Scripting;
using QueryEvaluator = EFQueryLens.Core.Scripting.Evaluation.QueryEvaluator;

namespace EFQueryLens.Core.Tests;

public class QueryEvaluatorExecutionPayloadTests
{
    private static readonly MethodInfo s_parseExecutionPayload = typeof(QueryEvaluator)
        .GetMethod("ParseExecutionPayload", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Could not find QueryEvaluator.ParseExecutionPayload via reflection.");

    [Fact]
    public void ParseExecutionPayload_MissingCommands_ThrowsInvalidOperationException()
    {
        var payload = new PayloadMissingCommands
        {
            Queryable = new object(),
            CaptureSkipReason = null,
            CaptureError = null,
        };

        var ex = Assert.Throws<TargetInvocationException>(() => s_parseExecutionPayload.Invoke(null, [payload]));
        var inner = Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Contains("missing property 'Commands'", inner.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseExecutionPayload_CommandsNotEnumerable_ThrowsInvalidOperationException()
    {
        var payload = new PayloadCommandsNotEnumerable
        {
            Queryable = new object(),
            CaptureSkipReason = null,
            CaptureError = null,
            Commands = 42,
        };

        var ex = Assert.Throws<TargetInvocationException>(() => s_parseExecutionPayload.Invoke(null, [payload]));
        var inner = Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Contains("must be enumerable", inner.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseExecutionPayload_ValidPayload_ParsesSqlCommands()
    {
        var payload = new PayloadValid
        {
            Queryable = null,
            CaptureSkipReason = null,
            CaptureError = null,
            Commands =
            [
                new CommandPayload
                {
                    Sql = "SELECT 1",
                    Parameters =
                    [
                        new ParameterPayload { Name = "@p0", ClrType = "int", InferredValue = "1" },
                    ],
                },
            ],
        };

        var result = s_parseExecutionPayload.Invoke(null, [payload]);
        Assert.NotNull(result);

        var tuple = Assert.IsAssignableFrom<ITuple>(result);
        var commands = Assert.IsAssignableFrom<IReadOnlyList<QuerySqlCommand>>(tuple[3]);
        Assert.Single(commands);
        Assert.Equal("SELECT 1", commands[0].Sql);
        Assert.Single(commands[0].Parameters);
        Assert.Equal("@p0", commands[0].Parameters[0].Name);
    }

    private sealed record PayloadMissingCommands
    {
        public object? Queryable { get; init; }
        public string? CaptureSkipReason { get; init; }
        public string? CaptureError { get; init; }
    }

    private sealed record PayloadCommandsNotEnumerable
    {
        public object? Queryable { get; init; }
        public string? CaptureSkipReason { get; init; }
        public string? CaptureError { get; init; }
        public int Commands { get; init; }
    }

    private sealed record PayloadValid
    {
        public object? Queryable { get; init; }
        public string? CaptureSkipReason { get; init; }
        public string? CaptureError { get; init; }
        public IReadOnlyList<CommandPayload> Commands { get; init; } = [];
    }

    private sealed record CommandPayload
    {
        public string? Sql { get; init; }
        public IReadOnlyList<ParameterPayload> Parameters { get; init; } = [];
    }

    private sealed record ParameterPayload
    {
        public string? Name { get; init; }
        public string? ClrType { get; init; }
        public string? InferredValue { get; init; }
    }
}
