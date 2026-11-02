using System.Text;
using System.Text.Json;

namespace Thoughts;

/// <summary>Where each Thought is in the organizer's line, so its card can say so.</summary>
sealed record OrganizeQueue(string? Current, IReadOnlyList<string> Waiting, IReadOnlyList<string> Failed, string? Connecting = null, IReadOnlyList<string>? ForCaller = null)
{
    public static readonly OrganizeQueue Empty = new(null, [], []);
}

/// <summary>Organizes new Thoughts in the background. Capture has already saved the note, so nothing here can lose one: a
/// failure leaves the Thought in the queue on disk to try again, including after a restart. Keep-local Thoughts are
/// checked again immediately before sending and are never sent.</summary>
sealed class Organizer(Vault vault, Func<IModelBackend?> backend, Action<OrganizeOutcome> done, Action<string?> status, Action<OrganizeQueue> queue, UsageLog usage, Func<double> budget, Action<string> connected, AppSettings settings) : IDisposable
{
    const int MaxRelatedLinks = 3;
    const int MaxTokens = 2000;
    const int MaxCandidates = 300;
    const string RefreshPrefix = "card\t"; // A tab can't be in a note's name, so this can't be mistaken for one.
    const string ConnectPrefix = "connect\t"; // Followed by the Thought and then the nearby notes Organize chose, each after a tab.
    static readonly int[] RetryDelaysMs = [5_000, 30_000, 120_000, 600_000];
    static readonly TimeSpan PausedRecheck = TimeSpan.FromHours(1); // A new month ends a pause, and nothing wakes this for that.

    /// <summary>Debug check that Keep-local Thoughts stay out: every request's user turn is appended here when set.</summary>
    public static string? RequestLog;

    readonly CancellationTokenSource _stop = new();
    readonly SemaphoreSlim _wake = new(0);
    readonly object _lock = new();
    readonly LocalIndex _index = new(vault.StoreDir, vault.ReadCard);
    readonly List<string> _failed = [];
    string? _current;
    string? _connecting;
    Task? _worker;

    string QueueFile => Path.Combine(vault.StoreDir, "queue.txt");

    public void Start()
    {
        Publish();
        _worker = Task.Run(Run);
    }

    public void Enqueue(string title)
    {
        lock (_lock)
        {
            Directory.CreateDirectory(vault.StoreDir);
            CallerQueue.Locked(vault.StoreDir, () => File.AppendAllText(QueueFile, title + "\n"));
            _failed.Remove(title);
        }
        Publish();
        _wake.Release();
    }

    void Publish()
    {
        lock (_lock)
        {
            var (waiting, forCaller) = CallerQueue.Locked(vault.StoreDir, () =>
            {
                var own = File.Exists(QueueFile)
                    ? File.ReadAllLines(QueueFile).Where(l => l.Length > 0 && !l.StartsWith(ConnectPrefix, StringComparison.Ordinal)).Select(TitleOf).Where(l => l != _current).ToList()
                    : [];
                return (own, CallerQueue.ForCaller(vault.StoreDir));
            });
            queue(new OrganizeQueue(_current, waiting, _failed.ToList(), _connecting, forCaller));
        }
    }

    static string TitleOf(string entry) =>
        entry.StartsWith(RefreshPrefix, StringComparison.Ordinal) ? entry[RefreshPrefix.Length..]
        : entry.StartsWith(ConnectPrefix, StringComparison.Ordinal) ? entry.Split('\t')[1]
        : entry;

    /// <summary>The server may have changed things from its own process. The Board asks again after any change on disk.</summary>
    public void Refresh() => Publish();

    /// <summary>The person chose to spend their own API key on a Thought that was waiting for a Caller.</summary>
    public void OrganizeWithKey(string title)
    {
        CallerQueue.Locked(vault.StoreDir, () => CallerQueue.RemoveFromCaller(vault.StoreDir, title));
        Enqueue(title);
    }

    void Working(string? title, bool connecting = false)
    {
        lock (_lock)
        {
            _current = connecting ? null : title;
            _connecting = connecting ? title : null;
        }
        Publish();
    }

    void Failed(string title)
    {
        lock (_lock) _failed.Add(title);
    }

