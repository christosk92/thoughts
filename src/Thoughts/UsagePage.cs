using FluentGpu.Controls;
using FluentGpu.Dsl;
using FluentGpu.Foundation;
using FluentGpu.Signals;
using static FluentGpu.Dsl.Ui;

namespace Thoughts;

/// <summary>What the AI has cost this month against the Budget, by step and model, and each call.</summary>
static class UsagePage
{
    const float PageWidth = 768f; // Exactly the table: two text columns, five number columns and their gaps.
    const float TextColumn = 170f;
    const float NumberColumn = 76f;
    const int MaxCallRows = 200;

    public static Element Create(IReadOnlyList<UsageCall> calls, Signal<double> budget, Action<double> changeBudget, Action back)
    {
        double spent = calls.Sum(c => c.Cost);
        double ratio = budget.Peek() > 0 ? spent / budget.Peek() : 1;
        string month = DateTimeOffset.Now.ToString("MMMM yyyy");

        var groups = calls.GroupBy(c => (c.Step, c.Model)).OrderByDescending(g => g.Sum(c => c.Cost))
            .Select(g => Row(
                [g.Key.Step, g.Key.Model],
                [g.Count().ToString("N0"), g.Sum(c => c.Input).ToString("N0"), g.Sum(c => c.Output).ToString("N0"), Cached(g), Money(g.Sum(c => c.Cost))]))
            .ToArray();
        var rows = calls.Reverse().Take(MaxCallRows)
            .Select(c => Row(
                [c.At.ToLocalTime().ToString("MMM d, HH:mm"), c.Step + ", " + c.Model],
                [c.Input.ToString("N0"), c.Output.ToString("N0"), c.CacheRead.ToString("N0"), c.CacheWrite.ToString("N0"), Money(c.Cost)]))
            .ToArray();

        return new BoxEl
        {
            Key = "usage",
            Direction = 1,
            Grow = 1f,
            Padding = new Edges4(Spacing.XXL, Spacing.S, Spacing.XXL, Spacing.XXL),
            Children =
            [
                AppTheme.PageHeader("Usage", back),
                ScrollView(VStack(Spacing.L,
                [
                    BodyStrong($"{Money(spent)} of {Money(budget.Peek())} used in {month}"),
                    ProgressBar.Determinate((float)Math.Min(ratio, 1), PageWidth,
                        ratio >= 1 ? ProgressBarState.Error : ratio >= UsageLog.WarnAt ? ProgressBarState.Paused : ProgressBarState.Normal),
                    .. ratio >= 1
                        ? new Element[] { Bar(InfoBar.Create(InfoBarSeverity.Warning, "AI work is paused", "Thoughts are saved and wait until you raise the Budget or the month ends.", isClosable: false)) }
                        : ratio >= UsageLog.WarnAt
                            ? [Bar(InfoBar.Create(InfoBarSeverity.Warning, "Close to the Budget", $"{ratio * 100:F0}% of this month's Budget is used. AI work pauses at 100%.", isClosable: false))]
                            : [],
                    NumberBox.Create(budget, changeBudget, new NumberBox.NumberBoxOptions
                    {
                        Header = "Monthly Budget in US dollars",
                        Minimum = 0,
                        Width = 200f,
                    }),
                    Caption("Worked out from the tokens the API reported and list prices. Your Anthropic bill is the final word.") with { Color = Tok.TextSecondary },
                    Subtitle("By step and model"),
                    .. groups.Length == 0
                        ? new Element[] { Body("No AI calls yet this month.") }
                        : [Header(["Step", "Model"], ["Calls", "Input", "Output", "Cached", "Cost"]), .. groups],
                    Subtitle("Each call, newest first"),
                    .. rows.Length == 0
                        ? new Element[] { Body("No AI calls yet this month.") }
                        : [Header(["When", "Step and model"], ["Input", "Output", "Cache read", "Cache write", "Cost"]), .. rows],
                    .. calls.Count > MaxCallRows ? new Element[] { Caption($"Showing the latest {MaxCallRows} of {calls.Count:N0}.") } : [],
                ])),
            ],
        };
    }

    static string Cached(IEnumerable<UsageCall> calls) => calls.Sum(c => c.CacheRead + c.CacheWrite).ToString("N0");

    static string Money(double dollars) => "$" + dollars.ToString(dollars != 0 && Math.Abs(dollars) < 1 ? "F4" : "F2");

    static BoxEl Bar(Element infoBar) => new() { Width = PageWidth, Children = [infoBar] };

    static BoxEl Header(string[] text, string[] numbers) => Cells(text, numbers, s => Caption(s) with { Color = Tok.TextSecondary });

    static BoxEl Row(string[] text, string[] numbers) => Cells(text, numbers, Caption);

    static BoxEl Cells(string[] text, string[] numbers, Func<string, TextEl> cell) => new()
    {
        Direction = 0,
        Gap = Spacing.S,
        Width = PageWidth,
        AlignItems = FlexAlign.Center,
        Children =
        [
            .. text.Select(s => (Element)new BoxEl { Width = TextColumn, Shrink = 0f, Children = [cell(s) with { MaxLines = 1, Trim = TextTrim.CharacterEllipsis, Shrink = 1f, MinWidth = 0f }] }),
            .. numbers.Select(s => (Element)new BoxEl { Width = NumberColumn, Direction = 0, Justify = FlexJustify.End, Children = [cell(s) with { MaxLines = 1 }] }),
        ],
    };
}
