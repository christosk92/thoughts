// PROTOTYPE, throw away. Question: what should the Board look like?
// Three structurally different layouts of the same fake Thoughts, switched from the bar at the bottom.
// Everything lives in memory: no vault, no AI. Run with run.ps1 next to this file, never plain dotnet run:
// that would write build output into the engine checkout.
using FluentGpu;
using FluentGpu.Animation;
using FluentGpu.Controls;
using FluentGpu.Dsl;
using FluentGpu.Foundation;
using FluentGpu.Hooks;
using FluentGpu.Signals;
using static FluentGpu.Dsl.Ui;

static class Program
{
    public static int StartVariant;
    public static string? StartThought;
    public static bool StartReview;
    public static bool ShowSwitcher;

    [STAThread]
    static void Main(string[] args)
    {
        string? Arg(string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i < args.Length - 1 ? args[i + 1] : null;
        }

        StartVariant = "ABC".IndexOf(Arg("--variant") ?? "B", StringComparison.Ordinal);
        ShowSwitcher = args.Contains("--switcher");
        StartThought = Arg("--open");
        StartReview = args.Contains("--review");
        if (Arg("--theme") == "light") Tok.Use(ThemeKind.Light); else Tok.Use(ThemeKind.Dark);

        var options = new AppOptions { Title = "Thoughts (prototype)", Width = 1360, Height = 860 };
        if (Arg("--screenshot") is { } shot)
            FluentAppHarness.Run(() => new Root(), options, new HarnessOptions { Frames = 180, Screenshot = shot, SurfaceSizeDip = new Size2(1360, 860) });
        else
            FluentApp.Run(() => new Root(), options);
    }
}

enum Status { Inbox, Parked, Next, Doing, Done, Dropped }

sealed record Thought(
    string Id, string Text, string Project, Status Status, string Age,
    string[] Related, string[] FollowUps, string? Person = null, string? Echo = null, bool KeepLocal = false);

static class Fake
{
    public static readonly string[] Projects = ["Atlas", "Bulletin", "Wavee", "Personal", "Unsorted"];

    public static readonly List<Thought> Thoughts =
    [
        new("sso", "Atlas needs SSO before the first customer pilot", "Atlas", Status.Doing, "3 days ago", ["tenants", "pilot"],
            ["Must also handle multiple Azure tenants", "Ask whether the pilot customer already uses Entra"]),
        new("tenants", "Write down how tenant switching should work", "Atlas", Status.Next, "2 days ago", ["sso"], []),
        new("pilot", "Pick the first customer for the Atlas pilot", "Atlas", Status.Parked, "3 weeks ago", ["sso"], [], "Sam"),
        new("wizard", "Atlas wizard feels too long, try fewer steps", "Atlas", Status.Inbox, "today", [], [], null, "You wrote something like this 3 times since August"),
        new("ann-audience", "Bulletin should target by role, not only everyone", "Bulletin", Status.Next, "1 week ago", ["ann-read"], ["Could reuse the existing role picker"]),
        new("ann-read", "Show who has read a bulletin", "Bulletin", Status.Parked, "1 month ago", ["ann-audience"], []),
        new("ann-design", "One-page design doc for Bulletin, as Sam asked", "Bulletin", Status.Doing, "yesterday", ["sam-talk"], [], "Sam"),
        new("lyrics", "Synced lyrics for Wavee", "Wavee", Status.Parked, "2 months ago", ["zune-type"], ["Karaoke highlighting per word"], null, "You came back to this 4 times and never started it"),
        new("zune-type", "Zune theme type still feels small on the album page", "Wavee", Status.Inbox, "today", ["lyrics"], []),
        new("video", "Wavee video playback stutters when resizing", "Wavee", Status.Done, "2 weeks ago", [], []),
        new("sam-talk", "Plan the team offsite with Sam", "Personal", Status.Inbox, "today", ["ann-design"], [], "Sam", null, true),
        new("sleep", "Water the plants", "Personal", Status.Next, "today", ["sam-talk"], [], null, null, true),
        new("gp", "Book the car service", "Personal", Status.Parked, "today", [], [], null, null, true),
        new("rewrite", "Rewrite the whole Atlas API in one go", "Atlas", Status.Dropped, "1 month ago", ["sso"], []),
        new("todo-app", "Maybe a to-do app for Wavee playlists?", "Unsorted", Status.Inbox, "yesterday", [], []),
    ];
}

