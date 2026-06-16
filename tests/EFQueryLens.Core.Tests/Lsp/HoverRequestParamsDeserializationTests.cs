using EFQueryLens.Lsp.Protocol;
using Newtonsoft.Json;

namespace EFQueryLens.Core.Tests.Lsp;

public sealed class HoverRequestParamsDeserializationTests
{
    [Fact]
    public void Deserialize_BindsTextDocumentAndPosition()
    {
        const string json =
            """
            {
              "textDocument": { "uri": "file:///C:/proj/Query.cs" },
              "position": { "line": 12, "character": 8 }
            }
            """;

        var request = JsonConvert.DeserializeObject<HoverRequestParams>(json);

        Assert.NotNull(request);
        Assert.Equal("file:///C:/proj/Query.cs", request!.TextDocument.Uri.ToString());
        Assert.Equal(12, request.Position.Line);
        Assert.Equal(8, request.Position.Character);
    }

    [Fact]
    public void Deserialize_IgnoresDeprecatedSqlReadyEligibleProperty()
    {
        const string json =
            """
            {
              "textDocument": { "uri": "file:///C:/proj/Query.cs" },
              "position": { "line": 1, "character": 2 },
              "sqlReadyEligible": true
            }
            """;

        var request = JsonConvert.DeserializeObject<HoverRequestParams>(json);

        Assert.NotNull(request);
        Assert.Equal(1, request!.Position.Line);
    }
}
