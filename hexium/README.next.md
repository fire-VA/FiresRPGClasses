# FiresRPGClasses

RPG classes for Valheim: ten archetypes, talent trees, two ability bars, ultimates, extra skills and a character
sheet.

You pick a class for each of two ability bars, earn class levels as you play, and spend talent and attribute
points in the Classes window. The weapon in your hand decides which classes a bar can be, and the bar's class
decides which skills it casts. It works on its own, and as an addon to FiresRPGmaker when that mod is present.

## Requires

- BepInExPack for Valheim
- YamlDotNet
- FiresUnifiedCore - update it together with this mod.
- FiresRPGmaker (optional) - when installed, its achievements and quests show up in the Classes window.
- VAInventory (optional) - when installed, the Classes window gets a tab in the inventory, and each bar gets its
  own weapon and off-hand equipment slots.

## Installation

- Install with a mod manager, or copy the `FiresRPGClasses` folder into `BepInEx/plugins`.
- Install it on every client. Install it on the dedicated server too: there it turns on locational hits,
  shares monster deaths so class XP is awarded, checks the Druid and Necromancer possession skills, and keeps the
  attribute and extra-skill settings in sync. Nothing else runs on the server.
- Update it together with FiresUnifiedCore.

## What it adds

- **Ten archetypes** - Tank, Paladin, Berserker, Rogue, Monk, Ranger, Mage, Healer, Druid and Necromancer.
- **Class levels.** Your class level rises as you fight, as you find new lands, places and things, and as your
  skills grow. Each level brings talent and attribute points.
- **Talent trees** with active skills, passives and level-10 ultimates. The trees are loaded from
  `classes-trees.yaml`, so an admin can retune every node without a new build (edits need a restart).
- **Druid and Necromancer** skills include taking on a creature's form (Wildshape) and raising the dead, plus
  summons, with their own panel in the Classes window.
- **Passives.** Equip passives in their own slots; they stay on whichever bar you use.
- **Attributes.** Strength, Agility, Intellect, Endurance and Vigour points raise damage, carry weight, health,
  stamina, eitr, armour, attack speed and regeneration, and lower stamina costs and elemental damage taken,
  each with a cap.
- **Five extra skills** that level like Valheim's own skills and show in the skills list:
  - **Acrobatics** - levels from running, jumping and time in the air; more maximum stamina.
  - **Stamina** - levels from hits taken and time away from base; less damage taken (up to 20% at level 100).
  - **Fortitude** - levels from hitting and killing enemies and time away from base; more maximum health, and
    bonus health regeneration from level 30.
  - **Arcana** - more maximum eitr.
  - **Hoarder** - more carry weight, and an extra inventory row from level 50.
  - Optional bonuses, off by default: a cheaper dodge for Acrobatics and cheaper eitr attacks for Arcana. Turn
    them on by setting their level requirement in the config.
- **Two ability bars** of eight slots, built from Valheim's own hotbar art. The inactive back bar is dimmed but
  keeps running its cooldowns. Drag skills onto a slot, or click a slot and then a skill. Drag the bars to move
  them anywhere on screen.
- **Aimed and placed skills** with a choice of cast style, and a smart heal that sends an ally skill to the most
  injured ally in range when nobody is under the crosshair.
- **The Classes window** - Skills, Achievements and Classes tabs in one resizable window. Open it with K, or
  with the inventory's Skills button, which shows Valheim's skills as its first tab.
- **Achievements board** showing this mod's tasks and, with FiresRPGmaker, its achievements in one list.
- **Raven hints.** Hugin explains each part of the class system once per character, the first time it
  matters.
- **Two icon sets** for every ability: fantasy art (default) or clean line art.
- **FiresLoreBooks rewards** (optional) - when FiresLoreBooks is installed, books can reward class XP or raise
  a Valheim skill.

## Controls

- K opens the Classes window.
- Slot keys are 1-8, used with a modifier (Shift+Alt by default) so the number row stays free for Valheim's
  hotbar.
- While a weapon is drawn, slots 1-8 are skills; while it is sheathed, the item hotbar takes them back.
- Slot 1 draws the active bar's weapon, and swaps bars once it is in hand. There is also a dedicated bar-swap
  key (mouse button 4 by default).
- Every key can be rebound in the config.

## Configuration

`BepInEx/config/com.Fire.FiresRPGClasses.cfg`, or the Fires config window (F1) in game. Sections are numbered
1 to 9 (bars, bar keys, item hotbar, item hotbar keys, HUD, UI, attributes, extra skills, targeting).
Highlights:

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
- `7 - Attributes` - how much each attribute point gives, for example Strength 0.5% physical damage and 2 carry
  weight, Endurance 1 health, Vigour 1 stamina.
- `8 - Extra skills` - how fast each extra skill levels, what it gives per level, and the level each bonus
  needs (0 turns a bonus off).
- Attribute and extra-skill settings are server-synced: the server's values apply to everyone.

Window size, window position and bar position are saved for you when you drag them.

## Console commands

All are cheat commands and need devcommands.

| command | what it does |
|---|---|
| `class_xp [amount]` | give your character class XP |
| `class_level [level]` | set your class level |
| `class_ult_xp [ultimateId\|all] [amount]` | give an ultimate XP |
| `class_set [archetype] [front\|back]` | choose a bar's class |
| `class_cooldown_reset [skillId\|all]` | make a class skill (or every one) ready now |

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