sealed class Root : Component
{
    public override Element Render() => OverlayHost.Create(Embed.Comp(() => new Board()));
}

sealed class Board : Component
{
    static readonly Status[] Flow6 = [Status.Inbox, Status.Parked, Status.Next, Status.Doing, Status.Done, Status.Dropped];
    static readonly string[] VariantNames = ["A: Status columns", "B: Project rows", "C: Now and the bucket"];
    const int DoingLimit = 2;

    public override Element Render()
    {
        var thoughts = UseSignal(Fake.Thoughts);
        var variant = UseSignal(Program.StartVariant);
        var selected = UseSignal(Program.StartThought);
        var filter = UseSignal<string?>(null);
        var notice = UseSignal<string?>(null);
        var capture = UseSignal("");
        var review = UseSignal<List<string>?>(Program.StartReview ? ReviewQueue(Fake.Thoughts) : null);
        var reviewPos = UseSignal(0);

        var all = thoughts.Value;
        var shown = filter.Value is { } f ? all.Where(t => t.Project == f).ToList() : all;

        void Open(string id) => selected.Value = id;

        void SetStatus(string id, Status next)
        {
            var cur = thoughts.Peek();
            var t = cur.First(x => x.Id == id);
            if (next == Status.Doing && t.Status != Status.Doing && cur.Count(x => x.Status == Status.Doing) >= DoingLimit)
            {
                notice.Value = $"Doing is full ({DoingLimit}). Move something out first.";
                return;
            }
            notice.Value = null;
            thoughts.Value = cur.Select(x => x.Id == id ? x with { Status = next } : x).ToList();
        }

        void Add(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var fresh = new Thought(Guid.NewGuid().ToString("N"), text.Trim(), "Unsorted", Status.Inbox, "just now", [], []);
            thoughts.Value = [fresh, .. thoughts.Peek()];
            capture.Value = "";
        }

        // The Thought card every layout shares, so only the structure differs between variants.
        BoxEl Card(Thought t) => (new BoxEl
        {
            Key = "card-" + t.Id,
            Direction = 1,
            Gap = Spacing.S,
            Padding = Edges4.All(Spacing.M),
            Fill = Tok.FillCardDefault,
            BorderColor = selected.Value == t.Id ? Tok.AccentDefault : Tok.StrokeCardDefault,
            BorderWidth = 1f,
            Corners = Radii.CardAll,
            OnClick = () => Open(t.Id),
            Children =
            [
                Body(t.Text) with { MaxLines = 3, Trim = TextTrim.CharacterEllipsis },
                ProjectTag(t),
            ],
        }).Interactive(Interaction.Card);

        Element Header()
        {
            var filters = new List<Element> { Chip("All", filter.Value is null, () => filter.Value = null) };
            foreach (var p in Fake.Projects)
                filters.Add(Chip(p, filter.Value == p, () => filter.Value = p));

            int due = ReviewQueue(all).Count;
            return VStack(Spacing.M,
                HStack(Spacing.M,
                    TextBox.Create(capture, null, new TextBox.TextBoxOptions
                    {
                        Placeholder = "Drop a thought here and press Enter",
                        Width = 620f,
                        OnCommit = Add,
                    }),
                    Button.Accent("Add", () => Add(capture.Peek())),
                    new BoxEl { Grow = 1f },
                    Button.Standard($"Review ({due})", () =>
                    {
                        reviewPos.Value = 0;
                        review.Value = ReviewQueue(thoughts.Peek());
                    })),
                new BoxEl { Direction = 0, Gap = Spacing.S, Wrap = true, Children = filters.ToArray() });
        }

        Element VariantA()
        {
            var columns = Flow6.Select(s =>
            {
                var items = shown.Where(t => t.Status == s).ToList();
                string count = s == Status.Doing ? $"{all.Count(t => t.Status == s)}/{DoingLimit}" : items.Count.ToString();
                return (Element)new BoxEl
                {
                    Key = "col-" + s,
                    Direction = 1,
                    Grow = 1f,
                    Shrink = 1f,
                    Basis = 0f,
                    MinWidth = 0f,
                    Gap = Spacing.S,
                    Children =
                    [
                        HStack(Spacing.S, BodyStrong(s.ToString()), Caption(count)),
                        ScrollView(VStack(Spacing.S, items.Select(t => (Element)Card(t)).ToArray())),
                    ],
                };
            }).ToArray();
            return new BoxEl { Direction = 0, Grow = 1f, Gap = Spacing.M, Children = columns };
        }

        Element VariantB()
        {
            Status[] statuses = [Status.Inbox, Status.Parked, Status.Next, Status.Doing, Status.Done];
            BoxEl Cell(Element child, bool lead = false) => new()
            {
                Direction = 1,
                Gap = Spacing.S,
                Width = lead ? 130f : float.NaN,
                Grow = lead ? 0f : 1f,
                Shrink = lead ? 0f : 1f,
                Basis = lead ? float.NaN : 0f,
                MinWidth = 0f,
                Children = [child],
            };

            var rows = new List<Element>
            {
                HStack(Spacing.M, [Cell(Caption(""), lead: true), .. statuses.Select(s => (Element)Cell(BodyStrong(s.ToString())))]),
            };
            foreach (var p in Fake.Projects)
            {
                if (filter.Value is { } only && only != p) continue;
                rows.Add(Divider());
                rows.Add(new BoxEl
                {
                    Key = "row-" + p,
                    Direction = 0,
                    Gap = Spacing.M,
                    Children =
                    [
                        Cell(BodyStrong(p), lead: true),
                        .. statuses.Select(s => (Element)Cell(VStack(Spacing.S,
                            all.Where(t => t.Project == p && t.Status == s).Select(t => (Element)Card(t)).ToArray()))),
                    ],
                });
            }
            return ScrollView(VStack(Spacing.M, rows.ToArray()));
        }

        Element VariantC()
        {
            Element Row(Thought t) => (new BoxEl
            {
                Key = "row-" + t.Id,
                Direction = 0,
                Gap = Spacing.M,
                AlignItems = FlexAlign.Center,
                Padding = new Edges4(Spacing.S, Spacing.S, Spacing.S, Spacing.S),
                Corners = Radii.ControlAll,
                OnClick = () => Open(t.Id),
                Children =
                [
                    new TextEl(t.Text) { Size = 14f, Grow = 1f, Shrink = 1f, MinWidth = 0f, MaxLines = 1, Trim = TextTrim.CharacterEllipsis },
                    ProjectTag(t),
                    Caption(t.Age),
                ],
            }).Interactive(Interaction.ListRow);

            Element Big(Thought t) => (new BoxEl
            {
                Key = "big-" + t.Id,
                Direction = 1,
                Gap = Spacing.M,
                Grow = 1f,
                Shrink = 1f,
                Basis = 0f,
                MinWidth = 0f,
                Padding = Edges4.All(Spacing.XL),
                Fill = Tok.FillCardDefault,
                BorderColor = Tok.StrokeCardDefault,
                BorderWidth = 1f,
                Corners = Radii.CardAll,
                OnClick = () => Open(t.Id),
                Children =
                [
                    new TextEl(t.Text) { Size = 20f, Weight = 600, Wrap = TextWrap.Wrap },
                    ProjectTag(t),
                    Caption($"{t.FollowUps.Length} follow-ups, {t.Related.Length} related"),
                ],
            }).Interactive(Interaction.Card);

            var doing = shown.Where(t => t.Status == Status.Doing).Select(Big).ToList();
            while (doing.Count < DoingLimit)
                doing.Add(new BoxEl
                {
                    Key = "free-" + doing.Count,
                    Grow = 1f,
                    Shrink = 1f,
                    Basis = 0f,
                    MinWidth = 0f,
                    Justify = FlexJustify.Center,
                    AlignItems = FlexAlign.Center,
                    BorderColor = Tok.StrokeCardDefault,
                    BorderWidth = 1f,
                    Corners = Radii.CardAll,
                    Children = [Caption("Free slot")],
                });

            Element Section(string title, IEnumerable<Thought> items)
            {
                var list = items.ToList();
                return VStack(Spacing.XS,
                [
                    HStack(Spacing.S, BodyStrong(title), Caption(list.Count.ToString())),
                    .. list.Select(Row),
                ]);
            }

            return new BoxEl
            {
                Direction = 0,
                Grow = 1f,
                Gap = Spacing.XXXL,
                Children =
                [
                    new BoxEl
                    {
                        Direction = 1,
                        Grow = 3f,
                        Shrink = 1f,
                        Basis = 0f,
                        MinWidth = 0f,
                        Gap = Spacing.XL,
                        Children =
                        [
                            Subtitle($"Doing now ({all.Count(t => t.Status == Status.Doing)}/{DoingLimit})"),
                            new BoxEl { Direction = 0, Gap = Spacing.M, Height = 220f, Children = doing.ToArray() },
                            Section("Next", shown.Where(t => t.Status == Status.Next)),
                            Section("Done", shown.Where(t => t.Status == Status.Done)),
                        ],
                    },
                    new BoxEl
                    {
                        Direction = 1,
                        Grow = 2f,
                        Shrink = 1f,
                        Basis = 0f,
                        MinWidth = 0f,
                        Gap = Spacing.L,
                        Children =
                        [
                            Subtitle("The bucket"),
                            ScrollView(VStack(Spacing.XL,
                                Section("Inbox", shown.Where(t => t.Status == Status.Inbox)),
                                Section("Parked", shown.Where(t => t.Status == Status.Parked)),
                                Section("Dropped", shown.Where(t => t.Status == Status.Dropped)))),
                        ],
                    },
                ],
            };
        }

        // Shared by the drill-in and Review: everything the app knows about one Thought.
        List<Element> Details(Thought t)
        {
            var parts = new List<Element>
            {
                new TextEl(t.Text) { Size = 20f, Weight = 600, Wrap = TextWrap.Wrap },
                HStack(Spacing.M, ProjectTag(t), Caption(t.Age)),
            };
            if (t.Person is { } who) parts.Add(Caption($"About: {who}"));
            if (t.KeepLocal) parts.Add(Caption("Keep local: never sent to the AI"));
            if (t.Echo is { } echo)
                parts.Add(new BoxEl
                {
                    Padding = Edges4.All(Spacing.M),
                    Fill = Tok.FillCardSecondary,
                    Corners = Radii.ControlAll,
                    Children = [Body(echo)],
                });
            if (t.FollowUps.Length > 0)
            {
                parts.Add(BodyStrong("Follow-ups"));
                parts.AddRange(t.FollowUps.Select(x => (Element)Body(x)));
            }
            var related = t.Related.Select(id => all.First(x => x.Id == id)).ToList();
            if (related.Count > 0)
            {
                parts.Add(BodyStrong("Related Thoughts"));
                parts.AddRange(related.Select(r => (Element)(new BoxEl
                {
                    Key = "rel-" + r.Id,
                    Direction = 1,
                    Gap = Spacing.XS,
                    Padding = Edges4.All(Spacing.S),
                    Corners = Radii.ControlAll,
                    OnClick = () => Open(r.Id),
                    Children = [Body(r.Text), Caption($"{r.Status} in {r.Project}")],
                }).Interactive(Interaction.ListRow)));
            }
            return parts;
        }

        Element Panel(Thought t)
        {
            var tint = ProjectColor(t.Project);

            Element Step(Status s)
            {
                bool on = t.Status == s;
                return new BoxEl
                {
                    Key = "step-" + s,
                    Grow = 1f,
                    Shrink = 1f,
                    Basis = 0f,
                    MinWidth = 0f,
                    Height = 34f,
                    Justify = FlexJustify.Center,
                    AlignItems = FlexAlign.Center,
                    Corners = Radii.ControlAll,
                    Fill = on ? Tok.AccentDefault : ColorF.Transparent,
                    HoverFill = on ? Tok.AccentDefault : Tok.FillSubtleSecondary,
                    PressedFill = on ? Tok.AccentDefault : Tok.FillSubtleTertiary,
                    Cursor = CursorId.Hand,
                    OnClick = () => SetStatus(t.Id, s),
                    Children = [new TextEl(s.ToString()) { Size = 14f, Weight = (ushort)(on ? 600 : 400), Color = on ? Tok.OnAccent : Tok.TextPrimary }],
                };
            }

            Element Pill(string text) => new BoxEl
            {
                Padding = new Edges4(Spacing.S, Spacing.XXS, Spacing.S, Spacing.XXS),
                Corners = Radii.PillAll,
                Fill = Tok.FillControlDefault,
                Children = [Caption(text)],
            };

            var head = new List<Element>
            {
                new BoxEl
                {
                    Direction = 0,
                    Gap = Spacing.M,
                    AlignItems = FlexAlign.Center,
                    Children =
                    [
                        ProjectTag(t),
                        Caption(t.Age),
                        new BoxEl { Grow = 1f },
                        IconButton.Create(Icons.Cancel, () => selected.Value = null, size: ControlSize.Small),
                    ],
                },
                new TextEl(t.Text) { Size = 24f, LineHeight = 30f, Weight = 600, Wrap = TextWrap.Wrap },
            };
            var pills = new List<Element>();
            if (t.Person is { } who) pills.Add(Pill("About " + who));
            if (t.KeepLocal) pills.Add(Pill("Keep local"));
            if (pills.Count > 0) head.Add(new BoxEl { Direction = 0, Gap = Spacing.S, Wrap = true, Children = pills.ToArray() });

            var body = new List<Element>
            {
                new BoxEl
                {
                    Direction = 0,
                    Gap = Spacing.XXS,
                    Padding = Edges4.All(Spacing.XS),
                    Corners = Radii.CardAll,
                    Fill = Tok.FillCardDefault,
                    Children = [Step(Status.Inbox), Step(Status.Parked), Step(Status.Next), Step(Status.Doing), Step(Status.Done)],
                },
            };
            if (t.Echo is { } echo)
                body.Add(new BoxEl
                {
                    Direction = 0,
                    Gap = Spacing.M,
                    Padding = Edges4.All(Spacing.M),
                    Corners = Radii.CardAll,
                    Fill = tint with { A = 0.12f },
                    Children = [Icon(Icons.History, 16f, tint), Body(echo) with { Shrink = 1f, MinWidth = 0f }],
                });
            if (t.FollowUps.Length > 0)
                body.Add(VStack(Spacing.S,
                [
                    BodyStrong("Follow-ups"),
                    .. t.FollowUps.Select(x => (Element)new BoxEl
                    {
                        Direction = 0,
                        Gap = Spacing.M,
                        Children =
                        [
                            new BoxEl { Width = 6f, Height = 6f, Margin = new Edges4(0f, 7f, 0f, 0f), Corners = Radii.FullAll, Fill = tint },
                            Body(x) with { Shrink = 1f, MinWidth = 0f },
                        ],
                    }),
                ]));
            var related = t.Related.Select(id => all.First(x => x.Id == id)).ToList();
            if (related.Count > 0)
                body.Add(VStack(Spacing.S,
                [
                    BodyStrong("Related Thoughts"),
                    .. related.Select(r => (Element)new BoxEl
                    {
                        Key = "rel-" + r.Id,
                        Direction = 0,
                        Gap = Spacing.M,
                        AlignItems = FlexAlign.Center,
                        Padding = Edges4.All(Spacing.M),
                        Corners = Radii.ControlAll,
                        Fill = Tok.FillCardDefault,
                        HoverFill = Tok.FillCardSecondary,
                        PressedFill = Tok.FillCardSecondary,
                        Cursor = CursorId.Hand,
                        OnClick = () => Open(r.Id),
                        Children =
                        [
                            new BoxEl
                            {
                                Direction = 1,
                                Gap = Spacing.XXS,
                                Grow = 1f,
                                Shrink = 1f,
                                MinWidth = 0f,
                                Children = [Body(r.Text), Caption($"{r.Status} in {r.Project}")],
                            },
                            Icon(Icons.ChevronRight, 12f, Tok.TextTertiary),
                        ],
                    }),
                ]));

            return new BoxEl
            {
                Key = "panel",
                Direction = 1,
                Width = 460f,
                JustifySelf = FlexAlign.End,
                AlignSelf = FlexAlign.Stretch,
                Margin = Edges4.All(Spacing.M),
                Fill = Tok.FillSolidTertiary,
                Corners = Radii.OverlayAll,
                Shadow = Elevation.Dialog,
                Enter = new EnterExit(Dx: 32f, Opacity: 0f, Active: true),
                Exit = new EnterExit(Dx: 32f, Opacity: 0f, Active: true),
                Transition = MotionTok.PaneOpen,
                Children =
                [
                    new BoxEl
                    {
                        Direction = 1,
                        Gap = Spacing.M,
                        Padding = new Edges4(Spacing.XXL, Spacing.L, Spacing.XXL, Spacing.XL),
                        Corners = Radii.OverlayTop,
                        Fill = tint with { A = 0.16f },
                        Children = head.ToArray(),
                    },
                    ScrollView(VStack(Spacing.XL, body.ToArray()) with { Padding = new Edges4(Spacing.XXL, Spacing.XL, Spacing.XXL, Spacing.XL) }),
                    Divider(),
                    new BoxEl
                    {
                        Direction = 0,
                        Gap = Spacing.S,
                        AlignItems = FlexAlign.Center,
                        Padding = new Edges4(Spacing.XXL, Spacing.M, Spacing.XXL, Spacing.M),
                        Children =
                        [
                            Button.Standard("Open in Obsidian", () => notice.Value = "Prototype: this would open the note in Obsidian."),
                            new BoxEl { Grow = 1f },
                            t.Status == Status.Dropped
                                ? Button.Subtle("Back to Inbox", () => SetStatus(t.Id, Status.Inbox))
                                : Button.Subtle("Drop", () => SetStatus(t.Id, Status.Dropped)),
                        ],
                    },
                ],
            };
        }

        Element Review(List<string> queue)
        {
            int pos = reviewPos.Value;
            if (pos >= queue.Count)
                return new BoxEl
                {
                    Key = "review",
                    Direction = 1,
                    Grow = 1f,
                    Gap = Spacing.L,
                    Justify = FlexJustify.Center,
                    AlignItems = FlexAlign.Center,
                    Fill = Tok.FillSolidBase,
                    Children =
                    [
                        Title("Review done"),
                        Body($"You went through {queue.Count} Thoughts."),
                        Button.Accent("Back to the Board", () => review.Value = null),
                    ],
                };

            var t = all.First(x => x.Id == queue[pos]);
            void Choose(Status s)
            {
                SetStatus(t.Id, s);
                if (notice.Peek() is null) reviewPos.Value = pos + 1;
            }

            return new BoxEl
            {
                Key = "review",
                Direction = 1,
                Grow = 1f,
                AlignItems = FlexAlign.Center,
                Fill = Tok.FillSolidBase,
                Padding = Spacing.PagePadWide,
                Children =
                [
                    new BoxEl
                    {
                        Direction = 1,
                        Width = 680f,
                        Grow = 1f,
                        Gap = Spacing.L,
                        Children =
                        [
                            new BoxEl
                            {
                                Direction = 0,
                                AlignItems = FlexAlign.Center,
                                Children =
                                [
                                    new BoxEl { Grow = 1f, Children = [Caption($"Review: {pos + 1} of {queue.Count}, now {t.Status}")] },
                                    Button.Subtle("Stop", () => review.Value = null),
                                ],
                            },
                            ScrollView(VStack(Spacing.L, Details(t).ToArray())),
                            notice.Value is { } n ? Body(n) : new BoxEl { Height = 20f },
                            HStack(Spacing.S,
                                Button.Standard("Keep parked", () => Choose(Status.Parked)),
                                Button.Standard("Next", () => Choose(Status.Next)),
                                Button.Accent("Doing", () => Choose(Status.Doing)),
                                Button.Standard("Drop", () => Choose(Status.Dropped)),
                                new BoxEl { Grow = 1f },
                                Button.Subtle("Skip", () => reviewPos.Value = pos + 1)),
                        ],
                    },
                ],
            };
        }

        Element Switcher() => new BoxEl
        {
            Key = "switcher",
            Direction = 0,
            Gap = Spacing.S,
            AlignItems = FlexAlign.Center,
            JustifySelf = FlexAlign.Center,
            AlignSelf = FlexAlign.End,
            Margin = new Edges4(0f, 0f, 0f, Spacing.L),
            Padding = new Edges4(Spacing.S, Spacing.XS, Spacing.S, Spacing.XS),
            Fill = Tok.AccentDefault,
            Corners = Radii.FullAll,
            Children =
            [
                Button.Standard("<", () => variant.Value = (variant.Peek() + 2) % 3),
                new TextEl("Prototype " + VariantNames[variant.Value]) { Size = 14f, Weight = 600, Color = Tok.OnAccent },
                Button.Standard(">", () => variant.Value = (variant.Peek() + 1) % 3),
            ],
        };

        var main = new List<Element> { Header() };
        if (notice.Value is { } message && review.Value is null) main.Add(Body(message));
        main.Add(new BoxEl
        {
            Key = "variant-" + variant.Value,
            Direction = 1,
            Grow = 1f,
            Shrink = 1f,
            MinHeight = 0f,
            Children = [variant.Value switch { 0 => VariantA(), 1 => VariantB(), _ => VariantC() }],
        });

        var layers = new List<Element>
        {
            new BoxEl
            {
                Key = "main",
                Direction = 1,
                Gap = Spacing.L,
                Padding = new Edges4(Spacing.XL, Spacing.XL, Spacing.XL, 0f),
                Children = main.ToArray(),
            },
        };
        if (selected.Value is { } open && all.FirstOrDefault(t => t.Id == open) is { } picked) layers.Add(Panel(picked));
        if (review.Value is { } queue) layers.Add(new BoxEl { Direction = 1, Children = [Review(queue)] });
        else if (Program.ShowSwitcher) layers.Add(Switcher());

        return new BoxEl { ZStack = true, Grow = 1f, Fill = Tok.FillSolidBase, Children = layers.ToArray() };
    }

