using FiresCore.Npc.Archetypes;
using FiresCore.Npc.Archetypes.StatusEffects;
using FiresRPGClasses.Data;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// The ally-bound actives. Core (0.2.399+) only carries the names - ClassOath, ClassLifeline20/30/40,
    /// ClassSpiritLink15/20/25, each with SourceCharacter = the caster - given by AbilityRPCManager to each ally's owner. This kit acts
    /// there, on the buffed player's own peer. A share of damage moved to another player is sent as a routed Character.Damage
    /// marked with <see cref="SharedMark"/> (HitData.m_backstabBonus, serialized; backstabs never apply to players), so it is
    /// never shared a second time. Perf: int hashes, no allocation except the forwarded hits.
    /// - Guardian's Oath (Tank): 30% of each hit on the bound ally goes to the tank instead (the tank's armor applies there).
    /// - Lifeline (Healer): the first time the ally drops below 30% health, it is healed {heal} of max and the tether ends.
    /// - Spirit Link (Healer, ultimate): a hit on a linked player is split evenly across the linked party members within 12 m,
    ///   and every linked player regenerates {regen} of max health per second.
    /// </summary>
    public static class LinkKit
    {
        private const string OathId = "tank_guardian_s_oath";
        private const string LifelineId = "healer_lifeline";
        private const string SpiritId = "healer_spirit_link";

        private const float SharedMark = -7f;
        private const float OathShare = 0.3f;
        private const float LifelineBelow = 0.3f;
        private const float LinkReach = 12f;
        private const float OathDefaultSeconds = 20f;       // used when the node gives no duration
        private const float LifelineDefaultSeconds = 15f;
        private const float SpiritLinkDefaultSeconds = 8f;

        private static readonly int OathHash = "ClassOath".GetStableHashCode();
        private static readonly string[] LifelineByRank = { "ClassLifeline20", "ClassLifeline30", "ClassLifeline40" };
        private static readonly float[] LifelineHeal = { 0.2f, 0.3f, 0.4f };
        private static readonly int[] LifelineHash = Hashes(LifelineByRank);
        private static readonly string[] SpiritByRank = { "ClassSpiritLink15", "ClassSpiritLink20", "ClassSpiritLink25" };
        private static readonly float[] SpiritRegen = { 0.015f, 0.02f, 0.025f };
        private static readonly int[] SpiritHash = Hashes(SpiritByRank);

        private static float _regenNext;
        private static readonly System.Collections.Generic.List<Player> Linked = new System.Collections.Generic.List<Player>();
        private static bool _linkWhyLogged;
        private static bool _hadLink;
        private static bool _registered;

        public static void RegisterAll()
        {
            if (_registered) return;
            _registered = true;
            ClassSkillKit.Register(OathId, Oath);
            ClassSkillKit.Register(LifelineId, Lifeline);
            ClassSkillKit.Register(SpiritId, SpiritLink);
        }

        private static int[] Hashes(string[] names)
        {
            var h = new int[names.Length];
            for (int i = 0; i < names.Length; i++) h[i] = names[i].GetStableHashCode();
            return h;
        }

        private static void Log(string s) => FiresRPGClassesPlugin.Log?.LogDebug("[Classes] " + s);

        private static Character Ally(Character caster, CastTarget target)
        {
            Character a = target.TargetCharacter;
            return a != null && a != caster && !a.IsDead() && ClassTargeting.IsPartyMember(caster, a) ? a : null;
        }

        // "Bind to an ally for 20 s: {share} of the damage they take is dealt to you instead, reduced by your armor."
        private static bool Oath(ClassTreeNode node, Character caster, CastTarget target)
        {
            Character ally = Ally(caster, target);
            if (ally == null) { (caster as Player)?.Message(MessageHud.MessageType.Center, node.DisplayName + ": no ally under the crosshair"); return false; }
            float seconds = node.EffectDuration > 0f ? node.EffectDuration : OathDefaultSeconds;
            bool ok = AbilityRPCManager.ApplySingleEffect(caster, ally, "ClassOath", seconds);
            Log($"{OathId}: bound to {ally.GetHoverName()} for {seconds:0} s: {ok}");
            return ok;
        }

        // "Tether an ally for 15 s: the first time they drop below 30% health, they are healed for {heal}."
        private static bool Lifeline(ClassTreeNode node, Character caster, CastTarget target)
        {
            Character ally = target.TargetCharacter != null ? target.TargetCharacter : caster;
            if (ally.IsDead() || !ClassTargeting.IsPartyMember(caster, ally)) return false;
            int rank = Mathf.Clamp(DesignKit.RankOf(node), 1, LifelineByRank.Length);
            float seconds = node.EffectDuration > 0f ? node.EffectDuration : LifelineDefaultSeconds;
            bool ok = AbilityRPCManager.ApplySingleEffect(caster, ally, LifelineByRank[rank - 1], seconds);
            Log($"{LifelineId}: {LifelineByRank[rank - 1]} on {ally.GetHoverName()} for {seconds:0} s: {ok}");
            return ok;
        }

        // "Link allies within 12 m for 8 s: damage taken is shared evenly, and everyone regenerates {regen} of max health per second."
        private static bool SpiritLink(ClassTreeNode node, Character caster, CastTarget target)
        {
            int rank = Mathf.Clamp(DesignKit.RankOf(node), 1, SpiritByRank.Length);
            float radius = node.Targeting.Radius > 0f ? node.Targeting.Radius : LinkReach;
            float seconds = node.EffectDuration > 0f ? node.EffectDuration : SpiritLinkDefaultSeconds;
            foreach (Player p in Player.GetAllPlayers())   // who the link considers (logged so a "nobody linked" report can be traced)
                if (p != null) Log($"{SpiritId}: {p.GetPlayerName()} at {Vector3.Distance(p.transform.position, caster.transform.position):0.0} m, party {ClassTargeting.IsPartyMember(caster, p)}");
            int n = AbilityRPCManager.ApplyGroupBuff(caster, SpiritByRank[rank - 1], radius, seconds);
            PublishLink(caster, radius, seconds);
            Log($"{SpiritId}: {SpiritByRank[rank - 1]} on {n} party member(s) within {radius:0} m for {seconds:0} s");
            return n > 0;
        }

        private static int Rank(SEMan seman, int[] hashes)
        {
            for (int i = hashes.Length - 1; i >= 0; i--) if (seman.HaveStatusEffect(hashes[i])) return i + 1;
            return 0;
        }

        private static Character Source(Character c, int hash) =>
            (c.GetSEMan().GetStatusEffect(hash) as CompanionStatusEffectBase)?.SourceCharacter;

        private static HitData Forward(HitData hit, float share, Character to)
        {
            HitData copy = hit.Clone();
            copy.m_damage.Modify(share);
            copy.m_backstabBonus = SharedMark;
            copy.m_point = to.GetCenterPoint();
            copy.m_pushForce = 0f;
            copy.m_staggerMultiplier = 0f;
            return copy;
        }

        /// <summary>Damage the local player takes (RPC_Damage, on their owner): Oath and Spirit Link move shares of it.</summary>
        internal static void OnLocalHitTaken(Character victim, HitData hit)
        {
            Player me = Player.m_localPlayer;
            // A forwarded share ARRIVING here is counted before armor (the proof it crossed peers; after armor it can be too
            // small to read).
            if (victim != null && victim == me && hit != null && hit.m_backstabBonus == SharedMark)
            {
                SharesReceived++;
                SharesReceivedDamage += hit.GetTotalDamage();
                Log($"a shared hit arrived on {me.GetPlayerName()}: {hit.GetTotalDamage():0.0} before armor (#{SharesReceived})");
                return;
            }
            if (victim == null || victim != me || hit == null || hit.m_backstabBonus == SharedMark) return;
            SEMan seman = me.GetSEMan();

            if (seman.HaveStatusEffect(OathHash))
            {
                Character tank = Source(me, OathHash);
                if (tank != null && tank != me && !tank.IsDead())
                {
                    tank.Damage(Forward(hit, OathShare, tank));
                    hit.m_damage.Modify(1f - OathShare);
                    Log($"{OathId}: {OathShare:0%} of a hit on {me.GetPlayerName()} sent to {tank.GetHoverName()}");
                }
            }

            int link = Rank(seman, SpiritHash);
            if (link > 0)
            {
                // On the linked player's own client the inviter may not be "party" yet, so the link's caster always counts as linked (it cast the link on this player), plus the party members in reach.
                Character caster = Source(me, SpiritHash[link - 1]);
                // On the linked player's own peer the caster reference and the party can both come up empty, so the caster's ZDO
                // lists who it linked; that list counts first.
                string listed = LinkListFor(me);
                Linked.Clear();
                var why = _linkWhyLogged ? null : new System.Text.StringBuilder();
                foreach (Player p in Player.GetAllPlayers())
                {
                    if (p == null || p == me) continue;
                    float dist = Vector3.Distance(p.transform.position, me.transform.position);
                    bool isListed = listed != null && Listed(listed, p.GetPlayerID()), party = ClassTargeting.IsPartyMember(me, p);
                    why?.Append($" | {p.GetPlayerName()} id {p.GetPlayerID()} {dist:0.0} m dead {p.IsDead()} listed {isListed} party {party} caster {p == caster}");
                    if (p.IsDead() || dist > LinkReach) continue;
                    if (p == caster || isListed || party) Linked.Add(p);
                }
                if (!_linkWhyLogged)
                {
                    _linkWhyLogged = true;
                    // The first split of each link, every candidate and why, at Info (once per link, not per hit) so the remote
                    // side is visible even when its log drops Debug.
                    FiresRPGClassesPlugin.Log?.LogDebug($"[Classes] {SpiritId}: split check on {me.GetPlayerName()} (id {me.GetPlayerID()}), list {listed ?? "none"}:{why}");
                    Log($"{SpiritId}: on {me.GetPlayerName()}, caster {(caster != null ? caster.GetHoverName() : "unknown")}, ZDO list {listed ?? "none"}; linked: {(Linked.Count > 0 ? string.Join(", ", Linked.ConvertAll(p => p.GetPlayerName()).ToArray()) : "nobody in reach")}");
                }
                int others = Linked.Count;
                if (others > 0)
                {
                    float share = 1f / (others + 1);
                    foreach (Player p in Linked) p.Damage(Forward(hit, share, p));
                    hit.m_damage.Modify(share);
                    Log($"{SpiritId}: a hit on {me.GetPlayerName()} split {others + 1} ways");
                }
            }
        }

        private const string ZdoLinkIds = "rpg_link_ids";
        private const string ZdoLinkUntil = "rpg_link_until";

        // On the caster's peer: the players this link reached (the caster and its party members within the radius), with the end time,
        // on the caster's own ZDO - every member's peer can read it, whatever it knows of the party.
        private static void PublishLink(Character caster, float radius, float seconds)
        {
            if (!(caster is Player cp) || ZNet.instance == null) return;
            ZNetView nview = cp.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid() || !nview.IsOwner()) return;
            var ids = new System.Text.StringBuilder(",").Append(cp.GetPlayerID()).Append(",");
            foreach (Player p in Player.GetAllPlayers())
                if (p != null && p != cp && !p.IsDead() && Vector3.Distance(p.transform.position, cp.transform.position) <= radius
                    && ClassTargeting.IsPartyMember(cp, p)) ids.Append(p.GetPlayerID()).Append(",");
            nview.GetZDO().Set(ZdoLinkIds, ids.ToString());
            nview.GetZDO().Set(ZdoLinkUntil, ZNet.instance.GetTime().Ticks + (long)(seconds * 10000000L));
            Log($"{SpiritId}: link list on {cp.GetPlayerName()}'s ZDO: {ids}");
        }

        // The live link list that names this player (from any player's ZDO, the caster's included), or null.
        private static string LinkListFor(Player me)
        {
            if (ZNet.instance == null) return null;
            long now = ZNet.instance.GetTime().Ticks, myId = me.GetPlayerID();
            foreach (Player p in Player.GetAllPlayers())
            {
                ZNetView nview = p != null ? p.GetComponent<ZNetView>() : null;
                ZDO zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
                if (zdo == null || zdo.GetLong(ZdoLinkUntil, 0L) < now) continue;
                string ids = zdo.GetString(ZdoLinkIds, "");
                if (Listed(ids, myId)) return ids;
            }
            return null;
        }

        private static bool Listed(string ids, long id) => ids.IndexOf("," + id + ",", System.StringComparison.Ordinal) >= 0;

        /// <summary>Called every frame by ClassCombatDriver: Lifeline's threshold and Spirit Link's regeneration (local player).</summary>
        public static void Tick()
        {
            Player me = Player.m_localPlayer;
            if (me == null || me.IsDead()) return;
            SEMan seman = me.GetSEMan();
            int life = Rank(seman, LifelineHash);
            if (life > 0 && me.GetHealth() < me.GetMaxHealth() * LifelineBelow)
            {
                Character healer = Source(me, LifelineHash[life - 1]) ?? me;
                AbilityHeals.Apply(healer, me, me.GetMaxHealth() * LifelineHeal[life - 1]);
                seman.RemoveStatusEffect(LifelineHash[life - 1]);
                Log($"{LifelineId}: {me.GetPlayerName()} below {LifelineBelow:0%}: healed {LifelineHeal[life - 1]:0%}, tether spent");
            }
            if (Time.time < _regenNext) return;
            _regenNext = Time.time + 1f;
            int link = Rank(seman, SpiritHash);
            // The link arriving on this peer (once per link, at Info); the split check re-arms.
            if (link > 0 && !_hadLink) { _linkWhyLogged = false; FiresRPGClassesPlugin.Log?.LogDebug($"[Classes] {SpiritId}: {SpiritByRank[link - 1]} arrived on {me.GetPlayerName()}, from {(Source(me, SpiritHash[link - 1]) is Character s ? s.GetHoverName() : "unknown")}"); }
            _hadLink = link > 0;
            if (link > 0) AbilityHeals.Apply(Source(me, SpiritHash[link - 1]) ?? me, me, me.GetMaxHealth() * SpiritRegen[link - 1]);
        }

        /// <summary>For ClassTest: forwarded shares (Oath / Spirit Link) that arrived on the local player, and their damage before armor.</summary>
        public static int SharesReceived { get; private set; }
        public static float SharesReceivedDamage { get; private set; }

        /// <summary>For ClassTest.</summary>
        public static bool HasOath(Character c) => c != null && c.GetSEMan().HaveStatusEffect(OathHash);
        public static int LifelineRank(Character c) => c != null ? Rank(c.GetSEMan(), LifelineHash) : 0;
        public static int SpiritRank(Character c) => c != null ? Rank(c.GetSEMan(), SpiritHash) : 0;
    }
}
