# FiresRPGClasses

RPG classes for Valheim: ten archetypes, talent trees, two ability bars, ultimates and a character sheet.

You pick a class for each of two ability bars, earn class levels as you play, and spend talent and attribute
points in the Classes window. The weapon in your hand decides which classes a bar can be, and the bar's class
decides which skills it casts. It works on its own, and as an addon to FiresRPGmaker when that mod is present.

## Tested and proven (0.1.172)

- **Class bars (kit look)**: all 8 slots stay on screen at 1024x768, docked or floating, with no overlap with other HUD elements (kit_bars test, layout golden shots).
- **Paragon labels** and the **Skills window skin** (UI golden shots).
- **Possession form**: the form strip and summons chip show while possessed (possession test).

## Still being worked on

- Restoring the possession strip's position after a drag test (automated test restore step).

## Requires

- BepInExPack for Valheim
- YamlDotNet
- FiresUnifiedCore 0.2.550 or newer
- FiresRPGmaker (optional) - when installed, its achievements and quests show up in the Classes window.

## Installation

- Install with a mod manager, or copy the `FiresRPGClasses` folder into `BepInEx/plugins`.
- Install it on every client. Install it on the dedicated server too: there it turns on locational hits,
  shares monster deaths so class XP is awarded, and keeps the attribute settings in sync. Nothing else runs on
  the server.
- Update it together with FiresUnifiedCore.

## What it adds

- **Ten archetypes** - Tank, Paladin, Berserker, Rogue, Monk, Ranger, Mage, Healer, Druid and Necromancer.
- **Class levels.** Your class level rises as you fight, as you find new lands, places and things, and as your
  skills grow. Each level brings talent and attribute points.
- **Talent trees** with active skills, passives and level-10 ultimates. The trees are loaded from
  `classes-trees.yaml`, so an admin can retune every node without a new build (edits need a restart).
- **Passives.** Equip passives in their own slots; they stay on whichever bar you use.
- **Attributes.** Strength, Agility, Intellect, Endurance and Vigour points raise damage, carry weight, health,
  stamina, eitr, armour and regeneration, each with a cap.
- **Two ability bars** of eight slots, built from Valheim's own hotbar art. The inactive back bar is dimmed but
  keeps running its cooldowns. Drag skills onto a slot, or click a slot and then a skill. Drag the bars to move
  them anywhere on screen.
- **Aimed and placed skills** with a choice of cast style, and a smart heal that sends an ally skill to the most
  injured ally in range when nobody is under the crosshair.
- **The Classes window** - Skills, Achievements and Classes tabs in one resizable window. The inventory's
  Skills button opens it, with Valheim's skills as its first tab.
- **Achievements board** showing this mod's tasks and, with FiresRPGmaker, its achievements in one list.
- **Raven hints.** Hugin explains each part of the class system once per character, the first time it
  matters.
- **Two icon sets** for every ability: fantasy art (default) or clean line art.

## Controls

- Slot keys are 1-8, used with a modifier (Shift+Alt by default) so the number row stays free for Valheim's
  hotbar.
- While a weapon is drawn, slots 1-8 are skills; while it is sheathed, the item hotbar takes them back.
- Slot 1 draws the active bar's weapon, and swaps bars once it is in hand. There is also a dedicated bar-swap
  key.
- Every key can be rebound in the config.

## Configuration

`BepInEx/config/com.Fire.FiresRPGClasses.cfg`, or the Fires config window (F1) in game. Highlights:

- `Skills need a drawn weapon` - default on (see Controls).
- `Skill keys modifier` - default Shift+Alt. `Item hotbar modifier` - default Alt.
- `Slot 1 swaps bars` - default on.
- `Cast style` - how aimed and placed skills are committed: hold to aim and release, press then click, or
  place at the crosshair at once. `Smart heal` - default on.
- `BackBarAlpha` - how solid the inactive bar looks. Default 0.55.
- `Raven hints` - default on.
- `IconSet` - `Fantasy` (default) or `Line`.
- `SkillsButtonOpensClasses` - default on. Turn off to get the vanilla Skills dialog back.
- `ScrollSpeed` - how fast the Classes window's lists scroll. Default 1.
- `[Attributes]` - how much each attribute point gives, for example Strength 0.5% physical damage and 2 carry
  weight, Endurance 1 health, Vigour 1 stamina. These are server-synced: the server's values apply to everyone.

Window size, window position and bar position are saved for you when you drag them.

## Console commands

All are cheat commands and need devcommands.

| command | what it does |
|---|---|
| `class_xp [amount]` | give your character class XP |
| `class_level [level]` | set your class level |
| `class_ult_xp [ultimateId\|all] [amount]` | give an ultimate XP |
| `class_set [archetype] [front\|back]` | choose a bar's class |

## Issues / questions

https://discord.gg/zQRgHbqms4

## Credits / licence

MIT. See the LICENSE file. Part of the Fires mod family.

The 182 ability icons (and the ten class crests) are third-party art, used under their licences:

- **game-icons.net** (https://game-icons.net/) - the default icon set, by Lorc, Delapouite and contributors,
  licensed CC BY 3.0 (https://creativecommons.org/licenses/by/3.0/).
- **Tabler Icons** (https://github.com/tabler/tabler-icons) - the alternate line-art set, MIT,
  (c) 2020-2024 Pawel Kuna.

Icons were rendered to PNG from the upstream vector sources and recoloured at runtime; no upstream file is
redistributed. Every shipped icon is listed against its original icon name in THIRDPARTY-NOTICES.md, which
ships inside the package.
