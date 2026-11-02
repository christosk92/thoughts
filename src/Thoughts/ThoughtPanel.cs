using FluentGpu.Controls;
using FluentGpu.Dsl;
using FluentGpu.Foundation;
using FluentGpu.Hooks;
using FluentGpu.Signals;
using static FluentGpu.Dsl.Ui;

namespace Thoughts;

/// <summary>What the panel needs from the Board. The callbacks read the Board's signals, so they stay correct across renders.</summary>
sealed record PanelActions(
    Action Close,
    Action<Thought, Status> SetStatus,
    Action<string, string> SaveBody,
    Action<string> FollowLink,
    Action<string> OpenInObsidian,
    Func<string, Task<FanOutAnswer>> SuggestPieces,
    Action<Thought, IReadOnlyList<string>> CreatePieces,
    Func<string, (int Done, int Total)> PieceCount,
    IReadSignal<string?> Message);

sealed record TickedPiece(FanOutPiece Piece, Signal<bool> Ticked);

/// <summary>One Thought: its Status, and its body as preview or Markdown source. The source is the one truth.</summary>
sealed class ThoughtPanel(PanelActions actions) : Component
{
    const float EditorWidth = 412f; // The sheet's width less its padding, and TextBox has no fill mode.
    const float EditorHeight = 420f;
    const float StepHeight = 34f;
    const int SaveDelayMs = 400;
    const int EscapeKey = 27;
    const float CheckBoxTextOffset = 6f; // Lines a 20 px text row up with the middle of the 32 px CheckBox.
    static readonly Status[] Steps = [Status.Inbox, Status.Parked, Status.Next, Status.Doing, Status.Done];

