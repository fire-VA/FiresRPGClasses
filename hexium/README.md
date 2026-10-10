# FiresRPGClasses

An RPG class system for Valheim: ten archetypes, talent trees, two ability bars, ultimates and a
character sheet. It is a standalone mod that works as an addon to **FiresRPGmaker** - it reads RPG
Maker's achievements and quests when that mod is present, and runs on its own when it is not.

Requires **FiresUnifiedCore**. FiresRPGmaker is optional.

## What it adds

- **Ten archetypes** - Tank, Paladin, Berserker, Rogue, Monk, Ranger, Mage, Healer, Druid and
  Necromancer.
- **Talent trees** loaded from `classes-trees.yaml`, so an admin can retune every node without a new
  build. 612 points fills a tree.
- **Two ability bars** of eight slots, built from Valheim's own hotbar art, with the inactive bar dimmed
  but still running its cooldowns. Drag them anywhere by holding the move key.
- **Achievements board** showing this mod's tasks, RPG Maker's Marketplace achievements and Valheim's
  own Steam achievements in one list - and saying plainly when a modded session means the Steam ones
  can never unlock.
- The inventory's **Skills button** opens this window, with Valheim's skills as its first tab.

## Credits

The 138 ability icons are third-party art, used under their licences:

- **[game-icons.net](https://game-icons.net/)** - the default icon set, by Lorc, Delapouite and
  contributors, licensed **[CC BY 3.0](https://creativecommons.org/licenses/by/3.0/)**.
- **[Tabler Icons](https://github.com/tabler/tabler-icons)** - the alternate line-art set, MIT,
  (c) 2020-2024 Pawel Kuna.

Icons were rasterized from the upstream vector sources and recoloured at runtime; no upstream file is
redistributed. Every shipped file is listed against its original icon name in
**THIRDPARTY-NOTICES.md**, which ships inside the package.
