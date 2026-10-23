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
            if (BarController.TryChooseArchetype(side, archetype)) return $"{side} bar is now {ClassNames.Of(archetype)}";
            return $"the {side} bar's weapon doesn't allow {ClassNames.Of(archetype)}; hold one of: {WeaponArchetypes.WeaponsLine(archetype)}";
        }

        private static string LevelCommand(Terminal.ConsoleEventArgs args, ClassPlayerData data)
        {
            if (args.Length < 2 || !int.TryParse(args[1], out int level)) return "usage: class_level <level>";
            return ClassXp.SetLevelForTest(level);
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
