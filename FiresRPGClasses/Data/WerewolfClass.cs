using System;
using FiresCore.Npc.Archetypes;
using UnityEngine;

namespace FiresRPGClasses.Data
{
    /// <summary>
    /// The Werewolf class (Tools\WEREWOLF_CLASS_DESIGN.md). Core's ArchetypeClass ends at Necromancer = 10 and this
    /// mod may not change Core, so the Werewolf is the next number, 11, kept here only. Json.NET saves an enum as its number (and a
    /// dictionary key as "11"), so a record round-trips; the yaml's "class: Werewolf", the console and the UI go through
    /// <see cref="TryParse"/> / <see cref="ClassNames.Of"/>, which know the name. If Core ever adds a named 11 it must be this class.
    /// </summary>
    public static class WerewolfClass
    {
        public const ArchetypeClass Archetype = (ArchetypeClass)11;
        public const string Name = "Werewolf";

        /// <summary>An archetype name ("Werewolf" included), case-insensitive, spaces/hyphens ignored; None never parses.</summary>
        public static bool TryParse(string text, out ArchetypeClass archetype)
        {
            archetype = ArchetypeClass.None;
            if (string.IsNullOrEmpty(text)) return false;
            string collapsed = text.Replace(" ", "").Replace("-", "").Replace("_", "");
            if (string.Equals(collapsed, Name, StringComparison.OrdinalIgnoreCase)) { archetype = Archetype; return true; }
            if (string.Equals(collapsed, VampireClass.Name, StringComparison.OrdinalIgnoreCase)) { archetype = VampireClass.Archetype; return true; }
            // Not a bare number: "11" would otherwise parse to the Werewolf (and "42" to nothing anyone knows).
            if (collapsed.Length > 0 && char.IsDigit(collapsed[0])) return false;
            return Enum.TryParse(collapsed, true, out archetype) && archetype != ArchetypeClass.None;
        }
    }

    /// <summary>The name to show for a class: Core's enum name, or "Werewolf" / "Vampire" for the numbers Core has no name for.</summary>
    public static class ClassNames
    {
        public static string Of(ArchetypeClass archetype) =>
            archetype == WerewolfClass.Archetype ? WerewolfClass.Name
            : archetype == VampireClass.Archetype ? VampireClass.Name
            : archetype.ToString();
    }

    /// <summary>
    /// The Werewolf's requirement is a race, not a weapon (design §2 / §8.1): the player ZDO string "rpg_race" == "Werewolf", which
    /// VCS 0.8.6 publishes. Until every client runs VCS 0.8.6, a player ZDO "vcs_model" of Fenring / Fenring_Cultist counts too, but
    /// only while it is the BASE look (ZDO bool "vcs_in_form" false: VCS's cosmetic form toggle shows a Fenring through the same key)
    /// or while this mod's own Wolf Form runs ("rpg_wolf_active"). Another race in rpg_race ("Vampire") never passes the fallback.
    /// Read-only: RPGClasses never writes rpg_race / vcs_model / vcs_in_form (VCS owns them).
    /// </summary>
    public static class WerewolfRace
    {
        public const string RaceKey = "rpg_race";
        public const string RaceValue = "Werewolf";
        public const string WolfActiveKey = "rpg_wolf_active";
        private static readonly int RaceHash = RaceKey.GetStableHashCode();
        private static readonly int ModelHash = "vcs_model".GetStableHashCode();
        private static readonly int InFormHash = "vcs_in_form".GetStableHashCode();
        // Fires Character Creator's names for the same ZDO keys (the rebrand); a peer can run either mod, so both are read.
        private static readonly int FccModelHash = "fcc_model".GetStableHashCode();
        private static readonly int FccInFormHash = "fcc_in_form".GetStableHashCode();
        internal static readonly int WolfActiveHash = WolfActiveKey.GetStableHashCode();
        private static readonly string[] FallbackModels = { "Fenring", "Fenring_Cultist" };

        // The HUD asks WeaponArchetypes for sixteen slots a frame: the local answer is read once per frame.
        private static int _frame = -1;
        private static bool _local;
        private static string _lastWhy = "";
        private static bool _inputsKnown, _lastHasZdo, _lastInForm, _lastOurs;
        private static string _lastRace, _lastModel;

        /// <summary>The local player has the werewolf race (cached per frame).</summary>
        public static bool Local
        {
            get
            {
                if (Time.frameCount == _frame) return _local;
                _frame = Time.frameCount;
                Player p = Player.m_localPlayer;
                ZDO zdo = p != null && p.m_nview != null && p.m_nview.IsValid() ? p.m_nview.GetZDO() : null;
                // Has() formats its "why" string (boxed bools) and this runs every frame the
                // HUD paints. Only ask it again when one of its ZDO inputs changed.
                bool hasZdo = zdo != null;
                string raceNow = hasZdo ? zdo.GetString(RaceHash, "") : null;
                string modelNow = hasZdo ? zdo.GetString(ModelHash, "") : null;
                bool inFormNow = hasZdo && zdo.GetBool(InFormHash, false);
                bool oursNow = hasZdo && zdo.GetBool(WolfActiveHash, false);
                if (_inputsKnown && hasZdo == _lastHasZdo && inFormNow == _lastInForm && oursNow == _lastOurs
                    && string.Equals(raceNow, _lastRace, StringComparison.Ordinal) && string.Equals(modelNow, _lastModel, StringComparison.Ordinal))
                    return _local;
                _inputsKnown = true; _lastHasZdo = hasZdo; _lastRace = raceNow; _lastModel = modelNow; _lastInForm = inFormNow; _lastOurs = oursNow;
                FiresRPGClasses.UI.ClassesHud.CountRebuild();
                bool has = Has(zdo, out string why);
                if (why != _lastWhy)
                {
                    _lastWhy = why;
                    FiresRPGClassesPlugin.Log?.LogDebug($"[Werewolf] race check for the local player: {(has ? "has" : "lacks")} the race ({why})");
                }
                _local = has;
                return has;
            }
        }

        /// <summary>The same test on any player's ZDO (the server's validator reads the requester's).</summary>
        public static bool Has(ZDO zdo, out string why)
        {
            if (zdo == null) { why = "no player ZDO"; return false; }
            string race = zdo.GetString(RaceHash, "");
            if (race.Length > 0)
            {
                why = RaceKey + " = " + race;
                return string.Equals(race, RaceValue, StringComparison.OrdinalIgnoreCase);
            }
            // Fallback until VCS 0.8.6 (it publishes rpg_race): the base model.
            string fccModel = zdo.GetString(FccModelHash, "");
            bool fcc = fccModel.Length > 0;
            string model = fcc ? fccModel : zdo.GetString(ModelHash, "");
            bool fenring = Array.IndexOf(FallbackModels, model) >= 0;
            bool inForm = zdo.GetBool(fcc ? FccInFormHash : InFormHash, false);
            bool ours = zdo.GetBool(WolfActiveHash, false);
            string pre = fcc ? "fcc_" : "vcs_";
            why = $"no {RaceKey}; {pre}model '{model}', {pre}in_form {inForm}, {WolfActiveKey} {ours}";
            return fenring && (!inForm || ours);
        }
    }
}
