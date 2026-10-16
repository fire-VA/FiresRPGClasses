using BepInEx.Configuration;
using HarmonyLib;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// Doc 12 section 10: the inventory's Skills button opens THIS window, not Valheim's skills dialog.
    /// The vanilla list is not lost - it is the Skills tab inside, so the button still answers the question
    /// it always did and the class boards sit beside it.
    /// Plan: Docs/PLAN_ClassesCombatAndUI.md
    /// </summary>
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnOpenSkills))]
    internal static class InventoryGuiOnOpenSkillsPatch
    {
        private const string Section = "6 - UI";

        private static ConfigEntry<bool> _takeOver;

        public static void BindConfig(ConfigFile config)
        {
            if (_takeOver != null) return;
            _takeOver = config.Bind(Section, "SkillsButtonOpensClasses", true,
                "The inventory's Skills button opens the Classes window (with Valheim's skills as its first "
                + "tab). Turn off to get the vanilla skills dialog back.");
        }

        [HarmonyPrefix]
        private static bool OpenClassesInstead()
        {
            if (_takeOver == null || !_takeOver.Value) return true;
            if (Player.m_localPlayer == null) return true;

            ClassesPanel panel = ClassesPanel.Instance;
            if (panel == null) return true;

            panel.Open(ClassesTab.Skills);
            return false;
        }
    }
}
