using System.Text;
using System.Text.Json;

namespace Thoughts;

/// <summary>Why a tool can't do what it was asked. The Caller is told, and can put it right.</summary>
sealed class ToolRefused(string message) : Exception(message);

/// <summary>The protocol half of thoughts-mcp: JSON-RPC by hand, with a JSON writer, which keeps it dependency-free and NativeAOT clean.
/// Tools are in <see cref="CallerTools"/>.</summary>
sealed class McpServer(Vault vault, AppSettings settings)
{
    const string ServerVersion = "1.0.0";
    const string DefaultProtocol = "2025-06-18";

    string _client = "unknown client";
    CallerTools Tools => new(vault, settings, _client);

    /// <summary>The reply to send, or null for a notification, which gets none.</summary>
    public string? Handle(string line)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return Error(null, -32700, "That wasn't JSON.");
        }
        using (doc)
        {
            var root = doc.RootElement;
            JsonElement? id = root.TryGetProperty("id", out var idValue) ? idValue.Clone() : null;
            string method = root.TryGetProperty("method", out var m) ? m.GetString() ?? "" : "";
            var parameters = root.TryGetProperty("params", out var p) ? p : default;
            if (id is null) return null;
            try
            {
                return method switch
                {
                    "initialize" => Initialize(id, parameters),
                    "ping" => Result(id, w => { w.WriteStartObject(); w.WriteEndObject(); }),
                    "tools/list" => Result(id, w => Compact.Write(w, CallerTools.ToolList)),
                    "tools/call" => CallTool(id, parameters),
                    "prompts/list" => Result(id, w => Compact.Write(w, CallerTools.PromptList)),
                    "prompts/get" => Result(id, w => Compact.Write(w, CallerTools.OrganizePrompt())),
                    "resources/list" => Result(id, w => Compact.Write(w, """{"resources":[]}""")),
                    _ => Error(id, -32601, $"No method called {method}."),
                };
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
            {
                return Error(id, -32602, e.Message);
            }
        }
    }

    string Initialize(JsonElement? id, JsonElement parameters)
    {
        string protocol = DefaultProtocol;
        if (parameters.ValueKind == JsonValueKind.Object)
        {
            if (parameters.TryGetProperty("protocolVersion", out var asked) && asked.GetString() is { Length: > 0 } version) protocol = version;
            if (parameters.TryGetProperty("clientInfo", out var info) && info.TryGetProperty("name", out var name) && name.GetString() is { Length: > 0 } client) _client = client;
        }
        return Result(id, w =>
        {
            w.WriteStartObject();
            w.WriteString("protocolVersion", protocol);
            w.WriteStartObject("capabilities");
            w.WriteStartObject("tools");
            w.WriteEndObject();
            w.WriteStartObject("prompts");
            w.WriteEndObject();
            w.WriteEndObject();
            w.WriteStartObject("serverInfo");
            w.WriteString("name", "thoughts-mcp");
            w.WriteString("version", ServerVersion);
            w.WriteEndObject();
            w.WriteString("instructions", CallerTools.ServerInstructions);
            w.WriteEndObject();
        });
    }

    string CallTool(JsonElement? id, JsonElement parameters)
    {
        string name = parameters.GetProperty("name").GetString() ?? "";
        var arguments = parameters.TryGetProperty("arguments", out var a) ? a : default;
        string text;
        bool failed = false;
        try
        {
            text = Tools.Call(name, arguments);
        }
        catch (Exception e) when (e is ToolRefused or VaultException or IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            text = e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException ? "That answer doesn't match the schema you were given. Nothing was saved." : e.Message;
            failed = true;
        }
        return Result(id, w =>
        {
            w.WriteStartObject();
            w.WriteStartArray("content");
            w.WriteStartObject();
            w.WriteString("type", "text");
            w.WriteString("text", text);
            w.WriteEndObject();
            w.WriteEndArray();
            w.WriteBoolean("isError", failed);
            w.WriteEndObject();
        });
    }

    static string Result(JsonElement? id, Action<Utf8JsonWriter> write) => Message(id, w =>
    {
        w.WritePropertyName("result");
        write(w);
    });

    static string Error(JsonElement? id, int code, string message) => Message(id, w =>
    {
        w.WriteStartObject("error");
        w.WriteNumber("code", code);
        w.WriteString("message", message);
        w.WriteEndObject();
    });

    static string Message(JsonElement? id, Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("jsonrpc", "2.0");
            w.WritePropertyName("id");
            if (id is { } value) value.WriteTo(w);
            else w.WriteNullValue();
            write(w);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}

/// <summary>The protocol is one message per line, and these constants are written over several.</summary>
static class Compact
{
    public static void Write(Utf8JsonWriter w, string json)
    {
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.WriteTo(w);
    }
}
