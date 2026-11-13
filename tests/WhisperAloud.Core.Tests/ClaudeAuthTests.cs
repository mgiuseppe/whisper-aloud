using WhisperAloud.Core.Cleanup;
using WhisperAloud.Core.Transcription;

namespace WhisperAloud.Core.Tests;

public class ClaudeAuthTests
{
    [Theory]
    [InlineData("""{ "loggedIn": true, "authMethod": "claude.ai" }""", true)]
    [InlineData("""{ "loggedIn": false, "authMethod": "none" }""", false)]
    [InlineData("""{ "authMethod": "none" }""", null)]
    [InlineData("""{ "loggedIn": "yes" }""", null)]
    [InlineData("error: unknown command 'auth'", null)]
    [InlineData("", null)]
    public void ParseSignedIn_ReadsOnlyLoggedIn(string output, bool? expected) =>
        Assert.Equal(expected, ClaudeAuth.ParseSignedIn(output));

    [Fact]
    public void DownloadStatus_ShowsMegabytesAndPercent()
    {
        Loc.Use("en");
        try
        {
            Assert.Equal("Downloading the Whisper model: 274 / 547 MB (50%)", WhisperTranscriber.DownloadStatus(274, 547));
            Assert.Equal("Downloading the Whisper model: 120 MB", WhisperTranscriber.DownloadStatus(120, null));
        }
        finally
        {
            Loc.Use("auto");
        }
    }
}
