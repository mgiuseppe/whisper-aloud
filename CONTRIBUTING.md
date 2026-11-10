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

A release is a tag `vMAJOR.MINOR.PATCH` with the release notes as its message. Pushing it runs the [Release workflow](.github/workflows/release.yml), which runs the tests, publishes a self-contained single-file `WhisperAloud.exe` for Windows x64 and creates a GitHub Release with the exe attached and the tag message as notes.

### When

Release when something has changed for people who use the app: code in `src` or the workflow. Changes to docs, the README or tests alone need no release, because GitHub shows the README of `main` and the exe would be the same. Group several commits into one release rather than releasing each one.

### Version number

- **Patch** (`v1.0.1`): bug fixes only.
- **Minor** (`v1.1.0`): new features that do not break anything.
- **Major** (`v2.0.0`): changes that break something, for example a `settings.json` that older settings no longer fit.

### Check the exe first

Build the exe locally and run it from an empty folder before tagging:

```bash
dotnet publish src/WhisperAloud.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
```

Quit the running app first (only one instance runs at a time), start `publish\WhisperAloud.exe` from another folder and check `%LOCALAPPDATA%\WhisperAloud\app.log`: Whisper must load (`using Vulkan… backend` on a GPU) and no errors may appear. A real dictation is the best check.

The exe bundles .NET and the Whisper native libraries, keeping only those for `win-x64` (CPU) and `vulkan/win-x64` (GPU); on first start it extracts them to `%TEMP%\.net\WhisperAloud`.

### Release notes

Write them for people who use the app, not for developers: what is new, what changed, what was fixed, in a few lines, in English. Group them under bold labels (**New**, **Changed**, **Fixed**) and leave out internal changes. Do not start lines with `#`: git would treat them as comments.

```markdown
**New**
- Start with Windows: a switch in the settings starts the app when you sign in.

**Fixed**
- Long shortcuts are no longer cut off in the status window.
```

### Tag and push

Save the notes to a file outside the repository, then create an annotated tag on the commit already pushed to `main` and push it:

```bash
git tag -a v1.1.0 -F notes.md --cleanup=verbatim
```

```bash
git push origin v1.1.0
```

The workflow takes a couple of minutes; the release appears under **Releases** with the exe and the notes.

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
