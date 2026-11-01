using System.Text;
using YamlDotNet.RepresentationModel;

namespace Thoughts;

enum Status { Inbox, Parked, Next, Doing, Done, Dropped }

sealed record Thought(string Title, Status Status, string Body);

/// <summary>The vault on disk: the one source of truth for Thoughts (ADR 0001).</summary>
sealed class Vault(string root)
{
    const int MaxTitleLength = 80;

    public string Root { get; } = root;
    string ThoughtsDir => Path.Combine(Root, "Thoughts");

    /// <summary>Every note in Thoughts/. A note whose properties can't be parsed still shows, in Inbox: never hide or rewrite it.</summary>
    public List<Thought> ReadThoughts()
    {
        if (!Directory.Exists(ThoughtsDir)) return [];
        var thoughts = new List<(DateTime Written, Thought Thought)>();
        foreach (var path in Directory.EnumerateFiles(ThoughtsDir, "*.md"))
            thoughts.Add((File.GetLastWriteTimeUtc(path), ParseNote(path)));
        return thoughts.OrderByDescending(t => t.Written).Select(t => t.Thought).ToList();
    }

    /// <summary>Saves a new Thought and returns only once it's safely on disk.</summary>
    public Thought Capture(string text)
    {
        text = text.Trim();
        Directory.CreateDirectory(ThoughtsDir);
        string title = TitleFrom(text);
        string path = UniquePath(title);

        var note = new StringBuilder()
            .Append("---\n")
            .Append("status: ").Append(nameof(Status.Inbox)).Append('\n')
            .Append("captured: ").Append(Quote(text)).Append('\n')
            .Append("created: ").Append(DateTime.Now.ToString("yyyy-MM-ddTHH:mm")).Append('\n')
            .Append("---\n")
            .Append(text).Append('\n')
            .ToString();
        WriteDurably(path, note);
        return new Thought(Path.GetFileNameWithoutExtension(path), Status.Inbox, text);
    }

    static Thought ParseNote(string path)
    {
        string title = Path.GetFileNameWithoutExtension(path);
        string content = File.ReadAllText(path).Replace("\r\n", "\n");
        var (properties, body) = SplitNote(content);

        var status = Status.Inbox;
        if (properties.Length > 0)
        {
            try
            {
                var yaml = new YamlStream();
                yaml.Load(new StringReader(properties));
                if (yaml.Documents.Count > 0
                    && yaml.Documents[0].RootNode is YamlMappingNode map
                    && map.Children.TryGetValue(new YamlScalarNode("status"), out var node)
                    && node is YamlScalarNode { Value: { } value }
                    && Enum.TryParse<Status>(value, ignoreCase: true, out var parsed))
                    status = parsed;
            }
            catch (YamlDotNet.Core.YamlException)
            {
                // Someone's hand-edited properties are theirs to fix: show the note in Inbox and leave the file alone.
            }
        }
        return new Thought(title, status, body.Trim());
    }

    static (string Properties, string Body) SplitNote(string content)
    {
        if (!content.StartsWith("---\n", StringComparison.Ordinal)) return ("", content);
        int end = content.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0) return ("", content);
        int bodyStart = content.IndexOf('\n', end + 1);
        return (content[4..Math.Max(4, end)], bodyStart < 0 ? "" : content[(bodyStart + 1)..]);
    }

    // The title becomes the file name and the link target, so it avoids what breaks either: path characters, and
    // # ^ [ ] | which end or corrupt an Obsidian link (docs/research/2026-11-01-obsidian-links.md).
    static string TitleFrom(string text)
    {
        var title = new StringBuilder();
        foreach (char c in text.Split('\n')[0])
            title.Append(Path.GetInvalidFileNameChars().Contains(c) || "#^[]|".Contains(c) ? ' ' : c);
        string clean = string.Join(' ', title.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
        if (clean.Length > MaxTitleLength) clean = clean[..MaxTitleLength].TrimEnd();
        return clean.Length > 0 ? clean : "Thought";
    }

    string UniquePath(string title)
    {
        string path = Path.Combine(ThoughtsDir, title + ".md");
        for (int n = 2; File.Exists(path); n++)
            path = Path.Combine(ThoughtsDir, $"{title} {n}.md");
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
    static void WriteDurably(string path, string content)
    {
        string temp = path + ".tmp";
        using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            file.Write(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content));
            file.Flush(flushToDisk: true);
        }
        File.Move(temp, path, overwrite: false);
    }
}
