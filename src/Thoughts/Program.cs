using FluentGpu;
using FluentGpu.Controls;
using FluentGpu.Dsl;
using FluentGpu.Foundation;
using FluentGpu.Hooks;
using Thoughts;

static class Program
{
    /// <summary>--vault uses a vault for this run without saving it; --settings uses another settings file. Both keep
    /// checks away from the real vault. --screenshot, --frames, --theme and --drive are for those checks.</summary>
    [STAThread]
    static void Main(string[] args)
    {
        string? Arg(string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i < args.Length - 1 ? args[i + 1] : null;
        }

        string settingsFile = Arg("--settings") ?? AppSettings.DefaultFile;
        var settings = AppSettings.Load(settingsFile);
        if (Arg("--vault") is { } vault) settings.VaultPath = vault;
#if DEBUG
        Organizer.RequestLog = Arg("--ai-log");
#endif

        Tok.Use(Arg("--theme") == "light" ? ThemeKind.Light : ThemeKind.Dark);

        var options = new AppOptions { Title = "Thoughts", Width = 1360, Height = 860 };
        Component Root() => new AppRoot(settings, settingsFile, canSave: Arg("--vault") is null, driveScript: Arg("--drive"));
        if (Arg("--screenshot") is { } shot)
            FluentAppHarness.Run(Root, options, new HarnessOptions
            {
                Frames = int.TryParse(Arg("--frames"), out int frames) ? frames : 60,
                Screenshot = shot,
                SurfaceSizeDip = new Size2(1360, 860),
            });
        else
            FluentApp.Run(Root, options);
    }
}

sealed class AppRoot(AppSettings settings, string settingsFile, bool canSave, string? driveScript) : Component
{
    public override Element Render() => OverlayHost.Create(Embed.Comp(() => new Shell(settings, settingsFile, canSave, driveScript)));
}

// The preview layer is the top child of the root stack, so a dragged chip is never clipped by anything below it.
sealed class Shell(AppSettings settings, string settingsFile, bool canSave, string? driveScript) : Component
{
    static readonly Func<DragState, Element?> ThoughtChip =
        DragChip.Resolve(state => state.Payload is Thought t ? new DragChipSpec(Title: t.Title) : null);

    public override Element Render()
    {
        var layers = new List<Element>
        {
            new BoxEl { Direction = 1, Children = [Embed.Comp(() => new BoardPage(settings, settingsFile, canSave))] },
            DragPreviewLayer.Of(ThoughtChip),
        };
#if DEBUG
        if (driveScript is not null) layers.Add(Embed.Comp(() => new Drive(driveScript, 1f)));
#endif
        return new BoxEl { ZStack = true, Grow = 1f, Children = layers.ToArray() };
    }
}
