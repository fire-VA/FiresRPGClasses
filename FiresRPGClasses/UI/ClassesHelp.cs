using System;
using System.Collections.Generic;
using FiresRPGClasses.Hints;
using HarmonyLib;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// The class system's help in vanilla's compendium (Fire 10-01: "proper help menu entries", where the Raven texts are
    /// re-read): the classes and their paths, the two bars, what your weapon decides, talent points and ranks, active /
    /// passive / ultimate, and attributes.
    ///
    /// The words are [wishbone]'s Hints\ClassRavenHints, the same text the raven speaks, built live from the mod's real
    /// rules, so the compendium and the raven never disagree and never go stale. Every entry is added each time the
    /// compendium opens (vanilla rebuilds the list then); nothing is saved into the character, and the ravens are labelless
    /// so they don't add a second copy. Plan: Tools\CLASSES_UI_PLAN.md
    /// </summary>
    internal static class ClassesHelp
    {
        private const string TopicPrefix = "Classes: ";

        /// <summary>[wishbone]'s cheat commands (Combat\ClassAdminCommands), listed only while devcommands are on.</summary>
        private static readonly string[] AdminCommands = { "class_xp", "class_level", "class_ult_xp" };

        private static readonly AccessTools.FieldRef<Dictionary<string, Terminal.ConsoleCommand>> Commands =
            AccessTools.StaticFieldRefAccess<Dictionary<string, Terminal.ConsoleCommand>>(AccessTools.Field(typeof(Terminal), "commands"));

        /// <summary>
        /// "class_xp [amount] - give your character class XP (cheat)", one line per command, read from vanilla's console table
        /// where ClassAdminCommands registered them, so the help shows exactly what the console does. Empty unless
        /// devcommands are on (a server allows them only for its admins), so players never see cheats.
        /// </summary>
        private static string AdminText()
        {
            if (!Terminal.m_cheat) return "";
            Dictionary<string, Terminal.ConsoleCommand> table = Commands();
            if (table == null) return "";
            var lines = new List<string> { "Devcommands are on. These test the class system:" };
            foreach (string name in AdminCommands)
                if (table.TryGetValue(name, out Terminal.ConsoleCommand command) && command != null)
                    lines.Add("<color=orange>" + command.Command + "</color> " + command.Description);
            return lines.Count > 1 ? string.Join("\n\n", lines.ToArray()) : "";
        }

        [HarmonyPatch(typeof(TextsDialog), "UpdateTextsList")]
        private static class AddEntries
        {
            [HarmonyPostfix]
            private static void Postfix(List<TextsDialog.TextInfo> ___m_texts)
            {
                if (___m_texts == null) return;
                try
                {
                    foreach (string key in ClassRavenHints.Keys)
                    {
                        string topic = ClassRavenHints.Topic(key);
                        string text = ClassRavenHints.Text(key);
                        if (string.IsNullOrEmpty(topic) || string.IsNullOrEmpty(text)) continue;
                        ___m_texts.Add(new TextsDialog.TextInfo(TopicPrefix + topic, text));
                    }
                    string admin = AdminText();
                    if (admin.Length > 0) ___m_texts.Add(new TextsDialog.TextInfo(TopicPrefix + "Admin commands", admin));
                }
                catch (Exception ex)
                {
                    FiresRPGClassesPlugin.Log.LogWarning("[ClassesHelp] compendium entries skipped: " + ex.Message);
                }
            }
        }
    }
}
