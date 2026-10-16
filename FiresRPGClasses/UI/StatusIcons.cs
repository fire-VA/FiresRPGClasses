using System;
using System.Collections.Generic;
using System.Reflection;
using FiresCore.Npc.Archetypes.StatusEffects;
using FiresRPGClasses.Data;
using UnityEngine;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// Class buff / debuff icons (Fire 10-12, the placeholder-visuals audit's icon row): every status effect a class skill applies
    /// shows that skill's tree icon instead of Core's colour swatch. Core 0.2.404 asks StatusEffectManager.IconResolver first for
    /// any effect name; this resolver answers with ClassIcons.For(the skill that applies it), null (the swatch) for anything else.
    /// The effect -> skill map is RPGClasses' (it applies the effects by name), built from three places, first match wins:
    ///   1. the tree: a node's EffectName (ClassCastExecutor applies it);
    ///   2. CoreSkillKit.Casts: the Core-backed skills (id -> effect);
    ///   3. the hand table below: effects the kits apply by constant; for one several skills share, its main skill.
    /// </summary>
    internal static class StatusIcons
    {
        private static readonly Dictionary<string, string> Owner = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly HashSet<string> Unresolved = new HashSet<string>(StringComparer.Ordinal);
        private static bool _installed;

        private static readonly (string effect, string node)[] HandTable =
        {
            ("ClassWithered", "necromancer_wither"),
            ("ClassWounded", "necromancer_grave_pestilence"),
            ("ClassOath", "tank_guardian_s_oath"),
            ("ClassLifeline20", "healer_lifeline"), ("ClassLifeline30", "healer_lifeline"), ("ClassLifeline40", "healer_lifeline"),
            (StatusEffectManager.EFFECT_ROOTED, "druid_entangling_roots"),
            (StatusEffectManager.EFFECT_CLASS_DAMAGE25, "druid_alpha_howl"),
            (StatusEffectManager.EFFECT_CLASS_HASTE20, "druid_wild_hunt"),
            (StatusEffectManager.EFFECT_DEATH_MARK, "druid_wild_hunt"),
            (StatusEffectManager.EFFECT_STEALTH, "rogue_smoke_bomb"),
            (StatusEffectManager.EFFECT_CLASS_LOSE_TARGET, "rogue_smoke_bomb"),
            (StatusEffectManager.EFFECT_HUNTERS_MARK, "ranger_apex_predator"),
            (StatusEffectManager.EFFECT_POISON, "rogue_virulence"),
            (StatusEffectManager.EFFECT_CLASS_SLOW30, "mage_ice_lance"),
            (StatusEffectManager.EFFECT_CLASS_SLOW20, "mage_frostbite"),
            (StatusEffectManager.EFFECT_CLASS_KNOCKDOWN, "tank_breakwall"),
        };

        /// <summary>Installs the resolver once the trees are loaded (call again later is harmless). Needs Core 0.2.404+.</summary>
        internal static void Install()
        {
            if (_installed || !ClassTrees.IsLoaded) return;
            PropertyInfo prop = typeof(StatusEffectManager).GetProperty("IconResolver", BindingFlags.Public | BindingFlags.Static);
            if (prop == null)
            {
                FiresRPGClassesPlugin.Log?.LogDebug("[StatusIcons] Core has no StatusEffectManager.IconResolver (needs 0.2.404+): class effects keep their swatches");
                _installed = true;
                return;
            }
            BuildMap();
            prop.SetValue(null, (Func<string, Sprite>)Resolve);
            _installed = true;
            FiresRPGClassesPlugin.Log?.LogDebug($"[StatusIcons] icon resolver installed: {Owner.Count} effect(s) mapped to a skill icon");
        }

        private static void BuildMap()
        {
            Owner.Clear();
            foreach (ClassTreeNode node in ClassTrees.AllNodes)
                if (node != null && !string.IsNullOrEmpty(node.EffectName) && !Owner.ContainsKey(node.EffectName)) Owner[node.EffectName] = node.Id;
            try
            {
                var casts = typeof(Combat.CoreSkillKit).GetField("Casts", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as System.Collections.IDictionary;
                if (casts != null)
                    foreach (System.Collections.DictionaryEntry kv in casts)
                    {
                        string effect = kv.Value?.GetType().GetField("Effect")?.GetValue(kv.Value) as string;
                        if (!string.IsNullOrEmpty(effect) && !Owner.ContainsKey(effect)) Owner[effect] = (string)kv.Key;
                    }
            }
            catch (Exception e) { FiresRPGClassesPlugin.Log?.LogDebug($"[StatusIcons] reading CoreSkillKit.Casts threw {e.GetType().Name}: {e.Message}"); }
            foreach (var (effect, node) in HandTable)
                if (!string.IsNullOrEmpty(effect) && !Owner.ContainsKey(effect)) Owner[effect] = node;
        }

        private static Sprite Resolve(string effectName)
        {
            if (effectName == null) return null;
            // Fire 10-14: the effect's own family icon first (se_<Family>.png); the applying skill's icon only until it exists.
            Sprite own = ClassIcons.ForEffect(effectName);
            if (own != null) return own;
            if (!Owner.TryGetValue(effectName, out string node)) return null;
            Sprite icon = ClassIcons.For(node);
            if (icon == null && Unresolved.Add(effectName))
                FiresRPGClassesPlugin.Log?.LogDebug($"[StatusIcons] {effectName} maps to '{node}' but that skill has no icon: swatch kept");
            return icon;
        }
    }
}
