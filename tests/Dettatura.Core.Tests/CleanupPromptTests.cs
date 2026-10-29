using Dettatura.Core.Cleanup;

namespace Dettatura.Core.Tests;

public class CleanupPromptTests
{
    [Fact]
    public void Wrap_PutsTranscriptBetweenTags()
    {
        Assert.Equal("<dettato>\nciao\n</dettato>", CleanupPrompt.Wrap("  ciao  "));
    }

    [Theory]
    [InlineData("Domani passo.", "Domani passo.")]
    [InlineData("<dettato>\nDomani passo.\n</dettato>", "Domani passo.")]
    [InlineData("  Domani passo.\n", "Domani passo.")]
    public void Unwrap_RemovesEchoedTagsAndWhitespace(string response, string expected)
    {
        Assert.Equal(expected, CleanupPrompt.Unwrap(response));
    }

    [Fact]
    public void BuildArguments_DisablesToolsAndUsesStreamJson()
    {
        var args = ClaudeCleaner.BuildArguments(new ClaudeOptions { Model = "haiku" });

        Assert.Equal("-p", args[0]);
        Assert.Equal("haiku", args[args.ToList().IndexOf("--model") + 1]);
        Assert.Equal("", args[args.ToList().IndexOf("--tools") + 1]);
        Assert.Contains("--safe-mode", args);
        Assert.Equal("stream-json", args[args.ToList().IndexOf("--input-format") + 1]);
        Assert.Equal("stream-json", args[args.ToList().IndexOf("--output-format") + 1]);
    }

    [Fact]
    public void BuildArguments_UsesCustomSystemPromptWhenSet()
    {
        var args = ClaudeCleaner.BuildArguments(new ClaudeOptions { SystemPrompt = "Solo punteggiatura." });

        Assert.Equal("Solo punteggiatura.", args[args.ToList().IndexOf("--system-prompt") + 1]);
    }
}
