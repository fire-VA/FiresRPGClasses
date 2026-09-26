# The class tree loader

Reads `classes-trees.yaml` into the tree model chip 7 defined. Built 2026-09-23. The yaml is the
**data of record**: every node placement, rank, level, targeting mode, variant group and rank rider
comes from the file, and the loader invents nothing the file does not carry.

## Where it sits

```
classes-trees.yaml  ->  YamlClassTreeSource : IClassTreeSource  ->  ClassTrees.Load()
```

`IClassTreeSource` was already shaped exactly like the yaml's node schema, so no new model was needed
and there is only one. `RegistryTreeSource` remains as the fallback, and `CoreAbilityTargeting.cs`
(which knowingly duplicates the yaml) should now be **deleted**, not reconciled - chip 7 said so when
it wrote it.

`ClassTreeLoader` owns the lifecycle: seed, load, watch, reload.

- **Seed.** The yaml ships as an `EmbeddedResource` (`FiresRPGClasses.Data.classes-trees.yaml`) and is
  written to `BepInEx\config\FiresRPGmaker\Classes\classes-trees.yaml` on first run. An existing file
  is **never** overwritten - it is the admin's.
- **Load.** Reads the file. On any failure it falls back to `RegistryTreeSource` and says loudly which
  source is in force, rather than leaving the game with no trees.
- **Reload.** A `FileSystemWatcher` with a 0.5 s debounce, because an editor writes in bursts. Core's
  `FileWatchHub` absorbs ordinary watchers, so no registration call is needed.
- **`Tick()`** is called once per frame from `ClassCombatDriver.Update`, at the very top so an open menu
  or a dead player cannot stall a reload. `Initialize()` runs from the plugin's Awake (it replaced the
  `RegistryTreeSource` load) and `Shutdown()` from OnDestroy. The reload happens in Tick, on the
  main thread.

**The watcher callback touches no Unity API.** It records `DateTime.UtcNow` only. An earlier draft read
`Time.realtimeSinceStartup` in the callback, which runs on a watcher thread and would have thrown - the
same class of bug as calling an engine API from a field initializer.

## YamlDotNet

Referenced with `Private=False` and supplied at runtime by the **`ValheimModding-YamlDotNet`** plugin
package, which is already installed on the rigs. The manifest must declare it as a dependency.

Build-time and runtime identities were checked and match exactly:
`YamlDotNet, Version=16.0.0.0, PublicKeyToken=ec19458f3c15af5e`. `Private=False` is deliberate - two
YamlDotNet assemblies in one process is an identity conflict, so we never ship a second copy.

## Three defects the loader exposed

### 1. Four ultimate ids did not exist in Core - fixed

The yaml named four `source: core` ultimates that Core's registry does not have:

| yaml said | Core actually has |
|---|---|
| `paladin_avatar_of_light` | `paladin_avatar` |
| `berserker_avatar_of_war` | `berserker_avatar_war` |
| `monk_way_of_perfection` | `monk_perfection` |
| `healer_avatar_of_life` | `healer_avatar_life` |

Left alone, a loader would have looked them up, found nothing, and **all eight level-10 ultimates would
silently not exist** - slot 8 would offer only Ultimate II and the hybrids. Corrected in the yaml; all
eight now resolve.

### 2. Every ultimate is declared twice, and that is correct

The same 16 ids appear in `classes:` (with `slot: ultimate`) and again in `ultimates:`. They are not
duplicates: the class tree **places** the ultimate (tier, path, level, ranks) and the ultimates section
gives it **cost, cooldown and morphs**.

So `ReadUltimates` **enriches** the placed node rather than adding a second one. An earlier draft
skipped the ultimates entry as a duplicate, which silently lost every cost and cooldown. Only an
ultimate that no tree node places is added, and that is warned about.

### 3. `TotalPointCost` counted ultimates - fixed

Measured from the yaml, by slot:

| slot | nodes | points |
|---|---|---|
| skill | 64 | 299 |
| passive | 40 | 215 |
| auto | 10 | 50 |
| dodge | 8 | 48 |
| **subtotal** | **122** | **612** |
| ultimate | 16 | 56 |
| raw total | 138 | 668 |

**612 is doc 19's budget and chip 10's figure.** The 56 ultimate points are the discrepancy - exactly
**7 per tree**, which is why per-tree came out 82/88 instead of 75/81. Ultimates are not bought with
points (doc 19: they grow by use), so `ClassTree.TotalPointCost` now skips `NodeSlot.Ultimate`.

This is the check worth keeping: **the loader's own log line prints the total, and it should read 612.**
If it ever prints something else, either the data or the cost model moved.

## Slot and tier vocabulary

The yaml's `slot` is `auto` / `skill` / `ultimate` / `dodge`, and 40 nodes carry no slot at all - those
are decided by `kind`. Mapping:

| yaml | model |
|---|---|
| `auto` | `NodeSlot.Automatic` |
| `skill` | `NodeSlot.Skill` |
| `ultimate` | `NodeSlot.Ultimate` |
| `dodge` | `NodeSlot.Evasion` |
| absent + `kind: passive` | `NodeSlot.Passive` |
| absent + `kind: active` | `NodeSlot.Skill` |

The 10 `auto` nodes are the automatic abilities doc 19 counts in the 50-passive grid pool - Shield
Block, Fury, Spellcasting, Healing Staff and the rest. Ranger has none.

All eight targeting modes the yaml uses already existed on `TargetingSpec`.

## Rank riders

`ClassTreeNode.RankEffects` is new, as chip 10 asked: `{Rank, Counter, Effect}`, with `RankEffectAt`
and `RankEffectsUpTo`. **A rider never changes a node's rank count or point cost** - that is how chip
10's counters went in without moving the 612-point budget. A rider whose rank is outside the node's
range is dropped with a warning rather than silently kept.

## Warnings, not exceptions

`YamlClassTreeSource.Warnings` collects every problem - unknown class, unknown tier, unknown targeting
mode, undeclared path, out-of-range rider, unplaced ultimate - and the tree still loads. A malformed
file is a warning per item and a log line per warning; only unreadable YAML throws, and that falls back.

## Not done

- **Node positions.** The yaml carries `pos` per node, which the loader does not yet read because the
  model has no field for it. That is what the Talents pan/zoom canvas needs, so chip 7's tiered-list
  view stays until it is added.
- **Morphs.** The ultimates section carries two morphs with capstones per ultimate. The loader takes
  cost, cooldown and unlock level but not the morphs - `UltimateCatalog` is the natural owner.
- **Server sync.** Doc 12 wants the definitions synced. The file is read per peer today, so a server
  and client with different files will disagree.
- **`counters.matrix`** in the yaml is chip 10's coverage record and is not read.

## Verify (needs the running game)

1. First run on a clean profile writes `Classes\classes-trees.yaml` and logs it.
2. The load line reads schema 5, 8 trees, 138 nodes, and **612 points**.
3. No `[ClassTrees]` warnings on the shipped file.
4. Edit a node's `level` and save: it reloads within a second, with no restart.
5. Break the yaml (delete a `:`): one error, and the trees fall back to the registry stand-in rather
   than the game losing all trees.
6. Delete the file while running: the next reload falls back; restart re-seeds it.
