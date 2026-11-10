# Whisper Aloud

Voice dictation for Windows in the style of Wispr Flow: hold a shortcut, speak, release, and the text appears in the active window.

- **Local transcription**
  - With [Whisper.net](https://github.com/sandrohanea/whisper.net) (whisper.cpp) and the quantized `large-v3-turbo` model, on the GPU through Vulkan.
- **Optional rewriting with Claude**
  - Light proofreading, meeting notes, translation, or your own.
  - Through the [Claude Code](https://code.claude.com/docs/en/overview) CLI in headless mode, using the Claude subscription already signed in on the machine.

![Dictating an email with Whisper Aloud](docs/images/usage.gif)

Transcription runs locally on your GPU; rewriting with Claude is optional and only happens when you ask for it.

## Prerequisites

- Windows 10 or 11, x64, with the [.NET 10 SDK](https://dotnet.microsoft.com/download).
- A GPU with a Vulkan driver for fast transcription. Without one Whisper falls back to the CPU and is much slower. On laptops with hybrid graphics the app picks the dedicated GPU automatically: see [latency](docs/latency.md#laptops-with-two-gpus).
- For rewriting only: [Claude Code](https://code.claude.com/docs/en/overview) installed and signed in (`claude` on the `PATH` or in `%USERPROFILE%\.local\bin`). The app never handles logins or credentials; without Claude Code it still transcribes.

## Usage

```bash
dotnet run -c Release --project src/WhisperAloud.App
```

On first start the app downloads the Whisper model (~550 MB) to `%LOCALAPPDATA%\WhisperAloud\models` and creates `%APPDATA%\WhisperAloud\settings.json`.

### Shortcuts

| Shortcut | What it does |
|---|---|
| `Ctrl+Alt+D` (hold) | dictate and paste Whisper's transcription as it is |
| `Ctrl+Alt+Shift+D` (hold) | dictate, rewrite with the default Claude prompt, paste |
| `Ctrl+Alt+R` | rewrite the selected text with the default prompt and replace it |

Rewriting uses customizable [prompts](docs/settings.md#prompts).

> [!NOTE]
> Whisper transcribes in the language set in the settings (Italian or English by default, following the app language), not the one it hears. You can set it to `auto` to detect the language from the recording, handy if you switch languages often, but on short dictations it guesses wrong more often: see [Transcription settings](docs/settings.md#transcription).

## Settings

Double-click the tray icon, or choose **Settings...** from its menu. Four pages cover dictation mode and shortcuts, microphone and GPU, Claude and its prompts, and what happens after the transcription. Changes apply as soon as you press **Save**, without restarting the app.

![Settings window](docs/images/settings-dictation.png)

Every option, the prompt editor and the `settings.json` reference are described in [docs/settings.md](docs/settings.md).

## Documentation

- [Settings](docs/settings.md): the settings window, prompts and `settings.json`.
- [How it works](docs/how-it-works.md): recording, transcription, rewriting with the Claude Code CLI and pasting.
- [Latency](docs/latency.md): measurements, model choice and the hybrid-graphics pitfall.
- [Contributing](CONTRIBUTING.md): project structure, build, tests and benchmark.

## Known limitations

- Push-to-talk detects the release by checking the key every 15 ms, because `RegisterHotKey` only reports the key press.
- Pasting goes through the clipboard: the previous contents are restored after 400 ms, but clipboard history managers still see the dictated text.
- The bundled audio samples use a synthetic voice: they measure timing well, not transcription quality on a real voice.
- The standby `claude` process stays running even if you never use rewriting (memory only, no requests to the model until needed).
- Every rewritten dictation is a request to the model and counts towards the usage limits of your Claude subscription.

## Personal use and Anthropic's terms

Whisper Aloud is a personal project, not affiliated with or endorsed by Anthropic. For rewriting it runs the Claude Code CLI exactly as distributed, without modifying it: you sign in through Anthropic's own flow, and the app never reads, stores or transmits credentials or tokens. Anyone using it does so with their own Claude account, under [Anthropic's terms](https://code.claude.com/docs/en/legal-and-compliance) and the usage limits of their own subscription. To use it inside products or services offered to others, Anthropic requires API key authentication.

## License

[MIT](LICENSE).
