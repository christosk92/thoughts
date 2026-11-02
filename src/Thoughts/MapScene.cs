using FluentGpu.Dsl;
using FluentGpu.Foundation;

namespace Thoughts;

/// <summary>The Map's camera and everything it draws. The engine has no 3D, so this projects each point through a perspective
/// camera and hands the results to GPU sprites and bound transforms. Buffers are reused, and nothing runs while it's still.</summary>
sealed class MapScene
{
    const float FocalLength = 700f;
    const float NearPlane = 60f;
    const int StarCount = 170;
    const float TargetBlend = 0.35f; // How far the camera leans from the Thought towards the middle of what grew from it.
    const float StarSphere = 2600f;
    const int CurveSegments = 10;
    const float CurveBend = 0.16f;
    const float DotSpacing = 20f;
    const float OrbitDamping = 0.93f;
    const float ZoomEase = 0.16f;
    const float FlightMs = 650f;
    const float IntroMs = 900f;
    const float DriftSeconds = 14f;
    const float DriftSpeed = 0.05f; // Radians a second, so the depth reads before anyone touches it.
    const float LabelEase = 0.2f;
    const float LabelHeight = 26f;
    const float LabelMaxWidth = 210f;
    const float LabelMinScale = 0.22f;
    const float OrbRadius = 10f;
    const float HitRadius = 16f;
    const float PanelShift = -230f; // Half the panel, so the graph stays in the part of the window the panel leaves open.
    const float MinPitch = -1.45f, MaxPitch = 1.45f;
    const float RestYaw = -0.5f, RestPitch = 0.28f;

    readonly Sprite[] _stars = new Sprite[StarCount];
    readonly Sprite[] _lines = new Sprite[SpriteFieldEl.MaxSprites];
    readonly Sprite[] _dots = new Sprite[SpriteFieldEl.MaxSprites];
    readonly Sprite[] _orbs = new Sprite[SpriteFieldEl.MaxSprites];
    readonly (float X, float Y, float Z, float Bright)[] _starAt = new (float, float, float, float)[StarCount];
    int _lineCount, _dotCount, _orbCount;
    uint _version;

    MapGraph _graph = new([], [], 1f);
    float[] _x = [], _y = [], _z = [];
    float[] _fromX = [], _fromY = [], _fromZ = [];
    float[] _sx = [], _sy = [], _scale = [], _fog = [], _depth = [];
    int[] _order = [];
    float[] _labelWidth = [], _labelOpacity = [];
    bool[] _labelWanted = [];
    Affine2D[] _labelAt = [];
    float _flight = 1f, _intro = 1f, _clock;
    float _yaw = RestYaw, _pitch = RestPitch, _yawSpeed, _pitchSpeed;
    float _distance = 900f, _distanceGoal = 900f, _fitDistance = 900f;
    float _panX, _panY, _shift;
    float _targetX, _targetY, _targetZ, _targetFromX, _targetFromY, _targetFromZ, _targetToX, _targetToY, _targetToZ;
    bool _touched, _settledLabels;

    public float Width = 1f, Height = 1f;
    public string? Selected;
    public int Hovered = -1;

    public MapGraph Graph => _graph;
    public SpriteInstances Stars => new(_stars, StarCount, _version);
    public SpriteInstances Lines => new(_lines, _lineCount, _version);
    public SpriteInstances Dots => new(_dots, _dotCount, _version);
    public SpriteInstances Orbs => new(_orbs, _orbCount, _version);
    public Affine2D LabelAt(int i) => i < _labelAt.Length ? _labelAt[i] : Affine2D.Identity;
    public float LabelOpacity(int i) => i < _labelOpacity.Length ? _labelOpacity[i] : 0f;
    public float LabelWidth(int i) => _labelWidth[i];

    public MapScene()
    {
        // A fixed seed, so the same sky comes back every time.
        var random = new Random(20261102);
        for (int i = 0; i < StarCount; i++)
        {
            float y = 1f - 2f * (i + 0.5f) / StarCount, r = MathF.Sqrt(1f - y * y), a = i * 2.39996f;
            _starAt[i] = (MathF.Cos(a) * r * StarSphere, y * StarSphere, MathF.Sin(a) * r * StarSphere, 0.12f + (float)random.NextDouble() * 0.5f);
        }
    }

