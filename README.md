# Dettatura

Dettatura vocale per Windows in stile Wispr Flow: tieni premuta una scorciatoia, parli, rilasci, e il testo compare nella finestra attiva. La trascrizione è locale; la riscrittura con Claude è opzionale, su una scorciatoia separata.

- **Trascrizione locale** con [Whisper.net](https://github.com/sandrohanea/whisper.net) (whisper.cpp) e il modello `large-v3-turbo` quantizzato, sulla GPU via Vulkan.
- **Riscrittura leggera con Claude**, solo quando la chiedi, tramite la CLI di [Claude Code](https://docs.claude.com/en/docs/claude-code) in modalità headless: usa l'abbonamento Claude già attivo sulla macchina, senza chiavi API da pagare a consumo.

## Come funziona

```
scorciatoia premuta ──► microfono (NAudio, 16 kHz mono)
         rilasciata ──► Whisper large-v3-turbo (GPU, Vulkan)
                    ──► [solo con Shift] claude -p --model sonnet (processo già avviato, stream-json)
                    ──► appunti + Ctrl+V nella finestra attiva (appunti poi ripristinati)
```

| Scorciatoia | Cosa fa |
|---|---|
| `Ctrl+Alt+D` (tieni premuto) | detta e incolla la trascrizione di Whisper |
| `Ctrl+Alt+Shift+D` (tieni premuto) | detta, riscrivi con Claude, incolla |
| `Ctrl+Alt+R` | riscrive con Claude il testo selezionato e lo sostituisce |

In alternativa al push-to-talk c'è la modalità **avvio/stop** (`Mode: Toggle`): una pressione avvia la registrazione, la successiva la termina. È comoda per le dettature lunghe.

Le scorciatoie con `Spazio` sono spesso già prese (Superwhisper, PowerToys): se una scorciatoia è occupata l'app lo segnala e registra comunque le altre.

### La finestrella di stato

In basso al centro dello schermo compare una finestrella sempre in primo piano che non ruba il focus (l'incolla finisce comunque nella finestra in cui stavi scrivendo):

- **In ascolto**: pallino rosso, barra del livello del microfono; se dopo 1,5 s non arriva suono lo dice subito.
- **Trascrizione… / Riscrittura con Claude…**: pallino arancione con i secondi che scorrono, così si vede che sta lavorando.
- **Incollato**: il testo, il tempo totale e il pulsante **Copia**; si chiude da sola dopo 10 s (resta aperta finché il mouse è sopra).
- **Avvisi**: pressione troppo breve, microfono muto, nessuna parola riconosciuta, errori.

L'ultima dettatura si ricopia anche dal menu del tray (**Copia ultima dettatura**). Con `ShowOverlay: false` la finestrella sparisce e gli avvisi tornano nei fumetti del tray.

L'icona nel tray è grigia a riposo, rossa mentre ascolta, arancione mentre lavora.

### La riscrittura

Whisper large-v3-turbo dà già una buona trascrizione italiana con punteggiatura, quindi Claude non è applicato a ogni dettatura. Quando lo chiedi, riceve il testo tra tag `<dettato>` con un prompt da correttore di bozze: interventi minimi (punteggiatura, esitazioni, parole ripetute per sbaglio, autocorrezioni come "alle tre, anzi alle quattro"), stesse parole e stesso ordine, **mai cambiare il senso né aggiungere nulla**; nel dubbio lascia la parola originale. Il testo dettato è trattato come materiale da ripulire, non come istruzioni: se detti una domanda, viene ripulita, non risposta.

La CLI viene avviata con `--tools ""` (nessuno strumento: non può leggere file né eseguire comandi), `--safe-mode` (ignora CLAUDE.md, hook e plugin dell'utente), `--strict-mcp-config`, `--no-session-persistence` e una cartella di lavoro vuota.

## Latenza

Misure su MSI Raider GE76 (RTX 3080 Laptop, 8 GB), 29 ottobre 2026, con `tools/Dettatura.Bench`.

**Trascrizione** (Whisper large-v3-turbo q5_0, Vulkan, mediana di 3 esecuzioni; il modello si carica in ~1,2 s all'avvio dell'app):

| Audio | Tempo |
|---|---|
| 6 s | ~0,41–0,46 s |
| 12 s | ~0,48–0,50 s |
| 24 s | ~0,59–0,60 s |

**Attenzione ai portatili con due GPU.** Vulkan elenca per prima la Intel Iris Xe integrata e whisper.cpp, senza indicazioni, usa la GPU 0: sugli stessi file la trascrizione passava da ~0,4 s a **~11–13 s**. L'app sceglie quindi da sola la prima GPU dedicata (`GpuDevice: -1`) e scrive nel log errori quale usa (`using Vulkan1 backend`).

**Riscrittura con la CLI `claude`**: il costo vero è l'avvio della CLI, non il modello (la chiamata API dura circa 1 s). Una `claude -p` lanciata da shell per ogni testo impiegava 6–8 s; con il processo di riserva già avviato si paga solo la risposta.

| Modello | Processo nuovo a ogni dettatura | Processo di riserva già avviato |
|---|---|---|
| haiku | mediana 2,5–4,0 s (2,4–5,8 s) | mediana 1,7–2,5 s (1,3–5,1 s) |
| sonnet | mediana 4,4 s (2,5–7,8 s) | mediana 4,2 s (2,9–4,6 s) |

I numeri variano parecchio da una prova all'altra (rete, carico del servizio). In più, la CLI finisce il turno con una richiesta accessoria di circa 3 s dopo la risposta: l'app non la aspetta, prende il testo appena arriva il messaggio `assistant` dello stream-json e congeda il processo.

**Qualità**: con lo stesso prompt Haiku a volte riformula (ha tolto "volevo dire che", ha perso un "perché" trasformando la frase in una domanda), Sonnet è rimasto fedele in tutti i casi di `samples/trascrizioni.txt`. Per questo il default è Sonnet: la riscrittura è opzionale, e qualche secondo in più vale meno di un testo alterato.

Totale tipico dal rilascio della scorciatoia: **~0,5 s** solo Whisper, **~4–5 s** con la riscrittura Sonnet (~2–3 s con Haiku). Ogni dettatura reale viene registrata in `%LOCALAPPDATA%\Dettatura\latency.csv`.

### Perché un processo "di riserva"

Un processo persistente con `--input-format stream-json` risponde in 1,5–2 s, ma accumula la conversazione: le dettature successive vedono le precedenti e il modello può mescolarle. L'app quindi usa ogni processo per **una sola** dettatura e ne avvia subito un altro in background, che resta in attesa della successiva (e viene rinnovato ogni 20 minuti).

## Requisiti

- Windows 10/11 x64, [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Claude Code](https://docs.claude.com/en/docs/claude-code) installato e autenticato (`claude` nel PATH o in `%USERPROFILE%\.local\bin`). L'app non gestisce login né credenziali: si appoggia alla CLI già autenticata.
- GPU con driver Vulkan per la trascrizione veloce (senza GPU funziona su CPU, più lentamente). Sui portatili con grafica ibrida serve la GPU dedicata: vedi la nota nella sezione Latenza.

## Uso

```bash
dotnet run -c Release --project src/Dettatura.App
```

Al primo avvio l'app scarica il modello Whisper (~550 MB) in `%LOCALAPPDATA%\Dettatura\models` e crea le impostazioni in `%APPDATA%\Dettatura\settings.json`. Dal menu dell'icona nel tray si aprono impostazioni, registro latenze e log errori.

### Finestra Impostazioni

Doppio clic sull'icona del tray, oppure **Impostazioni...** dal menu. Quattro schede: **Dettatura** (push-to-talk o avvio/stop, scorciatoie da registrare premendole nel campo), **Trascrizione** (microfono, GPU, lingua, modello e contesto di Whisper), **Claude** (modello, processo di riserva, timeout, eseguibile, prompt personalizzato), **Incolla e finestrella**. Le modifiche valgono appena premi Salva: l'app ricarica solo quello che serve (Whisper se cambiano GPU, lingua o modello; il processo `claude` se cambiano le sue opzioni). Mentre la finestra è aperta le scorciatoie sono sospese, così si possono premere nei campi. Il link **Apri settings.json** apre il file per le modifiche a mano.

### Impostazioni principali

| Chiave | Default | Note |
|---|---|---|
| `Mode` | `PushToTalk` | `PushToTalk` (tieni premuto) o `Toggle` (premi per iniziare e per finire) |
| `HotkeyDictate` / `HotkeyDictateWithClaude` / `HotkeyCleanSelection` | vedi sopra | formato `Ctrl+Alt+D`, nomi tasti dell'enum `Keys` (`Space`, `F9`, ...) |
| `Language` | `it` | lingua di Whisper |
| `WhisperModel` | `ggml-large-v3-turbo-q5_0.bin` | nome in `models` o percorso assoluto |
| `WhisperPrompt` | frase in italiano | contesto per Whisper: utile per nomi e termini ricorrenti |
| `MicrophoneDevice` | `-1` | `-1` = microfono predefinito di Windows; si sceglie anche dal menu **Microfono** del tray |
| `GpuDevice` | `-1` | GPU Vulkan per Whisper; `-1` = prima GPU dedicata, altrimenti l'indice come lo elenca Vulkan |
| `ShowOverlay` | `true` | finestrella di stato in primo piano |
| `Claude.Model` | `sonnet` | `haiku` è più veloce ma a volte riformula |
| `Claude.UseWarmProcess` | `true` | tiene un processo `claude` pronto per la dettatura successiva |
| `Claude.SystemPrompt` | vuoto | sostituisce il prompt di riscrittura |
| `AppendSpace` / `RestoreClipboard` | `true` | spazio dopo il testo incollato / appunti ripristinati |

Le modifiche fatte a mano nel file valgono al riavvio dell'app; quelle dalla finestra Impostazioni subito.

## Struttura

| Progetto | Contenuto |
|---|---|
| `src/Dettatura.Core` | trascrizione (`WhisperTranscriber`), riscrittura (`ClaudeCleaner`, protocollo stream-json), impostazioni, registro latenze |
| `src/Dettatura.App` | app WinForms nel tray: `RegisterHotKey`, registrazione NAudio, finestrella di stato, finestra Impostazioni, incolla con `SendInput` |
| `tools/Dettatura.Bench` | benchmark della pipeline su file audio |
| `tests/Dettatura.Core.Tests` | test xUnit di protocollo, prompt e argomenti della CLI |
| `samples` | tre frasi sintetizzate con la voce Elsa di Windows (per i tempi di Whisper) e `trascrizioni.txt` (per qualità e tempi della riscrittura) |

```bash
dotnet test
dotnet run -c Release --project tools/Dettatura.Bench
dotnet run -c Release --project tools/Dettatura.Bench -- --solo-whisper --gpu 1
```

## Limiti noti

- Push-to-talk: il rilascio si rileva controllando il tasto ogni 15 ms, perché `RegisterHotKey` segnala solo la pressione.
- L'incolla passa dagli appunti: gli appunti vengono ripristinati dopo 400 ms, ma i gestori di cronologia degli appunti vedono comunque il testo dettato.
- I campioni audio sono voce sintetica: misurano bene i tempi, non la qualità della trascrizione sulla voce reale.
- Il processo di riserva di `claude` resta avviato anche se non usi la riscrittura (memoria, nessuna richiesta al modello finché non serve).
- Uso personale: ogni dettatura riscritta è una richiesta al modello e conta sui limiti d'uso dell'abbonamento Claude.
