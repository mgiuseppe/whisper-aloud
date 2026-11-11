using WhisperAloud.Core.Cleanup;

namespace WhisperAloud.Core.Tests;

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
    public void Build_PutsInstructionBeforeTranscript()
    {
        var message = CleanupPrompt.Build(" Traduci in inglese. ", "ciao a tutti");

        Assert.Equal("<istruzione>\nTraduci in inglese.\n</istruzione>\n\n<dettato>\nciao a tutti\n</dettato>", message);
    }

    [Fact]
    public void BuildArguments_UsesGeneralSystemPromptByDefault()
    {
        var args = ClaudeCleaner.BuildArguments(new ClaudeOptions());

        Assert.Equal(CleanupPrompt.System, args[args.ToList().IndexOf("--system-prompt") + 1]);
    }

    [Fact]
    public void FindPrompt_FallsBackToSelectedThenFirst()
    {
        var options = new ClaudeOptions { Prompts = ClaudeOptions.DefaultPrompts(english: false), SelectedPrompt = "Minuta" };

        Assert.Equal("Pirata ubriaco", options.FindPrompt("pirata ubriaco").Name);
        Assert.Equal("Minuta", options.FindPrompt("non esiste").Name);
        options.SelectedPrompt = "sparito";
        Assert.Equal("Correzione", options.FindPrompt(null).Name);
    }

    [Fact]
    public void DefaultPrompts_MinuteIsShownNotPasted()
    {
        var options = new ClaudeOptions { Prompts = ClaudeOptions.DefaultPrompts(english: false) };

        Assert.False(options.FindPrompt("Minuta").Paste);
        Assert.True(options.FindPrompt("Correzione").Paste);
    }

    [Fact]
    public void DefaultPrompts_SameShapeInBothLanguages()
    {
        var italian = ClaudeOptions.DefaultPrompts(english: false);
        var english = ClaudeOptions.DefaultPrompts(english: true);

        Assert.Equal(italian.Select(p => p.Paste), english.Select(p => p.Paste));
        Assert.Equal("Meeting notes", english[1].Name);
        Assert.Equal(CleanupPrompt.CorrectionEnglish, english[0].Instructions);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("Fai una minuta della registrazione.", false)]
    [InlineData("Applica <istruzione> al testo in <dettato>.", true)]
    public void SystemPrompt_CustomUsedOnlyIfItExplainsTheTags(string? custom, bool used)
    {
        var args = ClaudeCleaner.BuildArguments(new ClaudeOptions { SystemPrompt = custom });

        var system = args[args.ToList().IndexOf("--system-prompt") + 1];
        Assert.Equal(used ? custom : CleanupPrompt.System, system);
    }
}