    /// <summary>Nodes on both graphs fly to their new place. A new node comes out of the Thought at the centre.</summary>
    public void SetGraph(MapGraph graph)
    {
        var old = new Dictionary<string, (float X, float Y, float Z)>();
        for (int i = 0; i < _graph.Nodes.Count; i++) old[_graph.Nodes[i].Title] = (_x[i], _y[i], _z[i]);
        bool first = _graph.Nodes.Count == 0;
        int n = graph.Nodes.Count;
        _graph = graph;
        _x = new float[n]; _y = new float[n]; _z = new float[n];
        _fromX = new float[n]; _fromY = new float[n]; _fromZ = new float[n];
        _sx = new float[n]; _sy = new float[n]; _scale = new float[n]; _fog = new float[n]; _depth = new float[n];
        _order = new int[n];
        _labelWidth = new float[n]; _labelOpacity = new float[n]; _labelWanted = new bool[n];
        _labelAt = new Affine2D[n];
        for (int i = 0; i < n; i++)
        {
            var node = graph.Nodes[i];
            var from = old.TryGetValue(node.Title, out var was) ? was : (X: 0f, Y: 0f, Z: 0f);
            _fromX[i] = from.X; _fromY[i] = from.Y; _fromZ[i] = from.Z;
            _x[i] = from.X; _y[i] = from.Y; _z[i] = from.Z;
            _labelWidth[i] = Math.Min(LabelMaxWidth, 20f + node.Title.Length * (node.IsFocus ? 6.6f : 5.9f));
        }
        _flight = Motion.ReducedMotion ? 1f : 0f;
        Hovered = -1;
        _targetFromX = _targetX; _targetFromY = _targetY; _targetFromZ = _targetZ;
        float cx = 0f, cy = 0f, cz = 0f;
        foreach (var node in graph.Nodes)
        {
            cx += node.X; cy += node.Y; cz += node.Z;
        }
        float share = n > 0 ? TargetBlend / n : 0f;
        _targetToX = cx * share; _targetToY = cy * share; _targetToZ = cz * share;
        if (first || _flight >= 1f)
        {
            _targetX = _targetToX; _targetY = _targetToY; _targetZ = _targetToZ;
        }
        _fitDistance = FitDistance();
        if (first)
        {
            _intro = Motion.ReducedMotion ? 1f : 0f;
            _distance = _fitDistance;
        }
        _distanceGoal = _fitDistance;
        _settledLabels = false;
    }

    float FitDistance()
    {
        // The farthest node from where the camera looks, and room for its label.
        float reach = 1f;
        foreach (var node in _graph.Nodes)
        {
            float dx = node.X - _targetToX, dy = node.Y - _targetToY, dz = node.Z - _targetToZ;
            reach = Math.Max(reach, MathF.Sqrt(dx * dx + dy * dy + dz * dz));
        }
        float half = Math.Max(160f, Math.Min(Width * 0.42f, Height * 0.5f - 70f));
        return Math.Max(420f, FocalLength * reach / half * 0.88f); // Below 1, because most nodes sit nearer the centre than the farthest one.
    }

    public void Resized(float width, float height)
    {
        Width = width;
        Height = height;
        if (!_touched) _distanceGoal = _distance = _fitDistance = FitDistance();
    }

    public void Orbit(float dx, float dy)
    {
        _touched = true;
        _yaw += dx * 0.006f;
        _pitch = Math.Clamp(_pitch + dy * 0.006f, MinPitch, MaxPitch);
        _yawSpeed = dx * 0.006f;
        _pitchSpeed = dy * 0.006f;
    }

    public void Nudge(float yaw, float pitch)
    {
        _touched = true;
        _yawSpeed += yaw;
        _pitchSpeed += pitch;
    }

    public void Pan(float dx, float dy)
    {
        _touched = true;
        _panX += dx;
        _panY += dy;
    }

    public void Zoom(float steps)
    {
        _touched = true;
        _distanceGoal = Math.Clamp(_distanceGoal * MathF.Exp(-steps * 0.2f), 160f, Math.Max(1400f, _fitDistance * 2.6f));
    }

    public void Reset()
    {
        _touched = true;
        _yaw = RestYaw;
        _pitch = RestPitch;
        _yawSpeed = _pitchSpeed = 0f;
        _panX = _panY = 0f;
        _distanceGoal = _fitDistance;
    }

