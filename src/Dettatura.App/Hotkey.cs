namespace Dettatura.App;

/// <summary>Scorciatoia globale nel formato delle impostazioni, es. "Ctrl+Alt+Space".</summary>
internal sealed record Hotkey(uint Modifiers, Keys Key, string Text)
{
    public static Hotkey Parse(string text)
    {
        uint modifiers = 0;
        Keys? key = null;

        foreach (var part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= Native.MOD_CONTROL; break;
                case "alt": modifiers |= Native.MOD_ALT; break;
                case "shift": modifiers |= Native.MOD_SHIFT; break;
                case "win": modifiers |= Native.MOD_WIN; break;
                default:
                    if (key is not null || !Enum.TryParse<Keys>(part, ignoreCase: true, out var parsed))
                    {
                        throw new FormatException($"Scorciatoia non valida: '{text}'.");
                    }
                    key = parsed;
                    break;
            }
        }

        return key is null
            ? throw new FormatException($"Scorciatoia senza tasto principale: '{text}'.")
            : new Hotkey(modifiers, key.Value, text);
    }
}

/// <summary>Finestra invisibile che riceve WM_HOTKEY per le scorciatoie registrate.</summary>
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private readonly Dictionary<int, Action> _handlers = [];
    private int _nextId = 1;

    public HotkeyWindow()
    {
        CreateHandle(new CreateParams());
    }

    public void Register(Hotkey hotkey, Action handler)
    {
        var id = _nextId++;
        if (!Native.RegisterHotKey(Handle, id, hotkey.Modifiers | Native.MOD_NOREPEAT, (uint)hotkey.Key))
        {
            throw new InvalidOperationException(
                $"Impossibile registrare {hotkey.Text}: probabilmente è già usata da un'altra applicazione.");
        }
        _handlers[id] = handler;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY && _handlers.TryGetValue((int)m.WParam, out var handler))
        {
            handler();
            return;
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        foreach (var id in _handlers.Keys) Native.UnregisterHotKey(Handle, id);
        _handlers.Clear();
        DestroyHandle();
    }
}
