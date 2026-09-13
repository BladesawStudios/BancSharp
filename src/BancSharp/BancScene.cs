using System.Numerics;
using BymlSharp;

namespace BancSharp;

/// <summary>One actor placed in a scene, in the scene's own coordinates.</summary>
/// <param name="Gyaml">The actor's name, which is what resolves to a model.</param>
/// <param name="Translate">Where it sits, already folded through any batch it came from.</param>
/// <param name="Rotate">Euler angles in radians.</param>
/// <param name="Scale">Per-axis scale; 1 where the placement says nothing.</param>
/// <param name="Hash">The placement's own id, unique within the scene.</param>
public sealed record ActorPlacement(
    string Gyaml,
    Vector3 Translate,
    Vector3 Rotate,
    Vector3 Scale,
    ulong Hash);

/// <summary>
/// The actors a scene places.
/// </summary>
/// <remarks>
/// A scene is a <c>.bcett.byml</c> holding a list of actors, each named by its
/// <c>Gyaml</c> and positioned by a translate, rotate and scale. Some of those entries are
/// not actors at all but batches - a <c>BancPath</c> naming another placement file whose
/// contents sit relative to the batch's own position. Loading flattens those, so what comes
/// back is every actor the scene draws, each already in the scene's coordinates.
///
/// A shrine is one of these and nothing else: Dungeon000 flattens to 2,122 placements, of
/// which 2,107 draw something and the rest are triggers and anchors that were never meant to.
/// </remarks>
public static class BancScene
{
    /// <summary>How deep a batch may nest before the reader assumes it is looping.</summary>
    private const int MaxDepth = 8;

    /// <summary>Reads a scene, flattening the batches it references.</summary>
    /// <param name="romfs">The romfs the scene and its batches live in.</param>
    /// <param name="path">Romfs-relative, with or without <c>.zs</c>.</param>
    public static List<ActorPlacement> Load(Romfs romfs, string path)
    {
        List<ActorPlacement> placed = [];
        HashSet<string> visiting = new(StringComparer.OrdinalIgnoreCase);

        Collect(romfs, path, Vector3.Zero, Vector3.Zero, Vector3.One, Matrix4x4.Identity,
                0, placed, visiting);
        return placed;
    }

    private static void Collect(
        Romfs romfs, string path,
        Vector3 origin, Vector3 rotation, Vector3 scale, Matrix4x4 turn, int depth,
        List<ActorPlacement> placed, HashSet<string> visiting)
    {
        if (depth > MaxDepth || !visiting.Add(path)) return;

        try
        {
            byte[] data = romfs.Read(path);
            if (Byml.FromBinary(data)["Actors"] is not { } actors || !actors.IsArray) return;

            foreach (Byml actor in actors.AsArray)
            {
                if (actor["Gyaml"]?.AsString() is not { } gyaml) continue;

                // A batch that is turned has to turn what is inside it: the offsets its
                // actors carry are in its own frame, not the world's. Two thousand batches
                // are turned and half of those are pitched or rolled rather than only spun
                // about the vertical, which is what leaves a spiral of islands lying flat
                // in a row.
                Vector3 local = Read(actor["Translate"], 0f) * scale;
                Vector3 at = origin + (turn.IsIdentity ? local : Vector3.Transform(local, turn));

                // Its own turn, and the same turn seen from the world. Euler angles do not
                // compose by adding, so the two are multiplied as matrices and the result
                // read back out as angles - which is what the placement carries.
                Vector3 own = Read(actor["Rotate"], 0f);
                Matrix4x4 inner = Euler(own);
                Matrix4x4 world = turn.IsIdentity ? inner : inner * turn;

                Vector3 spin = turn.IsIdentity ? own : ToEuler(world);
                Vector3 size = scale * Read(actor["Scale"], 1f);

                // A batch stands in for the actors inside it rather than drawing anything.
                // Its own scale describes the volume it covers, not a transform for what is
                // inside - a tar field is an AreaMergeTar scaled fifty to two hundred times,
                // and the pieces it names are already laid out in metres about its centre.
                // Passing that scale down multiplies an eighty-metre offset into sixteen
                // kilometres and draws a twelve-metre puddle three quarters of a kilometre
                // across. Only where it stands carries into it.
                if (actor["Dynamic"]?["BancPath"]?.AsString() is { } batch && romfs.Exists(batch))
                {
                    Collect(romfs, batch, at, spin, Vector3.One, world, depth + 1, placed, visiting);
                    continue;
                }

                placed.Add(new ActorPlacement(
                    gyaml, at, spin, size,
                    actor["Hash"]?.Type is BymlType.UInt64 ? actor["Hash"]!.UInt64 : 0));
            }
        }
        finally
        {
            visiting.Remove(path);
        }
    }

    /// <summary>The rotation a placement's Euler angles describe, applied X, then Y, then Z.</summary>
    private static Matrix4x4 Euler(Vector3 angles)
        => Matrix4x4.CreateRotationX(angles.X)
         * Matrix4x4.CreateRotationY(angles.Y)
         * Matrix4x4.CreateRotationZ(angles.Z);

    /// <summary>
    /// The X-then-Y-then-Z Euler angles of a rotation, inverting <see cref="Euler"/>.
    /// </summary>
    /// <remarks>
    /// For row vectors the product is
    /// <c>[[cy*cz, cy*sz, -sy], [sx*sy*cz - cx*sz, sx*sy*sz + cx*cz, sx*cy], [...]]</c>, so the
    /// middle angle comes straight out of M13 and the other two out of a pair of ratios. Where
    /// the middle angle is a quarter turn the other two collapse into one and the first is
    /// taken as zero, which is the usual convention.
    /// </remarks>
    private static Vector3 ToEuler(Matrix4x4 m)
    {
        float sy = Math.Clamp(-m.M13, -1f, 1f);
        float y = MathF.Asin(sy);

        if (MathF.Abs(sy) > 0.999999f)
            return new Vector3(0f, y, MathF.Atan2(-m.M21, m.M22));

        return new Vector3(MathF.Atan2(m.M23, m.M33), y, MathF.Atan2(m.M12, m.M11));
    }

    /// <summary>
    /// A three-component vector, defaulting each axis the placement leaves out.
    /// </summary>
    /// <remarks>
    /// A placement writes only what differs from the default, so most actors carry no scale
    /// and many carry no rotation at all. The components are not consistently typed either -
    /// the same axis is a float in one actor and an integer in the next.
    /// </remarks>
    private static Vector3 Read(Byml? node, float fallback)
    {
        if (node is null || !node.IsArray || node.Count < 3) return new Vector3(fallback);

        return new Vector3(
            (float)(node[0]?.AsNumber() ?? fallback),
            (float)(node[1]?.AsNumber() ?? fallback),
            (float)(node[2]?.AsNumber() ?? fallback));
    }
}
