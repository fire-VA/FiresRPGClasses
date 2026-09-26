# FiresRPGClasses - combat bars, targeting, ultimates and the Classes views

Chip 7. Built against **FiresUnifiedCore 0.2.86** (`bin\Release\FiresUnifiedCore.dll`, verified to carry
`ClassAbilityRegistry`, `ProgressionEngine`, `ProgressionSettings`, `ClassProgressState`, `LuckModel`,
`KeybindRegistry`, `GroundRingManager` and `AbilityHeals`).

Designs built to: doc `14` (bars, weapons, targeting), doc `16` (ultimates), doc `19` (progression and the
passive grid), doc `17` (registering keys), with doc `13` for tree shape and doc `12` for the view list.

Nothing in FiresUnifiedCore was edited.

---

## What lives where

| Layer | Files | Job |
|---|---|---|
| Tree data | `Data/ClassTreeModel.cs` | `ClassTreeNode`, `ClassTree`, `TargetingSpec`, `IClassTreeSource`, the `ClassTrees` registry |
| Tree data fallback | `Data/RegistryTreeSource.cs`, `Data/CoreAbilityTargeting.cs` | one tree per archetype from Core's 73 abilities, with doc 14 section 6's slots and targeting |
| Weapons | `Data/WeaponArchetypes.cs` | doc 14 section 2, resolved from `m_skillType` + two-handed + shield off-hand |
| Ultimates | `Data/UltimateCatalog.cs` | doc 16's shared mechanics, Core's eight capstones, and all 56 hybrids generated from Core's own names |
| State | `State/BarLayout.cs`, `ClassPlayerData.cs`, `ClassPlayerStore.cs` | schema 3, in `Player.m_customData["FiresRPGClasses_v3"]` |
| Keys | `Input/ClassKeys.cs`, `HotbarHandoffPatches.cs` | doc 14's four schemes, the `Replaces` declarations, and the suppression that makes them true |
| Combat | `Combat/*.cs` | bars, cooldowns, targeting, cast routing, ultimates, evasion, passives, the per-frame driver |
| UI | `UI/*.cs` | the five boards and the two-bar HUD |

## Decisions and why

### Schema: one document, Core's state embedded

`ClassProgressState` is Core's and has no serializer, no store and no bars. Rather than invent a parallel
store, `ClassPlayerData` **contains** a `ClassProgressState` and adds the bars, the saved bars and the
per-ultimate levels. One JSON document under one key, declaring Core's `CurrentSchema` (3) as its own
schema, so a Core schema bump invalidates this record too. If chip 6 would rather the bars live in Core's
state, moving them is a field move plus a `[JsonIgnore]`; nothing else changes.

No back-compat path: a record whose stored schema is not the current one is discarded and a fresh one
started. This family is in dev.

### The tree data is the fallback, not the design

`classes-trees.yaml` has no loader anywhere in the tree. `IClassTreeSource` **is** the yaml's node shape:
id, name, archetype, path, tier, level, ranks, slot, targeting (mode / range / radius), variant group,
`after`, cooldown, cost. A loader only has to parse into it and call `ClassTrees.Load`.

Until then `RegistryTreeSource` derives one single-path tree per archetype from `ClassAbilityRegistry`:

- tier from `AbilityCategory`;
- slot from `ClassAbility.IsPassive`, plus two explicit id lists in `CoreAbilityTargeting` - the eight
  level-20 evasions and the ten always-on automatics (an explicit list, not a name heuristic);
- targeting from a table of doc 14 section 6's 37 non-trivial entries, defaulting to `Self`;
- `MinLevel` for an Ultimate-category node forced to **20**, because doc 16 makes Core's capstones
  Ultimate II at level 20 while the registry still calls them level 100.

**This table duplicates data the yaml also carries.** It exists so the bars demonstrably cast today. When
the loader lands, delete `CoreAbilityTargeting` and `RegistryTreeSource` rather than reconciling them.

