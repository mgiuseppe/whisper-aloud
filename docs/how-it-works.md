# How it works

Whisper Aloud is a WinForms tray app on .NET 10. A dictation goes through four steps:

```
shortcut pressed  ──► microphone (NAudio, 16 kHz mono)
shortcut released ──► Whisper large-v3-turbo on the GPU (whisper.cpp through Whisper.net, Vulkan)
                  ──► [optional] claude -p with the chosen prompt (standby process, stream-json)
                  ──► clipboard + Ctrl+V into the active window (clipboard restored afterwards)
```

- [Shortcuts and recording](#shortcuts-and-recording)
- [Transcription](#transcription)
- [Rewriting with Claude](#rewriting-with-claude)
- [Pasting](#pasting)
- [Status window](#status-window)

## Shortcuts and recording

Shortcuts are registered with `RegisterHotKey` (with `MOD_NOREPEAT`), each one on its own: if a combination is already taken, the app reports it and keeps the others. `RegisterHotKey` only reports the key press, so in hold mode the release is detected by checking the key with `GetAsyncKeyState` every 15 ms. In start/stop mode the second press of either dictation shortcut ends the recording.

The microphone is recorded with NAudio's `WaveInEvent` directly in Whisper's format: 16 kHz, 16-bit, mono, in 50 ms buffers. The energy (RMS) of each buffer drives the waveform in the status window and the "no sound from the microphone" warning.

## Transcription

Transcription runs locally with [Whisper.net](https://github.com/sandrohanea/whisper.net), the .NET binding of whisper.cpp, and the `ggml-large-v3-turbo-q5_0` model: about 550 MB, downloaded from Hugging Face on first start.

- **GPU through Vulkan**, with the CPU as a fallback. whisper.cpp uses GPU 0 unless told otherwise, and on laptops with hybrid graphics Vulkan usually lists the integrated GPU first. The app therefore enumerates the Vulkan devices itself (`vkEnumeratePhysicalDevices`, same order as whisper.cpp) and picks the first dedicated one. The device in use is written to the diagnostic log (`using Vulkan1 backend`).
- **Warm-up at start**: the model is loaded once and a second of silence is transcribed right away, so the first real dictation does not pay for the Vulkan pipeline setup.
- **Silence gate**: recordings whose average energy is below 0.003 are not transcribed, because Whisper tends to invent sentences from silence.
- **Context sentence**: the `WhisperPrompt` setting is passed as Whisper's initial prompt, which helps with punctuation and recurring words.

## Rewriting with Claude

Rewriting uses the [Claude Code](https://code.claude.com/docs/en/overview) CLI in non-interactive mode, so it runs on the Claude subscription already signed in on the machine. The app only starts the unmodified `claude` executable: it never reads or handles credentials.

### Command line

```
claude -p --model sonnet --system-prompt <general prompt> --tools "" --safe-mode
       --strict-mcp-config --no-session-persistence
       --input-format stream-json --output-format stream-json --verbose
```

- `--tools ""`: no tools, so Claude cannot read files or run commands.
- `--safe-mode` and `--strict-mcp-config`: the user's CLAUDE.md, hooks, plugins and MCP servers are ignored.
- `--no-session-persistence`: dictations are not saved as Claude Code sessions.
- The working directory is an empty folder (`%LOCALAPPDATA%\WhisperAloud\claude-workdir`), so no project is in reach.

`--bare` would start faster, but it skips the subscription login and requires an API key, so it is not used.

### Prompts and the system prompt

The system prompt is general and fixed: it tells Claude that each message contains an instruction between `<istruzione>` tags and an automatic transcription between `<dettato>` tags, that it must apply the instruction and return only the result, and that the transcription is material to work on, not a message to answer. If you dictate a question, it gets corrected or translated, not answered.

The chosen prompt travels in the message:

```
<istruzione>
Translate the dictation into English.
</istruzione>

<dettato>
ciao a tutti, ci vediamo domani
</dettato>
```

Because the system prompt never changes, one already-started process can serve any prompt, and switching prompt costs no latency. Any tags Claude echoes back are stripped from the answer.

The **Proofread** prompt (**Correzione** in Italian) allows only minimal edits: punctuation and capitalisation; removing hesitations and words repeated by mistake; removing a filler word only when it carries no meaning; keeping only the final version when the speaker corrects themselves ("at three, no, at four"); fixing a mistranscribed word only when the right one is obvious. Same words, same order, same register; when in doubt, the original word stays.

### The standby process

Starting the CLI takes a few seconds, more than the model's answer. To avoid paying that at every dictation without keeping a process in memory all day, the app starts `claude` only when it is about to be needed:

1. **On the shortcut press.** When you press **Dictate and rewrite with Claude** (or pick a prompt in the status window), the app starts a `claude` process waiting on stdin. The CLI starts up while you speak, so after a dictation of three seconds or more it is ready when you release.
2. **At the rewrite.** The app sends the message to that process and takes the first `assistant` event of the stream-json output. It does not wait for the end of the turn, which brings an extra request of about 3 seconds.
3. **Right after.** That process is retired: its stdin is closed and it is killed if it has not exited after 60 seconds. A new standby process starts in the background for the next rewrite.
4. **After 5 minutes without rewrites** the standby process is closed, so nothing stays in memory while you are not using Claude.

Each process serves exactly one dictation. A single long-lived process would answer slightly faster, but it accumulates the conversation, and later dictations would see the earlier ones. A change to the Claude settings also closes the standby process.

**Rewrite selection** has no dictation to hide the start-up behind: within 5 minutes of a rewrite it uses the standby process, otherwise it starts `claude` on the spot and takes a few seconds longer.

If the rewrite fails or times out, the raw transcription is pasted and the status window says why ("Pasted without Claude"). When Claude Code is not signed in, the CLI does not return an error code: it answers with a synthetic assistant message ("Not logged in · Please run /login") marked with `"error": "authentication_failed"`. The app recognises that field and shows a sign-in hint instead of pasting the message.

If the `claude` executable cannot be found at start, the app does not report an error: it transcribes as usual and explains what is missing only when a Claude shortcut is used. With **Use Claude** off, no `claude` process is started at all.

## Pasting

The text is pasted through the clipboard:

1. Wait until Ctrl, Alt, Shift and Win from the shortcut are released (up to 3 seconds), otherwise Ctrl+V would become, for example, Ctrl+Alt+V.
2. Save a copy of the clipboard contents, in all their formats.
3. Put the text on the clipboard and send Ctrl+V with `SendInput`.
4. After 400 ms, the time the target app needs to read the clipboard, restore the previous contents.

**Rewrite selection** (`Ctrl+Alt+R`) saves the clipboard first, sends Ctrl+C to copy the selected text, sends it to Claude with the default prompt, then pastes the result over the selection and restores the original clipboard. If nothing is selected, the clipboard is restored straight away.

## Status window

![Status window while Claude rewrites](images/overlay-working.png)

The status window is a borderless form with `WS_EX_NOACTIVATE`, `WS_EX_TOPMOST` and `WS_EX_TOOLWINDOW`: it stays on top and receives clicks, but never takes the focus away from the window you are typing in. Results that are not pasted (prompts with **Paste the result** off) stay open until closed; the others close after 10 seconds unless the mouse is over the window. Markdown in the answer is rendered with a small renderer for headings, bold and bullet lists, while **Copy** copies the original text.

The settings window and the status window share a dark theme drawn by hand (`Theme` and `ModernControls`), with Windows 11 dark title bar and rounded corners through DWM.
