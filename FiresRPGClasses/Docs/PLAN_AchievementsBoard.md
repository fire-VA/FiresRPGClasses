# The Achievements board

Doc 12 section 10 gives the Classes window three tabs - Skills, Achievements, Classes. This is the
Achievements one, and per Fire (2026-09-23) it shows **more than this mod's own achievements**: RPG
Maker's Marketplace achievements and Valheim's own Steam achievements too, "when applicable".

Built 2026-09-23.

## Why a Core bridge and not a reference

FiresRPGClasses must not depend on FiresRPGmaker. RPGmaker is the Marketplace and server-NPC mod; the
class system is an addon beside it, and a player may run either alone. So the seam is a Core one:

```
FiresRPGmaker  --registers-->  FiresCore.Bridge.AchievementBridge  <--reads--  FiresRPGClasses
FiresRPGClasses (vanilla reader) --registers-->
```

`AchievementBridge` is **read-only and pull-based**. Nothing in it awards, unlocks or persists anything -
the owning mod keeps that authority. A crossing is still announced the existing way, through
`ProgressEvents.RaiseAchievementCompleted`, which is what FiresDiscordIntegration already listens to.

Three consequences worth stating:

- **With no catalogue registered the board is empty and says so.** That is the RPGmaker-absent case, and
  it needs no special path.
- **A catalogue that throws costs its own section and one warning**, never the panel. A display path must
  not be able to take a window down.
- **Nothing is cached.** Every refresh re-reads, so the board can never show a staler number than the
  system that owns it. RPGmaker's reader goes to the live `LeaderboardTracker`, the same source its own
  leaderboard board and `AchievementAnnouncer` use.

`BepInDependency` on `com.Fire.FiresRPGmaker` is a **soft** dependency: it only orders the load so
RPGmaker has registered before this mod's Awake runs. Registration order does not actually matter -
`CataloguesChanged` exists for a late arrival - but ordering it removes the question.

## The three sources

| Source id | Owner | Progress | Registered from |
|---|---|---|---|
| `rpgmaker` | FiresRPGmaker | yes - `progress / MinAmount` | `Modules/Marketplace/AchievementCatalogue.cs` |
| `valheim` | vanilla | no - earned or not | `Data/VanillaAchievements.cs` |
| class tasks | this mod | not built yet | see **Not done** |

### RPG Maker

`AchievementDef` maps straight onto `AchievementRow`. RPGmaker resolves the active profile itself
(`AchievementProfileManager.ResolveActiveProfile`) and computes progress with
`LeaderboardTracker.GetProgress`, so **the completion predicate is not duplicated** - the board displays
the same `progress >= MinAmount` the announcer fires on. The def's `AccentColor` (the KG block's
`R,G,B` line) carries through as the row accent.

### Valheim

Read from `Achievements.m_instance.m_achievementLists` -> `AchievementList.m_achievements`, the same
walk `InventoryGui.UpdateAchievementsList` does. Everything needed is public; no Harmony patch and no
reflection.

- **`m_unlocked` is the display truth.** It is `[NonSerialized]`, rebuilt every boot by
  `Achievements.Initialize()` from the saved stats, which is exactly what the vanilla grid shows.
- **Tier** is the two-digit prefix of `m_iconLocked.name`, the field vanilla's own list sorts on. An
  icon without one sorts last rather than throwing.
- **Secrets** hide their name and description until earned, matching vanilla.
- **No progress numbers.** A vanilla achievement's requirements are a set of stat/dict/enemy triggers,
  not one counter, so `Target` stays 0 and the board shows no bar. Inventing a percentage here would be
  inventing data.

## "When applicable" is a real condition, and the board names it

`Achievements.CanGetAchievements()` is `!IsCheatedAtAll() || PlayerProfile.s_bypassCheatChecks`, and
`IsCheatedAtAll()` ends with `|| Game.isModded`. The gate is checked inside the
`PlayerProfile.IncrementStat*` / `SetStat` writers, so when it is false **no stat accrues and therefore
no vanilla achievement can ever unlock naturally**, however long you play.

So the board does not silently show 0/97 forever. `VanillaAchievements.EarningBlockedReason()` returns
the active cause and the section header prints it:

| Cause | Reported as | Clears? |
|---|---|---|
| `Game.isModded` | a loaded mod has flagged this session as modded | per session, by removing that mod |
| `profile.m_usedCheats` | this character has used cheats | never |
| cheated world modifiers | this world carries cheated modifiers | never |
| `inventory.AnyCheatedItem()` | a cheated item is in your inventory | yes, on dropping it |
| `s_bypassCheatChecks` | nothing - it overrides all four | n/a |

**No Fires mod sets `Game.isModded`** (checked across the whole family). Third-party mods do - Jotunn
and XRayVision both set it - so whether vanilla achievements are live is a per-rig fact, not a fact
about this stack. An unearned row under a blocked source reads `Unreachable`, not `Locked`.

`IsWorldCheated()` dereferences `ServerOptionsGUI.m_instance` without a null check, so the call is
wrapped; a missing GUI instance reports "not cheated" rather than throwing inside a UI refresh.

## Headless

Both readers touch `Player.m_localPlayer`, vanilla GUI state and localization, so **registration is
gated on `FiresMod.IsHeadless`** on each owner's side. The bridge itself is inert on a server: no
catalogue registers, `Read` returns empty.

## Not done

- **This mod's own class tasks.** Doc 15 has achievements and quests granting *permanent* luck, and says
  the Achievements tab shows it as the reward. There is no class-task catalogue yet, so there is no
  third section. When it lands it registers through the same bridge - the board needs no change, and the
  reward column is the one thing it will need to grow.
- **Icons.** `AchievementRow.Icon` carries vanilla's sprite and the board does not draw it yet; rows show
  a tier accent bar instead. RPGmaker's defs have no sprite at all.
- **Server-side vanilla state.** Vanilla achievements are per-account and client-only by design. There is
  nothing to sync and nothing a server can tell a client about them.
