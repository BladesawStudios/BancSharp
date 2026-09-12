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

        Collect(romfs, path, Vector3.Zero, Vector3.Zero, Vector3.One, 0, placed, visiting);
        return placed;
    }

    private static void Collect(
        Romfs romfs, string path,
        Vector3 origin, Vector3 rotation, Vector3 scale, int depth,
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

                Vector3 at = origin + Read(actor["Translate"], 0f) * scale;
                Vector3 spin = rotation + Read(actor["Rotate"], 0f);
                Vector3 size = scale * Read(actor["Scale"], 1f);

                // A batch stands in for the actors inside it rather than drawing anything.
                if (actor["Dynamic"]?["BancPath"]?.AsString() is { } batch && romfs.Exists(batch))
                {
                    Collect(romfs, batch, at, spin, size, depth + 1, placed, visiting);
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
