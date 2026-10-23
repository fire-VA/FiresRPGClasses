using System;
using FiresCore.Npc.Archetypes;
using UnityEngine;

namespace FiresRPGClasses.Data
{
    /// <summary>
    /// The Vampire class (Tools\VAMPIRE_CLASS_DESIGN.md). Core's ArchetypeClass ends at Necromancer = 10, the Werewolf is
    /// 11 (<see cref="WerewolfClass"/>), and the Vampire is 12, reserved with Core's owner and kept here only until Core names it.
    /// Json.NET saves the enum as its number, so a record round-trips; names go through <see cref="WerewolfClass.TryParse"/> /
    /// <see cref="ClassNames.Of"/>, which know "Vampire" too.
    /// </summary>
    public static class VampireClass
    {
        public const ArchetypeClass Archetype = (ArchetypeClass)12;
        public const string Name = "Vampire";
    }

    /// <summary>
    /// The Vampire's requirement is a race (design §2): the player ZDO string "rpg_race" == "Vampire", which VCS 0.8.7+ publishes. No
    /// model fallback (a vampire has no special model, only skin and eyes). Read-only: RPGClasses never writes rpg_race (VCS owns it).
    /// </summary>
    public static class VampireRace
    {
        public const string RaceValue = "Vampire";
        public const string BatActiveKey = "rpg_bat_active";
        private static readonly int RaceHash = WerewolfRace.RaceKey.GetStableHashCode();

        private static int _frame = -1;
        private static bool _local;
        private static string _lastWhy = "";
        private static bool _inputsKnown, _lastHasZdo;
        private static string _lastRace;

        /// <summary>The local player has the vampire race (cached per frame: the HUD asks many times a frame).</summary>
        public static bool Local
        {
            get
            {
                if (Time.frameCount == _frame) return _local;
                _frame = Time.frameCount;
                Player p = Player.m_localPlayer;
                ZDO zdo = p != null && p.m_nview != null && p.m_nview.IsValid() ? p.m_nview.GetZDO() : null;
                // Has() builds its "why" string, and this runs every frame the HUD paints.
                // Only ask it again when the ZDO's race (the only input) changed.
                bool hasZdo = zdo != null;
                string raceNow = hasZdo ? zdo.GetString(RaceHash, "") : null;
                if (_inputsKnown && hasZdo == _lastHasZdo && string.Equals(raceNow, _lastRace, StringComparison.Ordinal)) return _local;
                _inputsKnown = true; _lastHasZdo = hasZdo; _lastRace = raceNow;
                FiresRPGClasses.UI.ClassesHud.CountRebuild();
                bool has = Has(zdo, out string why);
                if (why != _lastWhy)
                {
                    _lastWhy = why;
                    FiresRPGClassesPlugin.Log?.LogDebug($"[Vampire] race check for the local player: {(has ? "has" : "lacks")} the race ({why})");
                }
                _local = has;
                return has;
            }
        }

        /// <summary>The same test on any player's ZDO (the server's Bat Form validator reads the requester's).</summary>
        public static bool Has(ZDO zdo, out string why)
        {
            if (zdo == null) { why = "no player ZDO"; return false; }
            string race = zdo.GetString(RaceHash, "");
            why = race.Length > 0 ? WerewolfRace.RaceKey + " = " + race : "no " + WerewolfRace.RaceKey;
            return string.Equals(race, RaceValue, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>A player (any, local or not) whose ZDO says Vampire.</summary>
        public static bool Of(Player p)
        {
            if (p == null) return false;
            if (p == Player.m_localPlayer) return Local;
            ZDO zdo = p.m_nview != null && p.m_nview.IsValid() ? p.m_nview.GetZDO() : null;
            return Has(zdo, out _);
        }
    }
}
