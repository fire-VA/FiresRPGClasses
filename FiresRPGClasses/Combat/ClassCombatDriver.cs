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
        private const int CommitMouseButton = 0;
        private const int CancelMouseButton = 1;

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
            SlotAvailability availability = BarController.Availability(side, slot);
            if (availability != SlotAvailability.Ready)
            {
                if (availability != SlotAvailability.Empty) Tell(Explain(availability));
                return;
            }

            ClassPlayerData data = ClassPlayerStore.Local;
            BarLayout bar = data.Bar(side);
            if (slot == BarLayout.UltimateSlot) { CastUltimate(data, bar); return; }

            string nodeId = bar.SkillAt(slot);
            ClassTreeNode node = ClassTrees.Node(nodeId);
            if (node == null) return;

            if (NeedsAiming(node) && TargetingController.Style.Value != CastStyle.Quick)
            {
                TargetingController.BeginAiming(nodeId);
                return;
            }
            Commit(node);
        }

        private static bool NeedsAiming(ClassTreeNode node) =>
            node.Targeting.NeedsGroundPoint || node.Targeting.NeedsAim;

        private void UpdateAim()
        {
            ClassTreeNode node = ClassTrees.Node(TargetingController.AimingNodeId);
            if (node == null) { CancelAim(); return; }

            if (UnityEngine.Input.GetMouseButtonDown(CancelMouseButton)) { CancelAim(); return; }
            TargetingController.UpdatePreview(node);

            bool release = TargetingController.Style.Value == CastStyle.QuickWithIndicator
                ? !ClassKeys.Slot(SlotHolding(node.Id)).IsHeld()
                : UnityEngine.Input.GetMouseButtonDown(CommitMouseButton);
            if (!release) return;

            CancelAim();
            Commit(node);
        }

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

        private static void Commit(ClassTreeNode node)
        {
            Player player = Player.m_localPlayer;
            if (player == null) return;
            CastTarget target = TargetingController.Resolve(node);
            if (!target.Resolved) { Tell(target.Refusal); return; }

            ClassCastExecutor.PlayLocal(node, player, target);
            CooldownTracker.Start(node.Id, node.Cooldown);
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
                return;
            }
            if (!UltimateController.CanPay(player, definition, ultimateLevel, out string refusal))
            {
                Tell(refusal);
                return;
            }

            UltimateController.Pay(player, definition, ultimateLevel);
            CooldownTracker.Start(definition.Id, UltimateController.EffectiveCooldown(definition, ultimateLevel));

            ClassTreeNode node = ClassTrees.Node(definition.Id);
            if (node != null)
            {
                CastTarget target = TargetingController.Resolve(node);
                if (target.Resolved) ClassCastExecutor.PlayLocal(node, player, target);
            }
            if (UltimateController.AwardCastXp(data, definition.Id, 0) > 0)
                Tell(definition.DisplayName + " reached level " + data.UltimateLevel(definition.Id));
            ClassPlayerStore.SaveLocal();
        }

        private static string Explain(SlotAvailability availability)
        {
            switch (availability)
            {
                case SlotAvailability.OnCooldown: return "on cooldown";
                case SlotAvailability.NotLearned: return "not learned";
                case SlotAvailability.WrongArchetype: return "this weapon cannot use that skill";
                case SlotAvailability.BarUnusable: return "this bar has no weapon";
                case SlotAvailability.LevelLocked: return "not unlocked yet";
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
