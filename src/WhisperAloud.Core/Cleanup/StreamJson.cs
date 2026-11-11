using System.Text.Json;
using System.Text.Json.Nodes;

namespace WhisperAloud.Core.Cleanup;

/// <summary>
/// Protocollo stream-json della CLI claude (--input-format/--output-format stream-json):
/// un oggetto JSON per riga in ingresso e in uscita.
/// </summary>
public static class StreamJson
{
    public static string UserMessage(string content) => new JsonObject
    {
        ["type"] = "user",
        ["message"] = new JsonObject
        {
            ["role"] = "user",
            ["content"] = content,
        },
    }.ToJsonString();

    /// <summary>Interpreta una riga di output; restituisce null per gli eventi che non interessano.</summary>
    public static StreamEvent? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(line);
        }
        catch (JsonException)
        {
            return null;
        }

        switch (node?["type"]?.GetValue<string>())
        {
            case "assistant":
                var blocks = node["message"]?["content"]?.AsArray();
                var text = blocks is null ? string.Empty : string.Concat(blocks
                    .Where(b => b?["type"]?.GetValue<string>() == "text")
                    .Select(b => b!["text"]?.GetValue<string>() ?? string.Empty));
                // Errori come "non hai fatto l'accesso" arrivano come risposta finta dell'assistente, con il campo error:
                // il testo ("Not logged in · Please run /login") non va mai incollato come se fosse la dettatura.
                if (node["error"] is JsonValue error && error.TryGetValue<string>(out var code))
                {
                    return new AssistantError(code, text);
                }
                return text.Length == 0 ? null : new AssistantText(text);

            case "result":
                var isError = node["is_error"]?.GetValue<bool>() ?? false;
                var subtype = node["subtype"]?.GetValue<string>() ?? string.Empty;
                var result = node["result"]?.GetValue<string>() ?? string.Empty;
                return new TurnResult(result, isError || subtype.StartsWith("error", StringComparison.Ordinal), subtype);

            default:
                return null;
        }
    }
}

public abstract record StreamEvent;

/// <summary>Testo della risposta: arriva prima del result, che la CLI emette solo a fine turno.</summary>
public sealed record AssistantText(string Text) : StreamEvent;

/// <summary>Risposta sintetica della CLI per un errore (es. authentication_failed): non è testo del modello.</summary>
public sealed record AssistantError(string Code, string Text) : StreamEvent;

public sealed record TurnResult(string Result, bool IsError, string Subtype) : StreamEvent;
