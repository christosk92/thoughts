using FluentGpu.Animation;
using FluentGpu.Controls;
using FluentGpu.Dsl;
using FluentGpu.Foundation;
using static FluentGpu.Dsl.Ui;

namespace Thoughts;

/// <summary>What a person can do with a Suggested Connection. Shared by the capture strip and the panel.</summary>
sealed record ConnectionActions(Action<Connection> Accept, Action<Connection> Dismiss, Action<Connection> BuildOn, Action<string> Open);

static class ConnectionView
{
    const float LinkInset = -11f; // Lines the link's text up with the text below it, past the button's own padding.

    // Each quote from the notes on its own, so two notes' words don't read as one sentence.
    static string Evidence(string paragraph)
    {
        var lines = paragraph.Split('\n');
        if (!lines[0].StartsWith('>')) return paragraph;
        return string.Join(" · ", lines.Select(l => l.TrimStart('>', ' ')).Where(l => l.Length > 0).Select(l => $"“{l}”"));
    }

    /// <summary>One Connection as seen from one Thought: the other Thought, the claim, what it rests on, and the actions if it's still a suggestion.</summary>
    public static Element Create(Connection c, string self, ConnectionActions actions)
    {
        string other = c.OtherThan(self);
        var paragraphs = c.Explanation.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        var evidence = paragraphs.Skip(1).Select(p => (Element)(Caption(Evidence(p)) with
        {
            Color = Tok.TextSecondary,
            Wrap = TextWrap.Wrap,
        }));
        return new BoxEl
        {
            Key = "connection-" + c.Name,
            Direction = 1,
            Gap = Spacing.XS,
            Padding = Edges4.All(Spacing.M),
            Corners = Radii.CardAll,
            Fill = Tok.FillCardDefault,
            Enter = new EnterExit(Opacity: 0f, Active: true),
            Exit = new EnterExit(Opacity: 0f, Active: true),
            Transition = MotionTok.ControlNormal,
            Children =
            [
                HyperlinkButton.Create(other, () => actions.Open(other)) with { Margin = new Edges4(LinkInset, 0f, 0f, 0f) },
                Body(paragraphs.FirstOrDefault() ?? "") with { Wrap = TextWrap.Wrap },
                .. evidence,
                .. c.State == ConnectionState.Suggested
                    ? new Element[]
                    {
                        new BoxEl
                        {
                            Direction = 0,
                            Gap = Spacing.S,
                            Margin = new Edges4(0f, Spacing.XS, 0f, 0f),
                            Children =
                            [
                                Button.Accent("Accept", () => actions.Accept(c)),
                                Button.Standard("Build on it", () => actions.BuildOn(c)),
                                Button.Subtle("Dismiss", () => actions.Dismiss(c)),
                            ],
                        },
                    }
                    : [],
            ],
        };
    }
}
