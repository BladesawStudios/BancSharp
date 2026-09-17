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
        SarcEntry? entry = pack.Entries.FirstOrDefault(
            e => e.Name.Contains("ModelInfo", StringComparison.Ordinal));

        if (entry is null) return null;

        Byml info = Byml.FromBinary(entry.Data);
        string? project = info["ModelProjectName"]?.AsString();
        string? fmdb = info["FmdbName"]?.AsString();

        if (string.IsNullOrEmpty(project) || string.IsNullOrEmpty(fmdb)) return null;

        foreach (string extension in new[] { ".bfres.mc", ".bfres" })
        {
            string path = $"Model/{project}.{fmdb}{extension}";
            if (_romfs.Exists(path)) return new ActorModel(path, project, fmdb);
        }

        return null;
    }
}
