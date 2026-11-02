using FluentGpu.Controls;
using FluentGpu.Dsl;
using FluentGpu.Foundation;
using FluentGpu.Hooks;
using FluentGpu.Signals;
using static FluentGpu.Dsl.Ui;

namespace Thoughts;

/// <summary>The one switch for thoughts-mcp: whether a Caller may read the rest of this Vault. Adding Thoughts needs no switch.</summary>
sealed class McpSettingsSection(Signal<string?> vaultPath) : Component
{
    public override Element Render()
    {
        string? path = vaultPath.Value;
        var allowed = UseSignal(path is not null && new Vault(path).CallersMayPull);
        UseEffect(() =>
        {
            if (path is null) return;
            var vault = new Vault(path);
            if (vault.CallersMayPull != allowed.Value) vault.CallersMayPull = allowed.Value;
        }, DepKey.From(allowed.Value ? 1 : 0));
        return new BoxEl
        {
            Direction = 1,
            Gap = Spacing.XS,
            Children =
            [
                CheckBox.Create("Let MCP Callers read other Thoughts in this Vault", allowed),
                Caption("This sends note text and cards to whatever model that client uses. Keep-local Thoughts are never included. Adding Thoughts through MCP works either way.") with { Color = Tok.TextSecondary, Wrap = TextWrap.Wrap },
            ],
        };
    }
}
