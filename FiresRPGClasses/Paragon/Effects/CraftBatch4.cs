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

        /// <summary>0.1.220 ([wishbone], [generator]'s piece-create audit row 9, TOWN50): a piece loading. Called from the ONE RPGClasses
        /// WearNTear.Awake postfix (BoneWallLook.DressOnWake); the passive's value (0 for nearly everyone) is tested BEFORE the
        /// GetComponent&lt;Piece&gt;, so a player without Sturdy Builder pays one dictionary lookup per piece. Same behaviour.</summary>
        internal static void OnWake(WearNTear w)
        {
            if (ParagonEffects.TX(Sturdy) <= 0f || w == null) return;
            Raise(w, w.GetComponent<Piece>());
        }

        [HarmonyPatch(typeof(Piece), nameof(Piece.SetCreator))]
        private static class Placed
        {
            private static void Postfix(Piece __instance) => Raise(__instance.GetComponent<WearNTear>(), __instance);
        }
    }
}