    public override Element Render()
    {
        var t = UseProps<Thought>();
        var body = UseSignal(t.Body);
        var editing = UseSignal(false);
        var unsaved = UseRef(false);
        var title = UseRef(t.Title);
        var post = UsePost();
        var overlay = UseContext(Overlay.Service);
        var fanning = UseSignal(false);
        var fanLoading = UseSignal(false);
        var fanNote = UseSignal<string?>(null);
        var fanPieces = UseSignal<IReadOnlyList<TickedPiece>>([]);

        void ShowPieces(IEnumerable<FanOutPiece> pieces, string? note)
        {
            fanPieces.Value = pieces.Select(p => new TickedPiece(p, new Signal<bool>(false))).ToList();
            fanNote.Value = note;
            fanLoading.Value = false;
        }

        void StartFanOut()
        {
            Save();
            string asked = title.Value;
            fanning.Value = true;
            fanLoading.Value = true;
            ShowPieces([], null);
            fanLoading.Value = true;
            _ = actions.SuggestPieces(asked).ContinueWith(answer => post(() =>
            {
                if (title.Value != asked || !fanning.Peek()) return;
                if (answer.IsCompletedSuccessfully) ShowPieces(answer.Result.Pieces, answer.Result.Note);
                else ShowPieces([], "The AI hit a problem. Nothing changed.");
            }));
        }

        void CreateTicked(Thought from)
        {
            var ticked = fanPieces.Peek().Where(p => p.Ticked.Peek()).Select(p => p.Piece.Text).ToList();
            if (ticked.Count == 0)
            {
                fanNote.Value = "Tick at least one piece first.";
                return;
            }
            actions.CreatePieces(from, ticked);
            fanning.Value = false;
        }

        void Save()
        {
            if (!unsaved.Value) return;
            unsaved.Value = false;
            actions.SaveBody(title.Value, body.Peek());
        }

        // Saves as you type, after a short pause, so a burst of keys is one write.
        var pause = UseMemo(() => new Timer(_ => post(Save), null, Timeout.Infinite, Timeout.Infinite), DepKey.Empty);
        UseEffect(() => () =>
        {
            pause.Dispose();
            Save();
        }, DepKey.Empty);

        // Another Thought, or this one changed elsewhere. Text still waiting to save wins until it's saved: last save wins.
        UseEffect(() =>
        {
            if (t.Title != title.Value)
            {
                Save();
                title.Value = t.Title;
                body.Value = t.Body;
                editing.Value = false;
                fanning.Value = false;
            }
            else if (!unsaved.Value && t.Body != body.Peek().Trim())
            {
                body.Value = t.Body;
            }
        }, DepKey.From(t.Title.GetHashCode(), t.Body.GetHashCode()));

        void Typed(string text)
        {
            unsaved.Value = true;
            pause.Change(SaveDelayMs, Timeout.Infinite);
        }

        var options = UseMemo(() => new MarkdownOptions
        {
            InlineSyntaxes =
            [
                new MarkdownInlineSyntax("wikilink", "[[", "]]", inner =>
                {
                    var parts = inner.Split('|', 2);
                    string shown = parts.Length > 1 ? parts[1] : parts[0];
                    return new MarkdownLink([new MarkdownText(shown)], parts[0].Trim(), "", "wikilink");
                }),
            ],
            OnLinkClicked = link =>
            {
                if (link.Syntax == "wikilink") actions.FollowLink(link.Url);
                else MarkdownLinkPolicy.ConfirmAndOpen(overlay, link.Url);
            },
            OnTaskToggled = (index, on) =>
            {
                body.Value = MarkdownTasks.Toggle(body.Peek(), index, on);
                unsaved.Value = true;
                Save();
            },
        }, DepKey.Empty);

        Element Step(Status s, bool on, Action act) => new BoxEl
        {
            Key = "step-" + s,
            Grow = 1f,
            Shrink = 1f,
            Basis = 0f,
            MinWidth = 0f,
            Height = StepHeight,
            Justify = FlexJustify.Center,
            AlignItems = FlexAlign.Center,
            Corners = Radii.ControlAll,
            Fill = on ? Tok.AccentDefault : ColorF.Transparent,
            HoverFill = on ? Tok.AccentDefault : Tok.FillSubtleSecondary,
            PressedFill = on ? Tok.AccentDefault : Tok.FillSubtleTertiary,
            Cursor = CursorId.Hand,
            Role = AutomationRole.Button,
            Focusable = true,
            OnClick = act,
            OnKeyDown = e =>
            {
                if (e.KeyCode is 13 or 32) { act(); e.Handled = true; }
            },
            Children = [new TextEl(s.ToString()) { Size = 14f, Weight = (ushort)(on ? 600 : 400), Color = on ? Tok.OnAccent : Tok.TextPrimary }],
        };

        Element FanOutSection() => VStack(Spacing.M,
        [
            BodyStrong("Fan out"),
            Body("Tick what becomes its own Thought. This note stays as it is."),
            .. fanLoading.Value
                ? new Element[] { HStack(Spacing.S, ProgressRing.Indeterminate(ProgressRing.MinSize), Caption("Finding the pieces\u2026")) }
                : [],
            .. fanNote.Value is { } note ? new Element[] { Caption(note) with { Color = Tok.TextSecondary, Wrap = TextWrap.Wrap } } : [],
            .. fanPieces.Value.Select((p, i) => (Element)new BoxEl
            {
                Key = "piece-" + i,
                Direction = 0,
                Gap = Spacing.XS,
                AlignItems = FlexAlign.Start,
                Children =
                [
                    CheckBox.Create("", p.Ticked, style: CheckBox.FluentStyle() with { MinWidth = 0f }), // Its 120 minimum is for a label beside the box.
                    VStack(Spacing.XXS,
                    [
                        Body(p.Piece.Text) with { Wrap = TextWrap.Wrap, Shrink = 1f, MinWidth = 0f },
                        .. p.Piece.Why is { Length: > 0 } why ? new Element[] { Caption(why) with { Color = Tok.TextSecondary, Wrap = TextWrap.Wrap } } : [],
                    ]) with { Grow = 1f, Shrink = 1f, MinWidth = 0f, Margin = new Edges4(0f, CheckBoxTextOffset, 0f, 0f) },
                ],
            }),
            HStack(Spacing.S,
            [
                .. fanPieces.Value.Count > 0 ? new Element[] { Button.Accent("Create Thoughts", () => CreateTicked(t)) } : [],
                .. !fanLoading.Value && FanOut.Lines(body.Peek()).Count >= 2 && (fanPieces.Value.Count == 0 || fanPieces.Value.Any(p => p.Piece.Why is not null))
                    ? new Element[] { Button.Standard("Pick lines", () => ShowPieces(FanOut.Lines(body.Peek()).Select(l => new FanOutPiece(l, null)), null)) }
                    : [],
                Button.Subtle("Cancel", () => fanning.Value = false),
            ]),
        ]);

        var (piecesDone, piecesTotal) = actions.PieceCount(t.Title);
        var tint = ProjectTint(t.Project);
        return new BoxEl
        {
            Direction = 1,
            Grow = 1f,
            OnKeyDown = e =>
            {
                if (e.KeyCode == EscapeKey) { actions.Close(); e.Handled = true; }
            },
            Children =
            [
                new BoxEl
                {
                    Direction = 1,
                    Gap = Spacing.M,
                    Padding = new Edges4(Spacing.XXL, Spacing.L, Spacing.XXL, Spacing.XL),
                    Corners = Radii.OverlayTop,
                    Fill = tint with { A = 0.16f },
                    Children =
                    [
                        new BoxEl
                        {
                            Direction = 0,
                            AlignItems = FlexAlign.Center,
                            Children =
                            [
                                new BoxEl { Grow = 1f, Children = [Caption(t.Project ?? "Unsorted")] },
                                IconButton.Create(Icons.Cancel, actions.Close, size: ControlSize.Small),
                            ],
                        },
                        new TextEl(t.Title) { Size = 24f, LineHeight = 30f, Weight = 600, Wrap = TextWrap.Wrap },
                    ],
                },
                ScrollView(VStack(Spacing.XL,
                    new BoxEl
                    {
                        Direction = 0,
                        Gap = Spacing.XXS,
                        Padding = Edges4.All(Spacing.XS),
                        Corners = Radii.CardAll,
                        Fill = Tok.FillCardDefault,
                        Children = Steps.Select(s => Step(s, t.Status == s, () => actions.SetStatus(t, s))).ToArray(),
                    },
                    piecesTotal > 0 ? Caption($"{piecesDone} of {piecesTotal} done") with { Color = Tok.TextSecondary } : new BoxEl { Visible = false },
                    new BoxEl
                    {
                        Direction = 0,
                        AlignItems = FlexAlign.Center,
                        Children =
                        [
                            new BoxEl { Grow = 1f, Children = [BodyStrong("Notes")] },
                            Button.Subtle("Fan out", StartFanOut),
                            Button.Subtle(editing.Value ? "Preview" : "Edit source", () =>
                            {
                                Save();
                                editing.Value = !editing.Peek();
                            }),
                        ],
                    },
                    // Both stay mounted, so switching keeps the caret and undo history, and moves nothing else.
                    fanning.Value ? new BoxEl { Key = "fan-out", Children = [FanOutSection()] } : new BoxEl { Key = "no-fan-out", Visible = false },
                    new BoxEl
                    {
                        Key = "source",
                        Visible = editing.Value,
                        Children =
                        [
                            TextBox.Create(body, Typed, new TextBox.TextBoxOptions
                            {
                                AcceptsReturn = true,
                                Width = EditorWidth,
                                Height = EditorHeight,
                                Placeholder = "Add detail here. Markdown works, and [[links]] reach other notes.",
                                KeepTextOnEscape = true, // A person's words are never thrown away by a key.
                                OnCancel = actions.Close,
                            }),
                        ],
                    },
                    new BoxEl
                    {
                        Key = "preview",
                        Visible = !editing.Value,
                        Children = [MarkdownTextBlock.Create(body, options)],
                    }) with { Padding = new Edges4(Spacing.XXL, Spacing.XL, Spacing.XXL, Spacing.XL) }),
                Divider(),
                new BoxEl
                {
                    Direction = 0,
                    Gap = Spacing.S,
                    AlignItems = FlexAlign.Center,
                    Padding = new Edges4(Spacing.XXL, Spacing.M, Spacing.XXL, Spacing.M),
                    Children =
                    [
                        Button.Standard("Open in Obsidian", () => actions.OpenInObsidian(t.Title)),
                        new BoxEl
                        {
                            Grow = 1f,
                            Shrink = 1f,
                            MinWidth = 0f,
                            Children = [new TextEl("") { Text = Prop.Of(() => actions.Message.Value ?? ""), Size = 12f, Color = Tok.TextSecondary, MaxLines = 2, Wrap = TextWrap.Wrap, Trim = TextTrim.CharacterEllipsis, Shrink = 1f, MinWidth = 0f }],
                        },
                        t.Status == Status.Dropped
                            ? Button.Subtle("Back to Inbox", () => actions.SetStatus(t, Status.Inbox))
                            : Button.Subtle("Drop", () => actions.SetStatus(t, Status.Dropped)),
                    ],
                },
            ],
        };
    }

    // The same name always gets the same colour, without a stored palette.
    static ColorF ProjectTint(string? project)
    {
        if (project is null) return ColorF.FromRgba(148, 163, 184);
        ColorF[] palette =
        [
            ColorF.FromRgba(96, 165, 250), ColorF.FromRgba(251, 146, 60), ColorF.FromRgba(167, 139, 250),
            ColorF.FromRgba(52, 211, 153), ColorF.FromRgba(244, 114, 182), ColorF.FromRgba(250, 204, 21),
        ];
        int hash = 0;
        foreach (char c in project) hash = (hash * 31 + c) & 0x7FFFFFFF;
        return palette[hash % palette.Length];
    }
}