    static List<string> ReviewQueue(IEnumerable<Thought> thoughts) =>
        thoughts.Where(t => t.Status == Status.Inbox).Concat(thoughts.Where(t => t.Status == Status.Parked)).Select(t => t.Id).ToList();

    static ColorF ProjectColor(string project) => project switch
    {
        "Atlas" => ColorF.FromRgba(96, 165, 250),
        "Bulletin" => ColorF.FromRgba(251, 146, 60),
        "Wavee" => ColorF.FromRgba(167, 139, 250),
        "Personal" => ColorF.FromRgba(52, 211, 153),
        _ => ColorF.FromRgba(148, 163, 184),
    };

    static BoxEl ProjectTag(Thought t) => new()
    {
        Direction = 0,
        Gap = Spacing.XS,
        AlignItems = FlexAlign.Center,
        Children =
        [
            new BoxEl { Width = 8f, Height = 8f, Corners = Radii.FullAll, Fill = ProjectColor(t.Project) },
            Caption(t.Project),
        ],
    };

    static BoxEl Chip(string label, bool on, Action onClick) => new()
    {
        Key = "chip-" + label,
        Padding = new Edges4(Spacing.M, Spacing.XS, Spacing.M, Spacing.XS),
        Corners = Radii.PillAll,
        Fill = on ? Tok.AccentDefault : Tok.FillControlDefault,
        HoverFill = on ? Tok.AccentDefault : Tok.FillControlSecondary,
        PressedFill = on ? Tok.AccentDefault : Tok.FillControlTertiary,
        Cursor = CursorId.Hand,
        OnClick = onClick,
        Children = [new TextEl(label) { Size = 14f, Color = on ? Tok.OnAccent : Tok.TextPrimary }],
    };
}
