using System.Text;
using YamlDotNet.RepresentationModel;

namespace Thoughts;

enum Status { Inbox, Parked, Next, Doing, Done, Dropped }

sealed record Thought(string Title, Status Status, string? Project, string Body);

/// <summary>A change the vault refused, with a reason meant for the person.</summary>
sealed class VaultException(string message) : Exception(message);

/// <summary>The vault on disk: the one source of truth for Thoughts (ADR 0001).</summary>
sealed class Vault(string root)
{
    const int MaxTitleLength = 80;

    public string Root { get; } = root;
    string ThoughtsDir => Path.Combine(Root, "Thoughts");
    string ProjectsDir => Path.Combine(Root, "Projects");

    /// <summary>Every note in Thoughts/. A note whose properties can't be parsed still shows, in Inbox: never hide or rewrite it.</summary>
    public List<Thought> ReadThoughts()
    {
        if (!Directory.Exists(ThoughtsDir)) return [];
        var thoughts = new List<(DateTime Written, Thought Thought)>();
        foreach (var path in Directory.EnumerateFiles(ThoughtsDir, "*.md"))
            thoughts.Add((File.GetLastWriteTimeUtc(path), ParseNote(path)));
        return thoughts.OrderByDescending(t => t.Written).Select(t => t.Thought).ToList();
    }

    public List<string> ReadProjects() =>
        Directory.Exists(ProjectsDir)
            ? Directory.EnumerateFiles(ProjectsDir, "*.md").Select(p => Path.GetFileNameWithoutExtension(p)!)
                .Order(StringComparer.OrdinalIgnoreCase).ToList()
            : [];

    /// <summary>Saves a new Thought and returns only once it's safely on disk.</summary>
    public Thought Capture(string text)
    {
        text = text.Trim();
        Directory.CreateDirectory(ThoughtsDir);
        string title = CleanName(text.Split('\n')[0]);
        if (title.Length == 0) title = "Thought";
        string path = UniquePath(ThoughtsDir, title);

        var note = new StringBuilder()
            .Append("---\n")
            .Append("status: ").Append(nameof(Status.Inbox)).Append('\n')
            .Append("captured: ").Append(Quote(text)).Append('\n')
            .Append("created: ").Append(DateTime.Now.ToString("yyyy-MM-ddTHH:mm")).Append('\n')
            .Append("---\n")
            .Append(text).Append('\n')
            .ToString();
        WriteDurably(path, note);
        return new Thought(Path.GetFileNameWithoutExtension(path), Status.Inbox, null, text);
    }

    public void SetStatus(string title, Status status) => SetProperty(ThoughtPath(title), "status", status.ToString());

    public void SetProject(string title, string? project) =>
        SetProperty(ThoughtPath(title), "project", project is null ? null : LinkTo(project));

    public void AddProject(string name)
    {
        name = RequireName(name);
        Directory.CreateDirectory(ProjectsDir);
        string path = Path.Combine(ProjectsDir, name + ".md");
        if (ProjectExists(name)) throw new VaultException($"There's already a Project called {name}.");
        WriteDurably(path, "");
    }

    /// <summary>Renames the note and rewrites every Thought's link to it, because a rename here is invisible to Obsidian.</summary>
    public void RenameProject(string oldName, string newName)
    {
        newName = RequireName(newName);
        if (newName == oldName) return;
        if (!string.Equals(newName, oldName, StringComparison.OrdinalIgnoreCase) && ProjectExists(newName))
            throw new VaultException($"There's already a Project called {newName}.");
        File.Move(Path.Combine(ProjectsDir, oldName + ".md"), Path.Combine(ProjectsDir, newName + ".md"));
        foreach (var thought in ReadThoughts().Where(t => t.Project == oldName))
            SetProject(thought.Title, newName);
    }

