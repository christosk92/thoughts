using FluentGpu.Controls;
using FluentGpu.Dsl;
using FluentGpu.Foundation;
using FluentGpu.Hooks;
using static FluentGpu.Dsl.Ui;

namespace Thoughts;

/// <summary>How much the AI suggests, in what language, and which model does each step. A change applies from the next call.</summary>
sealed class AiSettingsSection(AppSettings settings, Action save) : Component
{
    static readonly string[] Models = ["claude-haiku-5-5", "claude-sonnet-5-5", "claude-opus-5-5", "claude-fable-5-1"];
    static readonly string[] Languages = ["Follow each Thought", "English", "Dutch", "German", "French", "Spanish"];
    static readonly string[] Granularities = ["Fine", "Coarse"];

    public override Element Render()
    {
        Element Choice(string label, string[] items, int current, Action<int> changed, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
        {
            var index = UseSignal(current, __hl: line);
            UseEffect(() =>
            {
                if (index.Value != current)
                {
                    changed(index.Value);
                    save();
                }
            }, DepKey.From(index.Value), __hl: line);
            return new BoxEl
            {
                Direction = 1,
                Gap = Spacing.XS,
                Children = [Caption(label) with { Color = Tok.TextSecondary }, ComboBox.Create(items, index)],
            };
        }

        Element ModelChoice(string step, string current, Action<string> set, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0) =>
            Choice(step, Models, Math.Max(0, Array.IndexOf(Models, current)), i => set(Models[i]), line);

        return VStack(Spacing.M,
            Choice("Granularity. Fine suggests only Connections. Coarse also suggests plain related links.", Granularities, settings.Coarse ? 1 : 0, i => settings.Coarse = i == 1),
            Choice("Language of titles and explanations. Cards stay English.", Languages, Math.Max(0, Array.IndexOf(Languages, settings.Language ?? "")), i => settings.Language = i == 0 ? null : Languages[i]),
            HStack(Spacing.M,
                ModelChoice("Organize", settings.OrganizeModel, m => settings.OrganizeModel = m),
                ModelChoice("Connect", settings.ConnectModel, m => settings.ConnectModel = m)),
            HStack(Spacing.M,
                ModelChoice("Check", settings.CheckModel, m => settings.CheckModel = m),
                ModelChoice("Fan out", settings.FanOutModel, m => settings.FanOutModel = m)));
    }
}
