using BymlSharp;
using SarcSharp;

namespace BancSharp.Tests;

public sealed class GameBancParamTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("BancSharp").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static string? TotkRoot
        => Environment.GetEnvironmentVariable("TOTK_ROMFS") is { } root && Directory.Exists(root) ? root : null;

    private void Put(string path, byte[] bytes)
    {
        string full = Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
    }

    private void Touch(params string[] paths)
    {
        foreach (string path in paths) Put(path, Byml.Map().ToBinary());
    }

    private void Param(string name, Dictionary<string, Byml> fields)
        => Put($"Banc/GameBancParam/{name}.game__banc__GameBancParam.bgyml", Byml.Map(fields).ToBinary());

    private Romfs Open()
    {
        Put("Pack/ZsDic.pack", new SarcFile().Write());
        return Romfs.Open(_root);
    }

    [Theory]
    [InlineData("Work/Banc/Scene/HyruleField/HyruleField_Child.bcett.json", "Banc/HyruleField/HyruleField_Child.bcett.byml")]
    [InlineData("Work/Banc/Scene/MainField/A-1.bcett.json", "Banc/MainField/A-1.bcett.byml")]
    [InlineData("Work/Banc/Other/A.bcett.json", null)]
    [InlineData("Banc/MainField/A-1.bcett.byml", null)]
    public void AWorkPathBecomesARomfsPath(string work, string? expected)
        => Assert.Equal(expected, GameBancParam.PlacementPath(work));

    [Fact]
    public void AParamReadsThroughItsParent()
    {
        Param("Base", new()
        {
            ["TerrainSceneName"] = "Base",
            ["ExtendedTerrainSceneName"] = "Outer",
            ["BancUseDynamic"] = false,
            ["BancUnitSize"] = Byml.Map(new Dictionary<string, Byml> { ["X"] = 1000, ["Y"] = 500 }),
            ["Banc"] = Byml.Map(),
        });
        Param("Field", new()
        {
            ["$parent"] = "Work/Banc/GameBancParam/Base.game__banc__GameBancParam.gyml",
            ["TerrainSceneName"] = "Field",
            ["Banc"] = Byml.Map(new Dictionary<string, Byml> { ["Main"] = "Work/Banc/Scene/Area/Field.bcett.json" }),
        });

        using Romfs romfs = Open();
        GameBancParam param = GameBancParam.Read(romfs, "Field")!;

        Assert.Equal("Banc/Area/Field.bcett.byml", param.Banc["Main"]);
        Assert.Equal("TerrainArc/Field.tscb", param.TerrainPath);
        Assert.Equal("TerrainArc/Outer.tscb", param.ExtendedTerrainPath);
        Assert.True(param.UseStatic);
        Assert.False(param.UseDynamic);
        Assert.Equal((1000, 500), param.UnitSize);
        Assert.Null(GameBancParam.Read(romfs, "Missing"));
    }

    [Fact]
    public void AParentLoopEnds()
    {
        Param("A", new() { ["$parent"] = "Work/Banc/GameBancParam/B.game__banc__GameBancParam.gyml" });
        Param("B", new() { ["$parent"] = "Work/Banc/GameBancParam/A.game__banc__GameBancParam.gyml" });

        using Romfs romfs = Open();
        Assert.NotNull(GameBancParam.Read(romfs, "A"));
    }

    [Fact]
    public void PlacementsTakeTheSplitFilesOrElseTheWholeOne()
    {
        Touch("Banc/Area/Split_Static.bcett.byml", "Banc/Area/Split_Dynamic.bcett.byml", "Banc/Area/Whole.bcett.byml");
        Param("Split", new() { ["Banc"] = Byml.Map(new Dictionary<string, Byml> { ["Main"] = "Work/Banc/Scene/Area/Split.bcett.json" }) });
        Param("Whole", new() { ["Banc"] = Byml.Map(new Dictionary<string, Byml> { ["Main"] = "Work/Banc/Scene/Area/Whole.bcett.json" }) });

        using Romfs romfs = Open();

        Assert.Equal(["Banc/Area/Split_Static.bcett.byml", "Banc/Area/Split_Dynamic.bcett.byml"], GameBancParam.Read(romfs, "Split")!.Placements(romfs));
        Assert.Equal(["Banc/Area/Whole.bcett.byml"], GameBancParam.Read(romfs, "Whole")!.Placements(romfs));
    }

    [Fact]
    public void NamesAndFilesLeaveOutTheOtherEra()
    {
        Param("Field_Child", []);
        Param("Field_Adult", []);
        Param("Shared", []);
        Touch(
            "Banc/Field/Field_Child_Static.bcett.byml",
            "Banc/Field/Field_Child_Dynamic.bcett.byml",
            "Banc/Field/Field_Adult.bcett.byml",
            "Banc/Field/Shared.bcett.byml");

        using Romfs romfs = Open();

        Assert.Equal(["Field_Adult", "Field_Child", "Shared"], GameBancParam.Names(romfs));
        Assert.Equal(["Field_Child", "Shared"], GameBancParam.Names(romfs, BancEra.Child));
        Assert.Equal(["Field_Adult", "Shared"], GameBancParam.Names(romfs, BancEra.Adult));

        Assert.Equal(4, BancScene.Files(romfs, "Banc/Field").Count);
        Assert.Equal(
            ["Banc/Field/Field_Child_Dynamic.bcett.byml", "Banc/Field/Field_Child_Static.bcett.byml", "Banc/Field/Shared.bcett.byml"],
            BancScene.Files(romfs, "Banc/Field", BancEra.Child));
        Assert.Equal(
            ["Banc/Field/Field_Adult.bcett.byml", "Banc/Field/Shared.bcett.byml"],
            BancScene.Files(romfs, "Banc/Field", BancEra.Adult));
    }

    [Fact]
    public void ASplitSceneHidesAWholeFileOfTheSameName()
    {
        Touch("Banc/Area/A_Static.bcett.byml", "Banc/Area/A.bcett.byml", "Banc/Area/B.bcett.byml");

        using Romfs romfs = Open();

        Assert.Equal(["Banc/Area/A_Static.bcett.byml", "Banc/Area/B.bcett.byml"], BancScene.Files(romfs, "Banc/Area"));
    }

    [Fact]
    public void EveryShippedParamFindsItsPlacementsAndTerrain()
    {
        if (TotkRoot is not { } root) return;
        using Romfs romfs = Romfs.Open(root);

        List<string> names = GameBancParam.Names(romfs);
        Assert.NotEmpty(names);

        foreach (string name in names)
        {
            GameBancParam param = GameBancParam.Read(romfs, name)!;
            foreach (var (unit, path) in param.Banc)
                Assert.True((param with { Banc = new Dictionary<string, string> { [unit] = path } }).Placements(romfs).Count > 0, $"{name}: nothing at {path}");
            if (param.TerrainPath is { } terrain) Assert.True(romfs.Exists(terrain), $"{name}: no {terrain}");
        }

        GameBancParam field = GameBancParam.Read(romfs, "MainField")!;
        Assert.Equal("TerrainArc/MainField.tscb", field.TerrainPath);
        Assert.Equal((1000, 1000), field.UnitSize);
        Assert.Contains("Banc/MainField/A-1_Static.bcett.byml", field.Placements(romfs));

        GameBancParam dungeon = GameBancParam.Read(romfs, "Dungeon000")!;
        Assert.Equal(["Banc/SmallDungeon/Dungeon000_Static.bcett.byml", "Banc/SmallDungeon/Dungeon000_Dynamic.bcett.byml"], dungeon.Placements(romfs));
        Assert.NotEmpty(BancScene.Load(romfs, dungeon.Placements(romfs)[0]));

        Assert.Contains("Banc/SmallDungeon/Dungeon000_Static.bcett.byml", BancScene.Files(romfs, "Banc/SmallDungeon"));
    }
}
