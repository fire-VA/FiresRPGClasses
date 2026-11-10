# FiresRPGClasses

RPG classes for Valheim: ten classes, talent trees, two ability bars, ultimates, werewolves and vampires. Pick a
class for each bar, level up as you play, and spend your points on talents and attributes.

## What's new in 0.1.220

**Class settings now live in this mod.** The settings for class progression (how you earn class levels), the ground
rings under area skills, locational damage (where a hit lands on the body) and the melee aim's maximum tilt used to
sit in FiresUnifiedCore's config. They are now in this mod's own config, `com.Fire.FiresRPGClasses.cfg`. The first
time you load this version, your old values are copied over from Core once, so nothing resets. On a server, the
progression and locational damage settings still come from the server.

**Faster to load and to quit.** Building pieces load a little faster, and the game no longer freezes for a long time
when you quit.

**Quieter log.** Routine messages only show with debug logging turned on.

## What you get

- **Ten classes** - Tank, Paladin, Berserker, Rogue, Monk, Ranger, Mage, Healer, Druid and Necromancer.
- **Class levels.** You level up by fighting, exploring and raising your skills. Each level gives talent and
  attribute points.
- **Talent trees** with active skills, passives and level-10 ultimates.
- **Attributes.** Strength, Agility, Intellect, Endurance and Vigour raise damage, carry weight, health, stamina,
  eitr, armour and regeneration.
- **Two ability bars** of eight slots. The weapon in your hand decides which classes a bar can use. The back bar is
  dimmed but keeps counting down its cooldowns.
- **Werewolves and vampires.** Be born as one at character creation or catch it from Fenring or bat bites. A
  Purifying Draught cures it.
  - Werewolf: Wolf Form (faster, jumps higher, claws that grow with your level), Blood Rage and a Howl that scares
    enemies.
  - Vampire: Bat Form flight, Life Drain, Blood Frenzy, Night Stalker, Mesmerise and Crimson Tide. Daylight slows
    your stamina regeneration; Duskbloom Stew from the Cauldron protects you.
- **Paragon.** After the level cap you keep earning Paragon points for higher talent ranks and stars in three
  constellations, each giving a small lasting bonus.
- **Achievements board**, and **raven hints** that explain each part the first time it matters.
- **Two icon styles:** fantasy art (default) or clean line art.

## How to use

- Open the **Classes window** from the F1 hub (Player > Classes) or the Skills button in your inventory.
- Drag skills onto a bar slot, or click a slot and then a skill.
- **Skill keys:** Shift+Alt+1 to 8 while a weapon is drawn. When your weapon is put away, the number keys go back to
  your normal hotbar.
- Slot 1 draws the bar's weapon, and swaps bars once it's in your hand.
- Drag the bars to move them anywhere on screen; your spot is saved.
- Every key can be changed in the config.

## Install

- Install with a mod manager, or copy the `FiresRPGClasses` folder into `BepInEx/plugins`.
- Install on every client **and** on the dedicated server (the server hands out class XP and keeps attribute
  settings in sync).
- Update it together with FiresUnifiedCore.

## Requires

- BepInExPack for Valheim
- YamlDotNet
- FiresUnifiedCore
- FiresRPGmaker (optional) - its achievements and quests show up in the Classes window.

## Good to know

- Settings are in the F1 hub config page or `BepInEx/config/com.Fire.FiresRPGClasses.cfg`. Things you might
  change: the skill key modifier, how aimed skills are cast (hold and release, press then click, or instant), the
  icon style, raven hints, and whether the Skills button opens the Classes window or the normal Skills screen.
- Melee aim, the red crosshair over enemies, class progression, ground rings and locational damage are all set in
  this mod's config.
- On a server, attribute values, class progression, locational damage and the werewolf and vampire rules come from
  the server.
- Admins: the in-game help lists every admin command (they need devcommands).

## Issues / questions

https://discord.gg/zQRgHbqms4

## Credits / licence

MIT. See the LICENSE file. Part of the Fires mod family.

Ability icons are third-party art, used under their licences:

- **game-icons.net** (https://game-icons.net/) - the default icon set, by Lorc, Delapouite and contributors,
  licensed CC BY 3.0 (https://creativecommons.org/licenses/by/3.0/).
- **Tabler Icons** (https://github.com/tabler/tabler-icons) - the line-art set, MIT, (c) 2020-2024 Pawel Kuna.

Every shipped icon is listed against its original name in THIRDPARTY-NOTICES.md inside the package.

## Screenshots

<!-- Image hosting is Fire's call (morning 10-20). Replace HOSTED_URL with the image host and uncomment each line. Files: Tools\readme_shots\20261020\FiresRPGClasses\ -->
<!-- ![The Classes pill in the F1 hub](HOSTED_URL/FiresRPGClasses/01_hub_classes.png) -->
<!-- ![Overview: your attributes](HOSTED_URL/FiresRPGClasses/02_hub_overview.png) -->
<!-- ![Both ability bars in the HUD](HOSTED_URL/FiresRPGClasses/03_bars.png) -->
<!-- ![The Paragon constellation](HOSTED_URL/FiresRPGClasses/04_paragon.png) -->
<!-- ![Wolf Form](HOSTED_URL/FiresRPGClasses/05_wolf_form.png) -->
