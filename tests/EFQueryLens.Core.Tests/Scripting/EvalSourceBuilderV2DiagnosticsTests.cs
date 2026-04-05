// EvalSourceBuilderV2DiagnosticsTests.cs — unit tests validating structured diagnostic emission
// when v2 capture initialization encounters unsupported types or placeholder failures.
using EFQueryLens.Core.Contracts;
using EFQueryLens.Core.Scripting.Compilation;
using Xunit;

namespace EFQueryLens.Core.Tests.Scripting;

/// <summary>
/// Unit tests for diagnostic emission during v2 capture initialization code generation.
/// Validates that unsupported types and placeholder failures produce structured diagnostics
/// per the taxonomy in docs/unresolved-capture-examples.md.
/// </summary>
public class EvalSourceBuilderV2DiagnosticsTests
{
    [Fact]
    public void BuildV2CaptureInitializationCode_PlaceholderForUnsupportedType_EmitsDiagnostic()
    {
        // Arrange
        var context = new EvalSourceBuilderDiagnosticContext();
        EvalSourceBuilderDiagnosticContextHolder.SetContext(context);
        
        try
        {
            var entry = new V2CapturePlanEntry
            {
                Name = "customService",
                TypeName = "MyApp.Services.CustomService",
                CapturePolicy = LocalSymbolReplayPolicies.UsePlaceholder,
            };

            // Act
            var code = EvalSourceBuilder.BuildV2CaptureInitializationCode(entry);

            // Assert
            Assert.NotNull(code);
            Assert.Contains("var customService =", code);
            
            var diagnostics = context.Diagnostics;
            Assert.Single(diagnostics);
            
            var diag = diagnostics[0];
            Assert.Equal("placeholder-unsupported", diag.Category);
            Assert.Equal("customService", diag.SymbolName);
            Assert.Equal("no-canonical-default-available", diag.Reason);
            Assert.Contains("cannot be reliably reconstructed", diag.Message);
            Assert.NotNull(diag.SuggestedFix);
        }
        finally
        {
            EvalSourceBuilderDiagnosticContextHolder.ClearContext();
        }
    }

    [Fact]
    public void BuildV2CaptureInitializationCode_PlaceholderForSupportedScalarType_EmitsNoDiagnostic()
    {
        // Arrange
        var context = new EvalSourceBuilderDiagnosticContext();
        EvalSourceBuilderDiagnosticContextHolder.SetContext(context);
        
        try
        {
            var entry = new V2CapturePlanEntry
            {
                Name = "count",
                TypeName = "int",
                CapturePolicy = LocalSymbolReplayPolicies.UsePlaceholder,
            };

            // Act
            var code = EvalSourceBuilder.BuildV2CaptureInitializationCode(entry);

            // Assert
            Assert.NotNull(code);
            Assert.Contains("var count = 1", code);
            
            var diagnostics = context.Diagnostics;
            Assert.Empty(diagnostics);
        }
        finally
        {
            EvalSourceBuilderDiagnosticContextHolder.ClearContext();
        }
    }

    [Fact]
    public void BuildV2CaptureInitializationCode_PlaceholderForSupportedCollectionType_EmitsNoDiagnostic()
    {
        // Arrange
        var context = new EvalSourceBuilderDiagnosticContext();
        EvalSourceBuilderDiagnosticContextHolder.SetContext(context);
        
        try
        {
            var entry = new V2CapturePlanEntry
            {
                Name = "ids",
                TypeName = "int[]",
                CapturePolicy = LocalSymbolReplayPolicies.UsePlaceholder,
            };

            // Act
            var code = EvalSourceBuilder.BuildV2CaptureInitializationCode(entry);

            // Assert
            Assert.NotNull(code);
            Assert.Contains("var ids =", code);
            Assert.Contains("new int[]", code);
            Assert.Contains("1", code);
            Assert.Contains("2", code);
            
            var diagnostics = context.Diagnostics;
            Assert.Empty(diagnostics);
        }
        finally
        {
            EvalSourceBuilderDiagnosticContextHolder.ClearContext();
        }
    }

    [Fact]
    public void BuildV2CaptureInitializationCode_MultipleEntriesWithMixedTypes_EmitsDiagnosticsForUnsupported()
    {
        // Arrange
        var context = new EvalSourceBuilderDiagnosticContext();
        EvalSourceBuilderDiagnosticContextHolder.SetContext(context);
        
        try
        {
            var entries = new[]
            {
                new V2CapturePlanEntry
                {
                    Name = "count",
                    TypeName = "int",
                    CapturePolicy = LocalSymbolReplayPolicies.UsePlaceholder,
                },
                new V2CapturePlanEntry
                {
                    Name = "customService",
                    TypeName = "MyApp.Services.CustomService",
                    CapturePolicy = LocalSymbolReplayPolicies.UsePlaceholder,
                },
                new V2CapturePlanEntry
                {
                    Name = "ids",
                    TypeName = "System.Collections.Generic.List<string>",
                    CapturePolicy = LocalSymbolReplayPolicies.UsePlaceholder,
                },
            };

            // Act
            foreach (var entry in entries)
            {
                EvalSourceBuilder.BuildV2CaptureInitializationCode(entry);
            }

            // Assert
            var diagnostics = context.Diagnostics;
            Assert.Single(diagnostics);
            Assert.Equal("customService", diagnostics[0].SymbolName);
        }
        finally
        {
            EvalSourceBuilderDiagnosticContextHolder.ClearContext();
        }
    }