    /// <summary>Moves time on and redraws. False means nothing is moving any more, and it can stop asking for frames.</summary>
    public bool Step(float dt)
    {
        bool moving = false;
        _clock += dt;

        if (_flight < 1f)
        {
            _flight = Math.Min(1f, _flight + dt * 1000f / FlightMs);
            float t = EaseOut(_flight);
            for (int i = 0; i < _x.Length; i++)
            {
                var node = _graph.Nodes[i];
                _x[i] = _fromX[i] + (node.X - _fromX[i]) * t;
                _y[i] = _fromY[i] + (node.Y - _fromY[i]) * t;
                _z[i] = _fromZ[i] + (node.Z - _fromZ[i]) * t;
            }
            _targetX = _targetFromX + (_targetToX - _targetFromX) * t;
            _targetY = _targetFromY + (_targetToY - _targetFromY) * t;
            _targetZ = _targetFromZ + (_targetToZ - _targetFromZ) * t;
            moving = true;
        }
        else
        {
            for (int i = 0; i < _x.Length; i++)
            {
                var node = _graph.Nodes[i];
                _x[i] = node.X; _y[i] = node.Y; _z[i] = node.Z;
            }
        }
        if (_intro < 1f)
        {
            _intro = Math.Min(1f, _intro + dt * 1000f / IntroMs);
            moving = true;
        }

        if (!_touched && !Motion.ReducedMotion && _clock < DriftSeconds)
        {
            _yaw += DriftSpeed * dt * Math.Clamp(DriftSeconds - _clock, 0f, 1f);
            moving = true;
        }
        if (MathF.Abs(_yawSpeed) > 0.0002f || MathF.Abs(_pitchSpeed) > 0.0002f)
        {
            _yaw += _yawSpeed;
            _pitch = Math.Clamp(_pitch + _pitchSpeed, MinPitch, MaxPitch);
            float damping = MathF.Pow(OrbitDamping, dt * 60f);
            _yawSpeed *= damping;
            _pitchSpeed *= damping;
            moving = true;
        }
        if (MathF.Abs(_distance - _distanceGoal) > 0.5f)
        {
            _distance += (_distanceGoal - _distance) * (Motion.ReducedMotion ? 1f : ZoomEase);
            moving = true;
        }

        float shiftGoal = Selected is null ? 0f : PanelShift;
        if (MathF.Abs(_shift - shiftGoal) > 0.5f)
        {
            _shift += (shiftGoal - _shift) * (Motion.ReducedMotion ? 1f : ZoomEase);
            moving = true;
        }
        else
        {
            _shift = shiftGoal;
        }

        Draw();
        if (!_settledLabels) moving = true;
        _version++;
        return moving;
    }

    static float EaseOut(float t) => 1f - (1f - t) * (1f - t) * (1f - t);

    void Project(float x, float y, float z, out float sx, out float sy, out float scale, out float depth)
    {
        x -= _targetX;
        y -= _targetY;
        z -= _targetZ;
        float cy = MathF.Cos(_yaw), sy0 = MathF.Sin(_yaw), cp = MathF.Cos(_pitch), sp = MathF.Sin(_pitch);
        float rx = x * cy + z * sy0;
        float rz = -x * sy0 + z * cy;
        float ry = y * cp - rz * sp;
        float rz2 = y * sp + rz * cp;
        depth = Math.Max(NearPlane, Distance() - rz2);
        scale = FocalLength / depth;
        sx = Width * 0.5f + rx * scale + _panX + _shift;
        sy = Height * 0.5f - ry * scale + _panY;
    }

    float Distance() => _distance * (1f + (1f - EaseOut(_intro)) * 0.9f);

    void Draw()
    {
        DrawStars();
        int n = _x.Length;
        for (int i = 0; i < n; i++)
        {
            Project(_x[i], _y[i], _z[i], out _sx[i], out _sy[i], out _scale[i], out _depth[i]);
            _order[i] = i;
        }
        Array.Sort(_depth, _order, 0, n);
        Array.Reverse(_order, 0, n); // Farthest first, so nearer orbs draw over them.
        // Array.Sort reordered _depth along with _order, so read it back in node order for the fog.
        for (int i = 0; i < n; i++) Project(_x[i], _y[i], _z[i], out _, out _, out _, out _depth[i]);

        float near = Distance() - _graph.Radius, far = Distance() + _graph.Radius;
        for (int i = 0; i < n; i++)
            _fog[i] = 0.32f + 0.68f * Math.Clamp(1f - (_depth[i] - near) / Math.Max(1f, far - near), 0f, 1f);

        DrawEdges();
        DrawOrbs();
        PlaceLabels();
    }

