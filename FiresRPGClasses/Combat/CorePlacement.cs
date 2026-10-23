using FiresCore.Npc.Archetypes.StatusEffects;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// Hands a placed skill's aim to Core's next placed area effect (CompanionStatusEffectBase.SetNextPlacement, Core 0.2.200).
    /// An older Core lacks it: the first call finds out, logs once, and the effect keeps Core's own placement.
    /// </summary>
    internal static class CorePlacement
    {
        private static bool _missing;

        internal static void Hand(Vector3 point)
        {
            if (_missing) return;
            try { HandToCore(point); }
            catch (System.Exception e)
            {
                _missing = true;
                FiresRPGClassesPlugin.Log.LogInfo($"placed aim not handed to Core: this Core has no SetNextPlacement ({e.GetType().Name})");
            }
        }

        // Separate method: on an older Core the missing member throws when this is compiled, inside Hand's try.
        private static void HandToCore(Vector3 point) => CompanionStatusEffectBase.SetNextPlacement(point);
    }
}