    /// <summary>Asks the AI how a Thought's body could be split. The person started this, and it writes nothing.</summary>
    public async Task<FanOutAnswer> SuggestPieces(string title)
    {
        var note = vault.ReadThoughts().FirstOrDefault(t => t.Title == title);
        if (note is null) return new FanOutAnswer([], "That note no longer exists.");
        var lines = FanOut.Lines(note.Body).Select(l => new FanOutPiece(l, null)).ToList();
        if (note.KeepLocal) return new FanOutAnswer(lines, "Keep-local Thoughts don't go to the AI, so pick the lines yourself.");
        var segments = FanOut.Segments(note.Body);
        if (segments.Count < 2) return new FanOutAnswer([], "This note is one piece, so there's nothing to split.");
        if (usage.SpentThisMonth() >= budget()) return new FanOutAnswer(lines, "AI is paused by the Budget, so pick the lines yourself.");
        if (backend() is not { } model) return new FanOutAnswer(lines, "AI is off, so pick the lines yourself.");
        try
        {
            var request = new ModelRequest(settings.FanOutModel, OrganizeRules.InLanguage(FanOut.SystemPrompt, settings.Language), FanOut.UserTurn(segments), FanOut.Schema, FanOut.MaxTokens);
            var reply = await model.Complete(request, _stop.Token);
            usage.Record("Fan out", request, reply);
            if (reply.Json is null) return new FanOutAnswer(lines, (reply.Declined ?? "The AI gave no answer.") + " Pick the lines yourself.");
            var pieces = FanOut.Read(reply.Json, segments);
            return pieces.Count > 0 ? new FanOutAnswer(pieces, null) : new FanOutAnswer([], "The AI reads this as one piece of work. You can still pick lines yourself.");
        }
        catch (ModelException e)
        {
            return new FanOutAnswer(lines, e.Message + " Nothing changed. Pick the lines yourself, or try again.");
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return new FanOutAnswer(lines, "The AI's answer couldn't be read. Nothing changed. Pick the lines yourself, or try again.");
        }
    }

    /// <summary>Writes a new card for a Thought whose body grew, and touches nothing else about it.</summary>
    public void EnqueueRefresh(string title) => Enqueue(RefreshPrefix + title);

    public void Wake() => _wake.Release();

    public void Dispose()
    {
        _stop.Cancel();
        _wake.Release();
    }

    async Task Run()
    {
        int failures = 0;
        while (!_stop.IsCancellationRequested)
        {
            string? entry = Peek();
            if (entry is null)
            {
                await _wake.WaitAsync(_stop.Token).ContinueWith(_ => { });
                continue;
            }
            string title = TitleOf(entry);
            bool refresh = entry.StartsWith(RefreshPrefix, StringComparison.Ordinal);
            bool connect = entry.StartsWith(ConnectPrefix, StringComparison.Ordinal);
            if (usage.SpentThisMonth() >= budget())
            {
                // Waiting Thoughts stay in the queue on disk. Raising the Budget wakes this.
                Working(null);
                status(null);
                await Task.WhenAny(_wake.WaitAsync(_stop.Token), Task.Delay(PausedRecheck, _stop.Token)).ContinueWith(_ => { });
                continue;
            }
            try
            {
                Working(title, connect);
                await (connect ? FindConnections(title, entry.Split('\t').Skip(2).ToList()) : refresh ? RefreshCard(title) : Organize(title));
                Pop(entry);
                Working(null);
                failures = 0;
                status(null);
            }
            catch (ModelException e) when (e.Kind is ModelFailure.NoKey or ModelFailure.BadKey)
            {
                // Retrying can't help until the person changes the key, which wakes this.
                Working(null);
                status(e.Kind == ModelFailure.NoKey ? "AI is off until you add a key in Settings." : e.Message + " Check it in Settings.");
                await _wake.WaitAsync(_stop.Token).ContinueWith(_ => { });
            }
            catch (ModelException e)
            {
                Working(null);
                status(e.Message + " Your Thought is saved, and the AI will try again.");
                int delay = RetryDelaysMs[Math.Min(failures++, RetryDelaysMs.Length - 1)];
                await Task.Delay(delay, _stop.Token).ContinueWith(_ => { });
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or VaultException or JsonException or FormatException)
            {
                // One note that can't be organized mustn't block the rest. It's still saved and on the Board.
                status($"Couldn't organize {title}: {e.Message}");
                Failed(title);
                Pop(entry);
                Working(null);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                // Nothing may kill this loop silently, or Thoughts would wait forever with no sign of why.
                if (RequestLog is { } log) File.AppendAllText(log, "ERROR " + e + "\n");
                Working(null);
                status("The AI hit a problem: " + e.Message);
                int delay = RetryDelaysMs[Math.Min(failures++, RetryDelaysMs.Length - 1)];
                await Task.Delay(delay, _stop.Token).ContinueWith(_ => { });
            }
        }
    }

