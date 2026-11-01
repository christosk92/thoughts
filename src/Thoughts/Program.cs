using FluentGpu;
using FluentGpu.Controls;
using FluentGpu.Dsl;
using FluentGpu.Foundation;
using FluentGpu.Hooks;
using Thoughts;

static class Program
{
    /// <summary>--vault uses a vault for this run without saving it; --settings uses another settings file. Both keep
    /// checks away from the real vault. --screenshot, --frames and --theme are for those checks.</summary>
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

        Tok.Use(Arg("--theme") == "light" ? ThemeKind.Light : ThemeKind.Dark);

        var options = new AppOptions { Title = "Thoughts", Width = 1360, Height = 860 };
        Component Root() => new AppRoot(settings, settingsFile, canSave: Arg("--vault") is null);
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

sealed class AppRoot(AppSettings settings, string settingsFile, bool canSave) : Component
{
    public override Element Render() => OverlayHost.Create(Embed.Comp(() => new BoardPage(settings, settingsFile, canSave)));
}
