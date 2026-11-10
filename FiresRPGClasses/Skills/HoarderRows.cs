using System;
using System.Collections.Generic;
using UnityEngine;

namespace FiresRPGClasses.ExtraSkills
{
    /// <summary>
    /// Hoarder's extra inventory rows. Valheim 1.0's InventoryGrid rebuilds its slots by itself when the inventory's
    /// height changes, so this only sets the height, drops what no longer fits (as the old mod did) and stretches the
    /// player panel's background the way the old mod did.
    /// </summary>
    public static class HoarderRows
    {
        private static Player _player;
        private static int _applied;

        /// <summary>Rows added right now (0 when the bonus is off).</summary>
        public static int Applied => _applied;

        internal static void Tick(Player player)
        {
            if (player != _player) { _player = player; _applied = 0; }
            int target = ExtraSkillSystem.HoarderExtraRows(ExtraSkillSystem.Level(player, ExtraSkillSystem.Hoarder));
            if (target == _applied) return;
            Apply(player, target);
        }

        private static void Apply(Player player, int extraRows)
        {
            Inventory inv = player.GetInventory();
            if (inv == null) return;
            int baseHeight = inv.GetHeight() - _applied;
            int newHeight = baseHeight + extraRows;

            if (extraRows < _applied)
            {
                var drop = new List<ItemDrop.ItemData>();
                foreach (ItemDrop.ItemData item in inv.GetAllItems())
                    if (item.m_gridPos.y >= newHeight) drop.Add(item);
                Vector3 at = player.transform.position + FiresRPGClasses.Combat.Ring.Up(player.transform.position) * 0.5f;
                foreach (ItemDrop.ItemData item in drop)
                {
                    inv.RemoveItem(item);
                    ItemDrop.DropItem(item, item.m_stack, at + new Vector3(UnityEngine.Random.Range(-0.8f, 0.8f), UnityEngine.Random.Range(0.3f, 0.8f), UnityEngine.Random.Range(-0.8f, 0.8f)), Quaternion.identity);
                }
                if (drop.Count > 0)
                    player.Message(MessageHud.MessageType.Center, $"Lost {_applied - extraRows} extra row(s) - {drop.Count} item(s) dropped!");
            }

            inv.m_height = newHeight;
            int was = _applied;
            _applied = extraRows;

            InventoryGui gui = InventoryGui.instance;
            if (gui != null && gui.m_player != null)
            {
                Transform bkgT = gui.m_player.Find("Bkg");
                RectTransform bkg = bkgT != null ? bkgT as RectTransform : null;
                if (bkg != null)
                {
                    float anchorY = -(extraRows / 4f - 0.01f * Math.Max(extraRows - 1, 0));
                    bkg.anchorMin = new Vector2(bkg.anchorMin.x, anchorY);
                    bkg.anchorMax = new Vector2(bkg.anchorMax.x, 1f);
                }
                if (gui.m_playerGrid != null) gui.m_playerGrid.UpdateInventory(inv, player, null);
            }
            inv.Changed();
            ExtraSkillSystem.LogOnce("rows", $"inventory rows: skill adds {extraRows} (Hoarder, was {was}); height {baseHeight} -> {newHeight}");
        }
    }
}