    string? Peek()
    {
        lock (_lock)
        {
            return CallerQueue.Locked(vault.StoreDir, () =>
            {
                CallerQueue.ReturnExpired(vault.StoreDir);
                return File.Exists(QueueFile) ? File.ReadAllLines(QueueFile).FirstOrDefault(l => l.Length > 0) : null;
            });
        }
    }

    void Pop(string title)
    {
        lock (_lock)
        {
            CallerQueue.Locked(vault.StoreDir, () =>
            {
                if (!File.Exists(QueueFile)) return;
                var rest = File.ReadAllLines(QueueFile).Where(l => l.Length > 0).ToList();
                rest.Remove(title);
                File.WriteAllText(QueueFile, rest.Count > 0 ? string.Join('\n', rest) + "\n" : "");
            });
        }
    }

    async Task Organize(string title)
    {
        var note = vault.ReadThoughts().FirstOrDefault(t => t.Title == title);
        if (note is null || note.KeepLocal) return;
        var model = backend() ?? throw new ModelException(ModelFailure.NoKey, "No API key.");

        var projects = vault.ReadProjects();
        var people = vault.ReadPeople();
        var candidates = _index.Narrow(note, Sendable(title), MaxCandidates);
        string user = OrganizeRules.BuildUserTurn(vault, note, projects, people, candidates);
        if (RequestLog is { } log) File.AppendAllText(log, user + "\n");

        var request = new ModelRequest(settings.OrganizeModel, OrganizeRules.InLanguage(OrganizeRules.SystemPrompt, settings.Language), user, OrganizeRules.Schema, MaxTokens);
        var reply = await model.Complete(request, _stop.Token);
        usage.Record("Organize", request, reply);
        if (RequestLog is { } usageLog)
            File.AppendAllText(usageLog, $"USAGE in={reply.InputTokens} out={reply.OutputTokens} cacheRead={reply.CacheReadTokens} cacheWrite={reply.CacheWriteTokens} declined={reply.Declined}\nREPLY {reply.Json}\n");
        if (reply.Json is null)
        {
            status(reply.Declined);
            Failed(title);
            return;
        }
        var outcome = OrganizeRules.Apply(vault, note, reply.Json, projects, people, candidates);
        if (outcome.SuggestedProject is not null || outcome.NewProject is not null || outcome.Relation is not null)
            vault.SaveSuggestion(outcome.Title, new PendingSuggestion(outcome.SuggestedProject, outcome.NewProject, outcome.Why, outcome.Relation));
        done(outcome);
        EnqueueConnect(outcome.Title, reply.Json, candidates, outcome.Relation?.Target);
    }

    // Keep-local Thoughts never get this far, so they're in no prompt and no local index.
    List<Thought> Sendable(string except) =>
        vault.ReadThoughts().Where(t => t.Title != except && !t.KeepLocal && t.Status != Status.Dropped).ToList();

    // Organize read every card to choose these, so Connect finds notes that share few words. It never sees the cards itself.
    void EnqueueConnect(string title, string json, List<Thought> candidates, string? skip)
    {
        using var doc = JsonDocument.Parse(json);
        var nearby = OrganizeRules.Nearby(json, candidates, skip);
        _index.LogShadow(doc.RootElement.GetProperty("card").GetRawText(), candidates, nearby);
        if (nearby.Count > 0) Enqueue(ConnectPrefix + title + "\t" + string.Join('\t', nearby));
    }

