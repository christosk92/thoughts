using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentGpu.WindowsApi.Credentials;

namespace Thoughts;

sealed record ModelRequest(string Model, string System, string User, string SchemaJson, int MaxTokens, string Effort = "medium");

/// <summary>Json is null when the model declined or stopped early, and Declined says why. Tokens feed the Usage page.</summary>
sealed record ModelReply(string? Json, string? Declined, long InputTokens, long OutputTokens, long CacheReadTokens, long CacheWriteTokens);

enum ModelFailure { NoKey, BadKey, Busy, Network, Other }

sealed class ModelException(ModelFailure kind, string message) : Exception(message)
{
    public ModelFailure Kind { get; } = kind;
}

/// <summary>Where AI answers come from. One implementation per way of reaching Claude, so the chain doesn't care which.</summary>
interface IModelBackend
{
    Task<ModelReply> Complete(ModelRequest request, CancellationToken cancel);
}

/// <summary>The Messages API over plain HTTP. Not the Anthropic C# SDK, because that needs reflection-based JSON, which
/// NativeAOT turns off. This app uses one endpoint, so writing and reading its JSON by hand is a small price.</summary>
sealed class AnthropicBackend : IModelBackend
{
    const string Endpoint = "https://api.anthropic.com/v1/messages";
    const string ApiVersion = "2026-10-21";
    static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    readonly HttpClient _http;

    public AnthropicBackend(string apiKey)
    {
        _http = new HttpClient { Timeout = Timeout };
        _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _http.DefaultRequestHeaders.Add("anthropic-version", ApiVersion);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<ModelReply> Complete(ModelRequest request, CancellationToken cancel)
    {
        using var content = new ByteArrayContent(RequestBody(request));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        HttpResponseMessage response;
        string text;
        try
        {
            response = await _http.PostAsync(Endpoint, content, cancel);
            text = await response.Content.ReadAsStringAsync(cancel);
        }
        catch (HttpRequestException)
        {
            throw new ModelException(ModelFailure.Network, "The AI service can't be reached.");
        }
        catch (TaskCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new ModelException(ModelFailure.Network, "The AI service took too long to answer.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode) throw Failure(response.StatusCode, text);
            return ParseReply(text);
        }
    }

    // JSON writer, not a serializer: nothing here depends on reflection.
    static byte[] RequestBody(ModelRequest request)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("model", request.Model);
            w.WriteNumber("max_tokens", request.MaxTokens);

            // The system prompt is frozen text, so caching it is safe and cheaper when Thoughts come in a burst.
            w.WriteStartArray("system");
            w.WriteStartObject();
            w.WriteString("type", "text");
            w.WriteString("text", request.System);
            w.WriteStartObject("cache_control");
            w.WriteString("type", "ephemeral");
            w.WriteEndObject();
            w.WriteEndObject();
            w.WriteEndArray();

            w.WriteStartObject("output_config");
            w.WriteString("effort", request.Effort);
            w.WriteStartObject("format");
            w.WriteString("type", "json_schema");
            w.WritePropertyName("schema");
            w.WriteRawValue(request.SchemaJson);
            w.WriteEndObject();
            w.WriteEndObject();

            w.WriteStartArray("messages");
            w.WriteStartObject();
            w.WriteString("role", "user");
            w.WriteString("content", request.User);
            w.WriteEndObject();
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return stream.ToArray();
    }

    static ModelReply ParseReply(string text)
    {
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        string? json = null;
        foreach (var block in root.GetProperty("content").EnumerateArray())
            if (block.GetProperty("type").GetString() == "text") json = block.GetProperty("text").GetString();

        string? stop = root.TryGetProperty("stop_reason", out var reason) ? reason.GetString() : null;
        string? declined = stop switch
        {
            "refusal" => "The AI declined this note.",
            "max_tokens" => "The AI's answer was cut off.",
            _ => null,
        };

        var usage = root.GetProperty("usage");
        return new ModelReply(declined is null ? json : null, declined,
            Tokens(usage, "input_tokens"), Tokens(usage, "output_tokens"),
            Tokens(usage, "cache_read_input_tokens"), Tokens(usage, "cache_creation_input_tokens"));
    }

    static long Tokens(JsonElement usage, string name) =>
        usage.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : 0;

    static ModelException Failure(HttpStatusCode status, string body) => status switch
    {
        HttpStatusCode.Unauthorized => new ModelException(ModelFailure.BadKey, "The API key was rejected."),
        HttpStatusCode.Forbidden => new ModelException(ModelFailure.BadKey, "The API key isn't allowed to do this."),
        HttpStatusCode.TooManyRequests => new ModelException(ModelFailure.Busy, "The AI is rate limited for now."),
        HttpStatusCode.RequestTimeout => new ModelException(ModelFailure.Busy, "The AI service timed out."),
        >= HttpStatusCode.InternalServerError => new ModelException(ModelFailure.Busy, "The AI service is having trouble."),
        _ => new ModelException(ModelFailure.Other, ErrorMessage(body) ?? $"The AI service answered {(int)status}."),
    };

    static string? ErrorMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var message)
                ? message.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>The API key lives outside the vault and the repo: Windows Credential Manager in the app, and in debug builds
/// also dotnet user-secrets, which stores it unencrypted and so is only for development.</summary>
static class ApiKey
{
    const string CredentialTarget = "Thoughts/AnthropicApiKey";
    const string SecretName = "Anthropic:ApiKey";
    const string UserSecretsId = "thoughts-app";

    public static string? Find() => FromDevSecrets() ?? FromCredentialManager();

    public static string Describe() =>
        FromDevSecrets() is not null ? "Using a key from dotnet user-secrets."
        : FromCredentialManager() is not null ? "A key is saved in Windows Credential Manager."
        : "No key yet, so the AI is off. Everything else still works.";

    public static void Save(string key) => CredentialStore.Store(CredentialTarget, "thoughts", key.Trim());

    public static void Remove() => CredentialStore.Delete(CredentialTarget);

    static string? FromCredentialManager() =>
        CredentialStore.TryRetrieve(CredentialTarget, out _, out var secret) ? Encoding.UTF8.GetString(secret) : null;

    static string? FromDevSecrets()
    {
#if DEBUG
        string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "UserSecrets", UserSecretsId, "secrets.json");
        if (!File.Exists(file)) return null;
        using var secrets = JsonDocument.Parse(File.ReadAllText(file));
        return secrets.RootElement.TryGetProperty(SecretName, out var key) ? key.GetString() : null;
#else
        return null;
#endif
    }
}
