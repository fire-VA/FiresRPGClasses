using System.Collections.Generic;
using System.Linq;
using FiresCore.Npc.Archetypes;
using FiresRPGClasses.Data;
using FiresRPGClasses.State;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// The Werewolf class kit (Tools\WEREWOLF_CLASS_DESIGN.md §3-§5, LOCKED 10-13). The class needs the werewolf race, not a weapon
    /// (<see cref="WerewolfRace"/>, read by WeaponArchetypes so a bar without the race greys the class out and keeps its points).
    /// - Wolf Form (skill, 30 s / 120 s): the server grants it (<see cref="WerewolfServer"/>, Wildshape's validator pattern), then the
    ///   player ZDO bool rpg_wolf_active is set, VCS shows the Fenring (VcsApi.SetForm by reflection; refused = stats without the
    ///   look, logged), ClassWolfForm carries the stats, the hands are emptied and refilled after; unarmed hits become claws (slash
    ///   30 + 2 per class level, scaled like the vanilla unarmed hit, x1.3 attack speed); building, crafting and eating new food are
    ///   refused. Ends when the time runs out, on death, on a teleport / portal, or when the skill is pressed again.
    /// - Blood Rage (passive): below 25% health in a fight, a 15 s form, once per 300 s.
    /// - Full moon (server opt-in, off): forced form at night under a full moon, until dawn. No moon-phase source is published (see
    ///   <see cref="LunarClock"/>), so it never triggers yet.
    /// - Claws (passive): claw damage +10% a rank.
    /// - Howl (skill, 12 m / 45 s): Fenring's own howl sound + the Tank taunt's shockwave FX; the party gets ClassHowl (10 s: +15%
    ///   damage, +10% move speed, +30% stamina regen; with Pack Bond ClassHowlPack, +10% armour too); every enemy in range gets its
    ///   rule from [11 - Werewolf] "Howl creature rules" (Fear / Stagger / StaggerHalf / Debuff ClassHowlDread / None); with a taunt
    ///   class on the other bar (Tank, or Paladin with Shield Wall rank 3) it also taunts for 6 s through Core's TauntManager.
    /// - Lunar Hunger (passive): +10% damage at night, +20% under a full moon.
    /// - Alpha (ultimate, 20 s): the party within 15 m gets ClassAlpha (+20% damage) and each of your hits heals you 2% of max health.
    /// Everything acts for the local player only; other peers see the form through VCS's ZDO keys and rpg_wolf_active.
    /// </summary>
    public static class WerewolfKit
    {
        internal const string WolfFormId = "werewolf_wolf_form";
        internal const string ClawsId = "werewolf_claws";
        internal const string HowlId = "werewolf_howl";
        internal const string PackBondId = "werewolf_pack_bond";
        internal const string BloodRageId = "werewolf_blood_rage";
        internal const string LunarHungerId = "werewolf_lunar_hunger";
        internal const string AlphaId = "werewolf_alpha";
        private static readonly string[] PassiveIds = { ClawsId, PackBondId, BloodRageId, LunarHungerId };

        private const float WolfFormSeconds = 30f;          // the yaml's duration wins when set
        private const float ServerAnswerTimeout = 3f;
        private const float InCombatSeconds = 10f;          // "in combat" for Blood Rage: hit or hitting within this long
        private const float MoonRetrySeconds = 60f;
        private const float HowlRadius = 12f;
        private const float HowlAllySeconds = 10f;
        private const float HowlDreadSeconds = 8f;
        private const float StaggerHalfShare = 0.5f;        // StaggerHalf: half of a full stagger bar in one hit
        private const float AlphaRadius = 15f;
        private const float AlphaSeconds = 20f;
        private const float AlphaHealShare = 0.02f;
        private const int ShieldWallTauntRank = 3;
        private const string PaladinShieldWallId = "paladin_shield_wall";
        private const string FormFx = "vfx_odin_despawn";
        private static readonly int FmoTierKey = "fmo_tier".GetStableHashCode();   // FMO's synced tier (ClassKillNetwork reads it too)
        private const int EliteTier = 3;

        private static bool _registered;

        // Wolf Form
        private static bool _active;
        private static float _until;
        private static WerewolfServer.Reason _source;
        private static bool _lookShown;
        private static ItemDrop.ItemData _stashedRight, _stashedLeft;
        private static bool _pending;
        private static WerewolfServer.Reason _pendingReason;
        private static float _pendingSince, _pendingSeconds;
        private static float _rageReadyAt, _moonRetryAt, _lastCombatAt = float.NegativeInfinity;
        private static float _equipToldAt;

        // Alpha
        private static float _alphaUntil = -1f;

        // Passives, read once a second (learned, equipped in the passive grid, and the race).
        private static readonly Dictionary<string, float> Passives = new Dictionary<string, float>();
        private static readonly Dictionary<string, float> Shown = new Dictionary<string, float>();
        private static float _nextRead;

        public static void RegisterAll()
        {
            if (_registered) return;
            _registered = true;
            ClassSkillKit.Register(WolfFormId, WolfForm);
            ClassSkillKit.Register(HowlId, Howl);
            ClassSkillKit.Register(AlphaId, Alpha);
            foreach (string id in PassiveIds) ClassSkillKit.RegisterPassive(id);
            WerewolfServer.Answered += OnServerAnswer;
        }

        private static void Log(string s) => FiresRPGClassesPlugin.Log?.LogDebug("[Werewolf] " + s);

        private static bool Refuse(Player player, ClassTreeNode node, string why)
        {
            player?.Message(MessageHud.MessageType.Center, node.DisplayName + ": " + why);
            Log($"{node.Id} refused: {why}");
            return false;
        }

        // ── Wolf Form ─────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>True while the local player's Wolf Form runs.</summary>
        public static bool FormActive => _active;

        // "Transform for 30 s: ..." The cooldown starts with the request; a refusal from the server gives it back.
        private static bool WolfForm(ClassTreeNode node, Character caster, CastTarget target)
        {
            if (!(caster is Player player) || player != Player.m_localPlayer) return false;
            if (_active) { End("cancelled"); return true; }
            if (!WerewolfRace.Local) return Refuse(player, node, "you are not of the werewolf race");
            if (_pending) return Refuse(player, node, "waiting for the server");
            float seconds = node.EffectDuration > 0f ? node.EffectDuration : WolfFormSeconds;
            if (!Request(WerewolfServer.Reason.Skill, seconds)) return Refuse(player, node, "not connected");
            // A listen host answers at once: refused already = the cast didn't happen (the driver then starts only its short retry).
            return _pending || _active;
        }

        /// <summary>
        /// ClassCombatDriver.Press, before the slot's availability: pressing Wolf Form while transformed ends it (the slot is on its
        /// cooldown then, so the cast path would refuse). A full-moon form can't be cancelled. True = the press is used up.
        /// </summary>
        internal static bool CancelOnPress(string nodeId)
        {
            if (!_active || nodeId != WolfFormId) return false;
            if (_source == WerewolfServer.Reason.Moon)
            {
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "The full moon holds you in Wolf Form until dawn");
                return true;
            }
            End("cancelled");
            return true;
        }

        private static bool Request(WerewolfServer.Reason reason, float seconds)
        {
            // Pending BEFORE asking: on a listen host / single player the answer comes back inside Ask.
            _pending = true;
            _pendingReason = reason;
            _pendingSince = Time.time;
            _pendingSeconds = seconds;
            Log($"asking the server for Wolf Form ({reason}, {seconds:0} s)");
            if (WerewolfServer.Ask(reason, seconds)) return true;
            _pending = false;
            return false;
        }

        private static void OnServerAnswer(WerewolfServer.Reason reason, bool granted, float seconds, string why)
        {
            if (!_pending || reason != _pendingReason) { Log($"late server answer ignored ({reason}, granted {granted})"); return; }
            _pending = false;
            if (granted) { Begin(reason, seconds); return; }
            Refused(reason, why);
        }

        private static void Refused(WerewolfServer.Reason reason, string why)
        {
            if (reason == WerewolfServer.Reason.Skill) CooldownTracker.Reset(WolfFormId);
            if (reason != WerewolfServer.Reason.Moon) Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "Wolf Form: " + why);
            Log($"Wolf Form ({reason}) refused: {why}");
        }

        private static void Begin(WerewolfServer.Reason reason, float seconds)
        {
            Player player = Player.m_localPlayer;
            if (player == null || player.IsDead() || _active) return;
            _active = true;
            _source = reason;
            _until = Time.time + seconds;
            SetZdoFlag(player, true);
            Stash(player);
            string model = WerewolfConfig.Value(WerewolfConfig.FormModel, "Fenring");
            _lookShown = VcsBridge.SetForm(player, model, true);
            if (!_lookShown)
                Log($"VcsApi.SetForm({model}, true) returned false (VCS missing or older than 0.8.5, model swaps off, or '{model}' not on "
                    + "the allowlist): the stats apply without the look");
            AbilityRPCManager.ApplySelfBuff(player, WerewolfEffects.WolfForm, seconds);
            AbilityRPCManager.SpawnFX(player.transform.position, FormFx);
            Log($"Wolf Form on ({reason}) for {seconds:0} s: look {(_lookShown ? "shown" : "missing")}, IsInForm {VcsBridge.IsInForm(player)}, "
                + $"hands emptied (right {Name(_stashedRight)}, left {Name(_stashedLeft)})");
        }

        /// <summary>Ends the form now (the time ran out, death, a teleport, a cancel). Safe to call when it isn't running.</summary>
        internal static void End(string why)
        {
            if (!_active) return;
            _active = false;
            Player player = Player.m_localPlayer;
            if (player == null) { _stashedRight = _stashedLeft = null; Log($"Wolf Form off ({why}) without a player"); return; }
            SetZdoFlag(player, false);
            if (VcsBridge.Available) VcsBridge.SetForm(player, WerewolfConfig.Value(WerewolfConfig.FormModel, "Fenring"), false);
            AbilityRPCManager.RemoveSelfBuff(player, WerewolfEffects.WolfForm);
            string restored = Restore(player);
            Log($"Wolf Form off ({why}): IsInForm {VcsBridge.IsInForm(player)}, {restored}");
        }

        private static void SetZdoFlag(Player player, bool on)
        {
            ZDO zdo = player.m_nview != null && player.m_nview.IsValid() ? player.m_nview.GetZDO() : null;
            if (zdo != null && zdo.GetBool(WerewolfRace.WolfActiveHash, false) != on) zdo.Set(WerewolfRace.WolfActiveHash, on);
        }

        // The weapons go back in the hands that held them, if they are still in the inventory (a death leaves them in the tombstone).
        private static void Stash(Player player)
        {
            _stashedRight = player.RightItem;
            _stashedLeft = player.LeftItem;
            if (_stashedRight != null) player.UnequipItem(_stashedRight, false);
            if (_stashedLeft != null) player.UnequipItem(_stashedLeft, false);
        }

        private static string Restore(Player player)
        {
            int back = 0;
            foreach (ItemDrop.ItemData item in new[] { _stashedRight, _stashedLeft })
            {
                if (item == null || player.IsDead() || !player.GetInventory().ContainsItem(item) || player.IsItemEquiped(item)) continue;
                if (player.EquipItem(item, false)) back++;
            }
            string line = $"{back} item(s) re-equipped (right {Name(_stashedRight)}, left {Name(_stashedLeft)})";
            _stashedRight = _stashedLeft = null;
            return line;
        }

        private static string Name(ItemDrop.ItemData item) => item?.m_dropPrefab != null ? item.m_dropPrefab.name : item?.m_shared?.m_name ?? "none";

        private static bool IsHandItem(ItemDrop.ItemData item)
        {
            if (item?.m_shared == null) return false;
            switch (item.m_shared.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                case ItemDrop.ItemData.ItemType.Bow:
                case ItemDrop.ItemData.ItemType.Shield:
                case ItemDrop.ItemData.ItemType.Torch:
                case ItemDrop.ItemData.ItemType.Tool:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Humanoid.EquipItem prefix: no weapon, shield, torch or tool in Wolf Form (the claws are the weapon).</summary>
        internal static bool AllowEquip(Humanoid who, ItemDrop.ItemData item)
        {
            if (!_active || who == null || who != Player.m_localPlayer || !IsHandItem(item)) return true;
            if (Time.time > _equipToldAt + 2f)
            {
                _equipToldAt = Time.time;
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, "Wolf Form: your claws are your weapons");
            }
            return false;
        }

        /// <summary>The blocked actions (§3): building, crafting, eating new food. True = refuse, and tells the player.</summary>
        internal static bool Blocks(Player player, string what)
        {
            if (!_active || player == null || player != Player.m_localPlayer) return false;
            player.Message(MessageHud.MessageType.Center, "Wolf Form: you can't " + what);
            return true;
        }

        private static bool IsFood(ItemDrop.ItemData item) =>
            item?.m_shared != null && (item.m_shared.m_food > 0f || item.m_shared.m_foodStamina > 0f || item.m_shared.m_foodEitr > 0f);

        internal static bool BlocksEating(Player player, ItemDrop.ItemData item) => IsFood(item) && Blocks(player, "eat new food");

        // ── Howl ──────────────────────────────────────────────────────────────────────────────────────────────────────

        internal enum HowlAction { None, Fear, Stagger, StaggerHalf, Debuff }

        // "Howl: you and your party within 12 m deal 15% more damage, move 10% faster and regain stamina 30% faster for 10 s;
        // enemies are frightened, staggered or shaken by what they are."
        private const string HowlEmote = "roar";   // vanilla Emotes.Roar

        private static bool Howl(ClassTreeNode node, Character caster, CastTarget target)
        {
            if (!(caster is Player player) || player != Player.m_localPlayer) return false;
            float radius = node.Targeting.Radius > 0f ? node.Targeting.Radius : HowlRadius;
            Vector3 at = player.transform.position;
            PlayHowlSound(at);
            // 0.1.128 (Fire, R-1013BU: "just stands there not moving at all"): the Howl plays vanilla's one-shot Roar emote, in wolf form
            // and as a human alike (the swapped Fenring follows the player rig). Player.StartEmote syncs the emote to every peer.
            bool roared = player.StartEmote(HowlEmote, true);
            Log($"Howl: emote '{HowlEmote}' {(roared ? "started" : "refused (busy: attacking, sitting or in the air)")}, wolf form {FormActive}");
            float scale = Mathf.Clamp(radius / 8f, 0.5f, 2.5f);
            // 0.1.131 (Fire, R-1013BZ): the taunt shockwave + ground slam read as a rock break - dropped. The Howl is the Roar emote
            // + the Bjorn's own alert roar (PlayHowlSound); no vanilla ring fits the theme, so none.

            string allyBuff = PassiveOn(PackBondId) ? WerewolfEffects.HowlPack : WerewolfEffects.Howl;
            int allies = AbilityRPCManager.ApplyGroupBuff(player, allyBuff, radius, HowlAllySeconds);

            var counts = new Dictionary<HowlAction, int>();
            foreach (Character enemy in EnemiesAround(player, at, radius))
            {
                HowlAction action = HowlRules.Resolve(enemy);
                if (ApplyHowl(player, enemy, action)) counts[action] = (counts.TryGetValue(action, out int n) ? n : 0) + 1;
            }

            int taunted = 0;
            string taunter = OtherBarTaunt();
            if (taunter != null) taunted = TauntManager.ApplyTaunt(player, radius, WerewolfConfig.Value(WerewolfConfig.HowlTauntSeconds, 6f));

            Log($"{HowlId}: {allyBuff} on {allies} party member(s) within {radius:0} m; enemies "
                + (counts.Count == 0 ? "none" : string.Join(", ", counts.Select(c => c.Key + " " + c.Value).ToArray()))
                + (taunter != null ? $"; hybrid taunt ({taunter} on the other bar): {taunted} taunted" : ""));
            return true;
        }

        private static bool ApplyHowl(Player player, Character enemy, HowlAction action)
        {
            switch (action)
            {
                case HowlAction.Fear:
                    return ClassCrowdControl.Flee(enemy, WerewolfConfig.Value(WerewolfConfig.HowlFearSeconds, 4f), player) == CrowdControlResult.Applied;
                case HowlAction.Stagger:
                    if (enemy.m_staggerDamageFactor <= 0f) return false;   // a creature vanilla never staggers stays so
                    enemy.Stagger(Away(player, enemy));
                    return true;
                case HowlAction.StaggerHalf:
                    return StaggerHalf(player, enemy);
                case HowlAction.Debuff:
                    return ApplyDread(enemy);
                default:
                    return false;
            }
        }

        // 0.1.127 (R-1013BT howl_boss "enemies none"): Core's ApplySingleEffect treats every name outside its own debuff list as a
        // BUFF, so ClassHowlDread was refused on the boss (not a party member) and nothing was sent. Vanilla's SEMan.AddStatusEffect
        // by hash applies it on the owner (its own RPC_AddStatusEffect when this peer doesn't own the boss). The enemy check is
        // already EnemiesAround's (ClassTargeting.IsEnemyTarget). The 8 s is HowlDreadEffect's Duration.
        private static bool ApplyDread(Character enemy)
        {
            SEMan se = enemy != null ? enemy.GetSEMan() : null;
            ZNetView nview = enemy != null ? enemy.GetComponent<ZNetView>() : null;
            if (se == null || nview == null || !nview.IsValid()) return false;
            if (ObjectDB.instance == null || ObjectDB.instance.GetStatusEffect(WerewolfEffects.HowlDreadHash) == null)
            {
                Log($"{HowlId}: {WerewolfEffects.HowlDread} not in ObjectDB; {enemy.m_name} not shaken");
                return false;
            }
            bool owner = nview.IsOwner();
            se.AddStatusEffect(WerewolfEffects.HowlDreadHash, true);
            Log($"{HowlId}: {WerewolfEffects.HowlDread} on {enemy.m_name} ({HowlDreadSeconds:0} s) {(owner ? "here (owner)" : "via the owner's RPC_AddStatusEffect")}");
            return true;
        }

        // "A vanilla stagger (as a heavy hit), stagger damage x0.5": one hit worth half of the creature's stagger bar, through vanilla's
        // own stagger damage (so a second Howl or a heavy hit right after topples it). The 1 blunt is the carrier: vanilla ignores a
        // hit of 0.1 or less, stagger included.
        private static bool StaggerHalf(Player player, Character enemy)
        {
            if (enemy.m_staggerDamageFactor <= 0f) return false;
            float bar = enemy.GetMaxHealth() * enemy.m_staggerDamageFactor;
            var hit = new HitData { m_point = enemy.GetCenterPoint(), m_dir = Away(player, enemy) };
            hit.m_damage.m_blunt = 1f;
            hit.m_staggerMultiplier = Mathf.Max(1f, bar * StaggerHalfShare);
            hit.SetAttacker(player);
            enemy.Damage(hit);
            return true;
        }

        private static Vector3 Away(Character from, Character to)
        {
            Vector3 away = to.transform.position - from.transform.position;
            away.y = 0f;
            return away.sqrMagnitude > 0.01f ? away.normalized : from.transform.forward;
        }

        private static List<Character> EnemiesAround(Character caster, Vector3 center, float radius) =>
            Character.GetAllCharacters().Where(c => c != null && c != caster && !c.IsDead()
                && Vector3.Distance(c.transform.position, center) <= radius && ClassTargeting.IsEnemyTarget(caster, c)).ToList();

        /// <summary>
        /// The hybrid taunt (§4): the class on the OTHER bar has a taunt in the counters matrix ("taunt": Tank; Paladin through Shield
        /// Wall rank 3). Returns that class's name, or null.
        /// </summary>
        private static string OtherBarTaunt()
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null) return null;
            ArchetypeClass other = data.Inactive.Archetype;
            if (other == ArchetypeClass.Tank) return "Tank";
            if (other == ArchetypeClass.Paladin && data.Progress.TalentRank(ArchetypeClass.Paladin, PaladinShieldWallId) >= ShieldWallTauntRank)
                return "Paladin";
            return null;
        }

        /// <summary>For the tooltip / ClassTest: whether Howl taunts now, and through which class.</summary>
        public static string HowlTauntsVia => OtherBarTaunt();

        // Fenring's own alert / idle sound (whatever the prefab offers), then the wolf's. Local: every peer plays it from the cast
        // broadcast (OnRemoteCast). Logged once when neither prefab has one.
        private static readonly string[] HowlSources = { "Fenring", "Wolf" };
        private static bool _noHowlLogged;

        private static readonly string[] HowlSfx = { "sfx_bear_idle", "sfx_unbjorn_idle" };

        private static void PlayHowlSound(Vector3 at)
        {
            if (ZNetScene.instance == null) return;
            // 0.1.131 (Fire's pick): the Bjorn's ALERT roar - vanilla Bjorn.MonsterAI.m_alertedEffects plays sfx_bear_idle (Unbjorn:
            // sfx_unbjorn_idle). Played by its own prefab name, a local sound effect like EffectList.Create spawns.
            foreach (string sfx in HowlSfx)
            {
                GameObject prefab = ZNetScene.instance.GetPrefab(sfx);
                if (prefab == null) continue;
                UnityEngine.Object.Instantiate(prefab, at, Quaternion.identity);
                Log($"Howl sound: {sfx}");
                return;
            }
            foreach (string source in HowlSources)
            {
                BaseAI ai = ZNetScene.instance.GetPrefab(source)?.GetComponent<BaseAI>();
                if (ai == null) continue;
                foreach (EffectList list in new[] { ai.m_alertedEffects, ai.m_idleSound })
                {
                    if (list?.m_effectPrefabs == null || list.m_effectPrefabs.Length == 0) continue;
                    list.Create(at, Quaternion.identity);
                    return;
                }
            }
            if (_noHowlLogged) return;
            _noHowlLogged = true;
            Log("no howl sound: neither Fenring nor Wolf has alerted / idle effects");
        }

        /// <summary>ClassCastExecutor.PlayRemote: another player's Howl is heard here.</summary>
        internal static void OnRemoteCast(ClassTreeNode node, Character caster)
        {
            if (node != null && caster != null && node.Id == HowlId) PlayHowlSound(caster.transform.position);
        }

        /// <summary>
        /// [11 - Werewolf] "Howl creature rules", parsed once per value: "match:effect" pairs, first match wins. Match = a faction, a
        /// prefab name, or a tag (Boss, Elite, Tough, Tamed, Player, *). Unknown effects are dropped with a warning.
        /// </summary>
        internal static class HowlRules
        {
            private static string _parsedFrom;
            private static readonly List<KeyValuePair<string, HowlAction>> Rules = new List<KeyValuePair<string, HowlAction>>();

            internal static HowlAction Resolve(Character c)
            {
                Parse(WerewolfConfig.Value(WerewolfConfig.HowlRules, WerewolfConfig.DefaultHowlRules));
                foreach (KeyValuePair<string, HowlAction> rule in Rules)
                    if (Matches(rule.Key, c)) return rule.Value;
                return HowlAction.None;
            }

            private static void Parse(string text)
            {
                if (text == _parsedFrom) return;
                _parsedFrom = text;
                Rules.Clear();
                foreach (string part in (text ?? "").Split(','))
                {
                    string[] pair = part.Split(':');
                    if (pair.Length != 2) continue;
                    string match = pair[0].Trim();
                    if (match.Length == 0) continue;
                    if (!System.Enum.TryParse(pair[1].Trim(), true, out HowlAction action))
                    {
                        FiresRPGClassesPlugin.Log?.LogWarning($"[Werewolf] Howl creature rule '{part.Trim()}': unknown effect (Fear, Stagger, StaggerHalf, Debuff, None) - skipped");
                        continue;
                    }
                    Rules.Add(new KeyValuePair<string, HowlAction>(match, action));
                }
                Log($"Howl creature rules: {string.Join(", ", Rules.Select(r => r.Key + ":" + r.Value).ToArray())}");
            }

            private static bool Matches(string match, Character c)
            {
                switch (match.ToLowerInvariant())
                {
                    case "*": return true;
                    case "boss": return c.IsBoss();
                    case "elite": return Tier(c) >= EliteTier;
                    case "tough": return c.GetMaxHealth() >= WerewolfConfig.Value(WerewolfConfig.HowlFearHealth, 600f);
                    case "tamed": return c.IsTamed();
                    case "player": return c.IsPlayer();
                }
                return string.Equals(match, c.GetFaction().ToString(), System.StringComparison.OrdinalIgnoreCase)
                    || string.Equals(match, Utils.GetPrefabName(c.gameObject), System.StringComparison.OrdinalIgnoreCase);
            }

            // Fires Monster Overhaul's synced tier (0 Normal .. 3 Champion) on the creature's ZDO; 0 without FMO.
            private static int Tier(Character c)
            {
                ZNetView view = c.GetComponent<ZNetView>();
                return view != null && view.IsValid() ? view.GetZDO().GetInt(FmoTierKey, 0) : 0;
            }
        }

        // ── Alpha (ultimate) ──────────────────────────────────────────────────────────────────────────────────────────

        // "For 20 s your party within 15 m deals 20% more damage, and each of your hits heals you for 2% of your max health."
        private static bool Alpha(ClassTreeNode node, Character caster, CastTarget target)
        {
            if (!(caster is Player player) || player != Player.m_localPlayer) return false;
            float radius = node.Targeting.Radius > 0f ? node.Targeting.Radius : AlphaRadius;
            float seconds = node.EffectDuration > 0f ? node.EffectDuration : AlphaSeconds;
            int n = AbilityRPCManager.ApplyGroupBuff(player, WerewolfEffects.Alpha, radius, seconds);
            _alphaUntil = Time.time + seconds;
            PlayHowlSound(player.transform.position);
            AbilityRPCManager.SpawnFX(player.transform.position, "vfx_MeadBzerker");
            Log($"{AlphaId}: ClassAlpha on {n} party member(s) within {radius:0} m for {seconds:0} s; heal {AlphaHealShare:0%} a hit");
            return true;
        }

        // ── hooks ─────────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>Character.Damage prefix (PassiveHooks): the local player's hit before it is sent - claws, Lunar Hunger, Alpha's heal.</summary>
        internal static void OnLocalHitSending(Character victim, HitData hit)
        {
            Player player = Player.m_localPlayer;
            if (player == null || victim == null || hit == null || victim == player || hit.m_attacker != player.GetZDOID()) return;
            _lastCombatAt = Time.time;

            if (_active && hit.m_skill == Skills.SkillType.Unarmed) Claws(player, hit);

            if (PassiveOn(LunarHungerId) && EnvMan.IsNight())
            {
                bool full = LunarClock.IsFullMoon();
                float bonus = Passive(LunarHungerId, full ? "full" : "night");
                if (bonus > 0f) hit.m_damage.Modify(1f + bonus);
            }

            if (_alphaUntil >= Time.time && ClassTargeting.IsEnemyTarget(player, victim))
                AbilityHeals.Apply(player, player, player.GetMaxHealth() * AlphaHealShare);
        }

        // "Slash 30 + 2 per level, scaled by the Unarmed skill": the vanilla unarmed hit already carries the skill roll and every
        // damage modifier (Howl, Alpha, attack strength), so the claw damage keeps that ratio of the bare-hand base.
        private static void Claws(Player player, HitData hit)
        {
            float level = PossessionKit.ClassLevel();
            float claw = WerewolfConfig.Value(WerewolfConfig.ClawBase, 30f) + WerewolfConfig.Value(WerewolfConfig.ClawPerLevel, 2f) * level;
            claw *= 1f + Passive(ClawsId, "claw");
            ItemDrop.ItemData fists = player.m_unarmedWeapon != null ? player.m_unarmedWeapon.m_itemData : null;
            float baseTotal = fists != null ? fists.GetDamage().GetTotalDamage() : 0f;
            float scale = baseTotal > 0f ? hit.m_damage.GetTotalDamage() / baseTotal : Mathf.Lerp(0.4f, 1f, player.GetSkillFactor(Skills.SkillType.Unarmed));
            float was = hit.m_damage.GetTotalDamage();
            hit.m_damage = new HitData.DamageTypes { m_slash = claw * scale };
            Log($"claws: {was:0.0} unarmed -> {hit.m_damage.m_slash:0.0} slash (class level {level:0}, x{scale:0.00}, Claws +{Passive(ClawsId, "claw"):0%})");
        }

        /// <summary>RPC_Damage prefix (PassiveHooks): the local player was hit - Blood Rage's "in combat".</summary>
        internal static void OnLocalHitTaken(Character victim, HitData hit)
        {
            if (victim != null && victim == Player.m_localPlayer && hit != null && hit.GetAttacker() != null) _lastCombatAt = Time.time;
        }

        /// <summary>The attack-speed hook (AttributeEffects): claws strike x1.3 in Wolf Form.</summary>
        public static float AttackSpeedFactor(Character c)
        {
            if (!_active || !(c is Player player) || player != Player.m_localPlayer) return 1f;
            ItemDrop.ItemData weapon = player.GetCurrentWeapon();
            bool unarmed = weapon == null || weapon.m_shared.m_skillType == Skills.SkillType.Unarmed;
            return unarmed ? WerewolfConfig.Value(WerewolfConfig.ClawAttackSpeed, 1.3f) : 1f;
        }

        /// <summary>ClassCombatDriver, every frame: the form's end conditions, the server timeout, Blood Rage, the moon, passives.</summary>
        internal static void Tick()
        {
            Player player = Player.m_localPlayer;
            if (_active)
            {
                if (player == null || player.IsDead()) End("death");
                else if (Time.time >= _until) End("time");
                else if (_source == WerewolfServer.Reason.Moon && !EnvMan.IsNight()) End("dawn");
            }

            if (_pending && Time.time > _pendingSince + ServerAnswerTimeout)
            {
                _pending = false;
                if (!WerewolfConfig.Value(WerewolfConfig.NeedServerAnswer, true)) { Log("the server didn't answer: transforming without it (config)"); Begin(_pendingReason, _pendingSeconds); }
                else Refused(_pendingReason, "the server didn't answer");
            }

            if (Time.time < _nextRead) return;
            _nextRead = Time.time + 1f;
            ReadPassives();
            if (player == null || player.IsDead() || _active || _pending || !WerewolfRace.Local) return;

            // Blood Rage: below the health share in a fight.
            if (PassiveOn(BloodRageId) && Time.time >= _rageReadyAt && Time.time - _lastCombatAt <= InCombatSeconds
                && player.GetHealthPercentage() < WerewolfConfig.Value(WerewolfConfig.RageHealth, 0.25f))
            {
                _rageReadyAt = Time.time + WerewolfConfig.Value(WerewolfConfig.RageCooldown, 300f);
                Log($"Blood Rage at {player.GetHealthPercentage():0%} health");
                Request(WerewolfServer.Reason.Rage, WerewolfConfig.Value(WerewolfConfig.RageSeconds, 15f));
                return;
            }

            // The full moon (server opt-in): until dawn.
            if (WerewolfConfig.Value(WerewolfConfig.FullMoonForcesForm, false) && Time.time >= _moonRetryAt && EnvMan.IsNight() && LunarClock.IsFullMoon())
            {
                _moonRetryAt = Time.time + MoonRetrySeconds;
                Request(WerewolfServer.Reason.Moon, 1200f);
            }
        }

        private static void ReadPassives()
        {
            Passives.Clear();
            if (Player.m_localPlayer == null || !WerewolfRace.Local) return;
            Read(ClawsId, "claw");
            Read(PackBondId, null);
            Read(BloodRageId, null);
            Read(LunarHungerId, "night");
            if (Passives.ContainsKey(LunarHungerId)) Read(LunarHungerId + ":full", "full", LunarHungerId);
        }

        private static void Read(string key, string valueKey, string nodeId = null)
        {
            ClassTreeNode node = ClassTrees.Node(nodeId ?? key);
            if (node == null || PassiveLoadout.Status(node) != PassiveStatus.Active) return;
            float v = valueKey == null ? 1f : node.Value(valueKey, DesignKit.RankOf(node));
            Passives[key] = v;
            if (!Shown.TryGetValue(key, out float was) || Mathf.Abs(was - v) > 1e-4f)
            {
                Shown[key] = v;
                Log($"passive {key} on ({(valueKey == null ? "on" : valueKey + " " + v.ToString("0.###"))})");
            }
        }

        private static bool PassiveOn(string id) => Passives.ContainsKey(id);

        private static float Passive(string id, string valueKey)
        {
            if (id == LunarHungerId && valueKey == "full") id = LunarHungerId + ":full";
            return Passives.TryGetValue(id, out float v) ? v : 0f;
        }

        /// <summary>A new session (Ascend's ZNet.Start patch): nothing of the last world's form, request or Alpha carries over.</summary>
        internal static void ResetSession()
        {
            _active = _pending = false;
            _stashedRight = _stashedLeft = null;
            _alphaUntil = -1f;
            _rageReadyAt = _moonRetryAt = 0f;
            _lastCombatAt = float.NegativeInfinity;
            Passives.Clear();
        }

        // ── for FiresClassTest ────────────────────────────────────────────────────────────────────────────────────────

        public static float FormRemaining => _active ? Mathf.Max(0f, _until - Time.time) : 0f;
        public static bool FormLookShown => _active && _lookShown;
        public static bool AlphaActive => _alphaUntil >= Time.time;
        public static bool IsPassiveOn(string id, out float value) => Passives.TryGetValue(id, out value);
        public static IReadOnlyList<string> HandledPassives => PassiveIds;
    }

    /// <summary>
    /// The moon phase Lunar Hunger's +20% and the full-moon trigger need. Neither Vedr nor Galaxies publishes a moon phase (Vedr has
    /// VedrApi.DaysPerSeason, Galaxies computes its phase internally with no public read), so this answers false: the full moon never
    /// counts, Lunar Hunger gives its night bonus only, and the server opt-in has no effect until a source exists.
    /// </summary>
    internal static class LunarClock
    {
        // Galaxies' moon (timed from Vedr's seasons): static Galaxies.GalaxiesApi.IsFullMoon(), add-only contract with [seasons], read by
        // reflection once. Galaxies missing or older than the API: never a full moon.
        private static System.Func<bool> s_isFull;
        private static bool s_looked;

        internal static bool IsFullMoon()
        {
            if (!s_looked)
            {
                s_looked = true;
                var m = System.Type.GetType("Galaxies.GalaxiesApi, FiresValheimGalaxies")?.GetMethod("IsFullMoon", System.Type.EmptyTypes);
                if (m != null && m.IsStatic && m.ReturnType == typeof(bool)) s_isFull = (System.Func<bool>)System.Delegate.CreateDelegate(typeof(System.Func<bool>), m);
                FiresRPGClassesPlugin.Log?.LogDebug($"[Werewolf] moon:Galaxies.GalaxiesApi.IsFullMoon {(s_isFull != null ? "found" : "not found, full moon never")}");
            }
            try { return s_isFull != null && s_isFull(); } catch { return false; }
        }
    }

    // ── the blocked actions and end conditions (one patch class per game method) ───────────────────────────────────────────

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
    internal static class WerewolfEquipBlock
    {
        [HarmonyPrefix]
        private static bool BeforeEquip(Humanoid __instance, ItemDrop.ItemData item, ref bool __result)
        {
            if (WerewolfKit.AllowEquip(__instance, item)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
    internal static class WerewolfBuildBlock
    {
        [HarmonyPrefix]
        private static bool BeforePlace(Player __instance, ref bool __result)
        {
            if (!WerewolfKit.Blocks(__instance, "build")) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
    internal static class WerewolfCraftBlock
    {
        [HarmonyPrefix]
        private static bool BeforeCraft(Player player) => !WerewolfKit.Blocks(player, "craft");
    }

    [HarmonyPatch(typeof(Player), nameof(Player.CanEat))]
    internal static class WerewolfEatBlock
    {
        // Existing food keeps ticking; only new food is refused.
        [HarmonyPrefix]
        private static bool BeforeCanEat(Player __instance, ItemDrop.ItemData item, ref bool __result)
        {
            if (!WerewolfKit.BlocksEating(__instance, item)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.TeleportTo))]
    internal static class WerewolfTeleportEnds
    {
        [HarmonyPrefix]
        private static void BeforeTeleport(Player __instance)
        {
            if (__instance == Player.m_localPlayer && WerewolfKit.FormActive) WerewolfKit.End("teleport");
        }
    }
}