    [Fact]
    public void BuildV2CaptureInitializationCode_RejectPolicy_EmitsNoDiagnosticHere()
    {
        // Arrange - Note: Reject should be handled by capture plan builder, not EvalSourceBuilder
        var context = new EvalSourceBuilderDiagnosticContext();
        EvalSourceBuilderDiagnosticContextHolder.SetContext(context);
        
        try
        {
            var entry = new V2CapturePlanEntry
            {
                Name = "rejected",
                TypeName = "object",
                CapturePolicy = LocalSymbolReplayPolicies.Reject,
            };

            // Act
            var code = EvalSourceBuilder.BuildV2CaptureInitializationCode(entry);

            // Assert
            Assert.Null(code);
            var diagnostics = context.Diagnostics;
            Assert.Empty(diagnostics);  // Reject diagnostics emitted at capture-plan-build time, not here
        }
        finally
        {
            EvalSourceBuilderDiagnosticContextHolder.ClearContext();
        }
    }

    [Theory]
    [InlineData("MyApp.Domain.Customer", "placeholder-unsupported")]
    [InlineData("MyApp.Services.OrderService", "placeholder-unsupported")]
    [InlineData("SomeComplexCustomType", "placeholder-unsupported")]
    public void BuildV2CaptureInitializationCode_UnsupportedTypeVariants_AllEmitDiagnostics(
        string typeName,
        string expectedCategory)
    {
        // Arrange
        var context = new EvalSourceBuilderDiagnosticContext();
        EvalSourceBuilderDiagnosticContextHolder.SetContext(context);
        
        try
        {
            var entry = new V2CapturePlanEntry
            {
                Name = "unsupported",
                TypeName = typeName,
                CapturePolicy = LocalSymbolReplayPolicies.UsePlaceholder,
            };

            // Act
            var code = EvalSourceBuilder.BuildV2CaptureInitializationCode(entry);

            // Assert
            Assert.NotNull(code);
            var diagnostics = context.Diagnostics;
            Assert.NotEmpty(diagnostics);
            Assert.Equal(expectedCategory, diagnostics[0].Category);
        }
        finally
        {
            EvalSourceBuilderDiagnosticContextHolder.ClearContext();
        }
    }

    [Fact]
    public void DiagnosticContextEmit_StoresDiagnostics()
    {
        // Arrange & Act
        var context = new EvalSourceBuilderDiagnosticContext();
        var diag = EvalSourceBuilderDiagnostics.DetailedPlaceholderUnsupported("test", "TestType");
        context.Emit(diag);

        // Assert
        Assert.Single(context.Diagnostics);
        Assert.Equal("test", context.Diagnostics[0].SymbolName);
    }

    [Fact]
    public void DiagnosticContextFlush_ReturnsDiagnosticsAndClears()
    {
        // Arrange
        var context = new EvalSourceBuilderDiagnosticContext();
        var diag1 = EvalSourceBuilderDiagnostics.DetailedPlaceholderUnsupported("var1", "Type1");
        var diag2 = EvalSourceBuilderDiagnostics.DetailedPlaceholderUnsupported("var2", "Type2");
        
        // Act - Emit directly to the instance
        context.Emit(diag1);
        context.Emit(diag2);
        
        var diagnosticsBefore = context.Diagnostics.Count;
        var flushed = context.FlushDiagnostics();

        // Assert
        Assert.Equal(2, diagnosticsBefore);
        Assert.Equal(2, flushed.Count);
        Assert.Equal("var1", flushed[0].SymbolName);
        Assert.Equal("var2", flushed[1].SymbolName);
        
        // Verify context is cleared after flush
        Assert.Empty(context.Diagnostics);
    }

    [Fact]
    public void DiagnosticCode_SequenceIncrementsUniquely()
    {
        // The diagnostics created by the factory methods each get a unique sequence number
        var diag1 = EvalSourceBuilderDiagnostics.DetailedPlaceholderUnsupported("var1", "Type1");
        var diag2 = EvalSourceBuilderDiagnostics.DetailedPlaceholderUnsupported("var2", "Type2");
        var diag3 = EvalSourceBuilderDiagnostics.DetailedPlaceholderUnsupported("var3", "Type3");

        // Assert - codes should be unique and increment
        Assert.NotEqual(diag1.Code, diag2.Code);
        Assert.NotEqual(diag2.Code, diag3.Code);
        Assert.NotEqual(diag1.Code, diag3.Code);
        
        // Verify the format is consistent: QLDIAG_PLACEHOLDER_UNSUPPORTED_{number}
        Assert.StartsWith("QLDIAG_PLACEHOLDER_UNSUPPORTED_", diag1.Code);
        Assert.StartsWith("QLDIAG_PLACEHOLDER_UNSUPPORTED_", diag2.Code);
        Assert.StartsWith("QLDIAG_PLACEHOLDER_UNSUPPORTED_", diag3.Code);
    }
}
