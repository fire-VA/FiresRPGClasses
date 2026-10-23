using System.Collections.Generic;

namespace FiresRPGClasses.ExtraSkills
{
    /// <summary>
    /// The one place a player-facing name of an extra skill comes from (skills dialog, level-up messages, console).
    /// Code ids (and the saved SkillType numbers) stay Agility / Endurance / Vigor / Intellect / Hoarder; only these
    /// display names may change (currently Acrobatics, Stamina, Fortitude, Arcana, Hoarder).
    /// </summary>
    public static class ExtraSkillNames
    {
        private static readonly Dictionary<string, string> Display = new Dictionary<string, string>
        {
            { ExtraSkillSystem.AgilityId, "Acrobatics" },
            { ExtraSkillSystem.EnduranceId, "Stamina" },
            { ExtraSkillSystem.VigorId, "Fortitude" },
            { ExtraSkillSystem.IntellectId, "Arcana" },
            { ExtraSkillSystem.HoarderId, "Hoarder" },
        };

        /// <summary>The name the player sees for a skill code id; the id itself when unknown.</summary>
        public static string Get(string codeId) => codeId != null && Display.TryGetValue(codeId, out string name) ? name : codeId;
    }
}
