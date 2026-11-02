using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace Thoughts;

enum Status { Inbox, Parked, Next, Doing, Done, Dropped }

/// <summary>What Build on changed, enough to put it all back.</summary>
sealed record BuildOnUndo(string Source, string SourceNote, string? SourceCard, string Target, string Added, string? TargetCard);

enum ConnectionState { Suggested, Accepted, Dismissed }

/// <summary>A note in <c>Connections/</c>. The two Thoughts always come from <c>between</c>, never from the file name.</summary>
/// <summary>What Organize suggested and the person hasn't answered, kept so it's still there after the capture strip closes.</summary>
sealed record PendingSuggestion(string? Project, string? NewProject, string? Why, ThoughtRelation? Relation);

sealed record Connection(string Name, string A, string B, ConnectionState State, string Kind, string Explanation)
{
    public bool Involves(string title) => string.Equals(A, title, StringComparison.OrdinalIgnoreCase) || string.Equals(B, title, StringComparison.OrdinalIgnoreCase);

    public string OtherThan(string title) => string.Equals(A, title, StringComparison.OrdinalIgnoreCase) ? B : A;
}

sealed record Thought(string Title, Status Status, string? Project, string Body, string Created = "", bool KeepLocal = false, bool Organized = false, string From = "", string Reviewed = "")
{
    /// <summary>The Thoughts this one came from. Held as one string so that records still compare by value.</summary>
    public bool CameFrom(string title) => From.Split('\n').Contains(title, StringComparer.OrdinalIgnoreCase);
}

sealed record BrokenLink(string Note, string Target);

/// <summary>What's wrong with the vault's links: links that lead nowhere, and names used twice, which silently take
/// over each other's links (docs/research/2026-11-01-obsidian-links.md).</summary>
sealed record VaultHealth(List<BrokenLink> BrokenLinks, List<string> DuplicateNames)
{
    public int Issues => BrokenLinks.Count + DuplicateNames.Count;
}

/// <summary>A change the vault refused, with a reason meant for the person.</summary>
sealed class VaultException(string message) : Exception(message);

/// <summary>The vault on disk: the one source of truth for Thoughts (ADR 0001).</summary>
sealed partial class Vault(string root)
{
    const int MaxTitleLength = 80;

    public string Root { get; } = root;
    string ThoughtsDir => Path.Combine(Root, "Thoughts");
    string ProjectsDir => Path.Combine(Root, "Projects");
    string PeopleDir => Path.Combine(Root, "People");

    [GeneratedRegex(@"\[\[(?<target>[^\[\]|#^]*)(?<rest>[|#^][^\[\]]*)?\]\]")]
    private static partial Regex Link();

