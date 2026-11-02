using System.Text;
using System.Text.Json;

namespace Thoughts;

/// <summary>Local vectors of every card, kept in the vault's <c>.thoughts</c> folder. Above the cap, these pick which cards
/// the AI reads. Below it they do nothing but write a shadow log for later tuning: where the local model ranked what
/// the AI chose, with no note text. Any failure here means the app just carries on without it.</summary>
sealed class LocalIndex(string storeDir, Func<string, string?> readCard)
{
    /// <summary>Where <c>tools/get-embeddings.ps1</c> puts the runtime and the model.</summary>
    public static string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Thoughts", "embeddings");
    public static bool ForceCpu;
    const int FileVersion = 1;
    const int FallbackTextLength = 400;

    readonly Dictionary<string, (ulong Hash, float[] Vector)> _vectors = [];
    bool _loaded;

    string IndexFile => Path.Combine(storeDir, "embeddings.bin");
    string ShadowFile => Path.Combine(storeDir, "embedding-shadow.jsonl");

    public static string Describe() =>
        Embedder.IsInstalled(Dir)
            ? "Local embeddings are installed. They run on this PC, and nothing is sent anywhere."
            : "Local embeddings aren't installed, which only matters above 300 notes. Run tools/get-embeddings.ps1 to add them.";

    /// <summary>The cards nearest this note, up to the cap. Without embeddings, the newest ones.</summary>
    public List<Thought> Narrow(Thought note, List<Thought> all, int cap)
    {
        if (all.Count <= cap) return all;
        try
        {
            using var embedder = Embedder.TryLoad(Dir, ForceCpu);
            if (embedder is null) return all.Take(cap).ToList();
            Update(embedder, all);
            var query = embedder.Embed("query: " + note.Title + "\n" + note.Body);
            return all.OrderByDescending(t => Dot(query, _vectors[t.Title].Vector)).Take(cap).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or KeyNotFoundException)
        {
            return all.Take(cap).ToList();
        }
    }

    /// <summary>Where the local model ranked each note the AI picked. Only numbers go in the log.</summary>
    public void LogShadow(string cardJson, List<Thought> shown, List<string> picked)
    {
        if (picked.Count == 0 || shown.Count == 0 || !Embedder.IsInstalled(Dir)) return;
        try
        {
            using var embedder = Embedder.TryLoad(Dir, ForceCpu);
            if (embedder is null) return;
            Update(embedder, shown);
            var query = embedder.Embed("query: " + CardText(cardJson));
            var ranked = shown.OrderByDescending(t => Dot(query, _vectors[t.Title].Vector)).Select(t => t.Title).ToList();
            using var stream = new MemoryStream();
            using (var w = new Utf8JsonWriter(stream))
            {
                w.WriteStartObject();
                w.WriteString("at", DateTimeOffset.Now);
                w.WriteString("device", embedder.Device);
                w.WriteNumber("cards", shown.Count);
                w.WriteStartArray("ranks");
                foreach (string title in picked) w.WriteNumberValue(ranked.IndexOf(title) + 1);
                w.WriteEndArray();
                w.WriteEndObject();
            }
            File.AppendAllText(ShadowFile, Encoding.UTF8.GetString(stream.ToArray()) + "\n");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or KeyNotFoundException)
        {
            // Only a log.
        }
    }

    void Update(Embedder embedder, List<Thought> notes)
    {
        Load();
        bool changed = false;
        foreach (var note in notes)
        {
            string text = PassageText(note);
            ulong hash = Fnv(text);
            if (_vectors.TryGetValue(note.Title, out var known) && known.Hash == hash) continue;
            _vectors[note.Title] = (hash, embedder.Embed("passage: " + text));
            changed = true;
        }
        var keep = notes.Select(n => n.Title).ToHashSet();
        foreach (string gone in _vectors.Keys.Where(k => !keep.Contains(k)).ToList())
        {
            _vectors.Remove(gone);
            changed = true;
        }
        if (changed) Save();
    }

    // The card when there is one, because cards are short English text, which is what makes this work across languages.
    string PassageText(Thought note)
    {
        if (readCard(note.Title) is { } card) return note.Title + "\n" + CardText(card);
        string body = note.Body.Length <= FallbackTextLength ? note.Body : note.Body[..FallbackTextLength];
        return note.Title + "\n" + body;
    }

    static string CardText(string cardJson)
    {
        var text = new StringBuilder();
        using var doc = JsonDocument.Parse(cardJson);
        foreach (var part in doc.RootElement.EnumerateObject())
            foreach (var item in part.Value.EnumerateArray())
                text.Append(item.GetString()).Append('\n');
        return text.ToString();
    }

    static float Dot(float[] a, float[] b)
    {
        float sum = 0f;
        for (int i = 0; i < a.Length; i++) sum += a[i] * b[i];
        return sum;
    }

    static ulong Fnv(string text)
    {
        ulong hash = 14695981039346656037UL;
        foreach (char c in text)
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }
        return hash;
    }

    void Load()
    {
        if (_loaded) return;
        _loaded = true;
        if (!File.Exists(IndexFile)) return;
        try
        {
            using var reader = new BinaryReader(File.OpenRead(IndexFile));
            if (reader.ReadInt32() != FileVersion) return;
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                string title = reader.ReadString();
                ulong hash = reader.ReadUInt64();
                var vector = new float[Embedder.Dimensions];
                for (int j = 0; j < vector.Length; j++) vector[j] = reader.ReadSingle();
                _vectors[title] = (hash, vector);
            }
        }
        catch (Exception e) when (e is IOException or EndOfStreamException or FormatException)
        {
            _vectors.Clear(); // It can all be worked out again.
        }
    }

    void Save()
    {
        Directory.CreateDirectory(storeDir);
        string temporary = IndexFile + ".tmp";
        using (var writer = new BinaryWriter(File.Create(temporary)))
        {
            writer.Write(FileVersion);
            writer.Write(_vectors.Count);
            foreach (var (title, entry) in _vectors)
            {
                writer.Write(title);
                writer.Write(entry.Hash);
                foreach (float f in entry.Vector) writer.Write(f);
            }
        }
        File.Move(temporary, IndexFile, overwrite: true);
    }
}