**Defect found in the yaml:** four of the eight `source: core` ultimate ids do not match Core's registry.
`paladin_avatar_of_light` vs `paladin_avatar`, `berserker_avatar_of_war` vs `berserker_avatar_war`,
`monk_way_of_perfection` vs `monk_perfection`, `healer_avatar_of_life` vs `healer_avatar_life`. The other
four match. The loader must reconcile these or the ultimates will not bind.

### Hybrid ultimates are generated, not typed

Doc 16's 56-row pair table is already in Core as `HybridArchetypeDefinitions.GetAllHybrids()` (display
name plus special-ability name per ordered pair). `UltimateCatalog` reads it and applies doc 16's template:
50 adrenaline / 180 s, or 20% health for the four blood hybrids by name; a front-leaning and a back-leaning
morph naming each side's aspect; Resonance and Echo capstones. Hand-authoring any pair that deserves more
is a `UltimateCatalog.Register` call.

### Keys, and what a hand-off actually is

Core's `PLAN_KeybindConflicts.md` is explicit: `Replaces` is a **declaration**, and suppressing the vanilla
hotbar is this mod's job. So:

- the eight slot keys are `KeyBinding`s in section `Bars`, each declaring
  `Replaces("Hotbar1".."Hotbar8")` in **both** `WeaponDrawn` and `Gameplay`;
- `PlayerUseHotbarItemHandoff` is one prefix on `Player.UseHotbarItem` that returns false while
  `ClassKeys.SuppressVanillaHotbar()` is true - modifier held, or a weapon drawn, or always, per scheme.

Declaring both contexts is a deliberate superset. The only case where the declaration is wider than the
suppression is the drawn-weapon scheme while sheathed, where our binding is not live either, so there is
nothing for the conflict scanner to warn about.

In the fourth scheme (no drawn-weapon requirement, no skill modifier) 1-8 are always skills, so the item
hotbar gets its own eight `KeyBinding`s in section `Item hotbar` behind the item-hotbar modifier; they call
back into `UseThroughHandoff`, which re-enters `UseHotbarItem` past our own suppression.

The skill modifier is written into each slot key's Ctrl / Shift / Alt entries, and only for slots still on
their default key - doc 14's rule.

### Targeting

`TargetingController` resolves each of doc 14's modes. Ground points come from a camera raycast on
`GroundHeightSampler.SolidMask` and are then snapped with `GroundHeightSampler.Sample`, which is chip 8's
voxel-and-FAT-tile-aware sampler - so the reticle sits on voxel chunks and FAT tiles, not the vanilla
heightmap. Placed and aimed previews draw through `GroundRingManager.Show(..., aiming: true)`; the return
value is treated as nullable, because Core returns null when the local viewer may not see the ring.

Ally targeting is the ally under the crosshair, else (smart heal on, the default) the most injured ally in
range, else you.

`Player.Dodge` is prefixed once, so an archetype's level-20 evasion replaces the roll while that archetype
is on the active bar. **FiresVikHavnCombat also prefixes `Player.Dodge`** (its own dedicated-dodge key).
Harmony allows both, but the two mods should be run together before either ships.

### Adrenaline

`Player.GetMaxAdrenaline` is postfixed to a floor of 100 when either bar slots an adrenaline ultimate.
That is all doc 16 needs: vanilla's `AddAdrenaline` only auto-spends the bar when an equipped item carries
an `m_fullAdrenalineSE`, so without a trinket the bar simply fills and holds, and with one the trinket's
threshold still fires exactly as before.

### Casting, and the gap Core leaves

A cast broadcasts `(casterZdo, nodeId, point, targetZdo)` on this mod's own routed RPC, registered from
`ZNet.Start`, so every peer draws the ring in the same place. Core's `AbilityRPCManager` has no
point-carrying cast, so this message is ours rather than a Core change.

Effect application goes through Core's `ApplySelfBuff` / `ApplySingleEffect` / `ApplyGroupBuff` /
`ApplyAoEEffect` when a node names a Core effect. Two honest gaps, both loud rather than silent:

