namespace Thoughts;

enum MapEdgeKind { Branch, Connection, Suggested }

sealed record MapNode(string Title, Status Status, float X, float Y, float Z, bool IsFocus);

sealed record MapEdge(int A, int B, MapEdgeKind Kind);

sealed record MapGraph(IReadOnlyList<MapNode> Nodes, IReadOnlyList<MapEdge> Edges, float Radius);

/// <summary>Where each Thought sits in space. The focus is at the origin, where it came from runs back along -X, and what
/// grew from it branches out through a cone around +X. The same data always gives the same picture.</summary>
static class MapLayout
{
    const int MaxNodes = 200;
    const float BranchLength = 230f;
    const float SatelliteRadius = 300f;
    const float RootCone = MathF.PI * 0.72f;
    const float ConeFalloff = 0.6f;
    static readonly float GoldenAngle = MathF.PI * (3f - MathF.Sqrt(5f));

    public static MapGraph Build(string focus, IReadOnlyList<Thought> thoughts, IReadOnlyList<Connection> connections)
    {
        var byTitle = new Dictionary<string, Thought>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in thoughts) byTitle[t.Title] = t;
        if (!byTitle.TryGetValue(focus, out var center)) return new MapGraph([], [], 1f);

        string[] Parents(Thought t) => t.From.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(byTitle.ContainsKey).ToArray();

        // A Thought with two parents has its first as its branch. The others are dotted lines.
        var children = new Dictionary<string, List<Thought>>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in thoughts)
        {
            if (Parents(t).FirstOrDefault() is not { } parent) continue;
            if (!children.TryGetValue(parent, out var list)) children[parent] = list = [];
            list.Add(t);
        }

        var nodes = new List<MapNode>();
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var edges = new List<MapEdge>();

        int Add(Thought t, float x, float y, float z)
        {
            index[t.Title] = nodes.Count;
            nodes.Add(new MapNode(t.Title, t.Status, x, y, z, nodes.Count == 0));
            return nodes.Count - 1;
        }

        Add(center, 0f, 0f, 0f);

        // Where it came from, back along the first parents.
        var walk = center;
        for (int i = 1; nodes.Count < MaxNodes && Parents(walk).FirstOrDefault() is { } up && !index.ContainsKey(up); i++)
        {
            int at = Add(byTitle[up], -BranchLength * i, 40f * i, 0f);
            edges.Add(new MapEdge(at, index[walk.Title], MapEdgeKind.Branch));
            walk = byTitle[up];
        }

        void Grow(Thought parent, int parentAt, (float X, float Y, float Z) direction, float cone)
        {
            if (!children.TryGetValue(parent.Title, out var list)) return;
            var fresh = list.Where(c => !index.ContainsKey(c.Title)).OrderBy(c => c.Title, StringComparer.OrdinalIgnoreCase).ToList();
            float length = BranchLength * (1f + 0.14f * MathF.Sqrt(fresh.Count));
            var parentNode = nodes[parentAt];
            for (int i = 0; i < fresh.Count && nodes.Count < MaxNodes; i++)
            {
                var d = OnCap(i, fresh.Count, direction, cone);
                int at = Add(fresh[i], parentNode.X + d.X * length, parentNode.Y + d.Y * length, parentNode.Z + d.Z * length);
                edges.Add(new MapEdge(parentAt, at, MapEdgeKind.Branch));
                Grow(fresh[i], at, d, cone * ConeFalloff);
            }
        }

        // With nothing behind it, what grew from it can use the whole sphere.
        Grow(center, 0, (1f, 0f, 0f), nodes.Count == 1 ? MathF.PI * 0.96f : RootCone);

        // Other parents and Connections that aren't placed yet float around the Thought they touch.
        void Satellites(string title, IReadOnlyList<string> others)
        {
            var fresh = others.Where(o => !index.ContainsKey(o) && byTitle.ContainsKey(o)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var from = nodes[index[title]];
            for (int i = 0; i < fresh.Count && nodes.Count < MaxNodes; i++)
            {
                var d = OnCap(i + 1, fresh.Count + 1, (-0.5f, 0.86f, 0f), MathF.PI * 0.45f);
                Add(byTitle[fresh[i]], from.X + d.X * SatelliteRadius, from.Y + d.Y * SatelliteRadius, from.Z + d.Z * SatelliteRadius);
            }
        }

        var live = connections.Where(c => c.State != ConnectionState.Dismissed).ToList();
        Satellites(center.Title, [
            .. Parents(center).Skip(1),
            .. thoughts.Where(t => Parents(t).Skip(1).Contains(center.Title, StringComparer.OrdinalIgnoreCase)).Select(t => t.Title),
            .. live.Where(c => c.Involves(center.Title)).Select(c => c.OtherThan(center.Title)),
        ]);
        foreach (var node in nodes.ToList())
            if (byTitle.TryGetValue(node.Title, out var t)) Satellites(node.Title, Parents(t).Skip(1).ToList());

        // Every other parent draws dotted, and so does each Connection between Thoughts on the Map.
        foreach (var node in nodes)
        {
            foreach (string other in Parents(byTitle[node.Title]).Skip(1))
                if (index.TryGetValue(other, out int at)) edges.Add(new MapEdge(at, index[node.Title], MapEdgeKind.Connection));
        }
        foreach (var c in live)
        {
            if (index.TryGetValue(c.A, out int a) && index.TryGetValue(c.B, out int b))
                edges.Add(new MapEdge(a, b, c.State == ConnectionState.Suggested ? MapEdgeKind.Suggested : MapEdgeKind.Connection));
        }

        float radius = nodes.Max(n => MathF.Sqrt(n.X * n.X + n.Y * n.Y + n.Z * n.Z));
        return new MapGraph(nodes, edges, Math.Max(radius, 1f));
    }

    // Spreads count points evenly over a cap of the sphere around a direction, by the golden angle.
    static (float X, float Y, float Z) OnCap(int i, int count, (float X, float Y, float Z) axis, float cone)
    {
        float polar = count == 1 ? 0f : cone * MathF.Sqrt((i + 0.5f) / count);
        float around = i * GoldenAngle;
        // Any two directions square to the axis.
        var seed = MathF.Abs(axis.Y) < 0.9f ? (X: 0f, Y: 1f, Z: 0f) : (X: 1f, Y: 0f, Z: 0f);
        var u = Normal(Cross(axis, seed));
        var v = Cross(axis, u);
        float s = MathF.Sin(polar), c = MathF.Cos(polar), ca = MathF.Cos(around), sa = MathF.Sin(around);
        return Normal((
            axis.X * c + (u.X * ca + v.X * sa) * s,
            axis.Y * c + (u.Y * ca + v.Y * sa) * s,
            axis.Z * c + (u.Z * ca + v.Z * sa) * s));
    }

    static (float X, float Y, float Z) Cross((float X, float Y, float Z) a, (float X, float Y, float Z) b) =>
        (a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    static (float X, float Y, float Z) Normal((float X, float Y, float Z) a)
    {
        float length = MathF.Sqrt(a.X * a.X + a.Y * a.Y + a.Z * a.Z);
        return length > 0f ? (a.X / length, a.Y / length, a.Z / length) : (1f, 0f, 0f);
    }
}
