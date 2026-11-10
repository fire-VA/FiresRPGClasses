using System;
using System.Collections.Generic;
using System.Linq;
using FiresCore.Classes;
using FiresRPGClasses.Data;
using FiresRPGClasses.State;
using FiresRPGClasses.UI;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// Testing commands (give yourself XP to try every class), gated like
    /// vanilla's cheats: isCheat, so they need devcommands, which a server only allows its admins. They act on the local
    /// character's class record through the paths play uses:
    /// - class_xp &lt;amount&gt;: ClassXp's grant (levels, talent and attribute points, the level-up message);
    /// - class_level &lt;n&gt;: the class level set outright; lowering it below what is spent refunds the trees;
    /// - class_ult_xp &lt;ultimateId|all&gt; &lt;amount&gt;: an ultimate's own XP, levelling it as its casts do;
    /// - class_set &lt;archetype&gt; [front|back]: the bar's class, the way the Classes window picks it:
    ///   the held weapon must allow it, and the answer names the weapons that do when it doesn't.
    /// Each prints the result to the console and logs it at Info.
    /// </summary>
    internal static class ClassAdminCommands
    {
        private const string Tag = "[ClassAdmin] ";
        private const string All = "all";

        public static void Register()
        {
            new Terminal.ConsoleCommand("class_xp", "[amount] - give your character class XP (cheat)",
                args => Run(args, "class_xp", XpCommand), isCheat: true);
            new Terminal.ConsoleCommand("class_level", $"[level] - set your class level, {ProgressionEngine.FirstLevel} to {ProgressionEngine.MaxLevel} (cheat)",
                args => Run(args, "class_level", LevelCommand), isCheat: true);
            new Terminal.ConsoleCommand("class_ult_xp", "[ultimateId|all] [amount] - give an ultimate XP, as its casts do (cheat)",
                args => Run(args, "class_ult_xp", UltimateXpCommand), isCheat: true,
                optionsFetcher: () => new List<string> { All }.Concat(UltimateCatalog.All.Select(u => u.Id).OrderBy(id => id)).ToList());
            // so a test need not wait out a cooldown an earlier one started: a cheat, like the ones above.
            new Terminal.ConsoleCommand("class_cooldown_reset", "[nodeId|all] - make a class skill (or every one) ready now (cheat)",
                args => Run(args, "class_cooldown_reset", CooldownResetCommand), isCheat: true);
            // 0.1.207 ([lead] 10-30, Studio walk-mode hotbar icons): a test character gets a real class bar the way a player builds
            // one - the Classes window's learn button (TalentRules.TryLearn) then its drag to the bar (BarController.TrySlotSkill /
            // TrySlotUltimate); nothing new writes the bar.
            new Terminal.ConsoleCommand("class_slot", "[front|back] [slot 1-8] [weapon prefab|nodeId] - put a carried weapon (slot 1) or a learned class skill on the bar, as the Classes window does (cheat)",
                args => Run(args, "class_slot", SlotCommand), isCheat: true);
            // 0.1.209 (Studio round 5: "mage_infusion can't be learned: Learn a node in the tier above first"): learn one rank of any
            // class node - the starter tier first - through the Classes window's learn button (TalentRules.TryLearn); nothing is slotted
            new Terminal.ConsoleCommand("class_learn", "[nodeId] - learn one rank of a class node, as the Classes window's learn button does (cheat)",
                args => Run(args, "class_learn", LearnCommand), isCheat: true);
            new Terminal.ConsoleCommand("class_set", "[archetype] [front|back] - choose the bar's class, as the Classes window does (cheat)",
                args => Run(args, "class_set", SetCommand), isCheat: true,
                optionsFetcher: () => ClassTrees.Archetypes.Select(ClassNames.Of).OrderBy(a => a).ToList());
        }

        private static void Run(Terminal.ConsoleEventArgs args, string name, Func<Terminal.ConsoleEventArgs, ClassPlayerData, string> command)
        {
            string line;
            ClassPlayerData data = ClassPlayerStore.Local;
            if (Player.m_localPlayer == null || data == null) line = "no character in a world here";
            else if (ClassPlayerStore.LocalIsProtected) line = "the class record is protected this session (see the log)";
            else
            {
                try { line = command(args, data); }
                catch (Exception e) { line = $"failed: {e.GetType().Name}: {e.Message}"; }
            }
            args.Context?.AddString(name + ": " + line);
            FiresRPGClassesPlugin.Log.LogInfo(Tag + name + ": " + line);
        }

        private static string CooldownResetCommand(Terminal.ConsoleEventArgs args, ClassPlayerData data)
        {
            if (args.Length < 2) return "usage: class_cooldown_reset <nodeId|all>";
            if (string.Equals(args[1], All, StringComparison.OrdinalIgnoreCase)) { CooldownTracker.Clear(); return "every class cooldown is ready"; }
            float left = CooldownTracker.Remaining(args[1]);
            CooldownTracker.Reset(args[1]);
            return $"{args[1]} ready (had {left:0.0} s left)";
        }

        private static string XpCommand(Terminal.ConsoleEventArgs args, ClassPlayerData data)
        {
            if (args.Length < 2 || !long.TryParse(args[1], out long amount) || amount <= 0) return "usage: class_xp <amount>";
            int before = data.Progress.Level;
            long granted = ClassXp.GrantForTest(amount);
            ProgressionRates rates = TalentRules.Rates;
            return $"+{granted} XP, class level {before} -> {data.Progress.Level} ({data.Progress.Xp}/"
                + $"{ProgressionEngine.XpForNextLevel(data.Progress.Level, data.Progress.ValorLevel, rates)}); unspent talent points "
                + $"{data.Progress.UnspentSkillPoints(rates)}, attribute points {data.Progress.UnspentAttributePoints(rates)}";
        }

        private static string SetCommand(Terminal.ConsoleEventArgs args, ClassPlayerData data)
        {
            if (args.Length < 2 || !WerewolfClass.TryParse(args[1], out FiresCore.Npc.Archetypes.ArchetypeClass archetype)
                || !ClassTrees.Archetypes.Contains(archetype))
                return "usage: class_set <" + string.Join("|", ClassTrees.Archetypes.Select(ClassNames.Of).ToArray()) + "> [front|back]";
            BarSide side = data.ActiveBar;
            if (args.Length >= 3)
            {
                if (string.Equals(args[2], "front", StringComparison.OrdinalIgnoreCase)) side = BarSide.Front;
                else if (string.Equals(args[2], "back", StringComparison.OrdinalIgnoreCase)) side = BarSide.Back;
                else return "usage: class_set <archetype> [front|back]";
            }
            return Why(GodApi.SetArchetype(side.ToString(), args[1]));   // 0.1.210: one implementation (GodApi)
        }

        // 0.1.210: the console answers with GodApi's "why" (the JSON's state is for the Studio)
        private static string Why(string json)
        {
            try { var o = Newtonsoft.Json.Linq.JObject.Parse(json); return ((bool)o["ok"] ? "" : "refused: ") + (string)o["why"]; } catch { return json; }
        }

        private static string LearnCommand(Terminal.ConsoleEventArgs args, ClassPlayerData data)
        {
            if (args.Length < 2) return "usage: class_learn <nodeId>";
            return Why(GodApi.Learn(args[1]));
        }

        private static string SlotCommand(Terminal.ConsoleEventArgs args, ClassPlayerData data)
        {
            const string usage = "usage: class_slot <front|back> <slot 1-8> <weapon prefab (slot 1) | nodeId>";
            if (args.Length < 4 || !int.TryParse(args[2], out int slot)) return usage;
            BarSide side;
            if (string.Equals(args[1], "front", StringComparison.OrdinalIgnoreCase)) side = BarSide.Front;
            else if (string.Equals(args[1], "back", StringComparison.OrdinalIgnoreCase)) side = BarSide.Back;
            else return usage;
            string id = args[3];
            // 0.1.210: one implementation - slot 1 = GodApi.SetWeapon (the drag path), 2-8 = GodApi.SlotSkill (learn if needed, then the bar path)
            if (slot == BarLayout.WeaponSlot) return Why(GodApi.SetWeapon(side.ToString(), id));
            if (slot != BarLayout.UltimateSlot && !BarLayout.IsSkillSlot(slot)) return usage;
            return Why(GodApi.SlotSkill(side.ToString(), slot, id));
        }

        private static string LevelCommand(Terminal.ConsoleEventArgs args, ClassPlayerData data)
        {
            if (args.Length < 2 || !int.TryParse(args[1], out int level)) return "usage: class_level <level>";
            return Why(GodApi.SetLevel(level));
        }

        private static string UltimateXpCommand(Terminal.ConsoleEventArgs args, ClassPlayerData data)
        {
            if (args.Length < 3 || !long.TryParse(args[2], out long amount) || amount <= 0) return "usage: class_ult_xp <ultimateId|all> <amount>";
            string which = args[1];
            List<UltimateDefinition> targets = string.Equals(which, All, StringComparison.OrdinalIgnoreCase)
                ? UltimateCatalog.All.ToList()
                : UltimateCatalog.All.Where(u => string.Equals(u.Id, which, StringComparison.OrdinalIgnoreCase)).ToList();
            if (targets.Count == 0) return $"no ultimate '{which}' (tab lists them)";

            var levels = new List<string>();
            foreach (UltimateDefinition ultimate in targets)
            {
                int gained = UltimateController.AwardUltimateXp(data, ultimate.Id, amount);
                if (targets.Count == 1 || gained > 0) levels.Add($"{ultimate.Id} L{data.UltimateLevel(ultimate.Id)} (+{gained})");
            }
            ClassPlayerStore.SaveLocal();
            return $"+{amount} XP to {targets.Count} ultimate(s)"
                + (levels.Count > 0 ? ": " + string.Join(", ", levels.ToArray()) : "; none levelled");
        }
    }
}
