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

        Collect(romfs, path, default, null, 0, placed, visiting, null, false);
        return placed;
    }

    // A placement inside a nested batch is composed with every batch around it. The angles are
    // radians as the archive stores them, and the composition is done in double precision on a
    // rotation matrix: composing in float and reading Euler angles back at each level lost digits
    // that the next level then compounded, and an asin near a pole throws away half of them.
    // Only the finished placement is rounded to float.
    private static void Collect(
        Romfs romfs, string path,
        (double X, double Y, double Z) origin, double[,]? turn, int depth,
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

                var local = ReadD(actor["Translate"], 0.0);
                var at = turn is null ? Add(origin, local) : Add(origin, Transform(local, turn));

                var own = ReadD(actor["Rotate"], 0.0);
                double[,] inner = Euler(own);
                double[,] world = turn is null ? inner : Multiply(inner, turn);

                Vector3 spin = turn is null
                    ? new Vector3((float)own.X, (float)own.Y, (float)own.Z)
                    : ToEuler(world);
                Vector3 size = Read(actor["Scale"], 1f);

                string? flag = actor["Presence"]?["FlagName"]?.AsString();
                bool negated = flag is not null && actor["Presence"]?["IsNegation"]?.Type is BymlType.Bool
                    && actor["Presence"]!["IsNegation"]!.Bool;
                if (flag is null) { flag = outerFlag; negated = outerNegated; }

                if (actor["Dynamic"]?["BancPath"]?.AsString() is { } batch && romfs.Exists(batch))
                {
                    Collect(romfs, batch, at, world, depth + 1, placed, visiting, flag, negated);
                    continue;
                }

                placed.Add(new ActorPlacement(gyaml, new Vector3((float)at.X, (float)at.Y, (float)at.Z), spin, size,
                    actor["Hash"]?.Type is BymlType.UInt64 ? actor["Hash"]!.UInt64 : 0)
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

    private static (double X, double Y, double Z) Add((double X, double Y, double Z) a, (double X, double Y, double Z) b)
        => (a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    /// <summary>A row vector times a row-major rotation, as <c>Vector3.Transform</c> does.</summary>
    private static (double X, double Y, double Z) Transform((double X, double Y, double Z) v, double[,] m)
        => (v.X * m[0, 0] + v.Y * m[1, 0] + v.Z * m[2, 0],
            v.X * m[0, 1] + v.Y * m[1, 1] + v.Z * m[2, 1],
            v.X * m[0, 2] + v.Y * m[1, 2] + v.Z * m[2, 2]);

    private static double[,] Multiply(double[,] a, double[,] b)
    {
        var r = new double[3, 3];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                r[i, j] = a[i, 0] * b[0, j] + a[i, 1] * b[1, j] + a[i, 2] * b[2, j];
        return r;
    }

    /// <summary>Rotation about X, then Y, then Z, for row vectors - the order <c>Matrix4x4.CreateRotationX * Y * Z</c> gives.</summary>
    private static double[,] Euler((double X, double Y, double Z) angles)
    {
        double cx = Math.Cos(angles.X), sx = Math.Sin(angles.X);
        double cy = Math.Cos(angles.Y), sy = Math.Sin(angles.Y);
        double cz = Math.Cos(angles.Z), sz = Math.Sin(angles.Z);
        double[,] rx = { { 1, 0, 0 }, { 0, cx, sx }, { 0, -sx, cx } };
        double[,] ry = { { cy, 0, -sy }, { 0, 1, 0 }, { sy, 0, cy } };
        double[,] rz = { { cz, sz, 0 }, { -sz, cz, 0 }, { 0, 0, 1 } };
        return Multiply(Multiply(rx, ry), rz);
    }

    private static Vector3 ToEuler(double[,] m)
    {
        double sy = Math.Clamp(-m[0, 2], -1.0, 1.0);
        double y = Math.Asin(sy);

        if (Math.Abs(sy) > 0.9999999999)
            return new Vector3(0f, (float)y, (float)Math.Atan2(-m[1, 0], m[1, 1]));

        return new Vector3((float)Math.Atan2(m[1, 2], m[2, 2]), (float)y, (float)Math.Atan2(m[0, 1], m[0, 0]));
    }

    private static (double X, double Y, double Z) ReadD(Byml? node, double fallback)
    {
        if (node is null || !node.IsArray || node.Count < 3) return (fallback, fallback, fallback);

        return (node[0]?.AsNumber() ?? fallback, node[1]?.AsNumber() ?? fallback, node[2]?.AsNumber() ?? fallback);
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
