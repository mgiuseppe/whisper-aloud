using System.Runtime.InteropServices;

namespace Dettatura.App;

/// <summary>
/// Scrive e legge testo nella finestra attiva passando dagli appunti e simulando Ctrl+V / Ctrl+C.
/// Va chiamato dal thread UI (gli appunti richiedono STA).
/// </summary>
internal static class TextInjector
{
    private static readonly TimeSpan ModifierWait = TimeSpan.FromSeconds(3);

    /// <param name="original">Appunti da ripristinare se già salvati prima (riscrittura della selezione).</param>
    public static async Task PasteAsync(string text, bool restoreClipboard, DataObject? original = null)
    {
        await WaitForModifiersReleasedAsync();
        var backup = restoreClipboard ? original ?? SnapshotClipboard() : null;

        SetClipboardText(text);
        Native.SendCtrlChord(Keys.V);

        if (backup is not null)
        {
            // L'applicazione di destinazione legge gli appunti in modo asincrono: diamole tempo prima di ripristinarli.
            await Task.Delay(400);
            RetryClipboard(() => Clipboard.SetDataObject(backup, copy: true));
        }
    }

    /// <summary>
    /// Copia la selezione corrente (testo null se non c'è nulla di selezionato) e restituisce anche
    /// gli appunti di prima, che Ctrl+C sovrascrive, per poterli ripristinare.
    /// </summary>
    public static async Task<(string? Text, DataObject? Original)> CopySelectionAsync()
    {
        await WaitForModifiersReleasedAsync();
        var original = SnapshotClipboard();
        RetryClipboard(Clipboard.Clear);
        Native.SendCtrlChord(Keys.C);

        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(25);
            if (Clipboard.ContainsText()) return (Clipboard.GetText(), original);
        }
        return (null, original);
    }

    public static void Restore(DataObject? original)
    {
        if (original is not null) RetryClipboard(() => Clipboard.SetDataObject(original, copy: true));
    }

    /// <summary>
    /// Aspetta che l'utente rilasci Ctrl/Alt/Shift della scorciatoia,
    /// altrimenti Ctrl+V diventerebbe ad esempio Ctrl+Alt+V.
    /// </summary>
    private static async Task WaitForModifiersReleasedAsync()
    {
        var deadline = DateTime.UtcNow + ModifierWait;
        while (Native.AnyModifierDown() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(15);
        }
    }

    private static DataObject? SnapshotClipboard()
    {
        var current = Clipboard.GetDataObject();
        if (current is null) return null;

        var copy = new DataObject();
        foreach (var format in current.GetFormats(autoConvert: false))
        {
            try
            {
                if (current.GetData(format, autoConvert: false) is { } data) copy.SetData(format, data);
            }
            catch (Exception e) when (e is ExternalException or COMException or OutOfMemoryException)
            {
                // Alcuni formati (es. metafile di Office) non sono copiabili: si salta, il testo resta.
            }
        }
        return copy;
    }

    private static void SetClipboardText(string text) => RetryClipboard(() => Clipboard.SetText(text));

    /// <summary>Gli appunti possono essere bloccati per un attimo da un'altra app: si riprova qualche volta.</summary>
    private static void RetryClipboard(Action action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (ExternalException) when (attempt < 10)
            {
                Thread.Sleep(20);
            }
        }
    }
}
