# Latency

Measured on an MSI Raider GE76 laptop (RTX 3080 Laptop GPU, 8 GB) with `tools/WhisperAloud.Bench`, on 29 October 2026; the Sonnet standby figures and the GPU comparison are from 9 November 2026. See [Contributing](../CONTRIBUTING.md#benchmark) to run the benchmark yourself.

Typical total time from releasing the shortcut: **about 0.5 s** with Whisper alone, **about 2–3 s** with a Claude rewrite through the standby process (Sonnet).

## Transcription

Whisper large-v3-turbo, q5_0 quantization, Vulkan, median of 3 runs. The model loads in about 1.2 s when the app starts.

| Audio | Time |
|---|---|
| 6 s | ~0.41–0.46 s |
| 12 s | ~0.48–0.50 s |
| 24 s | ~0.59–0.60 s |

### Laptops with two GPUs

On laptops with hybrid graphics, Vulkan lists the integrated Intel Iris Xe first, and whisper.cpp uses GPU 0 unless told otherwise. On the same files, transcription went from about 0.4 s to **11–13 s**:

| GPU | 6 s of audio | 24 s of audio |
|---|---|---|
| Intel Iris Xe (integrated) | 11.1 s | 13.0 s |
| RTX 3080 Laptop (dedicated) | 0.4 s | 0.6 s |

That is why the app picks the first dedicated GPU by itself (`GpuDevice: -1`) and writes the device in use to the diagnostic log (`using Vulkan1 backend`). A specific GPU can be chosen in the settings.

## Rewriting

The real cost of rewriting through the `claude` CLI is starting it, not the model: the API call itself takes about a second. A `claude -p` launched from a shell for each text took 6–8 s. With the [standby process](how-it-works.md#the-standby-process) already started, only the answer is paid for.

| Model | New process for each dictation | Standby process |
|---|---|---|
| haiku | median 2.5–4.0 s (2.4–5.8 s) | median 1.7–2.5 s (1.3–5.1 s) |
| sonnet | median 4.4 s (2.5–7.8 s) | median 2.0–2.8 s (1.5–5.9 s) |

Numbers vary a lot between runs, depending on the network and the service load. The CLI also ends each turn with an extra request of about 3 s after the answer: the app does not wait for it, takes the text as soon as the `assistant` message arrives and retires the process.

Moving the prompt from the system prompt into the message, so that one standby process serves every prompt, did not change the times: Sonnet with the Correzione (Proofread) prompt stayed at a median of 2.0–2.8 s.

### Quality

With the same prompt, Haiku sometimes rephrased: it dropped "volevo dire che" (I meant to say that) and once lost a "perché" (because), turning the sentence into a question. Sonnet stayed faithful in every case in `samples/trascrizioni.txt`, including a dictated question, which was corrected rather than answered. That is why the default model is Sonnet: rewriting is optional, and a second more is worth less than an altered text.

## Latency log

Every real dictation is appended to `%LOCALAPPDATA%\WhisperAloud\latency.csv`, which can also be opened from the tray menu (**Registro latenze**):

| Column | Meaning |
|---|---|
| `timestamp` | when the dictation finished |
| `mode` | `raw` (Whisper only), `clean` (with a Claude prompt) or `selection` (rewrite selection) |
| `audio_s` | length of the recording in seconds |
| `transcribe_ms` | Whisper time |
| `clean_ms` | Claude time, 0 if not used |
| `paste_ms` | time to paste |
| `total_ms` | total from releasing the shortcut |
| `chars` | length of the final text |
