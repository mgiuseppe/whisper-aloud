using System.Text.Json.Nodes;
using Dettatura.Core.Cleanup;

namespace Dettatura.Core.Tests;

public class StreamJsonTests
{
    [Fact]
    public void UserMessage_EscapesContentAndFollowsTheCliShape()
    {
        var json = JsonNode.Parse(StreamJson.UserMessage("riga \"uno\"\nriga due"))!;

        Assert.Equal("user", json["type"]!.GetValue<string>());
        Assert.Equal("user", json["message"]!["role"]!.GetValue<string>());
        Assert.Equal("riga \"uno\"\nriga due", json["message"]!["content"]!.GetValue<string>());
    }

    [Fact]
    public void Parse_AssistantMessage_ReturnsOnlyTextBlocks()
    {
        const string line = """
            {"type":"assistant","message":{"content":[{"type":"thinking","thinking":"..."},{"type":"text","text":"Domani passo."}]}}
            """;

        var parsed = StreamJson.Parse(line);

        Assert.Equal(new AssistantText("Domani passo."), parsed);
    }

    [Fact]
    public void Parse_AssistantWithoutText_IsIgnored()
    {
        Assert.Null(StreamJson.Parse("""{"type":"assistant","message":{"content":[{"type":"thinking","thinking":"..."}]}}"""));
    }

    [Theory]
    [InlineData("""{"type":"result","subtype":"success","is_error":false,"result":"ok"}""", false)]
    [InlineData("""{"type":"result","subtype":"error_during_execution","is_error":false,"result":""}""", true)]
    [InlineData("""{"type":"result","subtype":"success","is_error":true,"result":"Not logged in"}""", true)]
    public void Parse_Result_DetectsErrors(string line, bool expectedError)
    {
        var result = Assert.IsType<TurnResult>(StreamJson.Parse(line));

        Assert.Equal(expectedError, result.IsError);
    }

    [Theory]
    [InlineData("")]
    [InlineData("non è json")]
    [InlineData("""{"type":"system","subtype":"init"}""")]
    public void Parse_IrrelevantLines_ReturnNull(string line)
    {
        Assert.Null(StreamJson.Parse(line));
    }
}
