using UnityEngine;
using Mirror;
using System.Collections.Generic;
using TOP.Core;
using TOP.Inventory;
using TOP.Systems;
using System;

namespace TOP.Player
{
    /// <summary>
    /// Dados de um item equipado (interno ao PlayerEquipment).
    /// ✅ RENOMEADO para evitar conflito com TOP.Inventory.EquippedItem
    /// </summary>
    [System.Serializable]
    public class EquipmentEntry
    {
        public EquipmentSlot Slot;
        public int ItemId;
        public int ItemDatabaseId;
        public int Durability;
        public TOP.Data.ItemBonus Extra;
    }

    public class PlayerEquipment : NetworkBehaviour
    {
        [SyncVar(hook = nameof(OnEquipmentDataChanged))] 
        private string _equipmentData = "";

        private readonly Dictionary<EquipmentSlot, EquipmentEntry> _equippedItems = new Dictionary<EquipmentSlot, EquipmentEntry>();
        private PlayerStats _stats;
        private PlayerInventory _inventory;

        public event Action<InventoryItem, EquipmentSlot> OnItemEquipped;
        public event Action<InventoryItem, EquipmentSlot> OnItemUnequipped;

        void Awake()
        {
            _stats = GetComponent<PlayerStats>();
            _inventory = GetComponent<PlayerInventory>();
        }

        [Server]
        public void EquipItem(InventoryItem item, EquipmentSlot slot)
        {
            EquipmentData itemData = ItemDatabase.Instance?.GetEquipment(item.ItemId);
            if (itemData == null) return;

            if (_equippedItems.ContainsKey(slot))
            {
                UnequipItem(slot);
            }

            _equippedItems[slot] = new EquipmentEntry
            {
                Slot = slot,
                ItemId = item.ItemId,
                ItemDatabaseId = item.ItemId,
                Durability = item.Durability,
                Extra = TOP.Data.PkoGems.InstanceBonus(item)
            };

            ApplyEquipmentStats(itemData, true);
            ApplyExtra(_equippedItems[slot].Extra, 1);
            OnItemEquipped?.Invoke(item, slot);
            SerializeEquipment();
        }

        [Server]
        public void UnequipItem(EquipmentSlot slot)
        {
            if (!_equippedItems.ContainsKey(slot)) return;

            EquipmentEntry equipped = _equippedItems[slot];
            _inventory?.ReleaseEquipped(equipped.ItemId);
            EquipmentData itemData = ItemDatabase.Instance?.GetEquipment(equipped.ItemDatabaseId);

            if (itemData != null)
                ApplyEquipmentStats(itemData, false);
            ApplyExtra(equipped.Extra, -1);

            InventoryItem unequippedItem = new InventoryItem 
            { 
                ItemId = equipped.ItemId, 
                Quantity = 1 
            };
            OnItemUnequipped?.Invoke(unequippedItem, slot);

            _equippedItems.Remove(slot);
            SerializeEquipment();
        }

        [Server]
        void ApplyEquipmentStats(EquipmentData data, bool add)
        {
            int multiplier = add ? 1 : -1;

            _stats.AddBonusStrength(data.bonusSTR * multiplier);
            _stats.AddBonusAgility(data.bonusAGI * multiplier);
            _stats.AddBonusConstitution(data.bonusINT * multiplier);
            _stats.AddBonusSpirit(data.bonusINT * multiplier);
            _stats.AddBonusHp(data.bonusHP * multiplier);
            _stats.AddBonusMp(data.bonusMP * multiplier);
            _stats.AddBonusSp(0 * multiplier);
            _stats.AddBonusAttack(data.bonusAttack * multiplier);
            _stats.AddBonusDefense(data.bonusDefense * multiplier);
        }

        // Refino e gemas da instancia (aproximacao, ver PkoGems). Acc ainda nao tem stat no jogo.
        [Server]
        void ApplyExtra(TOP.Data.ItemBonus b, int m)
        {
            _stats.AddBonusStrength(b.Str * m);
            _stats.AddBonusAgility(b.Agi * m);
            _stats.AddBonusConstitution(b.Con * m);
            _stats.AddBonusSpirit(b.Spr * m);
            _stats.AddBonusHp(b.Hp * m);
            _stats.AddBonusAttack(b.Atk * m);
            _stats.AddBonusDefense(b.Def * m);
        }

        [Server]
        void SerializeEquipment()
        {
            List<string> list = new List<string>();
            foreach (KeyValuePair<EquipmentSlot, EquipmentEntry> kvp in _equippedItems)
            {
                list.Add($"{kvp.Key}:{kvp.Value.ItemDatabaseId}");
            }
            _equipmentData = string.Join(";", list);
        }

        void OnEquipmentDataChanged(string oldValue, string newValue)
        {
            UpdateVisuals();
        }

        public event Action OnVisualsChanged;

        void UpdateVisuals() { OnVisualsChanged?.Invoke(); }

        // Item ids currently worn, parsed from the synchronized "Slot:itemId;..." string.
        public List<int> GetEquippedItemIds()
        {
            var ids = new List<int>();
            if (string.IsNullOrEmpty(_equipmentData)) return ids;
            foreach (var entry in _equipmentData.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int i = entry.LastIndexOf(':');
                if (i >= 0 && int.TryParse(entry.Substring(i + 1), out int id)) ids.Add(id);
            }
            return ids;
        }