1. **A node with no `EffectName` raises `ClassCastExecutor.Unmapped`** and logs once. `RegistryTreeSource`
   names no effects, so today every cast takes this path. That is the hook the class-kit work plugs into.
2. **Core's AoE centres on the caster.** A *placed* effect needs a point-centred apply that does not exist,
   so placed nodes go to `Unmapped` too rather than quietly landing under the caster's feet.

Also noted: `AbilityRPCManager.ApplySelfBuff` bails on
`CompanionPatches.AreCompanionTeleportsSuppressed()` - a companion gate that will also swallow player
self-buffs during a companion teleport.

### Points are computed, never stored

Every point figure comes from `ProgressionEngine` / `ClassProgressState` against a
`ProgressionSettings.CurrentRates` snapshot taken at the call site, never from the `ConfigEntry` objects.
On spawn, `ClassPlayerStore.ReconcileWithSettings` runs Core's `ReconcileOverspend` and
`TrimPassivesToSlots`; under **Refund** any skill dropped from the trees is also cleared off both bars, so
a bar never points at an unlearned node.

`ProgressionSettings.Initialize` is **not** called here - Core already owns section
`FiresRPGClasses.Progression` in its own config file and binds it to ConfigSync.

### UI

Five boards on one canvas, built with `UIBuilderHelper` (Core's code-first path in the UIBuilder family)
and `UIFontConfig.Colors`. No Core UI helper is forked and no Unity authoring is needed. Each board
implements `IClassesView` - build once into its page, then refresh from data.

| Board | Shows |
|---|---|
| Character | level, Valor, XP to next, earned / spent / unspent points, passive slots, the Valor level every tree fills at, the five attributes with buy buttons and what each moves, points spent per archetype with respec |
| Talents | one archetype at a time, its paths, nodes grouped by tier with rank pips, level gate, slot and targeting, and a buy button priced by `ClassTrees.RankCost` with doc 13's previous-tier prerequisite |
| Ultimates | the four archetype ultimates plus the pair's two, each with its own level, current node name, potency, effective cost and cooldown, its morphs, slot-to-front / slot-to-back, and a morph swap gated out of combat |
| Bars | both bars with weapon, chosen archetype, six skill slots and the ultimate, each labelled with why it is greyed; "Use <Archetype>" for every archetype the weapon allows; a skill picker drawn from every archetype the weapon allows; named saved bars with load and delete |
| Passives | every tree passive and automatic ability with its status - active, learned but not equipped, no slots left, conditions unmet, not learned - and equip / unequip inside `ProgressionEngine.PassiveSlots` |

The HUD draws the active bar full size with the other dimmed above it, each slot coloured by
`SlotAvailability` and swept by `CooldownTracker`.

## Deliberately not done

- **The yaml loader.** Not built, not duplicated. Named above as the single next step.
- **Per-ability effects.** The 73 Core abilities and the 65 new ones have no effect bindings here; that is
  the class-kit and FX work. `Unmapped` is the seam.
- **A point-centred AoE apply** in Core. Placed effects cannot land correctly without it.
- **Ultimate I.** The eight level-10 ultimates exist only in the yaml, so slot 8 currently offers Ultimate
  II (level 20) and the hybrids (level 25) only.
- **Morph effects.** Morph names, capstone names and the level tree are modelled; what a morph *does* is
  not applied, because the effects are not bound.
- **"Restore my last bar for this weapon."** Doc 14 has it off by default; the setting is not added.
- **Talent tree pan and zoom.** The Talents board is a tiered list, not doc 12's pan-zoom canvas. A canvas
  needs node positions, which come from the yaml's `pos`.
- **`UIMannequinPreview` class preview** and the class-selection cards - doc 12 phase 4.
- **Attribute effects.** Points are bought and stored; nothing reads them into player stats yet.
- **Ultimate XP share while slotted** (doc 16's 10% of character XP). Only per-cast XP is awarded, because
  the character XP hooks are not attached either (Core's own plan doc lists that as open).
- **Gamepad.** Core's keybind v1 excludes it.

## Verify - needs the running game

1. Equip a sword and shield: the front bar takes **Paladin**, and "Use Tank" appears beside it.
2. Put a bow on the back bar, press the swap key: the bow equips, there is a 0.5 s lockout, and pressing
   swap mid-cast is refused.
3. Take the bar's weapon out of the inventory: every slot on that bar reads "the bar has no weapon".
4. Slot a Tank skill on a bow bar: it greys with "this weapon cannot drive it" and does not fire.
5. Draw a weapon and press 1-8: skills fire and no hotbar item is used. Sheathe: 1-8 use hotbar items.
   Then set the skill modifier to Alt and confirm Alt+1 casts without also using hotbar item 1.
6. Open Core's config manager: **no** Hotbar 1-8 conflict, and the full keymap shows them as handled -
   this is chip 5's verify item 3.
7. Set the skill modifier to Ctrl: Core reports "modifier in use - Ctrl is Valheim's crouch key".
8. Hold a placed skill's key: a reticle follows the crosshair on vanilla ground, a voxel chunk and a FAT
   tile; right-click cancels; release places it.
9. Cast an ally heal with nobody targeted: it goes to the most injured ally in range, else you.
10. At level 20 with 20 points in one path, slot Ultimate II: the adrenaline bar fills without a trinket,
    the cost is marked, and casting spends it. Equip a trinket and confirm its own threshold still pops.
11. Reach level 20 with an evasion learned: the dodge roll becomes that archetype's evasion, and swapping
    to the other bar's archetype changes which one.
12. Equip passives up to the slot count, then exceed it: the extra reads "no passive slots left". Lower
    *Skill points per level* on the server and confirm the loadout trims and the bars drop what was
    refunded.
13. Run alongside FiresVikHavnCombat with its dedicated dodge key on, and confirm both dodge prefixes
    behave.

## The HUD bars are Valheim's own hotbar elements (2026-09-23)

Fire: "they were meant to look and feel exactly like the standard hotbar we see in the top left", plus
draggable, plus a translucent back bar that still shows cooldowns.

**Not imitated - cloned.** `ClassesHud` finds the live `HotkeyBar` and instantiates its `m_elementPrefab`
once per slot, and copies its `m_elementSpace` (70) for the spacing. So the frame, the slot background and
the binding corner are the game's own art at the game's own metrics; there is nothing to keep in sync with
a Valheim art change. The old hand-drawn blue panels are gone.

The element's parts are repurposed rather than rebuilt:

| vanilla part | used for |
|---|---|
| `binding` | the slot key, via `ClassKeys.SlotLabel` - one digit, or `A1`/`C1`/`S1` when the scheme puts a modifier on it |
| `icon` | the ability, tinted by the node's `IconColor` |
| `durability` (`GuiBar`) | the cooldown - it is already a drain-to-empty bar |
| `amount` | seconds remaining while cooling, otherwise the ability's short name |
| `equiped` / `queued` / `selected` | switched off - item concepts with no class meaning |

**The back bar is the same bar under a `CanvasGroup`** at `BackBarAlpha` (default 0.45). Alpha does not stop
`Update`, so its cooldowns and its seconds keep running while it looks inactive - which is what Fire asked
for and is the reason it is a CanvasGroup rather than per-element colour maths.

**Dragging is behind a held key** (`HUD / MoveBars`, default Left Alt). Outside it every graphic in the
block has `raycastTarget = false` and the backing image is transparent, so the bars cannot swallow an
attack click - the failure this would otherwise have. Holding the key turns the backing into a raycast
target, tints it faintly and shows a "drag to move" hint; the position saves to `HUD / BarsX,BarsY` on
release. Same shape as Core's `GroupHudDragHandler`.

**It waits for the hotbar.** `EnsureBuilt` returns false until a `HotkeyBar` exists, because the HUD object
is created in Awake and the player's hotbar is not. It retries per frame rather than failing once.

**Still missing: ability icons.** Nodes carry `IconColor`, not a sprite, so a slot is a tinted fill inside a
vanilla frame and the short name sits in the `amount` corner. When real icons land, set `Icon.sprite` and
the corner can go back to being the seconds only.

## The inventory's Skills button opens this window

Doc 12 section 10 always said so; it was never wired. One Harmony prefix on `InventoryGui.OnOpenSkills`
(the method the button's onClick calls) opens `ClassesPanel` on its new **Skills** tab and returns false, so
the vanilla dialog never shows. `UI / SkillsButtonOpensClasses` turns it off.

**Nothing is lost by taking the button over**, which is the only reason this is acceptable: `SkillsView` is
the first tab and lists every vanilla skill - localized name, level, the effective-vs-base bonus when a
buff moves it, and progress toward 100. It is read-only; raising a skill is still the game's job.

Tab order is now Skills, Character, Talents, Ultimates, Bars, Passives, Achievements, and the panel opens
on Skills so the button lands where the player expected.

## Ability icons (2026-09-24)

138 icons, one per class node, embedded in the DLL. **Two complete sets under the same 138 names**, so
switching can never leave a slot without art:

| set | source | licence | size | default |
|---|---|---|---|---|
| `Fantasy` | game-icons.net, 4,133-icon fantasy set | CC BY 3.0 | 128px, 438 KB | yes |
| `Line` | Tabler Icons | MIT | 64px, 98 KB | no |

`UI / IconSet` picks one; changing it clears the sprite cache, so it takes effect without a restart.

**Why two.** The Tabler set was built first and reads as UI furniture - correct for a toolbar, wrong for
a spellbar, and a fire hydrant came out for Firestorm. game-icons is the fantasy vocabulary these
abilities actually need (`shield-bash`, `hooded-assassin`, `chain-lightning`, `tombstone`, `totem`,
`frozen-orb`). Fire kept the line set as an alternate rather than deleting it.

**Nothing is painted.** Every PNG is white on transparent and the HUD tints it with the node's
`IconColor`, so one file serves an ability whatever colour its tree declares, and a locked or cooling
slot is the same art at a lower alpha. That is also why the slot no longer prints a short name: with real
art the corner text is only the countdown. The name falls back in automatically when an icon is missing.

**Loading.** `UI/ClassIcons.cs`, modelled on FATI's `FatiGlyphs`: resource-name match on
`.<Set>.` plus `cls_<nodeId>.png`, `Sprite.Create`, `HideFlags.HideAndDontSave`, and
**`Texture2D.LoadImage` by reflection** - a direct call drags `UnityEngine.ImageConversionModule` and
netstandard 2.1 into this net48 build. Sprites cache per set and per node; a null result is cached too,
so a missing icon costs one scan rather than one per frame.

**Baking.** `Tools\Bake-GameIcons.ps1 -MapFile class-icons.map` and
`Tools\Bake-TablerGlyphs.ps1 -MapFile class-glyphs.map` (the Tabler baker gained `-MapFile`/`-Prefix`/
`-OutName` for this and is unchanged without them). Both maps are `nodeId=icon-name`, one line per
ability, so a wrong icon is a one-line fix and a 20-second re-bake. The game-icons baker rasterizes SVG
path data through WPF `Geometry.Parse` because GDI+ has no path parser - see
`reference_game_icons_bake` in memory.

**No two abilities share an icon.** Checked with `cut -d= -f2 map | sort | uniq -d`; the first pass had
nine collisions and one of them (`ranger_kiting` / `ranger_rain_arrows`) was inside a single class, which
would have put identical art twice on one bar.

Attribution for both sets is in `FiresRPGClasses/THIRDPARTY-NOTICES.md`. CC BY 3.0 makes it a
requirement, not a courtesy.
