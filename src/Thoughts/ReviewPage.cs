using FluentGpu.Controls;
using FluentGpu.Dsl;
using FluentGpu.Foundation;
using FluentGpu.Hooks;
using static FluentGpu.Dsl.Ui;

namespace Thoughts;

/// <summary>Which Thoughts a Review goes through, oldest first: all of Inbox not yet looked at today, plus Parked ones not
/// looked at for two weeks. Each one is dated as it's looked at, so stopping and starting again carries on from there.</summary>
static class ReviewQueue
{
    public const int ParkedDays = 14;

    public static List<string> Select(IEnumerable<Thought> thoughts, DateTime today)
    {
        string now = today.ToString("yyyy-MM-dd");
        string cutoff = today.AddDays(-ParkedDays).ToString("yyyy-MM-dd");
        return thoughts
            .Where(t => t.Status switch
            {
                Status.Inbox => t.Reviewed != now,
                Status.Parked => string.CompareOrdinal(LastLooked(t), cutoff) <= 0,
                _ => false,
            })
            .OrderBy(t => t.Created, StringComparer.Ordinal)
            .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
            .Select(t => t.Title)
            .ToList();
    }

    // Parked yesterday hasn't been left alone for two weeks, even if it was never reviewed.
    static string LastLooked(Thought t)
    {
        string created = t.Created.Length >= 10 ? t.Created[..10] : "";
        return string.CompareOrdinal(t.Reviewed, created) > 0 ? t.Reviewed : created;
    }
}

sealed record ReviewProps(
    Thought? Current,
    int Position,
    int Count,
    IReadOnlyList<Connection> Connections,
    IReadOnlyList<string> CameFrom,
    IReadOnlyList<string> GrewInto,
    PendingSuggestion? Suggestion,
    ConnectionActions ConnectionActions,
    SuggestionActions SuggestionActions,
    Action<Thought, Status> Choose,
    Action<Thought> Next,
    Action<string> Open,
    Action Stop,
    string? Message);

/// <summary>One Thought at a time, with what the person needs to decide, and a Status to choose.</summary>
sealed class ReviewPage : Component
{
    const float ColumnWidth = 720f;
    const int EscapeKey = 27;
    static readonly Status[] Choices = [Status.Inbox, Status.Parked, Status.Next, Status.Doing, Status.Done, Status.Dropped];

    public override Element Render()
    {
        var p = UseProps<ReviewProps>();
        Element content = p.Current is { } t ? Current(p, t) : VStack(Spacing.M, Title("Review"), Body("That's everything for now."), HStack(Spacing.S, Button.Accent("Back to the Board", p.Stop)));
        return new BoxEl
        {
            Key = "review",
            Direction = 1,
            Grow = 1f,
            Padding = Edges4.All(Spacing.XXL),
            Fill = Tok.FillSolidBase,
            OnKeyDown = e =>
            {
                if (e.KeyCode == EscapeKey) { p.Stop(); e.Handled = true; }
            },
            Children = [ScrollView(new BoxEl { Direction = 1, Children = [new BoxEl { Width = ColumnWidth, AlignSelf = FlexAlign.Start, Direction = 1, Children = [content] }] })],
        };
    }

    static Element Current(ReviewProps p, Thought t) => VStack(Spacing.L,
    [
        new BoxEl
        {
            Direction = 0,
            Gap = Spacing.M,
            AlignItems = FlexAlign.Center,
            Children =
            [
                Button.Standard("Stop", p.Stop),
                Caption($"{p.Position} of {p.Count}. Stopping keeps your place.") with { Color = Tok.TextSecondary },
            ],
        },
        new BoxEl
        {
            Key = "review-title-" + t.Title,
            Enter = new EnterExit(Dy: 8f, Opacity: 0f, Active: true),
            Transition = FluentGpu.Animation.MotionTok.ControlNormal,
            Direction = 1,
            Gap = Spacing.XS,
            Children =
            [
                Caption($"{t.Status} · {t.Project ?? "Unsorted"}") with { Color = Tok.TextSecondary },
                Title(t.Title) with { Wrap = TextWrap.Wrap },
            ],
        },
        MarkdownTextBlock.Create(t.Body),
        .. p.Suggestion is { } pending ? new[] { SuggestionView.Create(t, pending, p.SuggestionActions) } : [],
        .. p.Connections.Count > 0
            ? new Element[]
            {
                new BoxEl
                {
                    Direction = 1,
                    Gap = Spacing.S,
                    Children = [BodyStrong("Connections"), .. p.Connections.Select(c => ConnectionView.Create(c, t.Title, p.ConnectionActions))],
                },
            }
            : [],
        .. Related("Came from", p.CameFrom, p.Open),
        .. Related("Grew into", p.GrewInto, p.Open),
        BodyStrong("Where does it go?"),
        new BoxEl
        {
            Direction = 0,
            Gap = Spacing.S,
            Wrap = true,
            Children =
            [
                .. Choices.Select(s => (Element)(t.Status == s
                    ? Button.Accent(s == Status.Dropped ? "Drop" : s.ToString(), () => p.Choose(t, s))
                    : Button.Standard(s == Status.Dropped ? "Drop" : s.ToString(), () => p.Choose(t, s)))),
                new BoxEl { Width = Spacing.M },
                Button.Subtle("Skip", () => p.Next(t)),
                Button.Subtle("Open", () => p.Open(t.Title)),
            ],
        },
        new TextEl(p.Message ?? "") { Size = 14f, Color = Tok.TextSecondary, Wrap = TextWrap.Wrap },
    ]);

    static Element[] Related(string heading, IReadOnlyList<string> titles, Action<string> open) =>
        titles.Count == 0
            ? []
            :
            [
                new BoxEl
                {
                    Direction = 1,
                    Gap = Spacing.XXS,
                    Children = [BodyStrong(heading), .. titles.Select(title => (Element)(HyperlinkButton.Create(title, () => open(title)) with { Margin = new Edges4(-11f, 0f, 0f, 0f) }))],
                },
            ];
}
