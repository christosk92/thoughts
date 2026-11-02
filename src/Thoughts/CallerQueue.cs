using System.Text.Json.Nodes;

namespace Thoughts;

/// <summary>A Caller's piece of work while it's being done. It stores what the prompt called c1, p1 and so on, because those
/// numbers only mean anything against the list that was sent.</summary>
sealed record CallerClaim(string Title, string Step, string Origin, string Client, DateTimeOffset Until, List<string> Candidates, List<string> Projects, List<string> People, List<CallerCheck> Checks);

/// <summary>A Connect claim that passed the code checks, waiting for a fresh look.</summary>
sealed record CallerCheck(string Id, string Other, string Kind, string Explanation);

/// <summary>The line of work shared between the app and thoughts-mcp, which are separate processes. Thoughts added through MCP wait
/// in their own file, so the app's AI never touches them. A claimed Thought leaves its file for a while, and goes back there if no
/// answer comes.</summary>
static class CallerQueue
{
    public static readonly TimeSpan ClaimLasts = TimeSpan.FromMinutes(10);
    const int LockAttempts = 200;
    const int LockWaitMs = 25;

    public const string AppOrigin = "queue.txt";
    public const string CallerOrigin = "caller-queue.txt";
    const string ClaimsFile = "claims.json";

    /// <summary>Everything that touches these files goes through here, so neither process can lose the other's entry.</summary>
    public static T Locked<T>(string storeDir, Func<T> work)
    {
        Directory.CreateDirectory(storeDir);
        string path = Path.Combine(storeDir, "queue.lock");
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var held = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return work();
            }
            catch (IOException) when (attempt < LockAttempts)
            {
                Thread.Sleep(LockWaitMs);
            }
        }
    }

    public static void Locked(string storeDir, Action work) => Locked(storeDir, () =>
    {
        work();
        return true;
    });

    public static List<string> Lines(string storeDir, string file)
    {
        string path = Path.Combine(storeDir, file);
        return File.Exists(path) ? File.ReadAllLines(path).Where(l => l.Length > 0).ToList() : [];
    }

    static void Write(string storeDir, string file, List<string> lines) =>
        File.WriteAllText(Path.Combine(storeDir, file), lines.Count > 0 ? string.Join('\n', lines) + "\n" : "");

    public static List<string> ForCaller(string storeDir) => Lines(storeDir, CallerOrigin);

    public static void AddForCaller(string storeDir, string title)
    {
        var lines = Lines(storeDir, CallerOrigin);
        if (!lines.Contains(title)) lines.Add(title);
        Write(storeDir, CallerOrigin, lines);
    }

    public static void RemoveFromCaller(string storeDir, string title) => Take(storeDir, CallerOrigin, title);

    /// <summary>Takes one line out of a queue file, and says whether it was there.</summary>
    public static bool Take(string storeDir, string file, string line)
    {
        var lines = Lines(storeDir, file);
        if (!lines.Remove(line)) return false;
        Write(storeDir, file, lines);
        return true;
    }

    public static List<CallerClaim> ReadClaims(string storeDir)
    {
        string path = Path.Combine(storeDir, ClaimsFile);
        if (!File.Exists(path)) return [];
        var claims = new List<CallerClaim>();
        if (JsonNode.Parse(File.ReadAllText(path)) is not JsonArray array) return claims;
        foreach (var node in array)
        {
            if (node is not JsonObject o) continue;
            claims.Add(new CallerClaim((string?)o["title"] ?? "", (string?)o["step"] ?? "", (string?)o["origin"] ?? "", (string?)o["client"] ?? "",
                DateTimeOffset.Parse((string?)o["until"] ?? "2000-01-01T00:00:00Z"), Strings(o["candidates"]), Strings(o["projects"]), Strings(o["people"]),
                (o["checks"] as JsonArray ?? []).OfType<JsonObject>().Select(c => new CallerCheck((string?)c["id"] ?? "", (string?)c["other"] ?? "", (string?)c["kind"] ?? "", (string?)c["explanation"] ?? "")).ToList()));
        }
        return claims;
    }

    static List<string> Strings(JsonNode? node) => (node as JsonArray ?? []).Select(n => (string?)n ?? "").ToList();

    public static void WriteClaims(string storeDir, List<CallerClaim> claims)
    {
        var nodes = new List<JsonNode>();
        foreach (var c in claims)
        {
            nodes.Add(new JsonObject
            {
                ["title"] = c.Title, ["step"] = c.Step, ["origin"] = c.Origin, ["client"] = c.Client, ["until"] = c.Until.ToString("O"),
                ["candidates"] = ToArray(c.Candidates), ["projects"] = ToArray(c.Projects), ["people"] = ToArray(c.People),
                ["checks"] = new JsonArray(c.Checks.Select(k => (JsonNode)new JsonObject { ["id"] = k.Id, ["other"] = k.Other, ["kind"] = k.Kind, ["explanation"] = k.Explanation }).ToArray()),
            });
        }
        File.WriteAllText(Path.Combine(storeDir, ClaimsFile), new JsonArray(nodes.ToArray()).ToJsonString());
    }

    static JsonArray ToArray(List<string> items) => new(items.Select(i => (JsonNode)JsonValue.Create(i)).ToArray());

    /// <summary>A session can die mid-task, and that mustn't hold a Thought forever. Call with the lock held.</summary>
    public static void ReturnExpired(string storeDir)
    {
        if (!File.Exists(Path.Combine(storeDir, ClaimsFile))) return;
        List<CallerClaim> claims;
        try
        {
            claims = ReadClaims(storeDir);
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or FormatException or InvalidOperationException)
        {
            return;
        }
        var now = DateTimeOffset.Now;
        var expired = claims.Where(c => c.Until < now).ToList();
        if (expired.Count == 0) return;
        foreach (var claim in expired)
        {
            claims.Remove(claim);
            // A Connect claim has no line of its own to go back to, because the Thought is already organized.
            if (claim.Step != "organize") continue;
            var lines = Lines(storeDir, claim.Origin);
            if (!lines.Contains(claim.Title)) lines.Add(claim.Title);
            Write(storeDir, claim.Origin, lines);
        }
        WriteClaims(storeDir, claims);
    }
}
