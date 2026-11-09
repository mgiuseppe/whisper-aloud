# Settings

Open the settings window by double-clicking the tray icon, or with **Settings...** in the tray menu. Changes apply as soon as you press **Save**: the app reloads only what changed, for example Whisper when you pick another GPU or model, or the `claude` process when you change its options. **Cancel** discards them.

The interface is available in English and Italian: see **App language** under [Dictation](#dictation). Labels below are the English ones.

- [Dictation](#dictation)
- [Transcription](#transcription)
- [Claude](#claude)
- [Prompts](#prompts)
- [Paste and status window](#paste-and-status-window)
- [Tray menu](#tray-menu)
- [settings.json reference](#settingsjson-reference)
- [Files and folders](#files-and-folders)

## Dictation

![Dictation page](images/settings-dictation.png)

- **Mode**:
  - **Hold**: records while you hold the shortcut. Best for short sentences.
  - **Start and stop**: press once to start and again to stop. Handy for long dictations.
- **Shortcuts**: click a shortcut and press the new key combination; `Esc` cancels. While a shortcut field has the focus, the global shortcuts are suspended, so pressing the combination does not start a dictation. Without modifiers only function keys (`F1`–`F24`) are accepted.
  - **Dictate and paste**: dictate and paste Whisper's transcription as it is.
  - **Dictate and rewrite with Claude**: dictate, rewrite with the default prompt, paste.
  - **Rewrite the selection**: rewrite the text selected in the active window with the default prompt.
  - If a shortcut is already taken by another app, Whisper Aloud says so and still registers the others.
- **App language**: **Automatic (Windows)** uses Italian when Windows is in Italian and English otherwise; or choose **Italiano** or **English**. Changing it restarts the app. The sample prompts are created in the app language on first start; after that they are yours and are not translated.
- **Minimum press (ms)**: hold mode only; shorter presses are treated as accidental and show a hint instead of recording.

## Transcription

![Transcription page](images/settings-transcription.png)

- **Microphone**: the Windows default device or a specific one. It can also be chosen from the tray menu.
- **GPU**: **Automatic** picks the first dedicated GPU listed by Vulkan; otherwise choose a device. On laptops with two GPUs the integrated one is about 20 times slower: see [latency](latency.md#laptops-with-two-gpus).
- **Language**: the spoken language (`it`, `en`, `fr`, `de`, `es`, `pt`) or `auto`, which detects it but makes more mistakes. It is independent of the app language.
- **Model**: the file name inside the models folder, or a full path to another ggml model.
- **Context**: a sentence that tells Whisper what to expect. It helps with punctuation and with recurring names or technical terms. Drag the handle at the bottom to enlarge the box.

## Claude

![Claude page](images/settings-claude.png)

- **Model**: `sonnet` is the most faithful to the dictated text; `haiku` is faster but sometimes rephrases. See [latency](latency.md#rewriting).
- **Process ready**: keeps a `claude` process started in the background, so a rewrite takes about 2 seconds instead of 4–5. See [how it works](how-it-works.md#the-standby-process).
- **Timeout (seconds)**: how long to wait for Claude; after that the raw transcription is pasted.
- **Executable**: `claude` is looked up on the `PATH` and in `%USERPROFILE%\.local\bin`; use a full path for a different installation.
- **Default prompt**: used by **Dictate and rewrite with Claude** and **Rewrite the selection**.

## Prompts

A prompt tells Claude what to do with the dictated text. Each one has:

- **Name**: shown in the status window and in the menus.
- **Instructions**: what Claude should do, in plain words. Drag the handle at the bottom of the box to enlarge it while you write; double-click the handle to switch between compact and large.
- **Paste the result**: when on, the result is pasted into the active window; when off, it stays in the status window until you close it, ready to copy. Useful for meeting notes and summaries.

**+ Add prompt** adds a new one and **Delete** deletes it; at least one prompt must remain, and names must be unique.

Included prompts (the Italian interface starts with the same four, in Italian):

| Prompt | Instructions in short | Pastes |
|---|---|---|
| Proofread | light proofreading: punctuation, hesitations, words repeated by mistake, self-corrections ("at three, no, at four"); same words, same order, never changes the meaning | yes |
| Meeting notes | short summary, list of topics, one or more calls to action, very concise | no |
| Translate into Italian | translate the dictation into Italian; in the Italian interface it translates into English | yes |
| Drunk pirate | rewrite it the way a drunk pirate would say it | yes |

![Meeting notes shown in the status window](images/overlay-minute.png)

Claude may answer in Markdown: the status window shows headings, **bold** and bullet lists formatted, while **Copy** and pasting use the original Markdown.

The default prompt is chosen here or in the tray menu. While recording, the selector in the status window picks a different prompt, or **Transcription only**, for that dictation alone; the next one goes back to the default.

## Paste and status window

![Paste page](images/settings-paste.png)

- **Space after the text**: adds a space after the pasted text, so consecutive dictations do not stick together.
- **Restore the clipboard**: puts the previous clipboard contents back after pasting.
- **Status window**: shows the always-on-top window while listening, working and pasting. When off, warnings appear as tray notifications.

The status window follows each dictation without stealing focus, so the text still lands where you were typing:

![Status window while listening](images/overlay-recording.png)

- **Listening**: a waveform follows your voice, with a warning if the microphone sends no sound, and the prompt selector.
- **Working**: transcription and rewriting show a running timer.
- **Done**: the text, the total time and a **Copy** button. It closes after 10 seconds, or stays open while the mouse is over it; results that are not pasted stay until you close them.
- **Warnings**: press too short, silent microphone, no words recognised, errors.

![Result pasted into the active window](images/overlay-result.png)

Drag it by its background, title or waveform: the position is saved, and a double click brings it back to the bottom centre of the screen.

## Tray menu

Whisper Aloud lives in the system tray. The icon is grey when idle, red while listening and orange while working. Right-click it for:

- **Copy last dictation**: copies the last dictation again.
- **Claude prompt**: chooses the default prompt.
- **Microphone**: chooses the microphone.
- **Settings...**: opens the settings window.
- **Latency log** and **Diagnostic log**: open the latency log and the diagnostic log.
- **Quit**: quits the app.

## settings.json reference

The settings window covers everything most people need. The link **Open settings.json** at the bottom of the window opens the file for manual edits, which apply at the next start of the app.

| Key | Default | Notes |
|---|---|---|
| `Mode` | `PushToTalk` | `PushToTalk` (hold) or `Toggle` (start and stop) |
| `HotkeyDictate` | `Ctrl+Alt+D` | format `Ctrl+Alt+D`; key names from the .NET `Keys` enum (`Space`, `F9`, ...) |
| `HotkeyDictateWithClaude` | `Ctrl+Alt+Shift+D` | |
| `HotkeyCleanSelection` | `Ctrl+Alt+R` | |
| `MinRecordingMs` | `300` | hold mode only |
| `UiLanguage` | `auto` | app language: `auto` (Italian if Windows is in Italian, otherwise English), `it` or `en` |
| `Language` | `en`, or `it` with the Italian interface | Whisper language code, or `auto` |
| `WhisperModel` | `ggml-large-v3-turbo-q5_0.bin` | name in the models folder or full path |
| `WhisperPrompt` | a sentence in the app language | context for Whisper |
| `MicrophoneDevice` | `-1` | `-1` is the Windows default device; otherwise the WaveIn device index |
| `GpuDevice` | `-1` | `-1` picks the first dedicated GPU; otherwise the Vulkan device index |
| `ShowOverlay` | `true` | status window on or off |
| `OverlayLeft` / `OverlayTop` | empty | saved position of the status window; empty means bottom centre |
| `AppendSpace` | `true` | space after the pasted text |
| `RestoreClipboard` | `true` | restore the clipboard after pasting |
| `Claude.Executable` | `claude` | name or full path of the CLI |
| `Claude.Model` | `sonnet` | any model name accepted by `claude --model` |
| `Claude.UseWarmProcess` | `true` | keep a `claude` process ready |
| `Claude.WarmProcessMaxAgeMinutes` | `20` | the standby process is replaced after this time |
| `Claude.TimeoutSeconds` | `30` | maximum wait for a rewrite |
| `Claude.Prompts` | four prompts | list of `{ "Name", "Instructions", "Paste" }` |
| `Claude.SelectedPrompt` | the first prompt | name of the default prompt |
| `Claude.SystemPrompt` | empty | file only: replaces the general system prompt. It must explain the `<istruzione>` and `<dettato>` tags described in [how it works](how-it-works.md#prompts-and-the-system-prompt) |

## Files and folders

| Path | Contents |
|---|---|
| `%APPDATA%\WhisperAloud\settings.json` | settings |
| `%LOCALAPPDATA%\WhisperAloud\models` | Whisper model, downloaded on first start |
| `%LOCALAPPDATA%\WhisperAloud\latency.csv` | time of every dictation, see [latency](latency.md#latency-log) |
| `%LOCALAPPDATA%\WhisperAloud\app.log` | diagnostic log: shortcuts, audio level, timings and errors, never the dictated text |
| `%LOCALAPPDATA%\WhisperAloud\claude-workdir` | empty working folder for the `claude` CLI |
