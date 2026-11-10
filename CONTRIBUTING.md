# Contributing

Thanks for taking a look. This page covers the project layout, how to build, test and benchmark it, and the conventions used. For how the app works inside, see [docs/how-it-works.md](docs/how-it-works.md).

## Requirements

- Windows 10 or 11, x64, with the [.NET 10 SDK](https://dotnet.microsoft.com/download).
- A GPU with a Vulkan driver to get realistic transcription times.
- [Claude Code](https://code.claude.com/docs/en/overview) installed and signed in, only to work on or measure rewriting.

## Project structure

| Path | Contents |
|---|---|
| `src/WhisperAloud.Core` | transcription (`WhisperTranscriber`, `VulkanDevices`), rewriting (`ClaudeCleaner`, `ClaudeProcess`, `CleanupPrompt`, stream-json protocol in `StreamJson`), settings (`WhisperAloudSettings`), data folders (`AppPaths`), latency log |
| `src/WhisperAloud.App` | WinForms tray app: shortcuts (`Hotkey`, `RegisterHotKey`), start with Windows (`Autostart`), recording (`MicRecorder`, NAudio), pasting (`TextInjector`, `SendInput`), status window (`OverlayForm`), settings window (`SettingsForm`), dark theme (`Theme`, `ModernControls`) |
| `tests/WhisperAloud.Core.Tests` | xUnit tests for the stream-json protocol, prompts, prompt lookup and CLI arguments |
| `tools/WhisperAloud.Bench` | benchmark of the pipeline on audio files |
| `samples` | three sentences synthesized with the Windows "Elsa" voice (for Whisper timing) and `trascrizioni.txt` (for rewriting quality and timing) |
| `docs` | user documentation and its screenshots |

`WhisperAloud.Core` has no UI dependency and holds everything that can be tested or benchmarked without the tray app.

## Build and run

```bash
dotnet build WhisperAloud.slnx -c Release
dotnet run -c Release --project src/WhisperAloud.App
```

Only one instance runs at a time: quit the running app from the tray menu (**Quit**) before starting a new build, otherwise the new one exits immediately.

## Release

Pushing a tag that starts with `v` runs the [Release workflow](.github/workflows/release.yml): it runs the tests, publishes a self-contained single-file `WhisperAloud.exe` for Windows x64 and creates a GitHub Release with the exe attached and notes generated from the commits.

```bash
git tag v1.0.0
```

```bash
git push origin v1.0.0
```

To build the same exe locally:

```bash
dotnet publish src/WhisperAloud.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
```

The exe bundles .NET and the Whisper native libraries, keeping only those for `win-x64` (CPU) and `vulkan/win-x64` (GPU); on first start it extracts them to `%TEMP%\.net\WhisperAloud`.

## Tests

```bash
dotnet test
```

The tests cover the parts that do not need a microphone, a GPU or the `claude` CLI: message building and parsing, prompt handling and the CLI arguments. Add a test when you change any of them.

## Benchmark

```bash
dotnet run -c Release --project tools/WhisperAloud.Bench
dotnet run -c Release --project tools/WhisperAloud.Bench -- --solo-whisper --gpu 1
dotnet run -c Release --project tools/WhisperAloud.Bench -- haiku --prompt "Meeting notes"
```

The benchmark transcribes the WAV files in `samples` (3 runs each), then rewrites the sentences in `samples/trascrizioni.txt`, first starting a new `claude` process each time and then through the standby process, printing the text before and after so quality can be checked too. Options:

| Option | Meaning |
|---|---|
| first plain argument | Claude model, default `sonnet` |
| `--gpu N` | Vulkan device index for Whisper; default picks the first dedicated GPU |
| `--solo-whisper` | measure transcription only, without Claude |
| `--prompt "Name"` | prompt to use for rewriting, by name; default: the default prompt in your settings |

Update [docs/latency.md](docs/latency.md) when a change affects the numbers.

## Conventions

- **Language**: every interface string is written in Italian and English where it is used, with `Loc.T("italiano", "English")` from `WhisperAloud.Core`; add both when you add one. Code comments and log messages are in Italian; documentation is in English.
- **Style**: follow `.editorconfig`; nullable reference types are on and the build should stay free of warnings.
- **Documentation**: the README is for people who want to understand and use the app; implementation details go in `docs`, contributor information here.
- **No secrets or personal data in the repository**: never commit `settings.json`, logs, `latency.csv` or models; they live in `%APPDATA%` and `%LOCALAPPDATA%`, outside the repository. The app must never read or store Claude credentials: it only starts the unmodified `claude` CLI.
- **Commits**: use the GitHub no-reply address as commit email (`git config user.email "<id>+<user>@users.noreply.github.com"`), so personal addresses do not end up in the public history.
