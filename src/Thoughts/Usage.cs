using System.Text.Json;

namespace Thoughts;

/// <summary>One AI call. It holds no note text and no title, so the log is safe to read and share.</summary>
sealed record UsageCall(DateTimeOffset At, string Step, string Model, long Input, long Output, long CacheRead, long CacheWrite, double Cost);

/// <summary>Every AI call, one JSON line each in the vault's <c>.thoughts</c> folder. The cost is fixed when the call
/// happens, so a later price change doesn't rewrite the past.</summary>
sealed class UsageLog(string storeDir)
{
    public const double WarnAt = 0.8;

    readonly object _lock = new();

    string LogFile => Path.Combine(storeDir, "usage.jsonl");

    public Action? Changed { get; set; }

    public void Record(string step, ModelRequest request, ModelReply reply)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("at", DateTimeOffset.Now);
            w.WriteString("step", step);
            w.WriteString("model", request.Model);
            w.WriteNumber("input", reply.InputTokens);
            w.WriteNumber("output", reply.OutputTokens);
            w.WriteNumber("cache_read", reply.CacheReadTokens);
            w.WriteNumber("cache_write", reply.CacheWriteTokens);
            w.WriteNumber("cost", ModelPrices.Cost(request.Model, reply));
            w.WriteEndObject();
        }
        lock (_lock)
        {
            Directory.CreateDirectory(storeDir);
            using var file = new FileStream(LogFile, FileMode.Append, FileAccess.Write, FileShare.Read);
            file.Write(stream.GetBuffer(), 0, (int)stream.Length);
            file.WriteByte((byte)'\n');
            file.Flush(flushToDisk: true);
        }
        Changed?.Invoke();
    }

    /// <summary>This calendar month's calls, oldest first. A line that can't be read is skipped rather than stopping the page.</summary>
    public List<UsageCall> ThisMonth()
    {
        var now = DateTimeOffset.Now;
        var calls = new List<UsageCall>();
        lock (_lock)
        {
            if (!File.Exists(LogFile)) return calls;
            foreach (string line in File.ReadLines(LogFile))
            {
                if (Parse(line) is { } call && call.At.Year == now.Year && call.At.Month == now.Month) calls.Add(call);
            }
        }
        return calls;
    }

    public double SpentThisMonth() => ThisMonth().Sum(c => c.Cost);

    static UsageCall? Parse(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            return new UsageCall(r.GetProperty("at").GetDateTimeOffset(), r.GetProperty("step").GetString() ?? "", r.GetProperty("model").GetString() ?? "",
                r.GetProperty("input").GetInt64(), r.GetProperty("output").GetInt64(),
                r.GetProperty("cache_read").GetInt64(), r.GetProperty("cache_write").GetInt64(), r.GetProperty("cost").GetDouble());
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}

/// <summary>Anthropic's list prices in US dollars per million tokens, as of October 2026. Cache writes are the 5-minute
/// kind, at 1.25 times input.</summary>
static class ModelPrices
{
    public static double Cost(string model, ModelReply reply)
    {
        var (input, output, cacheRead, cacheWrite) = model switch
        {
            "claude-haiku-5-5" => (0.10, 0.50, 0.01, 0.125),
            "claude-sonnet-5-5" => (2.00, 10.00, 0.20, 2.50),
            "claude-fable-5-1" => (10.00, 50.00, 0.25, 12.50),
            _ => (4.00, 20.00, 0.20, 5.00), // Opus 5.5. A model this table doesn't know yet counts as the dearer one, so the Budget errs on the safe side.
        };
        return (reply.InputTokens * input + reply.OutputTokens * output
            + reply.CacheReadTokens * cacheRead + reply.CacheWriteTokens * cacheWrite) / 1_000_000d;
    }
}
