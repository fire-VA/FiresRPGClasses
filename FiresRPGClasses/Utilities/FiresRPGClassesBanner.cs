using System;
using FiresCore.Logging;
using UnityEngine;

namespace FiresRPGClasses
{
    // FiresRPGClasses' console banner, rendered through the shared Banner engine. FIRES / RPG / CLASSES
    // block letters over a rune band, then the eight archetypes. Fires at the top of Awake.
    // Colour zones follow the family: FIRES Red, RPG Yellow (as in FiresRPGmaker), CLASSES Green.
    internal static class FiresRPGClassesBanner
    {
        public static void PrintBig()
        {
            try
            {
                if (FiresRPGClassesPlugin.Log != null)
                    FiresRPGClassesPlugin.Log.LogInfo("Fires RPG Classes Loading...");
                else
                    Debug.Log($"[{FiresRPGClassesPlugin.PluginName}] Fires RPG Classes Loading...");
                Banner.Print(s_bigLines);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[{FiresRPGClassesPlugin.PluginName}] Big banner failed: {ex.Message}");
            }
        }

        private static Banner.Segment[] L(params Banner.Segment[] segs) => segs;
        private static Banner.Segment S(string text, ConsoleColor color) => new Banner.Segment(text, color);

        private static readonly Banner.Segment[][] s_bigLines =
        {
            new Banner.Segment[0],
            L(S("      ᚠ  ᚢ  ᚦ  ᚨ  ᚱ  ᚲ  ᚷ  ᚹ  ᚺ  ᚾ  ᛁ  ᛃ", ConsoleColor.DarkGreen)),
            new Banner.Segment[0],
            L(S("        ✦      ·      ✧      ·      ✦", ConsoleColor.Green)),
            new Banner.Segment[0],
            L(S(" ███████╗██╗██████╗ ███████╗ ███████╗", ConsoleColor.Red)),
            L(S(" ██╔════╝██║██╔══██╗██╔════╝ ██╔════╝", ConsoleColor.Red)),
            L(S(" █████╗  ██║██████╔╝█████╗   ███████╗", ConsoleColor.Red)),
            L(S(" ██╔══╝  ██║██╔══██╗██╔══╝   ╚════██║", ConsoleColor.DarkRed)),
            L(S(" ██║     ██║██║  ██║███████╗ ███████║", ConsoleColor.DarkRed)),
            L(S(" ╚═╝     ╚═╝╚═╝  ╚═╝╚══════╝ ╚══════╝", ConsoleColor.DarkRed)),
            new Banner.Segment[0],
            L(S("        ██████╗ ██████╗  ██████╗", ConsoleColor.Yellow)),
            L(S("        ██╔══██╗██╔══██╗██╔════╝", ConsoleColor.Yellow)),
            L(S("        ██████╔╝██████╔╝██║  ███╗", ConsoleColor.Yellow)),
            L(S("        ██╔══██╗██╔═══╝ ██║   ██║", ConsoleColor.DarkYellow)),
            L(S("        ██║  ██║██║     ╚██████╔╝", ConsoleColor.DarkYellow)),
            L(S("        ╚═╝  ╚═╝╚═╝      ╚═════╝", ConsoleColor.DarkYellow)),
            new Banner.Segment[0],
            L(S("      ██████╗██╗      █████╗ ███████╗███████╗███████╗███████╗", ConsoleColor.Green)),
            L(S("     ██╔════╝██║     ██╔══██╗██╔════╝██╔════╝██╔════╝██╔════╝", ConsoleColor.Green)),
            L(S("     ██║     ██║     ███████║███████╗███████╗█████╗  ███████╗", ConsoleColor.Green)),
            L(S("     ██║     ██║     ██╔══██║╚════██║╚════██║██╔══╝  ╚════██║", ConsoleColor.DarkGreen)),
            L(S("     ╚██████╗███████╗██║  ██║███████║███████║███████╗███████║", ConsoleColor.DarkGreen)),
            L(S("      ╚═════╝╚══════╝╚═╝  ╚═╝╚══════╝╚══════╝╚══════╝╚══════╝", ConsoleColor.DarkGreen)),
            new Banner.Segment[0],
            L(S("═══════════════════════════════════════════════", ConsoleColor.DarkGray)),
            L(S("     SKILLS • TALENTS • CLASSES • ULTIMATES", ConsoleColor.Green)),
            L(S("═══════════════════════════════════════════════", ConsoleColor.DarkGray)),
            new Banner.Segment[0],
            L(S("   TANK      PALADIN     BERSERKER   ROGUE", ConsoleColor.White)),
            L(S("   MONK      RANGER      MAGE        HEALER", ConsoleColor.White)),
            new Banner.Segment[0],
            L(S("   RPG CLASSES TO ENHANCE YOUR VALHEIM ADVENTURE", ConsoleColor.White)),
            new Banner.Segment[0],
            L(S("      ᛚ  ᛜ  ᛟ  ᛞ  ᛖ  ᛗ  ᛊ  ᛏ  ᛒ  ᛉ  ᛇ  ᛈ", ConsoleColor.DarkGreen)),
            new Banner.Segment[0],
        };
    }
}
