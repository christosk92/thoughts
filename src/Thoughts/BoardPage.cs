using FluentGpu.Controls;
using FluentGpu.Dsl;
using FluentGpu.Foundation;
using FluentGpu.Hooks;
using static FluentGpu.Dsl.Ui;

namespace Thoughts;

sealed class BoardPage(AppSettings settings, string settingsFile, bool canSave) : Component
{
    static readonly Status[] Columns = [Status.Inbox, Status.Parked, Status.Next, Status.Doing, Status.Done];

    public override Element Render()
    {
        var vaultPath = UseSignal(settings.VaultPath);
        var showSettings = UseSignal(settings.VaultPath is null);
        var thoughts = UseSignal(LoadThoughts(settings.VaultPath));
        var capture = UseSignal("");
        var saved = UseSignal<string?>(null);
        var pathField = UseSignal(settings.VaultPath ?? AppSettings.SuggestedVaultPath);
        var captureBox = UseRef(NodeHandle.Null);
        var hooks = UseContext(InputHooks.Current);

        // Open and type: capture is the first thing the app is for.
        UseLayoutEffect(() =>
        {
            if (showSettings.Value || captureBox.Value.IsNull) return;
            var first = hooks.FirstFocusableIn?.Invoke(captureBox.Value) ?? NodeHandle.Null;
            if (!first.IsNull) hooks.FocusNode?.Invoke(first, false);
        });

        void Capture(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || vaultPath.Peek() is not { } path) return;
            try
            {
                var thought = new Vault(path).Capture(text);
                thoughts.Value = [thought, .. thoughts.Peek()];
                capture.Value = "";
                saved.Value = "Saved to Inbox";
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The text stays in the box, so nothing is lost.
                saved.Value = "Not saved: " + e.Message;
            }
        }

        void UseVault()
        {
            string path = pathField.Peek().Trim();
            if (path.Length == 0) return;
            settings.VaultPath = path;
            if (canSave) settings.Save(settingsFile);
            vaultPath.Value = path;
            thoughts.Value = LoadThoughts(path);
            saved.Value = null;
            showSettings.Value = false;
        }

        if (showSettings.Value)
            return new BoxEl
            {
                Key = "settings",
                Direction = 1,
                Grow = 1f,
                Gap = Spacing.L,
                Padding = Edges4.All(Spacing.XXL),
                Fill = Tok.FillSolidBase,
                Children =
                [
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
                        vaultPath.Value is null ? new BoxEl() : Button.Standard("Cancel", () => showSettings.Value = false)),
                ],
            };

        var all = thoughts.Value;
        return new BoxEl
        {
            Key = "board",
            Direction = 1,
            Grow = 1f,
            Gap = Spacing.L,
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
                        // The slot is always there, so the message appearing moves nothing.
                        new BoxEl
                        {
                            Key = "saved",
                            Grow = 1f,
                            Shrink = 1f,
                            MinWidth = 0f,
                            Children = [new TextEl(saved.Value ?? "") { Size = 14f, Color = Tok.TextSecondary, MaxLines = 1, Trim = TextTrim.CharacterEllipsis, Shrink = 1f, MinWidth = 0f }],
                        },
                        Button.Standard("Settings", () =>
                        {
                            pathField.Value = vaultPath.Peek() ?? AppSettings.SuggestedVaultPath;
                            showSettings.Value = true;
                        }),
                    ],
                },
                new BoxEl
                {
                    Direction = 0,
                    Grow = 1f,
                    Shrink = 1f,
                    MinHeight = 0f,
                    Gap = Spacing.M,
                    Children = Columns.Select(s => (Element)Column(s, all.Where(t => t.Status == s).ToList())).ToArray(),
                },
            ],
        };
    }

    static List<Thought> LoadThoughts(string? path) => path is null ? [] : new Vault(path).ReadThoughts();

    static BoxEl Column(Status status, List<Thought> items) => new()
    {
        Key = "col-" + status,
        Direction = 1,
        Grow = 1f,
        Shrink = 1f,
        Basis = 0f,
        MinWidth = 0f,
        Gap = Spacing.S,
        Children =
        [
            HStack(Spacing.S, BodyStrong(status.ToString()), Caption(items.Count.ToString())),
            ScrollView(VStack(Spacing.S, items.Select(t => (Element)Card(t)).ToArray())),
        ],
    };

    static BoxEl Card(Thought t) => new()
    {
        Key = "card-" + t.Title,
        Padding = Edges4.All(Spacing.M),
        Fill = Tok.FillCardDefault,
        BorderColor = Tok.StrokeCardDefault,
        BorderWidth = 1f,
        Corners = Radii.CardAll,
        Children = [Body(t.Title) with { MaxLines = 3, Trim = TextTrim.CharacterEllipsis }],
    };
}
