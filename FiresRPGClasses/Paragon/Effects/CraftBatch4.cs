using HarmonyLib;

namespace FiresRPGClasses.Paragon
{
    /// <summary>
    /// C2.4 Sturdy Builder, the last Craft passive. A piece the local
    /// player built (Piece creator = their player id) gets +x% max health on this client: when it loads (WearNTear.Awake, the creator is in
    /// its ZDO) and when it is placed (Piece.SetCreator runs after Awake). Damage and repair run on the piece's ZDO owner, normally the
    /// builder standing at it; on another owner's client the piece keeps vanilla health.
    /// </summary>
    [ParagonEffectSet]
    internal static class CraftBatch4
    {
        internal const string Sturdy = "+{value}% health of pieces you build";
        public static readonly string[] Formats = { Sturdy };

        private static void Raise(WearNTear w, Piece p)
        {
            float k = ParagonEffects.TX(Sturdy);
            Player me = Player.m_localPlayer;
            if (k <= 0f || w == null || p == null || me == null || p.GetCreator() != me.GetPlayerID()) return;
            w.m_health *= 1f + k;
            CraftBatch1.Once(Sturdy, $"{p.m_name} max health {w.m_health:0}");
        }

        [HarmonyPatch(typeof(WearNTear), "Awake")]
        private static class Loaded
        {
            private static void Postfix(WearNTear __instance) => Raise(__instance, __instance.GetComponent<Piece>());
        }

        [HarmonyPatch(typeof(Piece), nameof(Piece.SetCreator))]
        private static class Placed
        {
            private static void Postfix(Piece __instance) => Raise(__instance.GetComponent<WearNTear>(), __instance);
        }
    }
}