    /// <summary>Thoughts go to Unsorted. The note moves to Obsidian's own .trash folder, so it can be restored.</summary>
    public void RemoveProject(string name)
    {
        foreach (var thought in ReadThoughts().Where(t => t.Project == name))
            SetProject(thought.Title, null);
        string trash = Path.Combine(Root, ".trash");
        Directory.CreateDirectory(trash);
        File.Move(Path.Combine(ProjectsDir, name + ".md"), UniquePath(trash, name));
    }

    bool ProjectExists(string name) => ReadProjects().Contains(name, StringComparer.OrdinalIgnoreCase);

    string ThoughtPath(string title) => Path.Combine(ThoughtsDir, title + ".md");

    static string LinkTo(string name) => Quote($"[[{name}]]");

    static string RequireName(string name)
    {
        string clean = CleanName(name);
        if (clean.Length == 0) throw new VaultException("A Project needs a name.");
        return clean;
    }

    static Thought ParseNote(string path)
    {
        string title = Path.GetFileNameWithoutExtension(path);
        var (properties, body) = SplitNote(File.ReadAllText(path).Replace("\r\n", "\n"));
        var status = Status.Inbox;
        string? project = null;
        if (TryParseProperties(properties) is { } map)
        {
            if (Scalar(map, "status") is { } value && Enum.TryParse<Status>(value, ignoreCase: true, out var parsed))
                status = parsed;
            project = LinkTarget(Scalar(map, "project"));
        }
        return new Thought(title, status, project, body.Trim());
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

    static string? Scalar(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlScalarNode { Value: { } value } ? value : null;

    // "[[Atlas]]", "[[Projects/Atlas]]" and "[[Atlas|shown]]" all point at Atlas.
    static string? LinkTarget(string? value)
    {
        if (value is null || !value.StartsWith("[[", StringComparison.Ordinal) || !value.EndsWith("]]", StringComparison.Ordinal)) return null;
        string target = value[2..^2].Split('|')[0];
        target = target[(target.LastIndexOf('/') + 1)..].Trim();
        return target.Length > 0 ? target : null;
    }

    /// <summary>Changes one top-level property and leaves everything else in the note exactly as it is. Null removes it.</summary>
    static void SetProperty(string path, string key, string? value)
    {
        if (!File.Exists(path)) throw new VaultException("That note no longer exists.");
        string content = File.ReadAllText(path).Replace("\r\n", "\n");
        var (properties, body) = SplitNote(content);
        if (properties.Length > 0 && TryParseProperties(properties) is null)
            throw new VaultException($"The properties of {Path.GetFileNameWithoutExtension(path)} can't be read. Fix them in Obsidian first.");

        var lines = properties.Length > 0 ? properties.Split('\n').ToList() : [];
        int at = lines.FindIndex(l => l.StartsWith(key + ":", StringComparison.Ordinal));
        if (at >= 0)
        {
            // A list value continues on indented lines, which go with it.
            int end = at + 1;
            while (end < lines.Count && (lines[end].StartsWith(' ') || lines[end].StartsWith('-'))) end++;
            lines.RemoveRange(at, end - at);
        }
        if (value is not null) lines.Insert(at >= 0 ? at : lines.Count, $"{key}: {value}");

        var note = new StringBuilder("---\n");
        foreach (var line in lines) note.Append(line).Append('\n');
        note.Append("---\n").Append(body);
        WriteDurably(path, note.ToString(), overwrite: true);
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

    static string UniquePath(string dir, string name)
    {
        string path = Path.Combine(dir, name + ".md");
        for (int n = 2; File.Exists(path); n++)
            path = Path.Combine(dir, $"{name} {n}.md");
        return path;
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
    // "Saved" has to be true. The name isn't .md, so Obsidian never sees it.
    static void WriteDurably(string path, string content, bool overwrite = false)
    {
        string temp = path + ".tmp";
        using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            file.Write(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content));
            file.Flush(flushToDisk: true);
        }
        File.Move(temp, path, overwrite);
    }
}