    async Task FindConnections(string title, List<string> nearby)
    {
        var note = vault.ReadThoughts().FirstOrDefault(t => t.Title == title);
        if (note is null || note.KeepLocal) return;
        var known = vault.ReadConnections();
        // Any answer stands, whichever way it went: a dismissed pair never comes back. A note this one grew from is already linked.
        var others = vault.ReadThoughts()
            .Where(t => nearby.Contains(t.Title) && !t.KeepLocal && t.Status != Status.Dropped && !known.Any(c => c.Involves(title) && c.Involves(t.Title))
                && !note.CameFrom(t.Title) && !t.CameFrom(title))
            .ToList();
        if (others.Count == 0) return;
        var model = backend() ?? throw new ModelException(ModelFailure.NoKey, "No API key.");

        var request = new ModelRequest(settings.ConnectModel, OrganizeRules.InLanguage(Connect.SystemPrompt, settings.Language), Connect.UserTurn(note, others), Connect.Schema, Connect.MaxTokens, "high");
        if (RequestLog is { } log) File.AppendAllText(log, "CONNECT " + request.User + "\n");
        var reply = await model.Complete(request, _stop.Token);
        usage.Record("Connect", request, reply);
        if (RequestLog is { } replyLog) File.AppendAllText(replyLog, $"USAGE in={reply.InputTokens} out={reply.OutputTokens} cacheRead={reply.CacheReadTokens} cacheWrite={reply.CacheWriteTokens} declined={reply.Declined}\nCONNECT-REPLY {reply.Json}\n");
        if (reply.Json is null) return;

        int added = 0;
        foreach (var claim in Connect.Read(reply.Json, note, others))
        {
            var check = new ModelRequest(settings.CheckModel, Connect.CheckPrompt, Connect.CheckTurn(note, claim), Connect.CheckSchema, Connect.CheckMaxTokens);
            var verdict = await model.Complete(check, _stop.Token);
            usage.Record("Check", check, verdict);
            if (RequestLog is { } checkLog) File.AppendAllText(checkLog, $"CHECK {claim.Other.Title}: {verdict.Json}\n");
            if (verdict.Json is null || !Connect.Holds(verdict.Json)) continue;

            // The person may have changed things during those seconds, and what they did wins.
            var now = vault.ReadThoughts();
            if (now.FirstOrDefault(t => t.Title == title) is not { KeepLocal: false } current || current.Body != note.Body) return;
            if (now.FirstOrDefault(t => t.Title == claim.Other.Title) is not { KeepLocal: false }) continue;
            vault.AddConnection(title, claim.Other.Title, claim.Kind, claim.Explanation());
            added++;
        }
        // Coarse: what's left of the nearby notes becomes plain related links, which also show in Obsidian's graph.
        if (settings.Coarse)
        {
            var answered = vault.ReadConnections();
            foreach (var other in others.Where(o => !answered.Any(c => c.Involves(title) && c.Involves(o.Title))).Take(MaxRelatedLinks))
            {
                vault.AddConnection(title, other.Title, Connection.RelatedKind, "");
                added++;
            }
        }
        if (added > 0) connected(title);
    }

    async Task RefreshCard(string title)
    {
        var note = vault.ReadThoughts().FirstOrDefault(t => t.Title == title);
        if (note is null || note.KeepLocal) return;
        var model = backend() ?? throw new ModelException(ModelFailure.NoKey, "No API key.");
        var candidates = _index.Narrow(note, Sendable(title), MaxCandidates);
        var request = new ModelRequest(settings.OrganizeModel, OrganizeRules.InLanguage(OrganizeRules.SystemPrompt, settings.Language), OrganizeRules.BuildUserTurn(vault, note, vault.ReadProjects(), vault.ReadPeople(), candidates), OrganizeRules.Schema, MaxTokens);
        var reply = await model.Complete(request, _stop.Token);
        usage.Record("Refresh card", request, reply);
        if (reply.Json is null) return; // The old card still works, and the next Build on tries again.
        using var doc = JsonDocument.Parse(reply.Json);
        if (vault.ReadThoughts().Any(t => t.Title == title && !t.KeepLocal))
            vault.SaveCard(title, doc.RootElement.GetProperty("card").GetRawText());
        EnqueueConnect(title, reply.Json, candidates, null);
    }
}
