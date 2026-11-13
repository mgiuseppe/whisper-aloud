using Microsoft.Win32;

namespace WhisperAloud.App;

/// <summary>
/// Avvio con Windows tramite la chiave Run dell'utente (niente permessi di amministratore). Il percorso si
/// riscrive a ogni avvio, così l'opzione continua a funzionare anche se l'exe viene spostato.
/// </summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WhisperAloud";

    public static void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" {Program.AutostartArgument}");
        else if (key.GetValue(ValueName) is not null) key.DeleteValue(ValueName);
    }
}
