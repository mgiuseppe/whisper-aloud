using System.Globalization;

namespace WhisperAloud.Core;

/// <summary>
/// Lingua dell'interfaccia, italiano o inglese. Ogni testo è scritto nelle due lingue nel punto in cui si usa:
/// <c>T("Incollato", "Pasted")</c>. Con "auto" vale la lingua di Windows: italiano se è italiano, altrimenti inglese.
/// </summary>
public static class Loc
{
    public static bool English { get; private set; } = Resolve("auto") == "en";

    /// <summary>"it" o "en" a partire dall'impostazione ("auto", "it", "en").</summary>
    public static string Resolve(string? setting) => setting switch
    {
        "it" or "en" => setting,
        _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "it" ? "it" : "en",
    };

    public static void Use(string? setting) => English = Resolve(setting) == "en";

    public static string T(string italian, string english) => English ? english : italian;
}
