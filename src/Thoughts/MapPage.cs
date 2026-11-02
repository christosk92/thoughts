using System.Diagnostics;
using FluentGpu.Controls;
using FluentGpu.Dsl;
using FluentGpu.Foundation;
using FluentGpu.Hooks;
using FluentGpu.Signals;
using static FluentGpu.Dsl.Ui;

namespace Thoughts;

sealed record MapProps(string Focus, IReadOnlyList<Thought> Thoughts, IReadOnlyList<Connection> Connections, string? Selected,
    Action<string> Open, Action<string> Recenter, Action Back);

/// <summary>How one Thought grew, in space: drag to orbit, scroll to zoom, Shift-drag to pan, click a Thought to open it,
/// and double-click to recentre on it. Solid lines are <c>from</c> branches and dotted ones are Connections.</summary>
sealed class MapPage : Component
{
    const float FrameMs = 16f;
    const float ClickSlop = 4f; // A press that moves further than this was a drag.
    const float NudgeSpeed = 0.035f;

    public override Element Render()
    {
        var p = UseProps<MapProps>();
        var scene = UseRef(new MapScene()).Value;
        var frame = UseSignal(0u);
        var moving = UseSignal(true);
        var graphVersion = UseSignal(0);
        var clock = UseRef(Stopwatch.StartNew()).Value;
        var press = UseRef(new Press()).Value;

        void Wake()
        {
            if (!moving.Peek()) moving.Value = true;
        }

        UseInterval(() =>
        {
            float dt = Math.Min((float)clock.Elapsed.TotalSeconds, 0.1f);
            clock.Restart();
            bool more = scene.Step(dt);
            frame.Value = frame.Peek() + 1;
            if (!more) moving.Value = false;
        }, FrameMs, moving.Value);

        string shape = p.Focus + "|" + string.Join('|', p.Thoughts.Select(t => $"{t.Title}/{t.Status}/{t.From}"))
            + "|" + string.Join('|', p.Connections.Select(c => $"{c.A}/{c.B}/{c.State}"));
        UseEffect(() =>
        {
            scene.SetGraph(MapLayout.Build(p.Focus, p.Thoughts, p.Connections));
            clock.Restart();
            graphVersion.Value = graphVersion.Peek() + 1;
            Wake();
        }, DepKey.From(shape.GetHashCode()));
        UseEffect(() =>
        {
            scene.Selected = p.Selected;
            Wake();
        }, DepKey.From(p.Selected?.GetHashCode() ?? 0));

        _ = graphVersion.Value;
        var labels = scene.Graph.Nodes.Select((node, i) => (Element)new BoxEl
        {
            Key = "map-label-" + node.Title,
            AlignSelf = FlexAlign.Start,
            JustifySelf = FlexAlign.Start,
            Width = scene.LabelWidth(i),
            Height = 26f,
            HitTestVisible = false,
            Transform = Prop.Of(() =>
            {
                _ = frame.Value;
                return scene.LabelAt(i);
            }),
            Opacity = Prop.Of(() =>
            {
                _ = frame.Value;
                return scene.LabelOpacity(i);
            }),
            TransformOriginX = 0f,
            TransformOriginY = 0f,
            Padding = new Edges4(Spacing.S, 0f, Spacing.S, 0f),
            AlignItems = FlexAlign.Center,
            Direction = 0,
            Corners = Radii.ControlAll,
            Fill = Tok.FillSolidTertiary with { A = 0.72f },
            Children = [new TextEl(node.Title) { Size = node.IsFocus ? 13f : 12f, Weight = (ushort)(node.IsFocus ? 600 : 400), Color = Tok.TextPrimary, MaxLines = 1, Trim = TextTrim.CharacterEllipsis, Shrink = 1f, MinWidth = 0f }],
        }).ToArray();

        Element Field(string key, SpriteKernel kernel, Func<SpriteInstances> read) => new SpriteFieldEl
        {
            Key = key,
            Kernel = kernel,
            AlignSelf = FlexAlign.Stretch,
            JustifySelf = FlexAlign.Stretch,
            Instances = Prop.Of(() =>
            {
                _ = frame.Value;
                return read();
            }),
        };

        string TitleAt(int i) => scene.Graph.Nodes[i].Title;

        return new BoxEl
        {
            Key = "map",
            ZStack = true,
            Grow = 1f,
            ClipToBounds = true,
            Fill = Space(),
            HoverFill = Space(), // Otherwise the engine lightens the whole page under the pointer, because this box takes input.
            PressedFill = Space(),
            Focusable = true,
            Cursor = CursorId.SizeAll,
            OnBoundsChanged = bounds =>
            {
                scene.Resized(bounds.W, bounds.H);
                Wake();
            },
            OnPointerPressed = e =>
            {
                press.Start = press.Last = e.Local;
                press.Moved = false;
                press.Shift = (e.Mods & KeyModifiers.Shift) != 0;
                if (e.ClickCount >= 2 && scene.HitTest(e.Local.X, e.Local.Y) is >= 0 and var hit) p.Recenter(TitleAt(hit));
            },
            OnDrag = at =>
            {
                float dx = at.X - press.Last.X, dy = at.Y - press.Last.Y;
                press.Last = at;
                if (MathF.Abs(at.X - press.Start.X) + MathF.Abs(at.Y - press.Start.Y) > ClickSlop) press.Moved = true;
                if (!press.Moved) return;
                if (press.Shift) scene.Pan(dx, dy);
                else scene.Orbit(dx, dy);
                Wake();
            },
            OnPointerReleased = e =>
            {
                if (!press.Moved && scene.HitTest(e.Local.X, e.Local.Y) is >= 0 and var hit) p.Open(TitleAt(hit));
            },
            OnHoverMove = at =>
            {
                int hit = scene.HitTest(at.X, at.Y);
                if (hit == scene.Hovered) return;
                scene.Hovered = hit;
                Wake();
            },
            OnPointerExit = () =>
            {
                scene.Hovered = -1;
                Wake();
            },
            OnPointerWheel = e =>
            {
                scene.Zoom(-(e.Steps != 0 ? e.Steps : e.Delta / 120f)); // A positive wheel value scrolls towards the end, so it moves away.
                e.Handled = true;
                Wake();
            },
            OnKeyDown = e =>
            {
                switch (e.KeyCode)
                {
                    case 37: scene.Nudge(-NudgeSpeed, 0f); break;
                    case 39: scene.Nudge(NudgeSpeed, 0f); break;
                    case 38: scene.Nudge(0f, -NudgeSpeed); break;
                    case 40: scene.Nudge(0f, NudgeSpeed); break;
                    case 187 or 107: scene.Zoom(1f); break;
                    case 189 or 109: scene.Zoom(-1f); break;
                    case 36: scene.Reset(); break;
                    case 13: p.Open(p.Focus); break;
                    case 27: p.Back(); break;
                    default: return;
                }
                e.Handled = true;
                Wake();
            },
            Children =
            [
                Field("map-stars", SpriteKernel.Disc, () => scene.Stars),
                Field("map-lines", SpriteKernel.Segment, () => scene.Lines),
                Field("map-dots", SpriteKernel.Disc, () => scene.Dots),
                Field("map-orbs", SpriteKernel.Disc, () => scene.Orbs),
                .. labels,
                Overlay("map-header", FlexAlign.Start, FlexAlign.Start, 640f, 52f, new BoxEl
                {
                    Direction = 0,
                    Gap = Spacing.M,
                    AlignItems = FlexAlign.Center,
                    Children =
                    [
                        Button.Standard("Back", p.Back),
                        new TextEl(p.Focus) { Size = 18f, Weight = 600, MaxLines = 1, Trim = TextTrim.CharacterEllipsis, Shrink = 1f, MinWidth = 0f },
                    ],
                }),
                Overlay("map-tools", FlexAlign.End, FlexAlign.End, 44f, 132f, new BoxEl
                {
                    Direction = 1,
                    Gap = Spacing.XS,
                    Children =
                    [
                        Tool(Icons.Add, "Zoom in", () => { scene.Zoom(1.6f); Wake(); }),
                        Tool(Icons.Remove, "Zoom out", () => { scene.Zoom(-1.6f); Wake(); }),
                        Tool(Icons.Refresh, "Reset the view", () => { scene.Reset(); Wake(); }),
                    ],
                }),
                Overlay("map-legend", FlexAlign.End, FlexAlign.Start, 640f, 48f, new BoxEl
                {
                    Direction = 1,
                    Gap = Spacing.XS,
                    Justify = FlexJustify.End,
                    HitTestVisible = false,
                    Children =
                    [
                        new BoxEl
                        {
                            Direction = 0,
                            Gap = Spacing.M,
                            AlignItems = FlexAlign.Center,
                            Children =
                            [
                                .. new[] { Status.Inbox, Status.Parked, Status.Next, Status.Doing, Status.Done }.Select(LegendEntry),
                                Caption("Solid: grew from · Dotted: Connection") with { Color = Tok.TextSecondary },
                            ],
                        },
                        Caption("Drag to orbit · Scroll to zoom · Shift-drag to pan · Click to open · Double-click to recentre") with { Color = Tok.TextSecondary },
                    ],
                }),
            ],
        };
    }

