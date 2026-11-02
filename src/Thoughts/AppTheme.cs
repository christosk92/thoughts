using FluentGpu.Controls;
using FluentGpu.Dsl;
using FluentGpu.Foundation;
using FluentGpu.Hooks;
using FluentGpu.Pal.Windows;
using static FluentGpu.Dsl.Ui;

namespace Thoughts;

static class AppTheme
{
    /// <summary>Null follows Windows. A live change re-renders in place, so nothing on screen loses its state.</summary>
    public static void Apply(string? theme) => Tok.Use(theme switch
    {
        "Light" => ThemeKind.Light,
        "Dark" => ThemeKind.Dark,
        _ => Win32Theme.SystemUsesLightTheme() ? ThemeKind.Light : ThemeKind.Dark,
    });

    /// <summary>The same top of the page everywhere, outside the scrolling part, so the way back is always there.</summary>
    public static Element PageHeader(string title, Action? back) => new BoxEl
    {
        Key = "page-header",
        Direction = 0,
        Gap = Spacing.S,
        AlignItems = FlexAlign.Center,
        Margin = new Edges4(0f, 0f, 0f, Spacing.L),
        Children =
        [
            back is null ? new BoxEl { Visible = false } : ToolTip.Wrap(IconButton.Create(Icons.Back, back), "Back"),
            Title(title),
        ],
    };
}

/// <summary>Light, dark, or whatever Windows is set to.</summary>
sealed class ThemeSection(AppSettings settings, Action save) : Component
{
    static readonly string[] Names = ["Follow Windows", "Light", "Dark"];

    public override Element Render()
    {
        int current = Math.Max(0, Array.IndexOf(Names, settings.Theme ?? ""));
        var index = UseSignal(current);
        UseEffect(() =>
        {
            if (index.Value == current) return;
            settings.Theme = index.Value == 0 ? null : Names[index.Value];
            save();
            AppTheme.Apply(settings.Theme);
        }, DepKey.From(index.Value));
        return new BoxEl
        {
            Direction = 1,
            Gap = Spacing.XS,
            Children = [Caption("Theme") with { Color = Tok.TextSecondary }, ComboBox.Create(Names, index)],
        };
    }
}

/// <summary>A piece of a frozen slot, such as the title bar's, that re-renders by itself from the signals it reads.</summary>
sealed class Live(Func<Element> build) : Component
{
    public static Element Of(Func<Element> build) => Embed.Comp(() => new Live(build));

    public override Element Render() => build();
}
