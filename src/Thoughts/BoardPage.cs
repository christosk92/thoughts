using FluentGpu.Animation;
using FluentGpu.Controls;
using FluentGpu.Dsl;
using FluentGpu.Foundation;
using FluentGpu.Hooks;
using FluentGpu.Signals;
using static FluentGpu.Dsl.Ui;

namespace Thoughts;

sealed class BoardPage(AppSettings settings, string settingsFile, bool canSave) : Component
{
    const int DoingLimit = 2;
    const float RowLabelWidth = 130f;
    const float EmptyRowHeight = 72f; // An empty Project still has room to drop into.
    const string ThoughtDrag = "thought";
    const float DialogFieldWidth = 272f; // ContentDialog's narrowest width less its padding, and TextBox has no fill mode.
    const int ReloadDelayMs = 150;
    const float PanelWidth = 460f;
    const float PanelSlide = 32f;
    const float StripWidth = 620f;
    const float CardFooterHeight = 24f; // Reserved on every card, so what the AI adds never changes its height.
    const string SparkleGlyph = ""; // In Segoe Fluent Icons, but not yet in the engine's Icons.
    const int SettleMs = 1100; // How long a card the AI just filed stays where it is, so the person sees it change before it moves.
    const int MaxHealthRows = 20;
    const string Unsorted = "Unsorted";
    static readonly Status[] Columns = [Status.Inbox, Status.Parked, Status.Next, Status.Doing, Status.Done];
    static readonly Status[] AllStatuses = [.. Columns, Status.Dropped];

    public override Element Render()
    {
        var vaultPath = UseSignal(settings.VaultPath);
        var showSettings = UseSignal(settings.VaultPath is null);
        var thoughts = UseSignal(LoadThoughts(settings.VaultPath));
        var projects = UseSignal(LoadProjects(settings.VaultPath));
        var dropped = UseSignal<IReadOnlyList<Thought>>(DroppedOf(thoughts.Peek()));
        var filter = UseSignal<string?>(null);
        var capture = UseSignal("");
        var message = UseSignal<string?>(null);
        var pathField = UseSignal(settings.VaultPath ?? AppSettings.SuggestedVaultPath);
        var projectField = UseSignal("");
        var renaming = UseSignal<string?>(null);
        var dropOver = UseSignal<string?>(null);
        var health = UseSignal(LoadHealth(settings.VaultPath));
        var renameField = UseSignal("");
        var selected = UseSignal<string?>(null);
        var keepLocal = UseSignal(false);
        var strip = UseSignal<OrganizeOutcome?>(null);
        var stripOpen = UseSignal(false);
        var builtOn = UseSignal<BuildOnUndo?>(null);
        var lastCaptured = UseRef<string?>(null);
        var aiStatus = UseSignal<string?>(null);
        var keyField = UseSignal("");
        var keyState = UseSignal(ApiKey.Describe());
        var organizer = UseRef<Organizer?>(null);
        var queue = UseSignal(OrganizeQueue.Empty);
        var connections = UseSignal(LoadConnections(settings.VaultPath));
        var showUsage = UseSignal(false);
        var usageCalls = UseSignal<IReadOnlyList<UsageCall>>([]);
        var budget = UseSignal(settings.BudgetUsd);
        var settling = UseSignal<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
        var cardKeys = UseRef(new Dictionary<string, string>()); // A card keeps its identity when the AI renames its note.
        var post = UsePost();
        var captureBox = UseRef(NodeHandle.Null);
        var panelBox = UseRef(NodeHandle.Null);
        var hooks = UseContext(InputHooks.Current);
        var overlay = UseContext(Overlay.Service);

        // Open and type: capture is the first thing the app is for.
        UseLayoutEffect(() =>
        {
            if (showSettings.Value) return;
            _ = keepLocal.Value; // Turning on Keep local is the start of typing a Thought, so the keyboard goes back to capture.
            // An open panel takes the keyboard, so Escape reaches it, and closing it hands capture back.
            var host = selected.Value is null ? captureBox.Value : panelBox.Value;
            if (host.IsNull) return;
            var first = hooks.FirstFocusableIn?.Invoke(host) ?? NodeHandle.Null;
            if (!first.IsNull) hooks.FocusNode?.Invoke(first, false);
        });

        // Also runs for every change on disk, so it only touches what really changed.
        void Reload()
        {
            var fresh = LoadThoughts(vaultPath.Peek());
            if (!fresh.SequenceEqual(thoughts.Peek()))
            {
                thoughts.Value = fresh;
                dropped.Value = DroppedOf(fresh);
            }
            var freshConnections = LoadConnections(vaultPath.Peek());
            if (!freshConnections.SequenceEqual(connections.Peek())) connections.Value = freshConnections;
            var freshProjects = LoadProjects(vaultPath.Peek());
            if (!freshProjects.SequenceEqual(projects.Peek())) projects.Value = freshProjects;
            var freshHealth = LoadHealth(vaultPath.Peek());
            if (!freshHealth.BrokenLinks.SequenceEqual(health.Peek().BrokenLinks)
                || !freshHealth.DuplicateNames.SequenceEqual(health.Peek().DuplicateNames))
                health.Value = freshHealth;
        }

        // Obsidian, a sync tool or the person can change a note at any time, and the vault is the one source of truth.
        UseEffect(() =>
        {
            if (vaultPath.Value is not { } path) return (Action?)null;
            FileSystemWatcher watcher;
            try
            {
                var vault = new Vault(path);
                Directory.CreateDirectory(path);
                vault.EnsureObsidianSettings();
                watcher = new FileSystemWatcher(path)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                };
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                post(() => message.Value = "Can't watch the vault: " + e.Message);
                return null;
            }

            // A burst of events (a rename, a save through a temporary file) becomes one reload.
            var debounce = new Timer(_ => post(() =>
            {
                try
                {
                    Reload();
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    message.Value = "Couldn't read the vault: " + e.Message;
                }
            }), null, Timeout.Infinite, Timeout.Infinite);
            void Changed(object _, FileSystemEventArgs e)
            {
                if (!IsSettingsPath(e.Name)) debounce.Change(ReloadDelayMs, Timeout.Infinite);
            }
            watcher.Changed += Changed;
            watcher.Created += Changed;
            watcher.Deleted += Changed;
            watcher.Renamed += (s, e) => Changed(s, e);
            watcher.Error += (_, _) => debounce.Change(ReloadDelayMs, Timeout.Infinite); // Too many changes at once: reread.
            watcher.EnableRaisingEvents = true;
            return () =>
            {
                watcher.Dispose();
                debounce.Dispose();
            };
        });

