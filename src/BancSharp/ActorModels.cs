using BymlSharp;
using SarcSharp;

namespace BancSharp;

public sealed record ActorModel(string Path, string ProjectName, string FmdbName);

public sealed class ActorModels(Romfs romfs)
{
    private readonly Romfs _romfs = romfs;
    private readonly Dictionary<string, ActorModel?> _cache = new(StringComparer.Ordinal);

    public int PacksRead { get; private set; }
    public int Resolved => _cache.Count;

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
        IReadOnlyList<Byml> info = ModelInfoChain(pack, gyaml);
        if (info.Count == 0) return null;

        string? project = Field(info, "ModelProjectName")?.AsString();
        string? fmdb = Field(info, "FmdbName")?.AsString();

        if (string.IsNullOrEmpty(project) || string.IsNullOrEmpty(fmdb)) return null;

        foreach (string extension in new[] { ".bfres.mc", ".bfres" })
        {
            string path = $"Model/{project}.{fmdb}{extension}";
            if (_romfs.Exists(path)) return new ActorModel(path, project, fmdb);
        }

        return null;
    }

    /// <summary>
    /// The actor's own ModelInfo and then each one it inherits from, nearest first; empty when it has none.
    /// </summary>
    /// <remarks>
    /// A pack can carry several ModelInfo files - an armour piece's leggings pack holds its own and the helmet's, which
    /// its own names as <c>$parent</c> - so the right one is the one the actor's ActorParam points at
    /// (<c>Components/ModelInfoRef</c>, itself possibly inherited from a parent ActorParam), not whichever comes first.
    /// A field missing from a file is taken from its parent; <see cref="Field"/> walks the list for one.
    /// Packs with no reference fall back to a ModelInfo named for the actor, and then to the first there is.
    /// </remarks>
    public static IReadOnlyList<Byml> ModelInfoChain(SarcFile pack, string gyaml)
    {
        Dictionary<string, SarcEntry> entries = new(StringComparer.Ordinal);
        foreach (SarcEntry e in pack.Entries) entries[e.Name] = e;

        string? start = null;
        foreach (Byml actor in Chain(entries, $"Actor/{gyaml}.engine__actor__ActorParam.bgyml"))
            if (actor["Components"]?["ModelInfoRef"]?.AsString() is { Length: > 0 } reference)
            {
                start = EntryName(reference);
                break;
            }

        start ??= entries.ContainsKey($"Component/ModelInfo/{gyaml}.engine__component__ModelInfo.bgyml")
            ? $"Component/ModelInfo/{gyaml}.engine__component__ModelInfo.bgyml"
            : pack.Entries.FirstOrDefault(e => e.Name.Contains("ModelInfo", StringComparison.Ordinal))?.Name;

        return start is null ? [] : Chain(entries, start);
    }

    /// <summary>The first value of a field along an inheritance chain, the nearest file's winning.</summary>
    public static Byml? Field(IReadOnlyList<Byml> chain, string key)
    {
        foreach (Byml file in chain)
            if (file[key] is { } value) return value;
        return null;
    }

    /// <summary>A parameter file and the ones its <c>$parent</c> names in turn, as far as the pack holds them.</summary>
    private static List<Byml> Chain(Dictionary<string, SarcEntry> entries, string name)
    {
        List<Byml> chain = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        string? at = name;

        while (at is not null && seen.Add(at) && entries.TryGetValue(at, out SarcEntry? entry))
        {
            Byml file;
            try { file = Byml.FromBinary(entry.Data); }
            catch { break; }

            chain.Add(file);
            at = file["$parent"]?.AsString() is { Length: > 0 } parent ? EntryName(parent) : null;
        }
        return chain;
    }

    /// <summary>
    /// A reference as a pack entry name: <c>?Component/X.bgyml</c> is the entry as it is, and the authoring path
    /// <c>Work/Component/X.gyml</c> that <c>$parent</c> uses is the same file compiled.
    /// </summary>
    private static string EntryName(string reference)
    {
        string name = reference.TrimStart('?');
        if (name.StartsWith("Work/", StringComparison.Ordinal)) name = name["Work/".Length..];
        if (name.EndsWith(".gyml", StringComparison.Ordinal)) name = name[..^".gyml".Length] + ".bgyml";
        return name;
    }
}