    // Darker than the app's own background in dark themes, so the orbs carry the light.
    static ColorF Space()
    {
        var c = Tok.FillSolidBase;
        return c.R + c.G + c.B < 1.2f ? ColorF.FromRgba((byte)(c.R * 110f), (byte)(c.G * 110f), (byte)(c.B * 120f)) : c;
    }

    static Element Overlay(string key, FlexAlign vertical, FlexAlign horizontal, float width, float height, BoxEl content) => new BoxEl
    {
        Key = key,
        AlignSelf = vertical,
        JustifySelf = horizontal,
        Width = width,
        Height = height,
        Margin = Edges4.All(Spacing.L),
        Direction = 1,
        Children = [content with { Grow = 1f }],
    };

    static Element Tool(string glyph, string tip, Action act) => ToolTip.Wrap(IconButton.Create(glyph, act), tip);

    static Element LegendEntry(Status status) => new BoxEl
    {
        Key = "legend-" + status,
        Direction = 0,
        Gap = Spacing.XS,
        AlignItems = FlexAlign.Center,
        Children =
        [
            new BoxEl { Width = 8f, Height = 8f, Corners = CornerRadius4.All(4f), Fill = MapScene.StatusColor(status) },
            Caption(status.ToString()) with { Color = Tok.TextSecondary },
        ],
    };

    sealed class Press
    {
        public Point2 Start, Last;
        public bool Moved, Shift;
    }
}