        // Every change to the vault goes through here, so a refusal always reaches the person.
        void Change(Action<Vault> change, string? done = null)
        {
            if (vaultPath.Peek() is not { } path) return;
            try
            {
                change(new Vault(path));
                message.Value = done;
                Reload();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or VaultException)
            {
                message.Value = "Not saved: " + e.Message;
            }
        }

        void Capture(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || vaultPath.Peek() is null) return;
            string? saved = null;
            bool local = keepLocal.Peek();
            Change(v => saved = v.Capture(text, local), local ? "Saved to Inbox. Kept local." : "Saved to Inbox");
            if (saved is null) return;
            capture.Value = "";
            stripOpen.Value = false;
            builtOn.Value = null;
            lastCaptured.Value = saved;
            strip.Value = null;
            keepLocal.Value = false;
            if (!local) organizer.Value?.Enqueue(saved);
        }

        // The vault's organizer lives as long as that vault is open, and picks up what an earlier run left in its queue.
        UseEffect(() =>
        {
            if (vaultPath.Value is not { } path) return (Action?)null;
            var usage = new UsageLog(new Vault(path).StoreDir);
            usage.Changed = () =>
            {
                var calls = usage.ThisMonth();
                post(() => usageCalls.Value = calls);
            };
            usage.Changed();
            var running = new Organizer(new Vault(path),
                () => ApiKey.Find() is { } key ? new AnthropicBackend(key) : null,
                outcome => post(() =>
                {
                    var was = thoughts.Peek().FirstOrDefault(t => t.Title == outcome.Was);
                    if (outcome.Title != outcome.Was)
                        cardKeys.Value[outcome.Title] = cardKeys.Value.GetValueOrDefault(outcome.Was, outcome.Was);
                    settling.Value = new Dictionary<string, string>(settling.Peek()) { [outcome.Title] = was?.Project ?? Unsorted };
                    _ = Task.Delay(SettleMs).ContinueWith(_ => post(() =>
                    {
                        var rest = new Dictionary<string, string>(settling.Peek());
                        rest.Remove(outcome.Title);
                        settling.Value = rest;
                    }));
                    Reload();
                    if (selected.Peek() == outcome.Was) selected.Value = outcome.Title;
                    bool worthShowing = outcome.FiledIn is not null || outcome.Person is not null
                        || outcome.SuggestedProject is not null || outcome.NewProject is not null || outcome.Relation is not null;
                    if (outcome.Was == lastCaptured.Value)
                    {
                        // Kept even with nothing to show yet, because Connections can arrive later for this Thought.
                        strip.Value = outcome;
                        stripOpen.Value = worthShowing;
                    }
                    lastCaptured.Value = outcome.Title == lastCaptured.Value ? null : lastCaptured.Value;
                }),
                text => post(() => aiStatus.Value = text),
                state => post(() => queue.Value = state),
                usage,
                () => settings.BudgetUsd,
                title => post(() =>
                {
                    Reload();
                    if (strip.Peek()?.Title == title) stripOpen.Value = true;
                }));
            organizer.Value = running;
            running.Start();
            return () =>
            {
                organizer.Value = null;
                running.Dispose();
            };
        });

        void ChangeBudget(double amount)
        {
            if (double.IsNaN(amount) || amount < 0) return;
            budget.Value = amount;
            settings.BudgetUsd = amount;
            if (canSave) settings.Save(settingsFile);
            organizer.Value?.Wake();
        }

        void AddAsDetail(string source, string target)
        {
            BuildOnUndo? undo = null;
            Change(v => undo = v.BuildOn(source, target));
            if (undo is null) return;
            if (selected.Peek() == source) selected.Value = target;
            organizer.Value?.EnqueueRefresh(target);
            builtOn.Value = undo;
        }

        void UndoBuildOn(BuildOnUndo undo)
        {
            bool removed = true;
            Change(v => removed = v.UndoBuildOn(undo),
                null);
            if (!removed) message.Value = $"{undo.Source} is back. Its words are still in {undo.Target}, because that note changed.";
            builtOn.Value = null;
            stripOpen.Value = false;
        }

        void LinkAsNewWork(string source, string target)
        {
            Change(v => v.AddFrom(source, target));
            stripOpen.Value = false;
        }

        void SaveKey()
        {
            string key = keyField.Peek();
            if (string.IsNullOrWhiteSpace(key)) return;
            ApiKey.Save(key);
            keyField.Value = "";
            keyState.Value = ApiKey.Describe();
            organizer.Value?.Wake();
        }

        void SetStatus(Thought t, Status status)
        {
            if (status == Status.Doing && t.Status != Status.Doing
                && thoughts.Peek().Count(x => x.Status == Status.Doing) >= DoingLimit)
            {
                message.Value = $"Doing is full ({DoingLimit}). Move something out of Doing first.";
                return;
            }
            Change(v => v.SetStatus(t.Title, status));
        }

        IReadOnlyList<MenuFlyoutItem> CardMenu(Thought t)
        {
            var items = new List<MenuFlyoutItem>();
            foreach (var status in AllStatuses)
                items.Add(MenuFlyoutItem.RadioItem(status.ToString(), t.Status == status, () => SetStatus(t, status)));
            items.Add(MenuFlyoutItem.Separator);
            var moves = new List<MenuFlyoutItem>();
            foreach (var project in projects.Peek())
                moves.Add(MenuFlyoutItem.RadioItem(project, t.Project == project, () => Change(v => v.SetProject(t.Title, project))));
            moves.Add(MenuFlyoutItem.RadioItem(Unsorted, t.Project is null, () => Change(v => v.SetProject(t.Title, null))));
            items.Add(MenuFlyoutItem.SubMenu("Project", moves));
            items.Add(new MenuFlyoutItem("Rename", Invoke: () => ConfirmRename(t)));
            items.Add(MenuFlyoutItem.Toggle("Keep local", t.KeepLocal, () => Change(v => v.SetKeepLocal(t.Title, !t.KeepLocal))));
            return items;
        }

        void ConfirmRename(Thought t)
        {
            renameField.Value = t.Title;
            ContentDialog.Show(overlay, d =>
            {
                d.Title = "Rename Thought";
                d.Content = TextBox.Create(renameField, null, new TextBox.TextBoxOptions { Width = DialogFieldWidth });
                d.PrimaryText = "Rename";
                d.CloseText = "Cancel";
                d.Closed = result =>
                {
                    if (result != ContentDialogResult.Primary) return;
                    // The panel follows the note, so it needs the name the vault settled on.
                    string? renamed = null;
                    Change(v => renamed = v.RenameThought(t.Title, renameField.Peek()));
                    if (renamed is not null && selected.Peek() == t.Title) selected.Value = renamed;
                };
            });
        }

        // Dropping sets both, because the Board's two axes are Status and Project.
        void Move(Thought t, Status status, string project)
        {
            string? target = project == Unsorted ? null : project;
            Change(v =>
            {
                if (t.Project != target) v.SetProject(t.Title, target);
                if (t.Status != status) v.SetStatus(t.Title, status);
            });
        }

        DropTargetSpec Target(string id, Status? status, string? project, string caption) => Drop.Target<Thought>(ThoughtDrag,
            accepts: t => status != Status.Doing || t.Status == Status.Doing
                || thoughts.Peek().Count(x => x.Status == Status.Doing) < DoingLimit,
            transparent: t => (status is null || t.Status == status) && (project is null || (t.Project ?? Unsorted) == project),
            caption: _ => caption,
            refusalCaption: _ => $"Doing is full ({DoingLimit})",
            onEnter: (_, _) => dropOver.Value = id,
            onLeave: _ => { if (dropOver.Peek() == id) dropOver.Value = null; },
            onDrop: (t, _) =>
            {
                dropOver.Value = null;
                Move(t, status ?? t.Status, project ?? t.Project ?? Unsorted);
            });

        var connectionActions = UseMemo(() => new ConnectionActions(
            Accept: c => Change(v => v.AcceptConnection(c)),
            Dismiss: c => Change(v => v.DismissConnection(c)),
            BuildOn: c =>
            {
                string? made = null;
                Change(v => made = v.BuildOnConnection(c), "Saved to Inbox");
                if (made is null) return;
                organizer.Value?.Enqueue(made);
                selected.Value = made;
                stripOpen.Value = false;
            },
            Open: title =>
            {
                if (thoughts.Peek().Any(x => string.Equals(x.Title, title, StringComparison.OrdinalIgnoreCase)))
                {
                    selected.Value = title;
                    stripOpen.Value = false;
                }
                else message.Value = $"{title} no longer exists.";
            }), DepKey.Empty);
        var panelActions = UseMemo(() => new PanelActions(
            Close: () => selected.Value = null,
            SetStatus: SetStatus,
            SaveBody: (title, body) => Change(v => v.SetBody(title, body)),
            FollowLink: target =>
            {
                var linked = thoughts.Peek().FirstOrDefault(x => string.Equals(x.Title, target, StringComparison.OrdinalIgnoreCase));
                if (linked is not null) selected.Value = linked.Title;
                else message.Value = $"{target} isn't a Thought. Open it in Obsidian.";
            },
            SuggestPieces: title => organizer.Value?.SuggestPieces(title)
                ?? Task.FromResult(new FanOutAnswer([], "The vault isn't open.")),
            CreatePieces: (parent, pieces) =>
            {
                var created = new List<string>();
                Change(v =>
                {
                    foreach (string piece in pieces) created.Add(v.CreatePiece(parent, piece));
                }, $"Created {pieces.Count} in Parked");
                if (!parent.KeepLocal) foreach (string name in created) organizer.Value?.Enqueue(name);
            },
            ConnectionsOf: title => connections.Value.Where(c => c.State != ConnectionState.Dismissed && c.Involves(title)).ToList(),
            Connections: connectionActions,
            PieceCount: title =>
            {
                var pieces = thoughts.Value.Where(x => x.CameFrom(title) && x.Status != Status.Dropped).ToList();
                return (pieces.Count(x => x.Status == Status.Done), pieces.Count);
            },
            OpenInObsidian: title =>
            {
                if (vaultPath.Peek() is { } path) hooks.OpenUri?.Invoke("obsidian://open?path=" + Uri.EscapeDataString(new Vault(path).ThoughtPath(title)));
            },
            Message: message), DepKey.Empty);

        static Element FooterLine(string key, params Element[] children) => new BoxEl
        {
            Key = key,
            Direction = 0,
            Gap = Spacing.XS,
            AlignItems = FlexAlign.Center,
            Shrink = 1f,
            MinWidth = 0f,
            Enter = new EnterExit(Opacity: 0f, Active: true),
            Exit = new EnterExit(Opacity: 0f, Active: true),
            Transition = MotionTok.ControlNormal,
            Children = children,
        };

        static TextEl FooterText(string text) => Caption(text) with { Color = Tok.TextSecondary, MaxLines = 1, Trim = TextTrim.CharacterEllipsis, Shrink = 1f, MinWidth = 0f };

        // What the AI is doing with this Thought, or what it did. Always the same height, so nothing moves when it changes.
        Element CardFooter(Thought t)
        {
            var state = queue.Value;
            int suggested = connections.Value.Count(c => c.State == ConnectionState.Suggested && c.Involves(t.Title));
            Element? line = null;
            if (t.KeepLocal) line = FooterLine("local", Icon(Icons.Lock, 12f), FooterText("Kept local"));
            else if (state.Current == t.Title) line = FooterLine("working", ProgressRing.Indeterminate(ProgressRing.MinSize), FooterText("Organizing\u2026"));
            else if (state.Failed.Contains(t.Title))
                line = FooterLine("failed", Icon(Icons.Warning, 12f) with { Color = Tok.SystemFillCaution }, FooterText("Couldn't organize"),
                    HyperlinkButton.Create("Retry", () => organizer.Value?.Enqueue(t.Title)));
            else if (state.Waiting.Contains(t.Title)) line = FooterLine("waiting", Icon(Icons.Clock, 12f), FooterText("Waiting to organize"));
            else if (t.Project is not null || t.Organized || suggested > 0 || state.Connecting == t.Title)
                line = FooterLine("filed", [
                    .. t.Project is { } project ? new Element[] { new BoxEl
                    {
                        Padding = new Edges4(Spacing.S, 0f, Spacing.S, 0f),
                        Corners = Radii.ControlAll,
                        Fill = Tok.FillControlSecondary,
                        BorderColor = Tok.StrokeCardDefault,
                        BorderWidth = 1f,
                        Shrink = 1f,
                        MinWidth = 0f,
                        Children = [FooterText(project)],
                    } } : [],
                    .. t.Organized ? new Element[] { ToolTip.Wrap(Icon(SparkleGlyph, 12f), "Organized by AI") } : [],
                    .. suggested > 0 ? new Element[] { ToolTip.Wrap(FooterText($"{suggested} to review"), "Suggested Connections") } : [],
                    .. state.Connecting == t.Title ? new Element[] { ToolTip.Wrap(ProgressRing.Indeterminate(ProgressRing.MinSize), "Looking for Connections") } : [],
                ]);
            return new BoxEl
            {
                Height = CardFooterHeight,
                Margin = new Edges4(0f, Spacing.XS, Spacing.S, 0f),
                Direction = 0,
                AlignItems = FlexAlign.Center,
                Children = line is null ? [] : [line],
            };
        }

        BoxEl Card(Thought t) => new()
        {
            Key = "card-" + cardKeys.Value.GetValueOrDefault(t.Title, t.Title),
            Draggable = Drag.Source(ThoughtDrag, () => t),
            Layout = LayoutTransition.Slide,
            Animate = MotionRecipes.CardResizeHeight,
            Enter = new EnterExit(Sx: 0.96f, Sy: 0.96f, Opacity: 0f, Active: true),
            Exit = new EnterExit(Opacity: 0f, Active: true),
            Transition = MotionTok.StandardEnter,
            Cursor = CursorId.Hand,
            Role = AutomationRole.Button,
            Focusable = true,
            OnClick = () => selected.Value = t.Title,
            OnKeyDown = e =>
            {
                if (e.KeyCode is 13 or 32) { selected.Value = t.Title; e.Handled = true; }
            },
            Direction = 1,
            Padding = new Edges4(Spacing.M, Spacing.S, Spacing.XS, Spacing.XS),
            Fill = Tok.FillCardDefault,
            HoverFill = Tok.FillCardSecondary,
            PressedFill = Tok.FillCardSecondary,
            BorderColor = selected.Value == t.Title || settling.Value.ContainsKey(t.Title) ? Tok.AccentDefault : Tok.StrokeCardDefault,
            BorderWidth = 1f,
            BrushTransitionMs = 167f,
            Corners = Radii.CardAll,
            Children =
            [
                new BoxEl
                {
                    Direction = 0,
                    Gap = Spacing.XS,
                    AlignItems = FlexAlign.Start,
                    Children =
                    [
                        new BoxEl
                        {
                            Grow = 1f,
                            Shrink = 1f,
                            MinWidth = 0f,
                            Margin = new Edges4(0f, Spacing.XS, 0f, 0f),
                            Children =
                            [
                                new BoxEl
                                {
                                    Key = "title-" + t.Title,
                                    Enter = new EnterExit(Opacity: 0f, Active: true),
                                    Transition = MotionTok.ControlNormal,
                                    Children = [Body(t.Title) with { MaxLines = 3, Trim = TextTrim.CharacterEllipsis, Shrink = 1f, MinWidth = 0f }],
                                },
                            ],
                        },
                        IconButton.Menu(Icons.More, () => CardMenu(t), toolTip: "Status and Project", size: ControlSize.Small),
                    ],
                },
                CardFooter(t),
            ],
        };

        void UseVault()
        {
            string path = pathField.Peek().Trim();
            if (path.Length == 0) return;
            settings.VaultPath = path;
            if (canSave) settings.Save(settingsFile);
            vaultPath.Value = path;
            filter.Value = null;
            message.Value = null;
            Reload();
            showSettings.Value = false;
        }

        void SubmitProject()
        {
            string name = projectField.Peek();
            string? old = renaming.Peek();
            Change(v => { if (old is null) v.AddProject(name); else v.RenameProject(old, name); });
            if (message.Peek() is null)
            {
                if (old is not null && filter.Peek() == old) filter.Value = null;
                projectField.Value = "";
                renaming.Value = null;
            }
        }

        void ConfirmRemove(string project)
        {
            int count = thoughts.Peek().Count(t => t.Project == project);
            ContentDialog.Show(overlay, d =>
            {
                d.Title = $"Remove {project}?";
                d.Content = Body(count == 0
                    ? "Its note moves to the vault's .trash folder, where you can restore it."
                    : $"{(count == 1 ? "1 Thought moves" : $"{count} Thoughts move")} to Unsorted, and its note moves to the vault's .trash folder.");
                d.PrimaryText = "Remove";
                d.CloseText = "Cancel";
                d.Closed = result =>
                {
                    if (result != ContentDialogResult.Primary) return;
                    if (filter.Peek() == project) filter.Value = null;
                    Change(v => v.RemoveProject(project));
                };
            });
        }

        if (showSettings.Value)
        {
            var projectRows = projects.Value.Select(p => (Element)new BoxEl
            {
                Key = "project-" + p,
                Direction = 0,
                Gap = Spacing.S,
                AlignItems = FlexAlign.Center,
                Children =
                [
                    new BoxEl { Width = 300f, Shrink = 1f, MinWidth = 0f, Children = [Body(p) with { MaxLines = 1, Trim = TextTrim.CharacterEllipsis }] },
                    Button.Standard("Rename", () => { renaming.Value = p; projectField.Value = p; }),
                    Button.Standard("Remove", () => ConfirmRemove(p)),
                ],
            }).ToArray();

            return new BoxEl
            {
                Key = "settings",
                Direction = 1,
                Grow = 1f,
                Padding = Edges4.All(Spacing.XXL),
                Fill = Tok.FillSolidBase,
                Children =
                [
                    ScrollView(VStack(Spacing.L,
                        Title("Settings"),
                        Body("Where your Thoughts live. This folder is an Obsidian vault, and the notes stay readable without this app."),
                        TextBox.Create(pathField, null, new TextBox.TextBoxOptions
                        {
                            Header = "Vault location",
                            Width = 620f,
                            OnCommit = _ => UseVault(),
                        }),
                        HStack(Spacing.S,
                            Button.Accent("Use this vault", UseVault),
                            vaultPath.Value is null ? new BoxEl() : Button.Standard("Back", () => showSettings.Value = false)),
                        vaultPath.Value is null ? new BoxEl() : VStack(Spacing.M,
                        [
                            Subtitle("Projects"),
                            .. projectRows,
                            TextBox.Create(projectField, null, new TextBox.TextBoxOptions
                            {
                                Header = "Project name",
                                Width = 300f,
                                OnCommit = _ => SubmitProject(),
                            }),
                            HStack(Spacing.S, Button.Standard(renaming.Value is { } r ? $"Rename {r}" : "Add Project", SubmitProject)),
                            new TextEl(message.Value ?? "") { Size = 14f, Color = Tok.TextSecondary, Wrap = TextWrap.Wrap },
                            Subtitle("AI"),
                            Body(keyState.Value),
                            PasswordBox.Create(placeholder: "Anthropic API key", width: 420f, password: keyField, onCommit: _ => SaveKey()),
                            HStack(Spacing.S,
                                Button.Standard("Save key", SaveKey),
                                Button.Standard("Remove key", () =>
                                {
                                    ApiKey.Remove();
                                    keyState.Value = ApiKey.Describe();
                                })),
                            Subtitle("Vault health"),
                            .. HealthRows(health.Value),
                        ]))),
                ],
            };
        }

        if (showUsage.Value)
        {
            _ = budget.Value; // The page's totals follow the Budget as it changes.
            return UsagePage.Create(usageCalls.Value, budget, ChangeBudget, () => showUsage.Value = false);
        }

        Element StripContent()
        {
            if (builtOn.Value is { } added)
                return new BoxEl
                {
                    Direction = 0,
                    Gap = Spacing.S,
                    Width = StripWidth,
                    Padding = Edges4.All(Spacing.M),
                    AlignItems = FlexAlign.Center,
                    Children =
                    [
                        new BoxEl { Grow = 1f, Shrink = 1f, MinWidth = 0f, Children = [Body($"Added to {added.Target}") with { MaxLines = 1, Trim = TextTrim.CharacterEllipsis, Shrink = 1f, MinWidth = 0f }] },
                        Button.Standard("Undo", () => UndoBuildOn(added)),
                        IconButton.Create(Icons.Cancel, () => stripOpen.Value = false, size: ControlSize.Small),
                    ],
                };
            if (strip.Peek() is not { } found) return new BoxEl();
            string filedText = found.FiledIn is { } inProject ? $"Filed in {inProject}" : "";
            if (found.Person is { } about) filedText += (filedText.Length > 0 ? ", about " : "About ") + about;
            var actions = new List<Element>();
            if (found.SuggestedProject is { } suggested)
                actions.Add(Button.Standard($"Move to {suggested}", () =>
                {
                    Change(v => v.SetProject(found.Title, suggested));
                    stripOpen.Value = false;
                }));
            if (found.NewProject is { } brandNew)
                actions.Add(Button.Standard($"Create {brandNew}", () =>
                {
                    Change(v =>
                    {
                        v.AddProject(brandNew);
                        v.SetProject(found.Title, brandNew);
                    });
                    stripOpen.Value = false;
                }));
            if (found.Relation is { } relation)
            {
                Element addDetail = relation.MoreDetail
                    ? Button.Accent("Add as detail", () => AddAsDetail(found.Title, relation.Target))
                    : Button.Standard("Add as detail", () => AddAsDetail(found.Title, relation.Target));
                Element newWork = relation.MoreDetail
                    ? Button.Standard("Keep as new work", () => LinkAsNewWork(found.Title, relation.Target))
                    : Button.Accent("Keep as new work", () => LinkAsNewWork(found.Title, relation.Target));
                actions.Add(relation.MoreDetail ? addDetail : newWork);
                actions.Add(relation.MoreDetail ? newWork : addDetail);
                actions.Add(Button.Subtle("Unrelated", () => stripOpen.Value = false));
            }
            actions.Add(new BoxEl { Grow = 1f });
            actions.Add(Button.Subtle("Open", () =>
            {
                selected.Value = found.Title;
                stripOpen.Value = false;
            }));
            actions.Add(IconButton.Create(Icons.Cancel, () => stripOpen.Value = false, size: ControlSize.Small));
            return new BoxEl
            {
                Direction = 1,
                Gap = Spacing.S,
                Width = StripWidth,
                Padding = Edges4.All(Spacing.M),
                Children =
                [
                    new TextEl(found.Title) { Size = 14f, Weight = 600, MaxLines = 1, Trim = TextTrim.CharacterEllipsis, Shrink = 1f, MinWidth = 0f },
                    filedText.Length > 0 ? Caption(filedText) : new BoxEl { Visible = false },
                    found.Why is { Length: > 0 } why ? Body(why) : new BoxEl { Visible = false },
                    .. connections.Value.Where(c => c.State == ConnectionState.Suggested && c.Involves(found.Title)).Select(c => ConnectionView.Create(c, found.Title, connectionActions)),
                    found.Relation is { } related
                        ? VStack(Spacing.XS,
                            BodyStrong(related.MoreDetail ? $"More detail on {related.Target}" : $"Grew from {related.Target}") with { MaxLines = 1, Trim = TextTrim.CharacterEllipsis, Shrink = 1f, MinWidth = 0f },
                            Body(related.Why),
                            Caption($"\u201C{related.Evidence}\u201D") with { Color = Tok.TextSecondary })
                        : new BoxEl { Visible = false },
                    new BoxEl { Direction = 0, Gap = Spacing.S, AlignItems = FlexAlign.Center, Children = actions.ToArray() },
                ],
            };
        }

        double usageRatio = budget.Value > 0 ? usageCalls.Value.Sum(c => c.Cost) / budget.Value : 1;
        var all = thoughts.Value;
        int droppedCount = all.Count(t => t.Status == Status.Dropped);
        var rowNames = RowNames(projects.Value, all);

        BoxEl Cell(Element child, bool lead = false, DropTargetSpec? drop = null, string? dropId = null) => new()
        {
            Direction = 1,
            Gap = Spacing.S,
            Width = lead ? RowLabelWidth : float.NaN,
            Grow = lead ? 0f : 1f,
            Shrink = lead ? 0f : 1f,
            Basis = lead ? float.NaN : 0f,
            MinWidth = 0f,
            DropTarget = drop,
            Corners = Radii.CardAll,
            BrushTransitionMs = 83f,
            Fill = dropId is null ? default : Prop.Of(() => dropOver.Value == dropId ? Tok.AccentSubtle : ColorF.Transparent),
            Children = [child],
        };

        var rows = new List<Element>();
        foreach (var name in rowNames)
        {
            if (filter.Value is { } only && only != name) continue;
            var inRow = all.Where(t => (settling.Value.GetValueOrDefault(t.Title) ?? t.Project ?? Unsorted) == name).ToList();
            rows.Add(new BoxEl
            {
                Key = "row-" + name,
                Direction = 0,
                Gap = Spacing.M,
                MinHeight = EmptyRowHeight,
                Padding = new Edges4(0f, Spacing.M, 0f, Spacing.M),
                Animate = MotionRecipes.CardResizeHeight,
                Children =
                [
                    Cell(BodyStrong(name) with { Wrap = TextWrap.Wrap, Shrink = 1f, MinWidth = 0f }, lead: true,
                        drop: Target($"{name}/*", null, name, $"Move to {name}"), dropId: $"{name}/*"),
                    .. Columns.Select(s => (Element)Cell(VStack(Spacing.S,
                        inRow.Where(t => t.Status == s).Select(t => (Element)Card(t)).ToArray()),
                        drop: Target($"{name}/{s}", s, name, $"{s} in {name}"), dropId: $"{name}/{s}")),
                ],
            });
            rows.Add(Divider());
        }

        var chips = new List<Element> { Chip("All", filter.Value is null, () => filter.Value = null) };
        foreach (var name in rowNames)
            chips.Add(Chip(name, filter.Value == name, () => filter.Value = name));

        var board = new BoxEl
        {
            Key = "board",
            Direction = 1,
            Grow = 1f,
            Gap = Spacing.M,
            Padding = new Edges4(Spacing.XL, Spacing.XL, Spacing.XL, 0f),
            Fill = Tok.FillSolidBase,
            Children =
            [
                new BoxEl
                {
                    Direction = 0,
                    Gap = Spacing.M,
                    AlignItems = FlexAlign.Center,
                    Children =
                    [
                        // A popup anchored to capture, so suggestions arriving seconds later move nothing, and typing carries on.
                        Popup.Create(new BoxEl
                        {
                            Key = "capture",
                            OnRealized = h => captureBox.Value = h,
                            Children =
                            [
                                TextBox.Create(capture, null, new TextBox.TextBoxOptions
                                {
                                    Placeholder = "Drop a thought here and press Enter",
                                    Width = 620f,
                                    OnCommit = Capture,
                                }),
                            ],
                        }, StripContent, stripOpen, null, FlyoutPlacement.BottomEdgeAlignedLeft,
                            new PopupOptions(FocusTrap: false, DismissBehavior: DismissBehavior.None) { PreserveFocusOnOpen = true }),
                        Button.Accent("Add", () => Capture(capture.Peek())),
                        ToggleButton.Create("Keep local", keepLocal),
                        // The slot is always there, so a message appearing moves nothing.
                        new BoxEl
                        {
                            Key = "message",
                            Grow = 1f,
                            Shrink = 1f,
                            MinWidth = 0f,
                            Children = [new TextEl(string.Join(" · ", new[] { message.Value, aiStatus.Value, BudgetNote(usageRatio) }.Where(s => s is not null))) { Size = 14f, Color = Tok.TextSecondary, MaxLines = 1, Trim = TextTrim.CharacterEllipsis, Shrink = 1f, MinWidth = 0f }],
                        },
                        Button.Standard(usageRatio >= 1 ? "Usage: Budget used up" : usageRatio >= UsageLog.WarnAt ? $"Usage: {usageRatio * 100:F0}%" : "Usage", () => showUsage.Value = true),
                        Button.Standard(health.Value.Issues switch
                        {
                            0 => "Settings",
                            1 => "Settings: 1 vault problem",
                            var n => $"Settings: {n} vault problems",
                        }, () =>
                        {
                            pathField.Value = vaultPath.Peek() ?? AppSettings.SuggestedVaultPath;
                            message.Value = null;
                            showSettings.Value = true;
                        }),
                    ],
                },
                new BoxEl { Direction = 0, Gap = Spacing.S, Wrap = true, Children = chips.ToArray() },
                new BoxEl
                {
                    Direction = 0,
                    Gap = Spacing.M,
                    Children =
                    [
                        Cell(Caption(""), lead: true),
                        .. Columns.Select(s => (Element)Cell(HStack(Spacing.S, BodyStrong(s.ToString()), Caption(ColumnCount(s, all, filter.Value))))),
                    ],
                },
                Divider(),
                ScrollView(VStack(0f,
                [
                    .. rows,
                    new BoxEl
                    {
                        Key = "dropped",
                        Direction = 1,
                        Margin = new Edges4(0f, Spacing.M, 0f, Spacing.XL),
                        Corners = Radii.CardAll,
                        BrushTransitionMs = 83f,
                        Fill = Prop.Of(() => dropOver.Value == "dropped" ? Tok.AccentSubtle : ColorF.Transparent),
                        DropTarget = Target("dropped", Status.Dropped, null, "Drop"),
                        Children =
                        [
                            // Re-pushed slots, because Expander.Create freezes its header and the count changes.
                            Embed.Comp(new Expander.ExpanderSlots(
                                new TextEl($"Dropped ({droppedCount})") { Size = 14f },
                                VStack(Spacing.S, Flow.For<Thought>(() => dropped.Value, t => t.Title, (t, _) => Card(t))),
                                null), () => new Expander()),
                        ],
                    },
                ])),
            ],
        };

        var open = selected.Value is { } title ? all.FirstOrDefault(t => t.Title == title) : null;
        var layers = new List<Element> { board };
        // Over the Board, not beside it, so opening a Thought moves nothing.
        if (open is not null)
            layers.Add(new BoxEl
            {
                Key = "panel",
                OnRealized = h => panelBox.Value = h,
                Direction = 1,
                Width = PanelWidth,
                JustifySelf = FlexAlign.End,
                AlignSelf = FlexAlign.Stretch,
                Margin = Edges4.All(Spacing.M),
                Fill = Tok.FillSolidTertiary,
                Corners = Radii.OverlayAll,
                Shadow = Elevation.Dialog,
                Enter = new EnterExit(Dx: PanelSlide, Opacity: 0f, Active: true),
                Exit = new EnterExit(Dx: PanelSlide, Opacity: 0f, Active: true),
                Transition = MotionTok.PaneOpen,
                Children = [Embed.Comp(open, () => new ThoughtPanel(panelActions))],
            });
        return new BoxEl { Key = "board-stack", ZStack = true, Grow = 1f, Children = layers.ToArray() };
    }

    static string ColumnCount(Status status, List<Thought> all, string? filter)
    {
        int shown = all.Count(t => t.Status == status && (filter is null || (t.Project ?? Unsorted) == filter));
        // Doing's limit is across the whole Board, so its count is too.
        return status == Status.Doing ? $"{all.Count(t => t.Status == Status.Doing)}/{DoingLimit}" : shown.ToString();
    }

    // A Thought can link to a Project with no note, for example one edited in Obsidian: it still gets a row, so nothing hides.
    static List<string> RowNames(List<string> projects, List<Thought> all)
    {
        var names = new List<string>(projects);
        foreach (var t in all)
            if (t.Project is { } p && !names.Contains(p)) names.Add(p);
        names.Add(Unsorted);
        return names;
    }

    static List<Element> HealthRows(VaultHealth health)
    {
        if (health.Issues == 0) return [Body("Every link leads somewhere, and every name is used once.")];
        var rows = new List<Element>();
        foreach (var name in health.DuplicateNames)
            rows.Add(Body($"Two notes are called {name}, so links to it may reach the wrong one. Rename one in Obsidian."));
        foreach (var link in health.BrokenLinks.Take(MaxHealthRows))
            rows.Add(Body($"{link.Note} links to {link.Target}, which doesn't exist."));
        if (health.BrokenLinks.Count > MaxHealthRows)
            rows.Add(Caption($"And {health.BrokenLinks.Count - MaxHealthRows} more broken links."));
        return rows;
    }

    // The vault's folders plus this app's own files, whose changes never affect what's on the Board.
    static string? BudgetNote(double ratio) =>
        ratio >= 1 ? "AI paused"
        : ratio >= UsageLog.WarnAt ? $"{ratio * 100:F0}% of Budget used"
        : null;

    static bool IsSettingsPath(string? relative) =>
        relative is not null && relative.Split(Path.DirectorySeparatorChar)[0].StartsWith('.');

    static VaultHealth LoadHealth(string? path) => path is null ? new VaultHealth([], []) : new Vault(path).CheckHealth();
    static List<Thought> LoadThoughts(string? path) => path is null ? [] : new Vault(path).ReadThoughts();
    static List<Connection> LoadConnections(string? path) => path is null ? [] : new Vault(path).ReadConnections();
    static List<string> LoadProjects(string? path) => path is null ? [] : new Vault(path).ReadProjects();
    static List<Thought> DroppedOf(List<Thought> all) => all.Where(t => t.Status == Status.Dropped).ToList();

    static BoxEl Chip(string label, bool on, Action onClick) => new()
    {
        Key = "chip-" + label,
        Padding = new Edges4(Spacing.M, Spacing.XS, Spacing.M, Spacing.XS),
        Corners = Radii.PillAll,
        Fill = on ? Tok.AccentDefault : Tok.FillControlDefault,
        HoverFill = on ? Tok.AccentDefault : Tok.FillControlSecondary,
        PressedFill = on ? Tok.AccentDefault : Tok.FillControlTertiary,
        Cursor = CursorId.Hand,
        Role = AutomationRole.Button,
        Focusable = true,
        OnClick = onClick,
        OnKeyDown = e =>
        {
            if (e.KeyCode is 13 or 32) { onClick(); e.Handled = true; }
        },
        Children = [new TextEl(label) { Size = 14f, Color = on ? Tok.OnAccent : Tok.TextPrimary }],
    };
}
