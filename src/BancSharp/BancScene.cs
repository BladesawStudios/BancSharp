using System.Numerics;
using BymlSharp;

namespace BancSharp;

public sealed record ActorPlacement(
    string Gyaml,
    Vector3 Translate,
    Vector3 Rotate,
    Vector3 Scale,
    ulong Hash)
{
    public string? PresenceFlag { get; init; }

    public bool PresenceNegated { get; init; }
}

public static class BancScene
{
    private const int MaxDepth = 8;

    public static List<ActorPlacement> Load(Romfs romfs, string path)
    {
        List<ActorPlacement> placed = [];
        HashSet<string> visiting = new(StringComparer.OrdinalIgnoreCase);

        Collect(romfs, path, Vector3.Zero, Vector3.Zero, Vector3.One, Matrix4x4.Identity,
                0, placed, visiting, null, false);
        return placed;
    }

    private static void Collect(
        Romfs romfs, string path,
        Vector3 origin, Vector3 rotation, Vector3 scale, Matrix4x4 turn, int depth,
        List<ActorPlacement> placed, HashSet<string> visiting,
        string? outerFlag, bool outerNegated)
    {
        if (depth > MaxDepth || !visiting.Add(path)) return;

        try
        {
            byte[] data = romfs.Read(path);
            if (Byml.FromBinary(data)["Actors"] is not { } actors || !actors.IsArray) return;

            foreach (Byml actor in actors.AsArray)
            {
                if (actor["Gyaml"]?.AsString() is not { } gyaml) continue;

                Vector3 local = Read(actor["Translate"], 0f) * scale;
                Vector3 at = origin + (turn.IsIdentity ? local : Vector3.Transform(local, turn));

                Vector3 own = Read(actor["Rotate"], 0f);
                Matrix4x4 inner = Euler(own);
                Matrix4x4 world = turn.IsIdentity ? inner : inner * turn;

                Vector3 spin = turn.IsIdentity ? own : ToEuler(world);
                Vector3 size = scale * Read(actor["Scale"], 1f);

                string? flag = actor["Presence"]?["FlagName"]?.AsString();
                bool negated = flag is not null && actor["Presence"]?["IsNegation"]?.Type is BymlType.Bool
                    && actor["Presence"]!["IsNegation"]!.Bool;
                if (flag is null) { flag = outerFlag; negated = outerNegated; }

                if (actor["Dynamic"]?["BancPath"]?.AsString() is { } batch && romfs.Exists(batch))
                {
                    Collect(romfs, batch, at, spin, Vector3.One, world, depth + 1, placed, visiting,
                            flag, negated);
                    continue;
                }

                placed.Add(new ActorPlacement(gyaml, at, spin, size, actor["Hash"]?.Type is BymlType.UInt64 ? actor["Hash"]!.UInt64 : 0)
                {
                    PresenceFlag = flag,
                    PresenceNegated = negated,
                });
            }
        }
        finally
        {
            visiting.Remove(path);
        }
    }

    private static Matrix4x4 Euler(Vector3 angles)
        => Matrix4x4.CreateRotationX(angles.X)
         * Matrix4x4.CreateRotationY(angles.Y)
         * Matrix4x4.CreateRotationZ(angles.Z);

    private static Vector3 ToEuler(Matrix4x4 m)
    {
        float sy = Math.Clamp(-m.M13, -1f, 1f);
        float y = MathF.Asin(sy);

        if (MathF.Abs(sy) > 0.999999f)
            return new Vector3(0f, y, MathF.Atan2(-m.M21, m.M22));

        return new Vector3(MathF.Atan2(m.M23, m.M33), y, MathF.Atan2(m.M12, m.M11));
    }

    private static Vector3 Read(Byml? node, float fallback)
    {
        if (node is null || !node.IsArray || node.Count < 3) return new Vector3(fallback);

        return new Vector3(
            (float)(node[0]?.AsNumber() ?? fallback),
            (float)(node[1]?.AsNumber() ?? fallback),
            (float)(node[2]?.AsNumber() ?? fallback));
    }
}