    void DrawStars()
    {
        uint tone = Tok.TextPrimary.A > 0 ? Sprite.Pack(Tok.TextPrimary with { A = 1f }) & 0xFFFFFF00u : 0xFFFFFF00u;
        float cy = MathF.Cos(_yaw), sy = MathF.Sin(_yaw), cp = MathF.Cos(_pitch), sp = MathF.Sin(_pitch);
        for (int i = 0; i < StarCount; i++)
        {
            var (x, y, z, bright) = _starAt[i];
            float rx = x * cy + z * sy, rz = -x * sy + z * cy, ry = y * cp - rz * sp, rz2 = y * sp + rz * cp;
            float depth = StarSphere * 1.4f - rz2, scale = FocalLength * 1.5f / depth;
            float radius = (0.6f + bright * 1.5f) * Math.Clamp(scale * 2f, 0.6f, 1.4f);
            _stars[i] = new Sprite
            {
                X = Width * 0.5f + rx * scale + _panX * 0.15f,
                Y = Height * 0.5f - ry * scale + _panY * 0.15f,
                W = radius,
                H = radius,
                Soft = 0.5f,
                Rgba = tone | (uint)(bright * 200f),
            };
        }
    }

    void DrawEdges()
    {
        _lineCount = _dotCount = 0;
        var edges = _graph.Edges;
        int branches = 0;
        for (int i = 0; i < edges.Count; i++) if (edges[i].Kind == MapEdgeKind.Branch) branches++;
        int segments = branches == 0 ? CurveSegments : Math.Clamp(_lines.Length / branches, 2, CurveSegments);
        uint solid = Sprite.Pack(Tok.TextSecondary with { A = 1f }) & 0xFFFFFF00u;
        uint dotted = Sprite.Pack(Tok.TextSecondary with { A = 1f }) & 0xFFFFFF00u;
        uint suggested = Sprite.Pack(Tok.AccentDefault with { A = 1f }) & 0xFFFFFF00u;

        for (int e = 0; e < edges.Count; e++)
        {
            var edge = edges[e];
            int a = edge.A, b = edge.B;
            float dx = _x[b] - _x[a], dy = _y[b] - _y[a], dz = _z[b] - _z[a];
            float length = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            if (length < 1f) continue;
            // A curve, so branches read as growing rather than as a wire frame.
            float mx = (_x[a] + _x[b]) * 0.5f + dz / length * length * CurveBend;
            float my = (_y[a] + _y[b]) * 0.5f + length * CurveBend * 0.6f;
            float mz = (_z[a] + _z[b]) * 0.5f - dx / length * length * CurveBend;

            if (edge.Kind == MapEdgeKind.Branch)
            {
                float px = 0f, py = 0f, ps = 1f;
                for (int s = 0; s <= segments && _lineCount < _lines.Length; s++)
                {
                    float t = (float)s / segments;
                    Curve(a, b, mx, my, mz, t, out float x, out float y, out float z);
                    Project(x, y, z, out float qx, out float qy, out float scale, out _);
                    if (s > 0)
                    {
                        float fog = _fog[a] + (_fog[b] - _fog[a]) * t;
                        _lines[_lineCount++] = new Sprite
                        {
                            X = px, Y = py, W = qx, H = qy,
                            Rot = Math.Clamp(2.2f * (ps + scale) * 0.5f, 1f, 3.5f),
                            Rgba = solid | (uint)(fog * 210f),
                        };
                    }
                    px = qx; py = qy; ps = scale;
                }
            }
            else
            {
                int dots = Math.Clamp((int)(length / DotSpacing), 3, 80);
                uint tone = edge.Kind == MapEdgeKind.Suggested ? suggested : dotted;
                for (int s = 1; s < dots && _dotCount < _dots.Length; s++)
                {
                    float t = (float)s / dots;
                    Curve(a, b, mx, my, mz, t, out float x, out float y, out float z);
                    Project(x, y, z, out float qx, out float qy, out float scale, out _);
                    float fog = _fog[a] + (_fog[b] - _fog[a]) * t;
                    float radius = Math.Clamp(2.2f * scale, 1.2f, 3.2f);
                    _dots[_dotCount++] = new Sprite { X = qx, Y = qy, W = radius, H = radius, Rgba = tone | (uint)(fog * 220f) };
                }
            }
        }
    }

    void Curve(int a, int b, float mx, float my, float mz, float t, out float x, out float y, out float z)
    {
        float u = 1f - t;
        x = u * u * _x[a] + 2f * u * t * mx + t * t * _x[b];
        y = u * u * _y[a] + 2f * u * t * my + t * t * _y[b];
        z = u * u * _z[a] + 2f * u * t * mz + t * t * _z[b];
    }

