#if DEBUG
using FluentGpu;
using FluentGpu.Dsl;
using FluentGpu.Foundation;
using FluentGpu.Hooks;
using FluentGpu.Pal;

namespace Thoughts;

/// <summary>Debug builds only: plays a script of input through the window's real input queue, for checking a ticket
/// on a throwaway vault. Input can't leave this window, and the person's cursor doesn't move. Lines: wait MS, move X Y,
/// down X Y, up X Y, key VK, char TEXT.</summary>
sealed class Drive(string scriptFile, float scale) : Component
{
    public override Element Render()
    {
        var post = UsePost();
        UseEffect(() =>
        {
            var lines = File.ReadAllLines(scriptFile);
            new Thread(() => Play(lines, post)) { IsBackground = true }.Start();
        }, DepKey.Empty);
        return new BoxEl { Visible = false };
    }

    void Play(string[] lines, Action<Action> post)
    {
        foreach (var raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var parts = line.Split(' ', 2);
            string arg = parts.Length > 1 ? parts[1] : "";
            switch (parts[0])
            {
                case "wait": Thread.Sleep(int.Parse(arg)); break;
                case "move": Send(post, new InputEvent(InputKind.PointerMove, At(arg), 0, 0)); break;
                case "down": Send(post, new InputEvent(InputKind.PointerDown, At(arg), 0, 0)); break;
                case "up": Send(post, new InputEvent(InputKind.PointerUp, At(arg), 0, 0)); break;
                case "shiftdown": Send(post, new InputEvent(InputKind.PointerDown, At(arg), 0, 0, KeyModifiers.Shift)); break;
                case "wheel":
                    var w = arg.Split(' ');
                    Send(post, InputEvent.ForScroll(new FluentGpu.Scroll.Runtime.ScrollInputEvent(FluentGpu.Scroll.Runtime.ScrollSource.MouseWheel,
                        FluentGpu.Scroll.Runtime.ScrollGesture.Notch, 0, At(w[0] + " " + w[1]), 0f, float.Parse(w[2]), 0, KeyModifiers.None)));
                    break;
                case "key":
                    Send(post, new InputEvent(InputKind.Key, default, 0, int.Parse(arg)));
                    Send(post, new InputEvent(InputKind.KeyUp, default, 0, int.Parse(arg)));
                    break;
                case "char":
                    foreach (char c in arg) Send(post, new InputEvent(InputKind.Char, default, 0, c));
                    break;
            }
        }
    }

    Point2 At(string arg)
    {
        var xy = arg.Split(' ');
        return new Point2(float.Parse(xy[0]) * scale, float.Parse(xy[1]) * scale);
    }

    static void Send(Action<Action> post, InputEvent e)
    {
        post(() => FluentAppHarness.QueueInput(in e));
        Thread.Sleep(30);
    }
}
#endif
