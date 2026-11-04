using BymlSharp;

namespace BancSharp;

/// <summary>
/// What a scene loads, from <c>Banc/GameBancParam/{Name}.game__banc__GameBancParam.bgyml</c> and the files its
/// <c>$parent</c> names, the nearest file's value winning. An era is a param of its own, as <c>HyruleField_Child</c>.
/// </summary>
public sealed record GameBancParam(string Name)
{
    private const string Folder = "Banc/GameBancParam";
    private const string Extension = ".game__banc__GameBancParam.bgyml";
    private const string WorkScene = "Work/Banc/Scene/";
    private const string WorkExtension = ".json";

    /// <summary>Each unit's placement path with no static or dynamic suffix, as <c>Main</c> or <c>A-1</c> names it.</summary>
    public IReadOnlyDictionary<string, string> Banc { get; init; } = new Dictionary<string, string>();

    public string? TerrainSceneName { get; init; }

    public string? ExtendedTerrainSceneName { get; init; }

    public bool UseStatic { get; init; } = true;

    public bool UseDynamic { get; init; } = true;

    public (int X, int Y)? UnitSize { get; init; }

    public string? TerrainPath => TerrainPathOf(TerrainSceneName);

    public string? ExtendedTerrainPath => TerrainPathOf(ExtendedTerrainSceneName);

    public static List<string> Names(Romfs romfs, BancEra era = BancEra.Any)
    {
        List<string> names = [];
        foreach (string file in romfs.List(Folder, "*" + Extension))
        {
            string name = file[(file.LastIndexOf('/') + 1)..^Extension.Length];
            if (BancScene.InEra(name, era)) names.Add(name);
        }
        return names;
    }

    public static GameBancParam? Read(Romfs romfs, string name)
    {
        List<Byml> chain = Chain(romfs, $"{Folder}/{name}{Extension}");
        if (chain.Count == 0) return null;

        Dictionary<string, string> banc = [];
        if (ActorModels.Field(chain, "Banc") is { IsMap: true } units)
            foreach (var (unit, value) in units.AsMap)
                if (value.AsString() is { } work && PlacementPath(work) is { } path)
                    banc[unit] = path;

        return new GameBancParam(name)
        {
            Banc = banc,
            TerrainSceneName = Text(chain, "TerrainSceneName"),
            ExtendedTerrainSceneName = Text(chain, "ExtendedTerrainSceneName"),
            UseStatic = Flag(chain, "BancUseStatic"),
            UseDynamic = Flag(chain, "BancUseDynamic"),
            UnitSize = ActorModels.Field(chain, "BancUnitSize") is { IsMap: true } size
                && size["X"]?.AsNumber() is { } x && size["Y"]?.AsNumber() is { } y ? ((int)x, (int)y) : null,
        };
    }

    /// <summary><c>Work/Banc/Scene/A/B.bcett.json</c> as the romfs has it, <c>Banc/A/B.bcett.byml</c>; null for any other path.</summary>
    public static string? PlacementPath(string workPath)
        => workPath.StartsWith(WorkScene, StringComparison.Ordinal) && workPath.EndsWith(WorkExtension, StringComparison.Ordinal)
            ? $"Banc/{workPath[WorkScene.Length..^WorkExtension.Length]}.byml"
            : null;

    /// <summary>The placement files this param loads that the romfs has, static before dynamic in each unit.</summary>
    public List<string> Placements(Romfs romfs)
    {
        List<string> files = [];
        foreach (string path in Banc.Values)
        {
            int count = files.Count;
            if (UseStatic) AddIfThere(romfs, files, WithSuffix(path, "_Static"));
            if (UseDynamic) AddIfThere(romfs, files, WithSuffix(path, "_Dynamic"));
            if (files.Count == count) AddIfThere(romfs, files, path);
        }
        return files;
    }

    private static void AddIfThere(Romfs romfs, List<string> files, string path)
    {
        if (romfs.Exists(path)) files.Add(path);
    }

    private static string WithSuffix(string path, string suffix)
    {
        int dot = path.IndexOf('.', path.LastIndexOf('/') + 1);
        return dot < 0 ? path + suffix : path.Insert(dot, suffix);
    }

    private static string? TerrainPathOf(string? scene) => string.IsNullOrEmpty(scene) ? null : $"TerrainArc/{scene}.tscb";

    private static string? Text(List<Byml> chain, string key) => ActorModels.Field(chain, key)?.AsString();

    private static bool Flag(List<Byml> chain, string key)
        => ActorModels.Field(chain, key) is not { Type: BymlType.Bool } flag || flag.Bool;

    private static List<Byml> Chain(Romfs romfs, string path)
    {
        List<Byml> chain = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        string? at = path;
        while (at is not null && seen.Add(at) && romfs.TryRead(at) is { } bytes)
        {
            Byml file = Byml.FromBinary(bytes);
            chain.Add(file);
            at = file["$parent"]?.AsString() is { Length: > 0 } parent ? ActorModels.EntryName(parent) : null;
        }
        return chain;
    }
}