        // Restores what the database says is worn (visual only; the base stats already include the item bonuses).
        [Server]
        public void LoadEquippedFromInventory(List<TOP.Data.InventoryItemData> items)
        {
            if (items == null) return;
            _equippedItems.Clear();
            foreach (var it in items)
            {
                if (!it.IsEquipped) continue;
                EquipmentSlot slot;
                var data = ItemDatabase.Instance?.GetEquipment(it.ItemId);
                if (data != null) slot = data.slot;
                else if (TOP.Data.PkoTables.Items.TryGetValue(it.ItemId, out var pi)) slot = TOP.Data.PkoTables.SlotOf(pi);
                else continue;
                if (slot == EquipmentSlot.Weapon && _equippedItems.ContainsKey(slot) && IsOffhandSword(it.ItemId)) slot = EquipmentSlot.Shield;
                else if (IsRing(slot) && _equippedItems.ContainsKey(slot)) slot = slot == EquipmentSlot.Ring1 ? EquipmentSlot.Ring2 : slot;
                _equippedItems[slot] = new EquipmentEntry { Slot = slot, ItemId = it.ItemId, ItemDatabaseId = it.ItemId, Durability = (int)it.Durability };
            }
            SerializeEquipment();
        }

        public int GetTotalAttackBonus()
        {
            int bonus = 0;
            foreach (EquipmentEntry item in _equippedItems.Values)
            {
                EquipmentData data = ItemDatabase.Instance?.GetEquipment(item.ItemDatabaseId);
                if (data != null)
                    bonus += data.bonusAttack;
            }
            return bonus;
        }

        public int GetTotalDefenseBonus()
        {
            int bonus = 0;
            foreach (EquipmentEntry item in _equippedItems.Values)
            {
                EquipmentData data = ItemDatabase.Instance?.GetEquipment(item.ItemDatabaseId);
                if (data != null)
                    bonus += data.bonusDefense;
            }
            return bonus;
        }

        public InventoryItem GetEquippedItem(EquipmentSlot slot)
        {
            if (_equippedItems.TryGetValue(slot, out EquipmentEntry equipped))
            {
                if (_inventory != null)
                    for (int i = 0; i < _inventory.totalSlots; i++)
                    {
                        var bag = _inventory.GetSlot(i);
                        if (bag != null && bag.IsEquipped && bag.ItemId == equipped.ItemId) return bag;
                    }
                return new InventoryItem 
                { 
                    ItemId = equipped.ItemId, 
                    Quantity = 1 
                };
            }
            return null;
        }

        static bool IsRing(EquipmentSlot s) { return s == EquipmentSlot.Ring1 || s == EquipmentSlot.Ring2; }

        // Weapon item types of the original client that occupy both hands (2H sword, bow).
        // Any one-hand sword can go to the left hand (dual wield) when a sword is already in the right hand.
        static bool IsOffhandSword(int itemId) => TOP.Data.PkoTables.Items.TryGetValue(itemId, out var p) && p.Type == 1 && p.EquipSlots.Length > 0;

        static bool TwoHanded(int type) { return type == 2 || type == 3; }

        bool CanWear(int itemId, EquipmentData data, EquipmentSlot target, out EquipmentSlot slot)
        {
            slot = data.slot;
            bool offhandSword = target == EquipmentSlot.Shield && data.slot == EquipmentSlot.Weapon && IsOffhandSword(itemId) && TypeOf(EquipmentSlot.Weapon) == 1;
            if (offhandSword) slot = EquipmentSlot.Shield;
            else if (IsRing(data.slot) && IsRing(target)) slot = target;
            else if (target != data.slot) return false;
            var pc = GetComponent<PlayerController>();
            if (TOP.Data.PkoTables.Items.TryGetValue(itemId, out var pi) && pc != null)
            {
                if (pc.Level < pi.Level) return false;
                if (!TOP.Data.PkoClasses.RaceOk(pi, pc.Job)) return false;
                var pcls = GetComponent<PlayerClass>();
                if (pcls != null && !TOP.Data.PkoClasses.Allows(pi.Classes, pcls.CurrentClass)) return false;
            }
            return true;
        }

        int TypeOf(EquipmentSlot slot)
        {
            return _equippedItems.TryGetValue(slot, out var e) && TOP.Data.PkoTables.Items.TryGetValue(e.ItemId, out var pi) ? pi.Type : 0;
        }

        [Command]
        public void CmdEquipItem(ushort inventorySlot, EquipmentSlot targetSlot)
        {
            if (_inventory == null) return;

            InventoryItem item = _inventory.GetSlot(inventorySlot);
            if (item == null || item.IsEquipped) return;
            var data = ItemDatabase.Instance?.GetEquipment(item.ItemId);
            if (data == null) return;
            // A second sword goes to the off hand (where the shield would be) instead of replacing the first one.
            if (targetSlot == EquipmentSlot.Weapon && IsOffhandSword(item.ItemId) && TypeOf(EquipmentSlot.Weapon) == 1 && !_equippedItems.ContainsKey(EquipmentSlot.Shield)) targetSlot = EquipmentSlot.Shield;
            if (!CanWear(item.ItemId, data, targetSlot, out var slot)) return;

            TOP.Data.PkoTables.Items.TryGetValue(item.ItemId, out var pi);
            int type = pi != null ? pi.Type : 0;
            // Two-handed weapons free the off hand; a shield cannot be worn together with one.
            if (slot == EquipmentSlot.Weapon && TwoHanded(type)) UnequipItem(EquipmentSlot.Shield);
            if (slot == EquipmentSlot.Shield && TwoHanded(TypeOf(EquipmentSlot.Weapon))) UnequipItem(EquipmentSlot.Weapon);
            if (slot == EquipmentSlot.Weapon && type != 1 && TypeOf(EquipmentSlot.Shield) == 1) UnequipItem(EquipmentSlot.Shield);

            EquipItem(item, slot);
            item.IsEquipped = true;
            _inventory.SerializeInventory();
        }
    }
}