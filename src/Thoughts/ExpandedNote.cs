using FluentGpu.Controls;
using FluentGpu.Dsl;
using FluentGpu.Foundation;
using FluentGpu.Hooks;
using FluentGpu.Signals;
using static FluentGpu.Dsl.Ui;

namespace Thoughts;

/// <summary>A note at a size to write in: a second editor over the same text, so the panel's stays in step and saving stays in one place.</summary>
sealed class ExpandedNote(string title, Signal<string> body, MarkdownEditorOptions options, Action close) : Component
{
    const float MaxCardWidth = 1100f;
    const float WindowMargin = 48f;
    const float ReadingWidth = 860f;

    public static OverlayHandle Open(IOverlayService overlay, string title, Signal<string> body, MarkdownEditorOptions options, Action saved)
    {
        var large = options with
        {
            ReadingWidth = ReadingWidth,
            OnModel = model => model.Select(model.Text.Length, model.Text.Length), // Ready to carry on writing at the end.
        };
        OverlayHandle? handle = null;
        handle = overlay.Open(
            () => NodeHandle.Null,
            () => Embed.Comp(() => new ExpandedNote(title, body, large, () => handle?.Close())),
            FlyoutPlacement.BottomCenter,
            // The dialog's first tab stop would be its close button. The note takes the keyboard instead, in Render.
            new PopupOptions(FocusTrap: true, DismissBehavior: DismissBehavior.Modal, Chrome: PopupChrome.Modal) { PreserveFocusOnOpen = true });
        handle.ClosedAction = saved;
        return handle;
    }

    public override Element Render()
    {
        var window = UseContext(Viewport.Size);
        var hooks = UseContext(InputHooks.Current);
        var note = UseRef(NodeHandle.Null);
        var post = UsePost();
        UseEffect(() => post(() => // The next frame: the note's own nodes aren't there yet when this first runs.
        {
            var first = hooks.FirstFocusableIn?.Invoke(note.Value) ?? NodeHandle.Null;
            if (!first.IsNull) hooks.FocusNode?.Invoke(first, false);
        }), DepKey.Empty);
        return new BoxEl
        {
            Direction = 1,
            Width = MathF.Min(MaxCardWidth, window.Width - 2f * WindowMargin),
            Height = window.Height - 2f * WindowMargin,
            Corners = CornerRadius4.All(Radii.Overlay),
            Fill = Tok.FillSolidTertiary,
            BorderColor = Tok.StrokeSurfaceDefault,
            BorderWidth = 1f,
            Shadow = Elevation.Dialog,
            ClipToBounds = true,
            Children =
            [
                new BoxEl
                {
                    Direction = 0,
                    AlignItems = FlexAlign.Center,
                    Padding = new Edges4(Spacing.XXL, Spacing.M, Spacing.M, Spacing.S),
                    Children =
                    [
                        new BoxEl { Grow = 1f, Shrink = 1f, MinWidth = 0f, Children = [BodyStrong(title) with { MaxLines = 1, Trim = TextTrim.CharacterEllipsis, Shrink = 1f, MinWidth = 0f }] },
                        ToolTip.Wrap(IconButton.Create(Icons.BackToWindow, close, size: ControlSize.Small), "Back to the panel"),
                    ],
                },
                new BoxEl
                {
                    Direction = 1,
                    Grow = 1f,
                    Shrink = 1f,
                    MinHeight = 0f,
                    Margin = new Edges4(Spacing.M, 0f, Spacing.M, Spacing.M),
                    OnRealized = h => note.Value = h,
                    Children = [MarkdownEditor.Create(body, options)],
                },
            ],
        };
    }
}
