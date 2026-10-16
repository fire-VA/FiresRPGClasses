using FiresCore.Classes;
using HarmonyLib;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// The vanilla moments that feed ClassXp, all on the local player's own client (Valheim 1.0, checked against the assembly):
    /// - discoveries: Player.AddKnownBiome (a new biome sector name, vanilla's "biome found"; a named variant pays as a location),
    ///   AddKnownLocationName, AddKnownItem, AddKnownRecipe, AddKnownPiece: each pays once, when vanilla learns it;
    /// - Player.OnSkillLevelup;
    /// - fight activity: the local player's hits (Character.Damage runs on the attacker's side) and the hits it takes
    ///   (Character.RPC_Damage runs on the local player's own client);
    /// - gathering: hits on trees, logs, small rocks and destructibles (counted when the object is then destroyed), MineRock5
    ///   pieces breaking (RPC_SetAreaHealth reaches every client), and picking.
    /// </summary>
    internal static class ClassXpHooks
    {
        private static bool Local(Player player) => player != null && player == Player.m_localPlayer;
        private static bool ByLocal(HitData hit) => hit != null && Player.m_localPlayer != null && hit.GetAttacker() == Player.m_localPlayer;

        // -- discoveries ----------------------------------------------------------------------------------------------------

        [HarmonyPatch(typeof(Player), "AddKnownBiome")]
        private static class KnownBiome
        {
            private static void Prefix(Player __instance, BiomeSector biome, out bool __state) =>
                __state = Local(__instance) && biome != null && !__instance.IsBiomeKnown(biome);

            private static void Postfix(Player __instance, BiomeSector biome, bool __state)
            {
                if (!__state || !__instance.IsBiomeKnown(biome)) return;
                bool variant = biome.AltBiomes != null && biome.AltBiomes.Count > 0;
                ClassXp.Discovery(variant ? DiscoveryKind.LocationType : DiscoveryKind.Biome, biome.GetName(false));
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.AddKnownLocationName))]
        private static class KnownLocation
        {
            private static void Prefix(Player __instance, string label, out bool __state) =>
                __state = Local(__instance) && !string.IsNullOrEmpty(label) && !__instance.HaveSeenTutorial(label);

            private static void Postfix(Player __instance, string label, bool __state)
            {
                if (__state && __instance.HaveSeenTutorial(label)) ClassXp.Discovery(DiscoveryKind.LocationType, label);
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.AddKnownItem))]
        private static class KnownItem
        {
            private static void Prefix(Player __instance, ItemDrop.ItemData item, out bool __state) =>
                __state = Local(__instance) && item?.m_shared != null && !__instance.IsMaterialKnown(item.m_shared.m_name);

            private static void Postfix(Player __instance, ItemDrop.ItemData item, bool __state)
            {
                if (__state && __instance.IsMaterialKnown(item.m_shared.m_name)) ClassXp.Discovery(DiscoveryKind.ItemType, item.m_shared.m_name);
            }
        }

        [HarmonyPatch(typeof(Player), "AddKnownRecipe")]
        private static class KnownRecipe
        {
            private static string Name(Recipe recipe) => recipe?.m_item?.m_itemData?.m_shared?.m_name;

            private static void Prefix(Player __instance, Recipe recipe, out bool __state) =>
                __state = Local(__instance) && Name(recipe) != null && !__instance.IsRecipeKnown(Name(recipe));

            private static void Postfix(Player __instance, Recipe recipe, bool __state)
            {
                if (__state && __instance.IsRecipeKnown(Name(recipe))) ClassXp.Discovery(DiscoveryKind.RecipeOrPiece, Name(recipe));
            }
        }

        [HarmonyPatch(typeof(Player), "AddKnownPiece")]
        private static class KnownPiece
        {
            private static void Prefix(Player __instance, Piece piece, out bool __state) =>
                __state = Local(__instance) && piece != null && !string.IsNullOrEmpty(piece.m_name) && !__instance.IsRecipeKnown(piece.m_name);

            private static void Postfix(Player __instance, Piece piece, bool __state)
            {
                if (__state && __instance.IsRecipeKnown(piece.m_name)) ClassXp.Discovery(DiscoveryKind.RecipeOrPiece, piece.m_name);
            }
        }

        // -- skill level-ups ------------------------------------------------------------------------------------------------

        [HarmonyPatch(typeof(Player), nameof(Player.OnSkillLevelup))]
        private static class SkillLevelup
        {
            private static void Postfix(Player __instance, Skills.SkillType skill, float level)
            {
                if (Local(__instance)) ClassXp.SkillLevelUp(skill, level);
            }
        }

        // -- fight activity -------------------------------------------------------------------------------------------------

        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        private static class DamageDealt
        {
            private static void Prefix(Character __instance, HitData hit)
            {
                if (__instance != Player.m_localPlayer && ByLocal(hit)) ClassXp.MarkActive();
            }
        }

        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        private static class DamageTaken
        {
            private static void Prefix(Character __instance, HitData hit)
            {
                if (__instance == null || __instance != Player.m_localPlayer || hit == null) return;
                Character attacker = hit.GetAttacker();
                if (attacker != null && attacker != __instance) ClassXp.MarkActive();
            }
        }

        // -- gathering ------------------------------------------------------------------------------------------------------

        [HarmonyPatch(typeof(TreeBase), nameof(TreeBase.Damage))]
        private static class TreeHit
        {
            private static void Prefix(TreeBase __instance, HitData hit) { if (ByLocal(hit)) ClassXp.NoteWorkHit(__instance, "tree felled"); }
        }

        [HarmonyPatch(typeof(TreeLog), nameof(TreeLog.Damage))]
        private static class LogHit
        {
            private static void Prefix(TreeLog __instance, HitData hit) { if (ByLocal(hit)) ClassXp.NoteWorkHit(__instance, "log split"); }
        }

        [HarmonyPatch(typeof(MineRock), nameof(MineRock.Damage))]
        private static class SmallRockHit
        {
            private static void Prefix(MineRock __instance, HitData hit) { if (ByLocal(hit)) ClassXp.NoteWorkHit(__instance, "rock broken"); }
        }

        [HarmonyPatch(typeof(Destructible), nameof(Destructible.Damage))]
        private static class DestructibleHit
        {
            private static void Prefix(Destructible __instance, HitData hit) { if (ByLocal(hit)) ClassXp.NoteWorkHit(__instance, "destructible broken"); }
        }

        [HarmonyPatch(typeof(MineRock5), nameof(MineRock5.Damage))]
        private static class RockHit
        {
            private static void Prefix(MineRock5 __instance, HitData hit) { if (ByLocal(hit)) ClassXp.NoteRockHit(__instance); }
        }

        [HarmonyPatch(typeof(MineRock5), "RPC_SetAreaHealth")]
        private static class RockPiece
        {
            private static void Postfix(MineRock5 __instance, float health)
            {
                if (health <= 0f) ClassXp.RockPieceBroken(__instance, "rock piece broken");
            }
        }

        [HarmonyPatch(typeof(Pickable), nameof(Pickable.Interact))]
        private static class Pick
        {
            private static void Prefix(Pickable __instance, Humanoid character, out bool __state) =>
                __state = character != null && character == Player.m_localPlayer && !__instance.GetPicked() && __instance.CanBePicked();

            private static void Postfix(Pickable __instance, bool __result, bool __state)
            {
                if (__state && __result) ClassXp.Picked(__instance, "picked " + Utils.GetPrefabName(__instance.gameObject));
                if (__state && __result) DruidNecroPassives.Picked(__instance); // Forager
            }
        }
    }
}
