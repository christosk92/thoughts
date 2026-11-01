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
        var captureBox = UseRef(NodeHandle.Null);
        var hooks = UseContext(InputHooks.Current);
        var overlay = UseContext(Overlay.Service);

        // Open and type: capture is the first thing the app is for.
        UseLayoutEffect(() =>
        {
            if (showSettings.Value || captureBox.Value.IsNull) return;
            var first = hooks.FirstFocusableIn?.Invoke(captureBox.Value) ?? NodeHandle.Null;
            if (!first.IsNull) hooks.FocusNode?.Invoke(first, false);
        });

        void Reload()
        {
            var fresh = LoadThoughts(vaultPath.Peek());
            thoughts.Value = fresh;
            dropped.Value = DroppedOf(fresh);
            projects.Value = LoadProjects(vaultPath.Peek());
        }

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
            bool saved = false;
            Change(v => { v.Capture(text); saved = true; }, "Saved to Inbox");
            if (saved) capture.Value = "";
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
            return items;
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

        BoxEl Card(Thought t) => new()
        {
            Key = "card-" + t.Title,
            Draggable = Drag.Source(ThoughtDrag, () => t),
            Layout = LayoutTransition.Slide,
            Enter = new EnterExit(Sx: 0.96f, Sy: 0.96f, Opacity: 0f, Active: true),
            Transition = MotionTok.StandardEnter,
            Cursor = CursorId.Hand,
            Direction = 0,
            Gap = Spacing.XS,
            AlignItems = FlexAlign.Start,
            Padding = new Edges4(Spacing.M, Spacing.S, Spacing.XS, Spacing.S),
            Fill = Tok.FillCardDefault,
            BorderColor = Tok.StrokeCardDefault,
            BorderWidth = 1f,
            Corners = Radii.CardAll,
            Children =
            [
                new BoxEl
                {
                    Grow = 1f,
                    Shrink = 1f,
                    MinWidth = 0f,
                    Margin = new Edges4(0f, Spacing.XS, 0f, 0f),
                    Children = [Body(t.Title) with { MaxLines = 3, Trim = TextTrim.CharacterEllipsis, Shrink = 1f, MinWidth = 0f }],
                },
                IconButton.Menu(Icons.More, () => CardMenu(t), toolTip: "Status and Project", size: ControlSize.Small),
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
                        ]))),
                ],
            };
        }

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
            var inRow = all.Where(t => (t.Project ?? Unsorted) == name).ToList();
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

        return new BoxEl
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
                        new BoxEl
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
                        },
                        Button.Accent("Add", () => Capture(capture.Peek())),
                        // The slot is always there, so a message appearing moves nothing.
                        new BoxEl
                        {
                            Key = "message",
                            Grow = 1f,
                            Shrink = 1f,
                            MinWidth = 0f,
                            Children = [new TextEl(message.Value ?? "") { Size = 14f, Color = Tok.TextSecondary, MaxLines = 1, Trim = TextTrim.CharacterEllipsis, Shrink = 1f, MinWidth = 0f }],
                        },
                        Button.Standard("Settings", () =>
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

    static List<Thought> LoadThoughts(string? path) => path is null ? [] : new Vault(path).ReadThoughts();
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
