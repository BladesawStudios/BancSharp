# BancSharp

Reads *Tears of the Kingdom* scene placement: what actors a scene holds, where each one sits,
and which model it draws.

```csharp
using Romfs romfs = Romfs.Open(@"…\romfs");
ActorModels models = new(romfs);

foreach (ActorPlacement actor in BancScene.Load(romfs, "Banc/SmallDungeon/Dungeon000_Static.bcett.byml"))
{
    if (models.Find(actor.Gyaml) is not { } model) continue;   // triggers and anchors draw nothing
    Draw(model.Path, actor.Translate, actor.Rotate, actor.Scale);
}
```

Built on `BymlSharp`, `SarcSharp` and `ZsDicSharp`.

## What it does for you

**Flattens the batches.** Some entries in a scene are not actors but a `BancPath` naming
another placement file, whose contents sit relative to the batch's own position. Loading
folds those through, so what comes back is every actor the scene draws, already in the
scene's coordinates. A shrine is mostly these: Dungeon000 is 60 entries on the face of it and
2,120 once flattened.

**Resolves the model, which cannot be guessed.** `DgnObj_Hrl_Box4x4x4Top_01` draws
`DgnObj_Small_BoxParts_A.DgnObj_Small_Box4x4x4Top_01`, and 154 models share that one project
prefix. The link is stated in a `ModelInfo` inside the actor's own pack, so resolving means
opening the pack — `ActorModels` opens each one once, which is what makes this affordable:
2,120 placements across 99 packs.

**Finds the file either way.** Paths written inside the data leave off the `.zs` the file on
disk carries — a batch names `Mrg_….bcett.byml` and the file is `Mrg_….bcett.byml.zs`.
`Romfs.Read` tries both and decompresses with the right dictionary.

## What it does not do

Nothing about geometry. A resolved `ActorModel` is a path to a `.bfres.mc`, which is
MeshCodec's to decompress and a BFRES reader's to parse.

Plenty of actors have no model on purpose — triggers, anchors, the things that fire events —
so `Find` returning null is an ordinary answer, not a failure. Of Dungeon000's 2,120, thirteen
are these.

## Checked against

Eight shrines load in 1.0 s and come to 2,696,242 triangles between them, resolving 231
distinct actor names from 231 packs. Dungeon002 is the largest at 3,288 placements and 966,420
triangles; the smallest are the 47-placement template shrines.

## Build

```bash
dotnet build BancSharp.sln -c Release
```

## Licence

AGPL-3.0-or-later. See [license.md](license.md).
