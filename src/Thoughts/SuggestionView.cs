using FluentGpu.Controls;
using FluentGpu.Dsl;
using FluentGpu.Foundation;
using static FluentGpu.Dsl.Ui;

namespace Thoughts;

/// <summary>What the person can do with what Organize suggested and hasn't been answered. Shared by the panel and Review.</summary>
sealed record SuggestionActions(
    Action<Thought, string> MoveToProject,
    Action<Thought, string> CreateProject,
    Action<Thought, string> AddAsDetail,
    Action<Thought, string> KeepAsNewWork,
    Action<Thought> Dismiss);

static class SuggestionView
{
    public static Element Create(Thought t, PendingSuggestion s, SuggestionActions actions)
    {
        var rows = new List<Element> { BodyStrong("Suggested") };
        if (t.Project is null && s.Project is { } project)
            rows.Add(Line(s.Why, Button.Standard($"Move to {project}", () => actions.MoveToProject(t, project))));
        if (t.Project is null && s.NewProject is { } brandNew)
            rows.Add(Line(s.Why, Button.Standard($"Create {brandNew}", () => actions.CreateProject(t, brandNew))));
        if (s.Relation is { } relation)
        {
            rows.Add(BodyStrong(relation.MoreDetail ? $"More detail on {relation.Target}" : $"Grew from {relation.Target}") with { Wrap = TextWrap.Wrap });
            rows.Add(Body(relation.Why) with { Wrap = TextWrap.Wrap });
            rows.Add(Caption($"“{relation.Evidence}”") with { Color = Tok.TextSecondary, Wrap = TextWrap.Wrap });
            rows.Add(new BoxEl
            {
                Direction = 0,
                Gap = Spacing.S,
                Children =
                [
                    relation.MoreDetail
                        ? Button.Accent("Add as detail", () => actions.AddAsDetail(t, relation.Target))
                        : Button.Standard("Add as detail", () => actions.AddAsDetail(t, relation.Target)),
                    relation.MoreDetail
                        ? Button.Standard("Keep as new work", () => actions.KeepAsNewWork(t, relation.Target))
                        : Button.Accent("Keep as new work", () => actions.KeepAsNewWork(t, relation.Target)),
                ],
            });
        }
        rows.Add(new BoxEl { Direction = 0, Children = [Button.Subtle("Dismiss", () => actions.Dismiss(t))] });
        return new BoxEl
        {
            Key = "suggestion",
            Direction = 1,
            Gap = Spacing.S,
            Padding = Edges4.All(Spacing.M),
            Corners = Radii.CardAll,
            Fill = Tok.FillCardDefault,
            Children = rows.ToArray(),
        };
    }

    /// <summary>A Project suggestion means nothing once the Thought has a Project.</summary>
    public static bool HasAny(Thought t, PendingSuggestion s) =>
        s.Relation is not null || (t.Project is null && (s.Project is not null || s.NewProject is not null));

    static Element Line(string? why, Element action) => new BoxEl
    {
        Direction = 1,
        Gap = Spacing.XS,
        Children =
        [
            .. why is { Length: > 0 } ? new Element[] { Body(why) with { Wrap = TextWrap.Wrap } } : [],
            new BoxEl { Direction = 0, Children = [action] },
        ],
    };
}
