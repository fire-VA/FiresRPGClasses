using System.Collections.Generic;
using System.Linq;
using FiresCore.Npc.Archetypes;
using FiresCore.Possession;
using FiresRPGClasses.Data;
using FiresRPGClasses.State;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// The two possession skills (Tools\CLASSES_DRUID_NECROMANCER.md sections 3, 4, 8 and 8b) on Core's PossessionApi:
    /// - Druid Wildshape: consume a beaten creature and take its form (Body Hidden, ConsumeTarget, a form health pool, damage
    ///   scaled by class level);
    /// - Necromancer Bind the Dead: the body rises and channels, untouchable, while the mind walks in an undead humanoid
    ///   (Body Frozen, PlayerInvulnerable, ChannelLock, the 1.5 m hover, 2 eitr a second, the 60 m link).
    /// Both pay their eitr when the cast starts and get it (and the cooldown) back when the server refuses the grant.
    /// The server checks are registered on every machine (<see cref="RegisterValidators"/>); only the server's copy runs.
    /// Defaults used where the design gives no number: damage scale 1 + 1% per class level above 1; a refused grant
    /// refunds in full; the server allows Wildshape up to 60% health (rank 3's threshold) since it doesn't know the rank.
    /// </summary>
    public static class PossessionKit
    {
        internal const string WildshapeId = "druid_wildshape";
        internal const string BindDeadId = "necromancer_bind_dead";
        private const string LastingFormId = "druid_lasting_form";
        private const string DeepLinkId = "necromancer_deep_link";

        internal const string DruidTag = "rpg-druid";
        internal const string NecroTag = "rpg-necro";

        private const float WildshapeRange = 8f;
        // The engine carries the body (Hidden) only inside humanoid-rig creatures; any other form leaves it standing frozen and
        // untouchable, so the form still needs a leash to that body.
        private const float WildshapeLeashMetres = 60f;
        private const float BindRange = 15f;
        private const float BindLinkMetres = 60f;
        private const float BindUndeadHealth = 0.5f;
        private const float BindDrainPerSecond = 2f;
        private const float ServerWildshapeHealth = 0.6f;
        // The server reads health a moment after the client did: a creature regenerating in between still passes.
        private const float ServerHealthSlack = 0.05f;
        private const float DamagePerLevel = 0.01f;
        private const int EliteRank = 3;

        private const float HoverMetres = 1.5f;
        private const float HoverRiseSeconds = 0.6f;
        private const float HoverBobMetres = 0.15f;
        private const float HoverPeriodSeconds = 3f;
        private const float HoverDescentSeconds = 0.5f;

        private const string BindStartFx = "fx_summon_start";
        private const string BindRiseFx = "fx_summon_spirit_spawn";
        private const string BindReturnFx = "fx_summon_skeleton_spawn";
        private const string LinkSnapFx = "fx_StaffShield_Break";

        private static readonly int SummonOwnerKey = "fires_summon_owner".GetStableHashCode();
        private static readonly FiresCore.Logging.FiresLog DebugLog = new FiresCore.Logging.FiresLog("FiresRPGClasses", () => false);

        private static bool _registered;
        private static bool _validatorsRegistered;
        // One possession per client: the request in flight (paid, not yet granted) and the one that started.
        private static bool _pendingOpen;
        private static string _pendingNodeId;
        private static float _pendingEitr;
        private static PossessHandle _live;

        public static void RegisterAll()
        {
            if (_registered) return;
            _registered = true;
            ClassSkillKit.Register(WildshapeId, Wildshape);
            ClassSkillKit.Register(BindDeadId, BindDead);
            ClassSkillKit.Register(GhostwalkId, Ghostwalk);
            ClassSkillKit.Register(FormLibraryId, FormLibrary);
            ClassSkillKit.Register(PrimalAvatarId, PrimalAvatar);
            ClassSkillKit.RegisterPassive(LastingFormId);
            ClassSkillKit.RegisterPassive(DeepLinkId);
            PossessionApi.Started += OnStarted;
            PossessionApi.Ended += OnEnded;
            RegisterValidators();
        }

        /// <summary>The server's half: runs on the dedicated server too (Ascend's server branch calls it).</summary>
        public static void RegisterValidators()
        {
            if (_validatorsRegistered) return;
            _validatorsRegistered = true;
            PossessionApi.RegisterValidator(DruidTag, ValidateWildshape);
            PossessionApi.RegisterValidator(NecroTag, ValidateBind);
            PossessionApi.RegisterValidator(GhostTag, ValidateGhost);
        }

        // ── Druid: Wildshape ──────────────────────────────────────────────────────────────────────────────────────────

        // "Consume a creature under your crosshair within 8 m at or below {health} health (a tameable animal or your own summon
        // at any health) and take its form, moves and attacks for {time} s. The form has its own health; when it falls you drop
        // out and take the overflow." Lasting Form adds {time} and {hp}.
        private static bool Wildshape(ClassTreeNode node, Character caster, CastTarget target)
        {
            if (!(caster is Player player)) return false;
            int rank = DesignKit.RankOf(node);
            Character form = Pick(player, target, WildshapeRange);
            float threshold = node.Value("health", rank, 0.4f);
            string why = form == null ? "no creature under your crosshair" : WildshapeRefusal(player, form, rank, threshold);
            if (why != null) return Refuse(player, node, why);

            float eitr = EitrCost(node);
            if (!player.HaveEitr(eitr)) return Refuse(player, node, "not enough eitr");

            int lasting = LearnedRank(ArchetypeClass.Druid, LastingFormId);
            ClassTreeNode lastingNode = lasting > 0 ? ClassTrees.Node(LastingFormId) : null;
            float timeBonus = lastingNode != null ? lastingNode.Value("time", lasting) : 0f;
            float hpBonus = lastingNode != null ? lastingNode.Value("hp", lasting) : 0f;

            var options = new PossessOptions
            {
                Body = PossessBody.Hidden,
                ConsumeTarget = true,
                ReturnToAnchor = false,
                MaxDuration = node.Value("time", rank, 30f) * (1f + timeBonus),
                MaxRange = WildshapeLeashMetres,
                FormHealth = form.GetMaxHealth() * (1f + hpBonus),
                DamageScale = 1f + DamagePerLevel * (ClassLevel() - 1),
                Filter = c => WildshapeRefusal(player, c, rank, threshold),
                SourceTag = DruidTag,
            };
            return Begin(player, node, form, options, eitr);
        }

        private static string WildshapeRefusal(Player player, Character c, int rank, float threshold)
        {
            if (c == null) return "nothing there";
            if (c.m_faction == Character.Faction.Undead) return "the undead belong to the Necromancer";
            if (OwnSummon(c, player.GetPlayerID())) return null;
            if (Vector3.Distance(player.transform.position, c.transform.position) > WildshapeRange + 1f) return "too far for Wildshape";
            if (!BiomeAllowed(c.transform.position, rank)) return "too wild a creature for your Wildshape rank";
            if (c.GetComponent<Tameable>() == null && c.GetHealthPercentage() > threshold + 0.001f)
                return $"beat it below {threshold * 100f:0}% health first";
            return null;
        }

        // Rank 1 Meadows / Black Forest, rank 2 adds Swamp / Mountain, rank 3 adds Plains / Mistlands; by the ground the
        // creature stands on, so modded creatures fit by their biome.
        private static bool BiomeAllowed(Vector3 at, int rank)
        {
            if (WorldGenerator.instance == null) return true;
            Heightmap.Biome biome = WorldGenerator.instance.GetBiome(at);
            switch (biome)
            {
                case Heightmap.Biome.Meadows:
                case Heightmap.Biome.BlackForest:
                    return true;
                case Heightmap.Biome.Swamp:
                case Heightmap.Biome.Mountain:
                    return rank >= 2;
                case Heightmap.Biome.Plains:
                case Heightmap.Biome.Mistlands:
                    return rank >= 3;
                default:
                    return false;
            }
        }

        private static string ValidateWildshape(long playerId, ZDO zdo, Character c)
        {
            if (zdo.GetLong(SummonOwnerKey, 0L) == playerId) return null;
            if (c == null) return null;   // not loaded here: the client's checks stand
            if (c.m_faction == Character.Faction.Undead) return "the undead belong to the Necromancer";
            if (c.GetComponent<Tameable>() == null && c.GetHealthPercentage() > ServerWildshapeHealth + ServerHealthSlack)
                return "the creature isn't beaten down enough";
            return null;
        }

        // ── Necromancer: Bind the Dead ────────────────────────────────────────────────────────────────────────────────

        // "Possess an undead humanoid under your crosshair within 15 m that is below 50% health (or your own thrall) for up to
        // {time} s. Your body rises and channels, untouchable; the link breaks past 60 m, when your eitr runs out, or when the
        // thrall dies." Elites and the charred need rank 3. Deep Link adds {time} and {range}.
        private static bool BindDead(ClassTreeNode node, Character caster, CastTarget target)
        {
            if (!(caster is Player player)) return false;
            ChannelAskedTotal = 0f;
            int rank = DesignKit.RankOf(node);
            Character thrall = Pick(player, target, BindRange);
            string why = thrall == null ? "no undead under your crosshair" : BindRefusal(player, thrall, rank);
            if (why != null) return Refuse(player, node, why);

            float eitr = EitrCost(node);
            if (!player.HaveEitr(eitr)) return Refuse(player, node, "not enough eitr");

            int deep = LearnedRank(ArchetypeClass.Necromancer, DeepLinkId);
            ClassTreeNode deepNode = deep > 0 ? ClassTrees.Node(DeepLinkId) : null;
            float timeBonus = deepNode != null ? deepNode.Value("time", deep) : 0f;
            float rangeBonus = deepNode != null ? deepNode.Value("range", deep) : 0f;

            var options = new PossessOptions
            {
                Body = PossessBody.Frozen,
                PlayerInvulnerable = true,
                ChannelLock = true,
                ReturnToAnchor = false,
                MaxDuration = node.Value("time", rank, 60f) * (1f + timeBonus),
                MaxRange = BindLinkMetres + rangeBonus,
                Filter = c => BindRefusal(player, c, rank),
                ContinueWhile = () => DrainChannel(player),
                ChannelHover = HoverMetres,
                HoverRise = HoverRiseSeconds,
                HoverBob = HoverBobMetres,
                HoverPeriod = HoverPeriodSeconds,
                HoverDescent = HoverDescentSeconds,
                ChannelFxStart = body =>
                {
                    AbilityRPCManager.SpawnFX(body.transform.position, BindStartFx);
                    AbilityRPCManager.SpawnFX(body.transform.position, BindRiseFx);
                },
                ChannelPose = ChannelPose,
                SourceTag = NecroTag,
            };
            return Begin(player, node, thrall, options, eitr);
        }

        // ── Necromancer: Ghostwalk (0.1.80, Fire 10-08: talents group 4) ─────────────────────────────────────────────────────
        // "Possess a ghost under your crosshair for {time} s to scout through walls; a ghost cannot attack." Your body stays frozen
        // and untouchable where it stood and you come back to it; the ghost's hits do nothing (DamageScale 0).
        internal const string GhostTag = "rpg-necro-ghost";
        private const string GhostwalkId = "necromancer_ghostwalk";
        private const float GhostRange = 30f;

        private static bool IsGhost(Character c) =>
            c != null && global::Utils.GetPrefabName(c.gameObject).StartsWith("Ghost", System.StringComparison.OrdinalIgnoreCase);

        private static bool Ghostwalk(ClassTreeNode node, Character caster, CastTarget target)
        {
            if (!(caster is Player player)) return false;
            int rank = DesignKit.RankOf(node);
            Character ghost = Pick(player, target, GhostRange);
            if (!IsGhost(ghost)) return Refuse(player, node, "no ghost under your crosshair");
            float eitr = EitrCost(node);
            if (!player.HaveEitr(eitr)) return Refuse(player, node, "not enough eitr");
            var options = new PossessOptions
            {
                Body = PossessBody.Frozen,
                PlayerInvulnerable = true,
                ReturnToAnchor = true,
                MaxDuration = node.Value("time", rank, 20f),
                MaxRange = GhostRange * 4f,
                DamageScale = 0f,
                Filter = c => IsGhost(c) ? null : "only a ghost",
                SourceTag = GhostTag,
            };
            return Begin(player, node, ghost, options, eitr);
        }

        private static string ValidateGhost(long playerId, ZDO zdo, Character c) =>
            c == null || IsGhost(c) ? null : "only a ghost can be walked";

        // ── Druid: the form library (0.1.81, Fire 10-08: talents group 5) ─────────────────────────────────────────────────
        // Every Wildshape remembers its creature on the player (custom data "rpg_druid_forms", newest first, up to 3).
        // - Form Library: "Remembers your last {forms} form(s); shift into one without a fresh target." Each cast takes the next
        //   remembered form: a fresh one of that creature appears beside you and you Wildshape into it (the Wildshape's own rules).
        // - Primal Avatar: "a giant form of your strongest library creature for {time} s, with {hp} more health" (x1.5 size).
        //   Its "attacks hit everything in front of it" is not built yet (base only).
        private const string FormLibraryId = "druid_form_library";
        private const string PrimalAvatarId = "druid_primal_avatar";
        private const string FormsKey = "rpg_druid_forms";
        private const int FormsKept = 3;
        private const float AvatarScale = 1.5f;
        private static int _nextForm;

        internal static List<string> Forms(Player player)
        {
            var list = new List<string>();
            if (player != null && player.m_customData.TryGetValue(FormsKey, out string saved) && !string.IsNullOrEmpty(saved))
                list.AddRange(saved.Split(',').Where(s => s.Length > 0));
            return list;
        }

        private static void RememberForm(Player player, string prefab)
        {
            if (player == null || string.IsNullOrEmpty(prefab)) return;
            List<string> forms = Forms(player);
            forms.Remove(prefab);
            forms.Insert(0, prefab);
            player.m_customData[FormsKey] = string.Join(",", forms.Take(FormsKept));
        }

        private static bool FormLibrary(ClassTreeNode node, Character caster, CastTarget target)
        {
            if (!(caster is Player player)) return false;
            int known = Mathf.Clamp((int)node.Value("forms", DesignKit.RankOf(node), 1f), 1, FormsKept);
            List<string> forms = Forms(player).Take(known).ToList();
            if (forms.Count == 0) return Refuse(player, node, "no form remembered yet: Wildshape first");
            string prefab = forms[_nextForm++ % forms.Count];
            return ShiftInto(player, node, prefab, 1f, 0f, node.Value("time", DesignKit.RankOf(node), 0f));
        }

        private static bool PrimalAvatar(ClassTreeNode node, Character caster, CastTarget target)
        {
            if (!(caster is Player player)) return false;
            string strongest = null;
            float best = 0f;
            foreach (string prefab in Forms(player))
            {
                Character c = ZNetScene.instance?.GetPrefab(prefab)?.GetComponent<Character>();
                if (c != null && c.m_health > best) { best = c.m_health; strongest = prefab; }
            }
            if (strongest == null) return Refuse(player, node, "no form remembered yet: Wildshape first");
            int rank = DesignKit.RankOf(node);
            return ShiftInto(player, node, strongest, AvatarScale, node.Value("hp", rank, 0.25f), node.Value("time", rank, 12f));
        }

        // A fresh creature of the remembered kind beside the player, beaten down for the server's Wildshape check, then the
        // Wildshape possession (its own leash and damage; the time is the skill's, or the Wildshape's when the skill has none).
        private static bool ShiftInto(Player player, ClassTreeNode node, string prefab, float scale, float hpBonus, float seconds)
        {
            GameObject source = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefab) : null;
            if (source == null) return Refuse(player, node, $"the {prefab} form is not in this world");
            float eitr = EitrCost(node);
            if (!player.HaveEitr(eitr)) return Refuse(player, node, "not enough eitr");
            Vector3 at = player.transform.position + player.transform.forward * 2f;
            GameObject go = Object.Instantiate(source, at, player.transform.rotation);
            Character form = go.GetComponent<Character>();
            if (form == null) { ZNetScene.instance.Destroy(go); return Refuse(player, node, "that form has no body"); }
            ZNetView nview = go.GetComponent<ZNetView>();
            if (scale != 1f && nview != null && nview.IsValid()) nview.SetLocalScale(go.transform.localScale * scale);
            // 0.1.171 ([wishbone]; R-NIGHT7: the Form Library's fresh Boar, wild and at 20% health, was killed by the tester's companion in
            // the 1 s before the possession landed, so the form never happened). The body is now stamped as the caster's own (the
            // server's ValidateWildshape passes an owned creature whatever its health, so it is no longer beaten down) and sits in the
            // players' faction for that wait, so companions and other player-side AI do not see an enemy. The possessed body's health
            // comes from FormHealth as before.
            if (nview != null && nview.IsValid()) nview.GetZDO().Set(SummonOwnerKey, player.GetPlayerID());
            form.m_faction = Character.Faction.Players;
            ClassTreeNode wildshape = ClassTrees.Node(WildshapeId);
            int wsRank = Mathf.Max(1, LearnedRank(ArchetypeClass.Druid, WildshapeId));
            float time = seconds > 0f ? seconds : (wildshape != null ? wildshape.Value("time", wsRank, 30f) : 30f);
            var options = new PossessOptions
            {
                Body = PossessBody.Hidden,
                ConsumeTarget = true,
                ReturnToAnchor = false,
                MaxDuration = time,
                MaxRange = WildshapeLeashMetres,
                FormHealth = form.GetMaxHealth() * (1f + hpBonus),
                DamageScale = 1f + DamagePerLevel * (ClassLevel() - 1),
                SourceTag = DruidTag,
            };
            // 0.1.83 (round 22: "refused (Revoked)"): the server checks the form's ZDO, so the possession is asked a second after the
            // spawn, once the new creature has reached it. The cast counts now; a refusal still refunds through OnEnded.
            player.StartCoroutine(BeginShiftLater(player, node, go, form, options, eitr));
            return true;
        }

        private static System.Collections.IEnumerator BeginShiftLater(Player player, ClassTreeNode node, GameObject go, Character form, PossessOptions options, float eitr)
        {
            yield return new WaitForSeconds(1f);
            if (player == null || player.IsDead() || form == null || form.IsDead()) { if (go != null) ZNetScene.instance?.Destroy(go); yield break; }
            if (!Begin(player, node, form, options, eitr) && go != null) ZNetScene.instance?.Destroy(go);
        }

        private static string BindRefusal(Player player, Character c, int rank)
        {
            if (c == null) return "nothing there";
            if (!(c is Humanoid)) return "only undead humanoids can be bound";
            // Your own raised dead first: the Dead Raiser's skeleton is in the players' faction.
            if (OwnSummon(c, player.GetPlayerID())) return null;
            string prefab = global::Utils.GetPrefabName(c.gameObject);
            bool charred = IsCharred(prefab);
            if (c.m_faction != Character.Faction.Undead && !charred) return "only undead humanoids can be bound";
            if (Vector3.Distance(player.transform.position, c.transform.position) > BindRange + 1f) return "too far for Bind the Dead";
            bool elite = charred || prefab.IndexOf("Elite", System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (elite && rank < EliteRank) return "elites and the charred need rank 3";
            if (c.GetHealthPercentage() >= BindUndeadHealth) return $"beat it below {BindUndeadHealth * 100f:0}% health first";
            return null;
        }

        private static string ValidateBind(long playerId, ZDO zdo, Character c)
        {
            if (zdo.GetLong(SummonOwnerKey, 0L) == playerId) return null;
            if (c == null) return null;   // not loaded here: the client's checks stand
            if (c.m_faction != Character.Faction.Undead && !IsCharred(global::Utils.GetPrefabName(c.gameObject))) return "only undead can be bound";
            if (c.GetHealthPercentage() > BindUndeadHealth + ServerHealthSlack) return "the undead isn't beaten down enough";
            return null;
        }

        // The Ashlands charred are the Demon faction in vanilla, but the design counts them among the undead (rank 3).
        private static bool IsCharred(string prefab) => prefab.StartsWith("Charred", System.StringComparison.OrdinalIgnoreCase);

        private static float _drainAsked, _drainGone, _drainLogAt;
        private static bool _drainWhyLogged;

        /// <summary>For FiresClassTest: the eitr the current / last Bind Dead channel has asked for (before the world's eitr rate).</summary>
        public static float ChannelAskedTotal { get; private set; }

        // 2 eitr a second while the channel lasts; the channel ends ("released") when it can't be paid.
        private static bool DrainChannel(Player player)
        {
            if (player == null || player.IsDead()) return false;
            float need = BindDrainPerSecond * Time.deltaTime;
            if (player.GetEitr() < need) return false;
            float before = player.GetEitr();
            player.UseEitr(need);
            // 0.1.106 (R-1012N: "asked 2.01 eitr this second, 0.00 left the pool", world rate 1): UseEitr didn't move the pool. The channel
            // isn't a spell (no spell-cost passives), so it pays the way vanilla's UseEitr does on the owner (RPC_UseEitr), and says once why
            // UseEitr didn't (view / owner / Lich Form).
            if (player.GetEitr() >= before - need * 0.5f)
            {
                if (!_drainWhyLogged)
                {
                    _drainWhyLogged = true;
                    ZNetView nv = player.GetComponent<ZNetView>();
                    FiresRPGClassesPlugin.Log?.LogDebug($"[PossessionKit] {BindDeadId}: UseEitr({need:0.000}) left the pool at {before:0.00} (view valid {(nv != null && nv.IsValid())}, owner {(nv != null && nv.IsValid() && nv.IsOwner())}, Lich Form {DruidNecroKit.LichActive}, world eitr rate {Game.m_eitrRate:0.##}); paying through RPC_UseEitr");
                }
                if (player.m_nview != null && player.m_nview.IsValid() && player.m_nview.IsOwner()) player.RPC_UseEitr(0L, need * Game.m_eitrRate);
            }
            // 0.1.105 (R-1012M: "eitr didn't drain while channelling (-0.3)"): evidence once a second - what was asked, what left the pool,
            // and the world's eitr rate (vanilla multiplies every eitr cost by Game.m_eitrRate).
            _drainAsked += need; _drainGone += Mathf.Max(0f, before - player.GetEitr());
            ChannelAskedTotal += need;
            if (Time.time >= _drainLogAt)
            {
                FiresRPGClassesPlugin.Log?.LogDebug($"[PossessionKit] {BindDeadId} channel: asked {_drainAsked:0.00} eitr this second, {_drainGone:0.00} left the pool, eitr now {player.GetEitr():0.0}/{player.GetMaxEitr():0}, world eitr rate {Game.m_eitrRate:0.##}");
                _drainLogAt = Time.time + 1f; _drainAsked = 0f; _drainGone = 0f;
            }
            return true;
        }

        // Arms half-raised, head back (section 8b). Set as directions after the animator, so it doesn't depend on the bones'
        // local axes: each upper arm turns 60% of the way from where the animation left it towards out-and-up.
        private static void ChannelPose(Player body)
        {
            Animator animator = body != null ? body.GetComponentInChildren<Animator>() : null;
            if (animator == null || !animator.isHuman) return;
            Transform root = body.transform;
            RaiseArm(animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, -root.right, root);
            RaiseArm(animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, root.right, root);
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head != null)
                head.rotation = Quaternion.FromToRotation(root.forward, Vector3.Slerp(root.forward, root.up, 0.25f)) * head.rotation;
        }

        private static void RaiseArm(Animator animator, HumanBodyBones upper, HumanBodyBones lower, Vector3 outward, Transform root)
        {
            Transform a = animator.GetBoneTransform(upper);
            Transform b = animator.GetBoneTransform(lower);
            if (a == null || b == null) return;
            Vector3 now = b.position - a.position;
            if (now.sqrMagnitude < 1e-6f) return;
            Vector3 want = (outward * 0.75f + root.up * 0.2f + root.forward * 0.45f).normalized;
            a.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(now.normalized, want), 0.6f) * a.rotation;
        }

        // ── shared ────────────────────────────────────────────────────────────────────────────────────────────────────

        private static bool Begin(Player player, ClassTreeNode node, Character target, PossessOptions options, float eitr)
        {
            if (!PossessionApi.CanPossess(target, options, out string why)) return Refuse(player, node, why);
            player.UseEitr(eitr);
            _pendingNodeId = node.Id;
            _pendingEitr = eitr;
            _pendingOpen = true;
            PossessionApi.Begin(target, options);
            // A refusal inside Begin (the engine not linked) raises Ended before Begin returns; OnEnded has refunded it.
            if (!_pendingOpen) return false;
            DebugLog.Debug($"[PossessionKit] {node.Id}: asked for {target.m_name} ({options.SourceTag}, {options.MaxDuration:0} s)");
            return true;
        }

        private static bool Ours(PossessHandle handle) =>
            handle != null && (handle.SourceTag == DruidTag || handle.SourceTag == NecroTag || handle.SourceTag == GhostTag);

        private static void OnStarted(PossessHandle handle)
        {
            if (!Ours(handle)) return;
            _live = handle;
            _pendingOpen = false;
            string form = handle.Creature != null ? handle.Creature.m_name : "?";
            if (handle.SourceTag == DruidTag && handle.Creature != null) RememberForm(handle.Player, global::Utils.GetPrefabName(handle.Creature.gameObject));
            FiresRPGClassesPlugin.Log.LogInfo($"[PossessionKit] {_pendingNodeId}: in {form} ({handle.SourceTag}, {handle.Options.MaxDuration:0} s, form hp {handle.FormHealthMax:0})");
        }

        private static void OnEnded(PossessHandle handle, PossessEndReason reason)
        {
            if (!Ours(handle)) return;
            if (handle != _live)
            {
                if (!_pendingOpen) return;
                _pendingOpen = false;
                // Refused (by the server, the engine, or a grant that timed out): the eitr and the cooldown come back.
                Player p = handle.Player != null ? handle.Player : Player.m_localPlayer;
                if (p != null && _pendingEitr > 0f) p.AddEitr(_pendingEitr);
                CooldownTracker.Reset(_pendingNodeId);
                FiresRPGClassesPlugin.Log.LogInfo($"[PossessionKit] {_pendingNodeId}: refused ({reason}); eitr and cooldown refunded");
                return;
            }
            _live = null;
            if (handle.SourceTag == NecroTag)
            {
                bool snapped = reason == PossessEndReason.Range || reason == PossessEndReason.Death || reason == PossessEndReason.Gone;
                Character at = handle.Creature;
                if (at != null) AbilityRPCManager.SpawnFX(at.transform.position, snapped ? LinkSnapFx : BindReturnFx);
            }
            string form = handle.Creature != null ? handle.Creature.m_name : "?";
            FiresRPGClassesPlugin.Log.LogInfo($"[PossessionKit] {(handle.SourceTag == DruidTag ? WildshapeId : BindDeadId)}: left {form} ({reason}) after {handle.Elapsed:0.0} s");
        }

        private static Character Pick(Player player, CastTarget target, float range)
        {
            Character c = target.TargetCharacter;
            if (c != null && c != player && !c.IsDead()) return c;
            if (GameCamera.instance == null) return null;
            Transform cam = GameCamera.instance.transform;
            int mask = LayerMask.GetMask("character", "character_net", "character_ghost", "character_noenv", "Default", "static_solid", "terrain", "piece");
            RaycastHit[] hits = Physics.RaycastAll(cam.position, cam.forward, range + 12f, mask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
            foreach (RaycastHit hit in hits)
            {
                Character found = hit.collider != null ? hit.collider.GetComponentInParent<Character>() : null;
                if (found == null)
                {
                    if (hit.collider != null && hit.collider.attachedRigidbody == null) return null;   // a wall in the way
                    continue;
                }
                if (found == player || found.IsDead()) continue;
                return Vector3.Distance(player.transform.position, found.transform.position) <= range + 1f ? found : null;
            }
            return null;
        }

        private static bool OwnSummon(Character c, long playerId)
        {
            ZNetView nv = c.GetComponent<ZNetView>();
            ZDO zdo = nv != null && nv.IsValid() ? nv.GetZDO() : null;
            return zdo != null && zdo.GetLong(SummonOwnerKey, 0L) == playerId;
        }

        private static bool Refuse(Player player, ClassTreeNode node, string why)
        {
            player?.Message(MessageHud.MessageType.Center, node.DisplayName + ": " + why);
            DebugLog.Debug($"[PossessionKit] {node.Id} refused: {why}");
            return false;
        }

        internal static int LearnedRank(ArchetypeClass archetype, string nodeId)
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            return data == null ? 0 : data.Progress.TalentRank(archetype, nodeId);
        }

        internal static int ClassLevel()
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            return data == null ? 1 : Mathf.Max(1, data.Progress.Level);
        }

        /// <summary>The leading number of the yaml's cost text ("40 eitr", "50 eitr, then 2 eitr a second"); 0 when none.</summary>
        internal static float EitrCost(ClassTreeNode node)
        {
            string text = node?.CostText ?? "";
            var m = System.Text.RegularExpressions.Regex.Match(text, @"^\s*(\d+(?:\.\d+)?)\s*eitr", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return m.Success ? float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 0f;
        }
    }
}