    void DrawOrbs()
    {
        _orbCount = 0;
        uint accent = Sprite.Pack(Tok.AccentDefault with { A = 1f }) & 0xFFFFFF00u;
        foreach (int i in _order)
        {
            if (_orbCount + 3 > _orbs.Length) break;
            var node = _graph.Nodes[i];
            uint color = Sprite.Pack(StatusColor(node.Status)) & 0xFFFFFF00u;
            float grow = node.IsFocus ? 1.7f : i == Hovered ? 1.35f : 1f;
            float r = Math.Clamp(OrbRadius * _scale[i], 5.5f, 24f) * grow;
            float fog = _fog[i];
            _orbs[_orbCount++] = new Sprite { X = _sx[i], Y = _sy[i], W = r * 4f, H = r * 4f, Soft = 1f, Rgba = color | (uint)(fog * 150f) };
            if (node.Title == Selected)
                _orbs[_orbCount++] = new Sprite { X = _sx[i], Y = _sy[i], W = r * 1.6f, H = r * 1.6f, Soft = 0.1f, Rgba = accent | (uint)(fog * 255f) };
            _orbs[_orbCount++] = new Sprite { X = _sx[i], Y = _sy[i], W = r, H = r, Soft = 0.12f, Rgba = color | (uint)(fog * 255f) };
        }
    }

    public static ColorF StatusColor(Status status) => status switch
    {
        Status.Inbox => ColorF.FromRgba(148, 163, 184),
        Status.Parked => ColorF.FromRgba(96, 165, 250),
        Status.Next => ColorF.FromRgba(167, 139, 250),
        Status.Doing => ColorF.FromRgba(251, 146, 60),
        Status.Done => ColorF.FromRgba(52, 211, 153),
        _ => ColorF.FromRgba(113, 113, 122),
    };

    // Nearest first, so a label behind another one fades out rather than the two piling up. The Thought at the
    // centre, the selected one and the one under the pointer always get theirs.
    void PlaceLabels()
    {
        int n = _x.Length;
        Span<float> placed = n <= 64 ? stackalloc float[64 * 4] : new float[n * 4];
        int count = 0;
        bool settled = true;
        for (int pass = 0; pass < 2; pass++)
        {
            for (int k = n - 1; k >= 0; k--)
            {
                int i = _order[k];
                var node = _graph.Nodes[i];
                bool must = node.IsFocus || node.Title == Selected || i == Hovered;
                if (must != (pass == 0)) continue;
                float r = Math.Clamp(OrbRadius * _scale[i], 5.5f, 24f) * (node.IsFocus ? 1.7f : 1f);
                float scale = Math.Clamp(_scale[i], 0.8f, 1.15f);
                float left = _sx[i] + r + 7f, top = _sy[i] - LabelHeight * scale * 0.5f;
                float right = left + _labelWidth[i] * scale, bottom = top + LabelHeight * scale;
                bool show = must || _scale[i] >= LabelMinScale;
                for (int c = 0; show && !must && c < count; c++)
                {
                    int at = c * 4;
                    if (left < placed[at + 2] && right > placed[at] && top < placed[at + 3] && bottom > placed[at + 1]) show = false;
                }
                _labelWanted[i] = show;
                if (show)
                {
                    int at = count++ * 4;
                    placed[at] = left; placed[at + 1] = top; placed[at + 2] = right; placed[at + 3] = bottom;
                }
                float goal = show ? (must ? 1f : 0.55f + _fog[i] * 0.45f) : 0f;
                float now = _labelOpacity[i];
                float next = Motion.ReducedMotion ? goal : now + (goal - now) * LabelEase;
                if (MathF.Abs(next - goal) < 0.01f) next = goal;
                else settled = false;
                _labelOpacity[i] = next;
                _labelAt[i] = new Affine2D(scale, 0f, 0f, scale, left, top);
            }
        }
        _settledLabels = settled;
    }

    /// <summary>The nearest node to a point on screen: its orb, or its label if that's showing.</summary>
    public int HitTest(float x, float y)
    {
        for (int k = _order.Length - 1; k >= 0; k--)
        {
            int i = _order[k];
            float r = Math.Max(HitRadius, OrbRadius * _scale[i] * 1.4f);
            float dx = x - _sx[i], dy = y - _sy[i];
            if (dx * dx + dy * dy <= r * r) return i;
            if (_labelWanted[i])
            {
                var at = _labelAt[i];
                float scale = at.M11;
                if (x >= at.Dx && x <= at.Dx + _labelWidth[i] * scale && y >= at.Dy && y <= at.Dy + LabelHeight * scale) return i;
            }
        }
        return -1;
    }
}
