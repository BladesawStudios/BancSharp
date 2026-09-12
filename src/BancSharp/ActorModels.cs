using BymlSharp;
using SarcSharp;

namespace BancSharp;

/// <summary>Where an actor's model lives, and what it is called inside the file.</summary>
/// <param name="Path">Romfs-relative path of the model.</param>
/// <param name="ProjectName">The model project the file is named after.</param>
/// <param name="FmdbName">The model within that project.</param>
public sealed record ActorModel(string Path, string ProjectName, string FmdbName);

/// <summary>
/// Resolves an actor's name to the model it draws.
/// </summary>
/// <remarks>
/// The actor's name is not the model's. <c>DgnObj_Hrl_Box4x4x4Top_01</c> draws
/// <c>DgnObj_Small_BoxParts_A.DgnObj_Small_Box4x4x4Top_01</c>, and a hundred and fifty-four
/// models share that one project prefix, so the link cannot be guessed - it is stated in a
/// <c>ModelInfo</c> inside the actor's own pack, and reading it means opening the pack.
///
/// A scene places thousands of actors drawn from a few dozen kits, so what matters is that
/// each pack is opened once: a shrine of 2,122 placements resolves from 99 packs.
/// </remarks>
public sealed class ActorModels(Romfs romfs)
{
    private readonly Romfs _romfs = romfs;
    private readonly Dictionary<string, ActorModel?> _cache = new(StringComparer.Ordinal);

    /// <summary>How many actor packs have been opened, against how many names were asked for.</summary>
    public int PacksRead { get; private set; }
    public int Resolved => _cache.Count;

    /// <summary>
    /// The model an actor draws, or null when it draws nothing.
    /// </summary>
    /// <remarks>
    /// Plenty of actors have no model on purpose - triggers, anchors, the things that fire
    /// events - so null is an ordinary answer rather than a failure.
    /// </remarks>
    public ActorModel? Find(string gyaml)
    {
        if (_cache.TryGetValue(gyaml, out ActorModel? cached)) return cached;

        return _cache[gyaml] = Resolve(gyaml);
    }

    private ActorModel? Resolve(string gyaml)
    {
        string packPath = $"Pack/Actor/{gyaml}.pack";
        if (!_romfs.Exists(packPath)) return null;

        PacksRead++;

        SarcFile pack = SarcFile.FromBinary(_romfs.Read(packPath));
        SarcEntry? entry = pack.Entries.FirstOrDefault(
            e => e.Name.Contains("ModelInfo", StringComparison.Ordinal));

        if (entry is null) return null;

        Byml info = Byml.FromBinary(entry.Data);
        string? project = info["ModelProjectName"]?.AsString();
        string? fmdb = info["FmdbName"]?.AsString();

        if (string.IsNullOrEmpty(project) || string.IsNullOrEmpty(fmdb)) return null;

        // MeshCodec for most of them, plain zstd for the rest.
        foreach (string extension in new[] { ".bfres.mc", ".bfres" })
        {
            string path = $"Model/{project}.{fmdb}{extension}";
            if (_romfs.Exists(path)) return new ActorModel(path, project, fmdb);
        }

        return null;
    }
}
