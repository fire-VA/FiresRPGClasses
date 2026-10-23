using FiresCore.Input;
using FiresRPGClasses.Data;
using FiresRPGClasses.Input;
using FiresRPGClasses.State;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// The per-frame half of doc 14: reads the slot keys, swaps bars, aims and commits. Nothing here touches an
    /// engine API before Awake. Plan: Docs/PLAN_ClassesCombatAndUI.md
    /// </summary>
    public class ClassCombatDriver : MonoBehaviour
    {
        // Read through Core's FiresKeys (like every Fires key) so class_test ui can click them the way a player does.
        private const KeyCode CommitMouseKey = KeyCode.Mouse0;
        private const KeyCode CancelMouseKey = KeyCode.Mouse1;
        private const float RefusedRetrySeconds = 1f;

        public static ClassCombatDriver Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            ClassTreeLoader.Tick();
            PassiveKit.Tick();
            DesignKit.Tick();
            ActiveKit.Tick();   // Tracker's pins
            MoveKit.Tick();     // Breakwall / Thousand Steps
            LinkKit.Tick();     // Lifeline / Spirit Link
            PassiveKit2.Tick(); // batch 3a passives
            PassiveKit3.LateTick(); // Unseen keeps Stealth after its first hit
            PassiveKit3.Tick(); // batch 3b passives
            ClassProgressApi.Tick(); // class progress on the player's ZDO (FiresMonsters group scaling)
            ZoneKit.Tick();
            WerewolfKit.Tick(); // Wolf Form's end conditions, Blood Rage, the werewolf passives
            VampireKit.Tick();  // Bat Form's end conditions and skill, sunlight, the vampire passives
            // Before the UI checks below: the bar slots change while the inventory is open.
            BarController.PollBarSlots();
            // Class XP's gathering: the work the local player hit that is now gone.
            ClassXp.Tick();
            // Hugin's class hints, each the first time it matters.
            Hints.ClassRavenHints.Tick();

            if (!ClassKeys.IsBound) return;
            Player player = Player.m_localPlayer;
            if (player == null || player.IsDead()) { CancelAim(); return; }
            if (BlockedByUi()) { CancelAim(); return; }

            BarController.CaptureEquippedWeapon();

            if (TargetingController.IsAiming)
            {
                UpdateAim();
                return;
            }

            if (ClassKeys.BarSwap.IsPressed()) { Swap(); return; }

            // Slot 1's key pulls the bar's weapon out of its VAInventory slot, sheathed or while
            // holding something else; with the pair already in hand it swaps bars as before.
            if (BarController.HasBarSlots && ClassKeys.Slot(BarLayout.WeaponSlot).IsPressed())
            {
                if (BarController.DrawActiveBar()) return;
                if (ClassKeys.SlotOneSwapsBars.Value && BarController.CanSwap()) Swap();
                return;
            }

            if (ClassKeys.ItemHotbarNeedsOwnKeys) ReadItemHotbarKeys(player);
            if (!ClassKeys.SlotKeysLive()) return;

            // Matches SuppressVanillaHotbar: if we are not taking 1-8 from vanilla, we must not act on
            // them either, or the key both selects a hotbar item AND fires a class slot.
            if (!ClassKeys.BarHasSomethingToCast()) return;

            for (int slot = 1; slot <= BarLayout.SlotCount; slot++)
            {
                if (!ClassKeys.Slot(slot).IsPressed()) continue;
                if (slot == BarLayout.WeaponSlot)
                {
                    // Only swap when the swap can actually succeed. Refusing here used to print
                    // "the other bar has no weapon" at someone who just wanted hotbar slot 1.
                    if (ClassKeys.SlotOneSwapsBars.Value && BarController.CanSwap()) Swap();
                    return;
                }
                Press(slot);
                return;
            }
        }

        private static bool BlockedByUi()
        {
            // The Classes window hides the inventory while it is up, so the inventory check alone let slot keys cast from
            // inside it.
            if (UI.ClassesPanel.Instance != null && UI.ClassesPanel.Instance.IsOpen) return true;
            // Typing in ANY input field (other mods' search bars too) blocks every mod hotkey.
            if (FiresCore.Input.FiresInputBlock.IsTyping()) return true;
            if (Chat.instance != null && Chat.instance.HasFocus()) return true;
            if (Menu.IsVisible()) return true;
            if (InventoryGui.IsVisible()) return true;
            if (TextInput.IsVisible()) return true;
            return Console.IsVisible();
        }

        private static void Swap()
        {
            if (BarController.TrySwap(out string refusal)) return;
            if (!string.IsNullOrEmpty(refusal)) Tell(refusal);
        }

        private static void ReadItemHotbarKeys(Player player)
        {
            for (int slot = 1; slot <= BarLayout.SlotCount; slot++)
                if (ClassKeys.ItemHotbarKey(slot).IsPressed())
                {
                    PlayerUseHotbarItemHandoff.UseThroughHandoff(player, slot);
                    return;
                }
        }

        private void Press(int slot)
        {
            BarSide side = BarController.ActiveSide;
            // Pressing Wolf Form while transformed ends it; its slot is on cooldown then, so this comes before the availability check.
            if (slot != BarLayout.UltimateSlot && WerewolfKit.CancelOnPress(ClassPlayerStore.Local?.Bar(side)?.SkillAt(slot))) return;
            if (slot != BarLayout.UltimateSlot && VampireKit.CancelOnPress(ClassPlayerStore.Local?.Bar(side)?.SkillAt(slot))) return;
            SlotAvailability availability = BarController.Availability(side, slot);
            if (availability != SlotAvailability.Ready)
            {
                if (availability != SlotAvailability.Empty)
                {
                    Tell(Explain(availability));
                    BarLayout refusedBar = ClassPlayerStore.Local?.Bar(side);
                    LogRefused(refusedBar == null ? "slot" + slot : slot == BarLayout.UltimateSlot ? refusedBar.UltimateId : refusedBar.SkillAt(slot), Explain(availability));
                }
                return;
            }

            ClassPlayerData data = ClassPlayerStore.Local;
            BarLayout bar = data.Bar(side);
            if (slot == BarLayout.UltimateSlot) { CastUltimate(data, bar); return; }

            string nodeId = bar.SkillAt(slot);
            ClassTreeNode node = ClassTrees.Node(nodeId);
            if (node == null) return;

            // A scripted press would otherwise QUEUE and block every later press behind it, so a scripted press with a point on a
            // skill that lands on a body casts on the body nearest the point (an Ally skill falls back to the caster) - bots never queue.
            if (TargetingController.PicksTarget(node) && ClassKeys.TakePlacedPoint(slot, out Vector3 at))
            {
                Player caster = Player.m_localPlayer;
                Character on = node.Targeting.Mode == TargetingMode.Ally ? AllyAt(caster, at) : EnemyAt(caster, at);
                if (on == null && node.Targeting.Mode == TargetingMode.Ally) on = caster;
                if (on == null) { Tell("no target there"); LogRefused(nodeId, "no target there"); return; }
                Commit(node, CastTarget.On(on));
                return;
            }

            // A bot's press with a point (ClassKeys.PressSlotAt, Core's brain choosing where): cast there, no aiming step, within
            // the skill's range from the caster.
            if (NeedsAiming(node) && ClassKeys.TakePlacedPoint(slot, out Vector3 placed))
            {
                Player player = Player.m_localPlayer;
                float range = node.Targeting.Range > 0f ? node.Targeting.Range : TargetingController.AimRayLength;
                Vector3 offset = placed - player.transform.position;
                if (offset.magnitude > range) placed = player.transform.position + offset.normalized * range;
                // An Aimed skill that lands on a creature (Hunter's Mark, Death Mark) applies nothing with only a point -
                // the crosshair path hands it the creature. The brain's point is where its target stands: aim at that creature.
                if (node.Targeting.Mode == TargetingMode.Aimed && EnemyAt(player, placed) is Character aimed)
                {
                    Commit(node, CastTarget.On(aimed));
                    return;
                }
                Commit(node, CastTarget.At(placed));
                return;
            }
            // A skill placed away from the caster always QUEUES (a placement marker), whatever the cast style - the hotkey
            // never casts it; LMB places it, RMB / Esc / the hotkey again cancels (no cooldown until it is cast).
            if ((NeedsAiming(node) || TargetingController.PicksTarget(node)) && (node.Targeting.Mode == TargetingMode.Placed || TargetingController.PicksTarget(node) || TargetingController.Style.Value != CastStyle.Quick))
            {
                TargetingController.BeginAiming(nodeId);
                _aimSinceFrame = Time.frameCount;
                if (node.Targeting.Mode == TargetingMode.Placed || TargetingController.PicksTarget(node)) FiresRPGClassesPlugin.Log?.LogDebug($"[Placement] {nodeId}: queued ({node.Targeting.Mode}: LMB {(TargetingController.PicksTarget(node) ? "casts on the lit body" : "places")}, RMB/Esc/the hotkey cancels)");
                return;
            }
            Commit(node);
        }

        private const float AimedPointSnap = 2.5f;

        /// <summary>The enemy standing nearest <paramref name="point"/>, within a couple of metres; null when none.</summary>
        private static Character EnemyAt(Player player, Vector3 point)
        {
            Character best = null;
            float bestDistance = AimedPointSnap;
            foreach (Character character in Character.GetAllCharacters())
            {
                if (character == null || character.IsDead() || !FiresCore.Npc.Archetypes.ClassTargeting.IsEnemyTarget(player, character)) continue;
                float distance = Vector3.Distance(character.transform.position, point);
                if (distance > bestDistance) continue;
                best = character;
                bestDistance = distance;
            }
            return best;
        }

        /// <summary>The party member (or the caster) standing nearest <paramref name="point"/>, within a couple of metres; null when none.</summary>
        private static Character AllyAt(Player player, Vector3 point)
        {
            Character best = null;
            float bestDistance = AimedPointSnap;
            foreach (Character character in Character.GetAllCharacters())
            {
                if (character == null || character.IsDead()) continue;
                if (character != player && !TargetingController.IsAlly(player, character)) continue;
                float distance = Vector3.Distance(character.transform.position, point);
                if (distance > bestDistance) continue;
                best = character;
                bestDistance = distance;
            }
            return best;
        }

        private static bool NeedsAiming(ClassTreeNode node) =>
            node.Targeting.NeedsGroundPoint || node.Targeting.NeedsAim;

        private void UpdateAim()
        {
            ClassTreeNode node = ClassTrees.Node(TargetingController.AimingNodeId);
            if (node == null) { CancelAim(); return; }

            bool picks = TargetingController.PicksTarget(node);
            bool placed = node.Targeting.Mode == TargetingMode.Placed || picks;   // both queue: same cancel rules
            KeyBinding slotKey = ClassKeys.Slot(SlotHolding(node.Id));
            bool again = placed && Time.frameCount > _aimSinceFrame && slotKey != null && slotKey.IsPressed();
            if (FiresCore.Input.FiresKeys.GetKeyDown(CancelMouseKey) || (placed && FiresCore.Input.FiresKeys.GetKeyDown(KeyCode.Escape)) || again)
            {
                if (placed) FiresRPGClassesPlugin.Log?.LogDebug($"[Placement] {node.Id}: cancelled ({(again ? "the hotkey again" : "RMB/Esc")}); not cast, no cooldown");
                CancelAim();
                return;
            }
            if (picks) TargetingController.UpdateTargetLight(node);
            else TargetingController.UpdatePreview(node);

            bool release = placed ? FiresCore.Input.FiresKeys.GetKeyDown(CommitMouseKey)
                : TargetingController.Style.Value == CastStyle.QuickWithIndicator
                ? !slotKey.IsHeld()
                : FiresCore.Input.FiresKeys.GetKeyDown(CommitMouseKey);
            if (!release) return;

            CastTarget target;
            if (picks)
            {
                Character on = TargetingController.QueuedTarget;
                if (on == null) { Tell(TargetingController.QueuedFriendly ? "no ally under your aim" : "no enemy under your aim"); return; }   // stays queued
                target = CastTarget.On(on);
            }
            else target = TargetingController.Resolve(node);
            if (placed && !target.Resolved) { Tell(target.Refusal); return; }   // an invalid spot keeps the marker up
            CancelAim();
            if (placed) FiresRPGClassesPlugin.Log?.LogDebug($"[Placement] {node.Id}: {(picks ? "cast on " + (target.TargetCharacter != null ? target.TargetCharacter.m_name : "?") : "placed at " + target.Point.ToString("F1"))}");
            Commit(node, target);
        }

        private static int _aimSinceFrame;

        private static int SlotHolding(string nodeId)
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            BarLayout bar = data?.Active;
            if (bar == null) return BarLayout.WeaponSlot;
            for (int slot = BarLayout.FirstSkillSlot; slot <= BarLayout.LastSkillSlot; slot++)
                if (bar.SkillAt(slot) == nodeId) return slot;
            return bar.UltimateId == nodeId ? BarLayout.UltimateSlot : BarLayout.WeaponSlot;
        }

        private void CancelAim()
        {
            TargetingController.CancelAiming();
        }

        private static void Commit(ClassTreeNode node) => Commit(node, TargetingController.Resolve(node));

        private static void Commit(ClassTreeNode node, CastTarget target)
        {
            Player player = Player.m_localPlayer;
            if (player == null) return;
            if (!target.Resolved) { Tell(target.Refusal); LogRefused(node.Id, string.IsNullOrEmpty(target.Refusal) ? "no target" : target.Refusal); return; }

            ClassCastExecutor.PlayLocal(node, player, target);
            if (ClassCastExecutor.LastHandlerRefused) LogRefused(node.Id, "the skill's handler refused");
            else LogCast(node, player, target);
            // A handler that refused (Wildshape on a creature not beaten down yet) costs a short retry wait, not the cooldown.
            CooldownTracker.Start(node.Id, ClassCastExecutor.LastHandlerRefused ? Mathf.Min(node.Cooldown, RefusedRetrySeconds) : CooldownTracker.Scaled(node.Id, node.Cooldown)); // generic CDR hook
            // One line per cast: the skills drill pairs it with the hit or effect that proves it.
            FiresRPGClassesPlugin.Log.LogInfo($"cast {node.Id} on {(target.TargetCharacter != null ? target.TargetCharacter.m_name : "point " + target.Point.ToString("F1"))} ({node.Targeting.Mode})");
        }

        private static void CastUltimate(ClassPlayerData data, BarLayout bar)
        {
            Player player = Player.m_localPlayer;
            UltimateDefinition definition = UltimateCatalog.Get(bar.UltimateId);
            if (player == null || definition == null) return;

            int ultimateLevel = data.UltimateLevel(definition.Id);
            if (!UltimateController.MeetsPointGate(data, definition))
            {
                Tell(definition.DisplayName + " needs more points in its tree");
                LogRefused(definition.Id, "needs more points in its tree");
                return;
            }
            if (!UltimateController.CanPay(player, definition, ultimateLevel, out string refusal))
            {
                Tell(refusal);
                LogRefused(definition.Id, string.IsNullOrEmpty(refusal) ? "cannot pay" : refusal);
                return;
            }

            UltimateController.Pay(player, definition, ultimateLevel);
            CooldownTracker.Start(definition.Id, UltimateController.EffectiveCooldown(definition, ultimateLevel));

            ClassTreeNode node = ClassTrees.Node(definition.Id);
            if (node != null)
            {
                CastTarget target = TargetingController.Resolve(node);
                if (target.Resolved)
                {
                    ClassCastExecutor.PlayLocal(node, player, target);
                    LogCast(node, player, target);
                }
                else LogRefused(definition.Id, string.IsNullOrEmpty(target.Refusal) ? "no target" : target.Refusal);
            }
            if (UltimateController.AwardCastXp(data, definition.Id, 0) > 0)
                Tell(definition.DisplayName + " reached level " + data.UltimateLevel(definition.Id));
            ClassPlayerStore.SaveLocal();
        }

        /// <summary>Debug: one line per cast that went off (scripted tests count these against the presses).</summary>
        private static void LogCast(ClassTreeNode node, Player player, CastTarget target)
        {
            string on = target.TargetCharacter == null ? (node.Targeting.Mode == TargetingMode.Placed ? "point " + target.Point.ToString("F1") : "self")
                : target.TargetCharacter == player ? "self" : target.TargetCharacter.m_name;
            FiresRPGClassesPlugin.Log?.LogDebug($"[Cast] {node.Id} '{node.DisplayName}' by {player.GetPlayerName()} -> {on} (bar {BarController.ActiveSide} slot {SlotHolding(node.Id)})");
        }

        /// <summary>Debug: one line per refused press, with the reason.</summary>
        private static void LogRefused(string nodeId, string reason) =>
            FiresRPGClassesPlugin.Log?.LogDebug($"[Cast] {(string.IsNullOrEmpty(nodeId) ? "?" : nodeId)} refused: {reason}");

        private static string Explain(SlotAvailability availability)
        {
            switch (availability)
            {
                case SlotAvailability.OnCooldown: return "on cooldown";
                case SlotAvailability.NotLearned: return "not learned";
                case SlotAvailability.WrongArchetype: return "this weapon cannot use that skill";
                case SlotAvailability.BarUnusable: return "this bar has no weapon";
                case SlotAvailability.LevelLocked: return "not unlocked yet";
                case SlotAvailability.WrongWeapon: return "you are holding another weapon: switch bars or re-equip this bar's weapon";
                default: return "";
            }
        }

        private static void Tell(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center, message);
        }
    }
}