    /// <summary>Every note in Thoughts/. A note whose properties can't be parsed still shows, in Inbox: never hide or rewrite it.</summary>
    public List<Thought> ReadThoughts()
    {
        if (!Directory.Exists(ThoughtsDir)) return [];
        var thoughts = new List<Thought>();
        foreach (var path in Directory.EnumerateFiles(ThoughtsDir, "*.md"))
        {
            // A note can vanish between listing it and reading it, when it's renamed in Obsidian. The watcher reloads again.
            if (!File.Exists(path)) continue;
            var thought = ParseNote(path);
            thoughts.Add(thought with { Organized = File.Exists(CardPath(thought.Title)) });
        }
        // Newest first by when it was captured, not last written, so a card doesn't move while its note is edited.
        return thoughts.OrderByDescending(t => t.Created, StringComparer.Ordinal)
            .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public List<string> ReadProjects() =>
        Directory.Exists(ProjectsDir)
            ? Directory.EnumerateFiles(ProjectsDir, "*.md").Select(p => Path.GetFileNameWithoutExtension(p)!)
                .Order(StringComparer.OrdinalIgnoreCase).ToList()
            : [];

    /// <summary>Saves a new Thought and returns only once it's safely on disk. Never refuses over a name, because
    /// losing a thought is worse: a taken name gets a number.</summary>
    public string Capture(string text, bool keepLocal = false) => Create(text, keepLocal, Status.Inbox, null, null);

    /// <summary>A piece of a fanned-out Thought: its own Thought in Parked, with the Project and Keep-local of the one it came from.</summary>
    public string CreatePiece(Thought parent, string text) => Create(text, parent.KeepLocal, Status.Parked, parent.Project, [parent.Title]);

    string Create(string text, bool keepLocal, Status status, string? project, IReadOnlyList<string>? from)
    {
        text = text.Trim();
        Directory.CreateDirectory(ThoughtsDir);
        string title = CapturedName(text);
        var taken = TakenNames();
        string name = title;
        for (int n = 2; taken.Contains(name); n++) name = $"{title} {n}";

        var note = new StringBuilder()
            .Append("---\n")
            .Append("status: ").Append(status.ToString()).Append('\n')
            .Append(project is null ? "" : $"project: {LinkTo(project)}\n")
            .Append(from is null ? "" : "from:\n" + string.Concat(from.Select(f => $"  - {LinkTo(f)}\n")))
            .Append(keepLocal ? "keep_local: true\n" : "")
            .Append("captured: ").Append(Quote(text)).Append('\n')
            .Append("created: ").Append(DateTime.Now.ToString("yyyy-MM-ddTHH:mm")).Append('\n')
            .Append("---\n")
            .Append(text).Append('\n')
            .ToString();
        WriteDurably(Path.Combine(ThoughtsDir, name + ".md"), note);
        return name;
    }

    /// <summary>The name a Thought gets from its first line, before anyone gives it a better one.</summary>
    public string CapturedName(string text)
    {
        string title = CleanName(ListMarker().Replace(text.Trim().Split('\n')[0], ""));
        return title.Length > 0 ? title : "Thought";
    }

    public List<string> ReadPeople() =>
        Directory.Exists(PeopleDir)
            ? Directory.EnumerateFiles(PeopleDir, "*.md").Select(p => Path.GetFileNameWithoutExtension(p)!)
                .Order(StringComparer.OrdinalIgnoreCase).ToList()
            : [];

    public void SetPerson(string title, string person) => SetProperty(ThoughtPath(title), "person", LinkTo(person));

    /// <summary>Turning Keep-local on also deletes what the AI already derived from the note here.</summary>
    public void SetKeepLocal(string title, bool keepLocal)
    {
        SetProperty(ThoughtPath(title), "keep_local", keepLocal ? "true" : null);
        if (keepLocal)
        {
            File.Delete(CardPath(title));
            ClearSuggestion(title);
        }
    }

    /// <summary>The app's own store. Obsidian ignores dot folders (ADR 0001).</summary>
    public string StoreDir => Path.Combine(Root, ".thoughts");

    string CardPath(string title) => Path.Combine(StoreDir, "cards", title + ".json");

    public void SaveCard(string title, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CardPath(title))!);
        WriteDurably(CardPath(title), json + "\n", overwrite: true);
    }

    /// <summary>The day the person last looked at this Thought, which Review uses.</summary>
    public void SetReviewed(string title, DateTime day) => SetProperty(ThoughtPath(title), "reviewed", day.ToString("yyyy-MM-dd"));

    string SuggestionPath(string title) => Path.Combine(StoreDir, "suggestions", title + ".json");

    public void SaveSuggestion(string title, PendingSuggestion s)
    {
        var json = new JsonObject { ["project"] = s.Project, ["new_project"] = s.NewProject, ["why"] = s.Why };
        if (s.Relation is { } r)
            json["relation"] = new JsonObject { ["more_detail"] = r.MoreDetail, ["target"] = r.Target, ["evidence"] = r.Evidence, ["why"] = r.Why };
        Directory.CreateDirectory(Path.GetDirectoryName(SuggestionPath(title))!);
        WriteDurably(SuggestionPath(title), json.ToJsonString(), overwrite: true);
    }

    public void ClearSuggestion(string title) => File.Delete(SuggestionPath(title));

    /// <summary>A suggestion for a note that no longer exists, or that the file doesn't make sense of, is left out.</summary>
    public List<(string Title, PendingSuggestion Suggestion)> ReadSuggestions()
    {
        string dir = Path.Combine(StoreDir, "suggestions");
        if (!Directory.Exists(dir)) return [];
        var found = new List<(string, PendingSuggestion)>();
        foreach (string path in Directory.EnumerateFiles(dir, "*.json").Order(StringComparer.Ordinal))
        {
            string title = Path.GetFileNameWithoutExtension(path);
            if (!File.Exists(ThoughtPath(title))) continue;
            try
            {
                if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject o) continue;
                ThoughtRelation? relation = o["relation"] is JsonObject r
                    ? new ThoughtRelation((bool?)r["more_detail"] ?? false, (string?)r["target"] ?? "", (string?)r["evidence"] ?? "", (string?)r["why"] ?? "")
                    : null;
                if (relation is not null && !File.Exists(ThoughtPath(relation.Target))) relation = null;
                found.Add((title, new PendingSuggestion((string?)o["project"], (string?)o["new_project"], (string?)o["why"], relation)));
            }
            catch (Exception e) when (e is IOException or System.Text.Json.JsonException or InvalidOperationException)
            {
                // It's only a suggestion. The Thought itself is fine.
            }
        }
        return found;
    }

    public string? ReadCard(string title) => File.Exists(CardPath(title)) ? File.ReadAllText(CardPath(title)) : null;

    /// <summary>Adds a <c>from</c> link and keeps any it already has, as a block list.</summary>
    public void AddFrom(string title, string target)
    {
        string path = ThoughtPath(title);
        if (!File.Exists(ThoughtPath(target))) throw new VaultException($"{target} no longer exists.");
        if (!File.Exists(path)) throw new VaultException("That note no longer exists.");
        AddToList(path, "from", target);
    }

    void AddToList(string path, string key, string target)
    {
        var (properties, _) = SplitNote(ReadNote(path).Replace("\r\n", "\n"));
        var lines = properties.Split('\n');
        int at = Array.FindIndex(lines, l => l.StartsWith(key + ":", StringComparison.Ordinal));
        var items = new List<string>();
        for (int i = at + 1; at >= 0 && i < lines.Length && (lines[i].StartsWith(' ') || lines[i].StartsWith('-')); i++)
            items.Add(lines[i].TrimStart(' ', '-'));
        string link = LinkTo(target);
        if (items.Contains(link)) return;
        items.Add(link);
        SetProperty(path, key, string.Concat(items.Select(item => $"\n  - {item}")));
    }

    string ConnectionsDir => Path.Combine(Root, "Connections");

    string ConnectionPath(string name) => Path.Combine(ConnectionsDir, name + ".md");

    public List<Connection> ReadConnections()
    {
        if (!Directory.Exists(ConnectionsDir)) return [];
        var connections = new List<Connection>();
        foreach (string path in Directory.EnumerateFiles(ConnectionsDir, "*.md").Order(StringComparer.Ordinal))
        {
            if (File.Exists(path) && ParseConnection(path) is { } connection) connections.Add(connection);
        }
        return connections;
    }

    static Connection? ParseConnection(string path)
    {
        var (properties, body) = SplitNote(ReadNote(path).Replace("\r\n", "\n"));
        if (TryParseProperties(properties) is not { } map) return null;
        if (!map.Children.TryGetValue(new YamlScalarNode("between"), out var node) || node is not YamlSequenceNode list) return null;
        var ends = list.Children.OfType<YamlScalarNode>().Select(n => LinkTarget(n.Value)).Where(t => t is not null).ToList();
        if (ends.Count != 2) return null;
        var state = Enum.TryParse<ConnectionState>(Scalar(map, "state"), ignoreCase: true, out var parsed) ? parsed : ConnectionState.Suggested;
        return new Connection(Path.GetFileNameWithoutExtension(path), ends[0]!, ends[1]!, state, Scalar(map, "kind") ?? "", body.Trim());
    }

    /// <summary>A name that means nothing on purpose: a Thought's rename mustn't leave it stale, and it can't collide with a Thought's name.</summary>
    public string AddConnection(string a, string b, string kind, string explanation)
    {
        Directory.CreateDirectory(ConnectionsDir);
        var taken = TakenNames();
        string stamp = "Connection " + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string name = stamp;
        for (int n = 2; taken.Contains(name); n++) name = $"{stamp}-{n}";
        string note = $"---\nbetween:\n  - {LinkTo(a)}\n  - {LinkTo(b)}\nstate: suggested\nkind: {kind}\n---\n{explanation.Trim()}\n";
        WriteDurably(ConnectionPath(name), note);
        return name;
    }

    public void AcceptConnection(Connection c)
    {
        SetProperty(ConnectionPath(c.Name), "state", "accepted");
        if (File.Exists(ThoughtPath(c.A))) AddToList(ThoughtPath(c.A), "connected", c.B);
        if (File.Exists(ThoughtPath(c.B))) AddToList(ThoughtPath(c.B), "connected", c.A);
    }

    /// <summary>The note stays, as the record that this pair was already answered.</summary>
    public void DismissConnection(Connection c) => SetProperty(ConnectionPath(c.Name), "state", "dismissed");

    /// <summary>A new Thought that starts from the claim and comes from both. This is the one body that starts as the AI's
    /// words, because the person asked for it, and it's theirs from here.</summary>
    public string BuildOnConnection(Connection c)
    {
        string name = Create(c.Explanation.Split("\n\n")[0], false, Status.Inbox, null, [c.A, c.B]);
        AcceptConnection(c);
        return name;
    }

    /// <summary>Moves a Thought's words to the end of another Thought's body and removes the first note. The target is
    /// written first, so a failure can't lose the words. What it returns puts everything back.</summary>
    public BuildOnUndo BuildOn(string source, string target)
    {
        string sourcePath = ThoughtPath(source);
        if (!File.Exists(sourcePath)) throw new VaultException($"{source} no longer exists.");
        if (!File.Exists(ThoughtPath(target))) throw new VaultException($"{target} no longer exists.");
        string sourceNote = ReadNote(sourcePath);
        string added = SplitNote(sourceNote.Replace("\r\n", "\n")).Body.Trim();
        var undo = new BuildOnUndo(source, sourceNote, ReadCard(source), target, added, ReadCard(target));
        string body = SplitNote(ReadNote(ThoughtPath(target)).Replace("\r\n", "\n")).Body.TrimEnd();
        SetBody(target, body + "\n\n" + added);
        File.Delete(sourcePath);
        File.Delete(CardPath(source));
        ClearSuggestion(source);
        // A suggestion about a note that no longer exists can't be answered.
        foreach (var c in ReadConnections().Where(c => c.State == ConnectionState.Suggested && c.Involves(source)))
            File.Delete(ConnectionPath(c.Name));
        return undo;
    }

    /// <summary>False means the person has since changed that text in the target, so it stays there for them to
    /// sort out. The first note comes back either way, because its words must never be lost.</summary>
    public bool UndoBuildOn(BuildOnUndo undo)
    {
        RequireFreeName(undo.Source);
        bool removed = false;
        if (File.Exists(ThoughtPath(undo.Target)))
        {
            string body = SplitNote(ReadNote(ThoughtPath(undo.Target)).Replace("\r\n", "\n")).Body.TrimEnd();
            string tail = "\n\n" + undo.Added;
            if (body.EndsWith(tail, StringComparison.Ordinal))
            {
                SetBody(undo.Target, body[..^tail.Length]);
                if (undo.TargetCard is { } targetCard) WriteDurably(CardPath(undo.Target), targetCard, overwrite: true);
                else File.Delete(CardPath(undo.Target));
                removed = true;
            }
        }
        WriteDurably(ThoughtPath(undo.Source), undo.SourceNote, overwrite: false);
        if (undo.SourceCard is { } sourceCard) WriteDurably(CardPath(undo.Source), sourceCard, overwrite: true);
        return removed;
    }

    public void SetStatus(string title, Status status) => SetProperty(ThoughtPath(title), "status", status.ToString());

    public void SetProject(string title, string? project) =>
        SetProperty(ThoughtPath(title), "project", project is null ? null : LinkTo(project));

    /// <summary>The person's words, replacing the body. The properties stay exactly as they are, even if they don't parse.</summary>
    public void SetBody(string title, string body)
    {
        string path = ThoughtPath(title);
        if (!File.Exists(path)) throw new VaultException("That note no longer exists.");
        string content = ReadNote(path).Replace("\r\n", "\n");
        var (_, oldBody) = SplitNote(content);
        string properties = content[..(content.Length - oldBody.Length)];
        WriteDurably(path, properties + body.Replace("\r\n", "\n").TrimEnd() + "\n", overwrite: true);
    }

    public string RenameThought(string oldName, string newName) => RenameNote(ThoughtsDir, oldName, newName);

    public void AddProject(string name)
    {
        name = RequireFreeName(name);
        Directory.CreateDirectory(ProjectsDir);
        WriteDurably(Path.Combine(ProjectsDir, name + ".md"), "");
    }

    public void RenameProject(string oldName, string newName) => RenameNote(ProjectsDir, oldName, newName);

    /// <summary>Thoughts go to Unsorted. The note moves to Obsidian's own .trash folder, so it can be restored.</summary>
    public void RemoveProject(string name)
    {
        foreach (var thought in ReadThoughts().Where(t => t.Project == name))
            SetProject(thought.Title, null);
        string trash = Path.Combine(Root, ".trash");
        Directory.CreateDirectory(trash);
        string target = Path.Combine(trash, name + ".md");
        for (int n = 2; File.Exists(target); n++) target = Path.Combine(trash, $"{name} {n}.md");
        File.Move(Path.Combine(ProjectsDir, name + ".md"), target);
    }

    /// <summary>Links that lead nowhere and names used twice. Code blocks aren't links, so they don't count.</summary>
    public VaultHealth CheckHealth()
    {
        var notes = Notes().ToList();
        var names = new HashSet<string>(notes.Select(p => Path.GetFileNameWithoutExtension(p)!), StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(AllFiles().Select(p => Path.GetFileName(p)!), StringComparer.OrdinalIgnoreCase);

        var broken = new List<BrokenLink>();
        foreach (var path in notes)
        {
            string note = Path.GetFileNameWithoutExtension(path)!;
            foreach (var line in OutsideCode(ReadNote(path)))
                foreach (Match link in Link().Matches(line))
                {
                    string target = link.Groups["target"].Value.Trim();
                    if (target.Length == 0) continue; // [[#heading]] points inside this note
                    string last = target[(target.LastIndexOf('/') + 1)..];
                    if (last.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) last = last[..^3];
                    if (!names.Contains(last) && !files.Contains(last)) broken.Add(new BrokenLink(note, target));
                }
        }

        var duplicates = notes.GroupBy(p => Path.GetFileNameWithoutExtension(p)!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).Order(StringComparer.OrdinalIgnoreCase).ToList();
        return new VaultHealth(broken, duplicates);
    }

    /// <summary>What this vault needs from Obsidian: renames that always update links, and a graph that shows suggested
    /// Connections as coloured dots and accepted ones as plain lines. Written into this vault's own .obsidian folder, and
    /// only what's missing, so the person's own settings stay. A file that isn't valid JSON is left alone.</summary>
    public void EnsureObsidianSettings()
    {
        string dir = Path.Combine(Root, ".obsidian");
        Directory.CreateDirectory(dir);
        MergeJson(Path.Combine(dir, "app.json"), o => o["alwaysUpdateLinks"] = true);
        MergeJson(Path.Combine(dir, "graph.json"), o =>
        {
            if (string.IsNullOrEmpty((string?)o["search"])) o["search"] = "-[state:accepted] -[state:dismissed]";
            if (o["colorGroups"] is not JsonArray { Count: > 0 })
                o["colorGroups"] = new JsonArray(
                    ColorGroup("[state:suggested]", 0x60CDFF),
                    ColorGroup("[\"status\":Doing]", 0xFB923C),
                    ColorGroup("[\"status\":Next]", 0xA78BFA));
        });
    }

    static JsonObject ColorGroup(string query, int rgb) => new()
    {
        ["query"] = query,
        ["color"] = new JsonObject { ["a"] = 1, ["rgb"] = rgb },
    };

    static void MergeJson(string path, Action<JsonObject> merge)
    {
        JsonObject settings = [];
        if (File.Exists(path))
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject existing) return;
                settings = existing;
            }
            catch (JsonException)
            {
                return;
            }
        }
        string before = settings.ToJsonString();
        merge(settings);
        if (settings.ToJsonString() == before && File.Exists(path)) return;
        WriteDurably(path, settings.ToJsonString(s_indented) + "\n", overwrite: true);
    }

    // Relaxed, so a query in graph.json reads as ["status":Doing] when someone opens the file.
    static readonly JsonSerializerOptions s_indented = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    string RenameNote(string dir, string oldName, string newName)
    {
        string clean = CleanName(newName);
        if (clean == oldName) return oldName;
        // Changing only the case keeps the same name as far as the file system and links are concerned.
        if (!string.Equals(clean, oldName, StringComparison.OrdinalIgnoreCase)) RequireFreeName(clean);
        if (clean.Length == 0) throw new VaultException("A note needs a name.");
        string from = Path.Combine(dir, oldName + ".md");
        if (!File.Exists(from)) throw new VaultException("That note no longer exists.");
        File.Move(from, Path.Combine(dir, clean + ".md"));
        RewriteLinks(oldName, clean);
        if (File.Exists(CardPath(oldName))) File.Move(CardPath(oldName), CardPath(clean), overwrite: true);
        if (File.Exists(SuggestionPath(oldName))) File.Move(SuggestionPath(oldName), SuggestionPath(clean), overwrite: true);
        return clean;
    }

    // A rename made here is invisible to Obsidian, so every link in the vault is this method's job: links in properties,
    // links inside longer text, [[Folder/Name]], [[Name|shown]] and [[Name#heading]]. Code blocks stay as they are.
    void RewriteLinks(string oldName, string newName)
    {
        foreach (var path in Notes().ToList())
        {
            string content = ReadNote(path);
            var rewritten = new StringBuilder();
            bool inCode = false;
            foreach (var line in content.Split('\n'))
            {
                if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) inCode = !inCode;
                rewritten.Append(inCode ? line : Link().Replace(line, m =>
                {
                    string target = m.Groups["target"].Value;
                    int slash = target.LastIndexOf('/');
                    if (!string.Equals(target[(slash + 1)..].Trim(), oldName, StringComparison.OrdinalIgnoreCase)) return m.Value;
                    return $"[[{target[..(slash + 1)]}{newName}{m.Groups["rest"].Value}]]";
                })).Append('\n');
            }
            rewritten.Length--; // Split gives one more line than there were line breaks.
            if (rewritten.ToString() != content) WriteDurably(path, rewritten.ToString(), overwrite: true);
        }
    }

    // Notes in the vault's own folders. Dot folders hold settings, trash and this app's store, so they aren't notes.
    IEnumerable<string> Notes() => AllFiles().Where(p => p.EndsWith(".md", StringComparison.OrdinalIgnoreCase));

    IEnumerable<string> AllFiles()
    {
        if (!Directory.Exists(Root)) return [];
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = true };
        string prefix = Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(Root, "*", options)
            .Where(p => !Path.GetFullPath(p)[prefix.Length..].Split(Path.DirectorySeparatorChar).Any(part => part.StartsWith('.')));
    }

    HashSet<string> TakenNames() =>
        new(Notes().Select(p => Path.GetFileNameWithoutExtension(p)!), StringComparer.OrdinalIgnoreCase);

    string RequireFreeName(string name)
    {
        string clean = CleanName(name);
        if (clean.Length == 0) throw new VaultException("A note needs a name.");
        if (TakenNames().Contains(clean)) throw new VaultException($"There's already a note called {clean}.");
        return clean;
    }

    static IEnumerable<string> OutsideCode(string content)
    {
        bool inCode = false;
        foreach (var line in content.Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) inCode = !inCode;
            else if (!inCode) yield return line;
        }
    }

    public string ThoughtPath(string title) => Path.Combine(ThoughtsDir, title + ".md");

    static string LinkTo(string name) => Quote($"[[{name}]]");

    static Thought ParseNote(string path)
    {
        string title = Path.GetFileNameWithoutExtension(path);
        var (properties, body) = SplitNote(ReadNote(path).Replace("\r\n", "\n"));
        var status = Status.Inbox;
        string? project = null;
        string created = "";
        bool keepLocal = false;
        string from = "", reviewed = "";
        if (TryParseProperties(properties) is { } map)
        {
            from = FromLinks(map);
            reviewed = Scalar(map, "reviewed") ?? "";
            if (Scalar(map, "status") is { } value && Enum.TryParse<Status>(value, ignoreCase: true, out var parsed))
                status = parsed;
            project = LinkTarget(Scalar(map, "project"));
            created = Scalar(map, "created") ?? "";
            keepLocal = string.Equals(Scalar(map, "keep_local"), "true", StringComparison.OrdinalIgnoreCase);
        }
        // Properties that can't be read could be hiding a Keep-local flag, so that note stays off the AI too.
        else if (properties.Length > 0)
        {
            keepLocal = true;
        }
        return new Thought(title, status, project, body.Trim(), created, keepLocal, false, from, reviewed);
    }

    // Obsidian or a sync tool may be writing the same file, so share it, and wait out a moment's lock.
    static string ReadNote(string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(file, new UTF8Encoding(false));
                return reader.ReadToEnd();
            }
            catch (FileNotFoundException)
            {
                return "";
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(30);
            }
        }
    }

    // Returns null for properties that don't parse: they're the person's to fix, so show the note and leave the file alone.
    static YamlMappingNode? TryParseProperties(string properties)
    {
        if (properties.Length == 0) return null;
        try
        {
            var yaml = new YamlStream();
            yaml.Load(new StringReader(properties));
            return yaml.Documents.Count > 0 ? yaml.Documents[0].RootNode as YamlMappingNode : null;
        }
        catch (YamlDotNet.Core.YamlException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"^\s*(#{1,6}\s+|[-*+]\s+(\[[ xX]\]\s+)?|\d+[.)]\s+|>\s+)")]
    private static partial Regex ListMarker();

    static string FromLinks(YamlMappingNode map)
    {
        if (!map.Children.TryGetValue(new YamlScalarNode("from"), out var node)) return "";
        var values = node is YamlSequenceNode list ? list.Children.OfType<YamlScalarNode>().Select(n => n.Value) : [(node as YamlScalarNode)?.Value];
        return string.Join('\n', values.Select(LinkTarget).Where(v => v is not null));
    }

    static string? Scalar(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlScalarNode { Value: { } value } ? value : null;

    // "[[Atlas]]", "[[Projects/Atlas]]" and "[[Atlas|shown]]" all point at Atlas.
    static string? LinkTarget(string? value)
    {
        if (value is null) return null;
        var link = Link().Match(value);
        if (!link.Success || link.Length != value.Length) return null;
        string target = link.Groups["target"].Value;
        target = target[(target.LastIndexOf('/') + 1)..].Trim();
        return target.Length > 0 ? target : null;
    }

    /// <summary>Changes one top-level property and leaves everything else in the note exactly as it is. Null removes it.
    /// The result is parsed before it's written and compared after, so a bad write can't pass as saved.</summary>
    static void SetProperty(string path, string key, string? value)
    {
        if (!File.Exists(path)) throw new VaultException("That note no longer exists.");
        var (properties, body) = SplitNote(ReadNote(path).Replace("\r\n", "\n"));
        string name = Path.GetFileNameWithoutExtension(path);
        if (properties.Length > 0 && TryParseProperties(properties) is null)
            throw new VaultException($"The properties of {name} can't be read. Fix them in Obsidian first.");

        var lines = properties.Length > 0 ? properties.Split('\n').ToList() : [];
        int at = lines.FindIndex(l => l.StartsWith(key + ":", StringComparison.Ordinal));
        if (at >= 0)
        {
            // A list value continues on indented lines, which go with it.
            int end = at + 1;
            while (end < lines.Count && (lines[end].StartsWith(' ') || lines[end].StartsWith('-'))) end++;
            lines.RemoveRange(at, end - at);
        }
        if (value is not null) lines.Insert(at >= 0 ? at : lines.Count, value.StartsWith('\n') ? key + ":" + value : $"{key}: {value}");

        string changed = string.Join('\n', lines);
        if (changed.Length > 0 && TryParseProperties(changed) is null)
            throw new VaultException($"Changing {key} would break the properties of {name}.");

        string note = "---\n" + (changed.Length > 0 ? changed + "\n" : "") + "---\n" + body;
        WriteDurably(path, note, overwrite: true);
    }

    static (string Properties, string Body) SplitNote(string content)
    {
        if (!content.StartsWith("---\n", StringComparison.Ordinal)) return ("", content);
        int end = content.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0) return ("", content);
        int bodyStart = content.IndexOf('\n', end + 1);
        return (content[4..Math.Max(4, end)], bodyStart < 0 ? "" : content[(bodyStart + 1)..]);
    }

    // A name becomes the file name and the link target, so it avoids what breaks either: path characters, and
    // # ^ [ ] | which end or corrupt an Obsidian link (docs/research/2026-11-01-obsidian-links.md).
    static string CleanName(string text)
    {
        var name = new StringBuilder();
        foreach (char c in text)
            name.Append(Path.GetInvalidFileNameChars().Contains(c) || "#^[]|".Contains(c) ? ' ' : c);
        string clean = string.Join(' ', name.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
        return clean.Length > MaxTitleLength ? clean[..MaxTitleLength].TrimEnd() : clean;
    }

    static string Quote(string value)
    {
        var quoted = new StringBuilder("\"");
        foreach (char c in value)
            quoted.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => c.ToString(),
            });
        return quoted.Append('"').ToString();
    }

    // Through a temporary file in the same folder, so a crash never leaves half a note, and flushed to disk, because
    // "Saved" has to be true. The name isn't .md, so Obsidian never sees it. Read back, because a sync tool or another
    // writer could have got in the way.
    static void WriteDurably(string path, string content, bool overwrite = false)
    {
        string temp = path + ".tmp";
        byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content);
        using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            file.Write(bytes);
            file.Flush(flushToDisk: true);
        }
        File.Move(temp, path, overwrite);
        if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
            throw new VaultException($"{Path.GetFileName(path)} changed while saving. Check it in Obsidian.");
    }
}
