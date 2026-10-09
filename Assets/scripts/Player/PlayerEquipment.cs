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
        public ushort InventorySlot;
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
            if (_inventory != null && _inventory.RejectQuestMutation()) return;
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
                InventorySlot = item.SlotIndex,
                Durability = item.Durability,
                Extra = TOP.Data.PkoGems.InstanceBonus(item)
            };

            RebuildEquipmentStats();
            OnItemEquipped?.Invoke(item, slot);
            SerializeEquipment();
        }

        [Server]
        public void UnequipItem(EquipmentSlot slot)
        {
            if (_inventory != null && _inventory.RejectQuestMutation()) return;
            if (!_equippedItems.ContainsKey(slot)) return;

            EquipmentEntry equipped = _equippedItems[slot];
            _inventory?.ReleaseEquippedSlot(equipped.InventorySlot, equipped.ItemId);
            _equippedItems.Remove(slot);
            RebuildEquipmentStats();

            InventoryItem unequippedItem = new InventoryItem 
            { 
                ItemId = equipped.ItemId, 
                Quantity = 1 
            };
            OnItemUnequipped?.Invoke(unequippedItem, slot);

            SerializeEquipment();
        }

        [Server]
        void RebuildEquipmentStats()
        {
            if (_stats == null)
            {
                Debug.LogError("[PlayerEquipment] Cannot rebuild equipment bonuses without PlayerStats.");
                return;
            }
            _stats.ResetEquipmentBonuses();
            foreach (var entry in _equippedItems.Values)
            {
                var data = ItemDatabase.Instance.GetEquipment(entry.ItemDatabaseId);
                if (data == null)
                {
                    Debug.LogError($"[PlayerEquipment] Missing stat definition for equipped item {entry.ItemDatabaseId}.");
                    continue;
                }
                ApplyEquipmentStats(data, true);
                ApplyExtra(entry.Extra, 1);
            }
            _stats.FinishEquipmentStats();
        }

        [Server]
        void ApplyEquipmentStats(EquipmentData data, bool add)
        {
            int multiplier = add ? 1 : -1;

            _stats.AddBonusStrength(data.bonusSTR * multiplier);
            _stats.AddBonusAgility(data.bonusAGI * multiplier);
            _stats.AddBonusConstitution(data.bonusCON * multiplier);
            _stats.AddBonusSpirit((data.bonusSPR != 0 ? data.bonusSPR : data.bonusINT) * multiplier);
            _stats.AddBonusHp(data.bonusHP * multiplier);
            _stats.AddBonusMp(data.bonusMP * multiplier);
            _stats.AddBonusSp(data.bonusSP * multiplier);
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

        // Persisted base attributes do not include equipment or instance bonuses.
        [Server]
        public void LoadEquippedFromInventory(List<TOP.Data.InventoryItemData> items)
        {
            _equippedItems.Clear();
            foreach (var it in items ?? new List<TOP.Data.InventoryItemData>())
            {
                if (!it.IsEquipped) continue;
                EquipmentSlot slot;
                var data = ItemDatabase.Instance?.GetEquipment(it.ItemId);
                if (data != null) slot = data.slot;
                else if (TOP.Data.PkoTables.Items.TryGetValue(it.ItemId, out var pi)) slot = TOP.Data.PkoTables.SlotOf(pi);
                else continue;
                if (slot == EquipmentSlot.Weapon && _equippedItems.ContainsKey(slot) && IsOffhandSword(it.ItemId)) slot = EquipmentSlot.Shield;
                else if (IsRing(slot) && _equippedItems.ContainsKey(slot)) slot = slot == EquipmentSlot.Ring1 ? EquipmentSlot.Ring2 : slot;
                var instance = _inventory?.GetSlot(it.SlotIndex);
                if (data == null || instance == null || instance.ItemId != it.ItemId)
                {
                    Debug.LogError($"[PlayerEquipment] Cannot restore equipped item {it.ItemId} at inventory slot {it.SlotIndex}.");
                    continue;
                }
                _equippedItems[slot] = new EquipmentEntry
                {
                    Slot = slot, ItemId = it.ItemId, ItemDatabaseId = it.ItemId,
                    InventorySlot = it.SlotIndex, Durability = it.Durability,
                    Extra = TOP.Data.PkoGems.InstanceBonus(instance)
                };
            }
            RebuildEquipmentStats();
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
            int itemId;
            if (_equippedItems.TryGetValue(slot, out EquipmentEntry equipped))
                itemId = equipped.ItemId;
            else if (!TryGetSerializedItemId(slot, out itemId))
                return null;

            if (_inventory != null)
            {
                if (_equippedItems.TryGetValue(slot, out equipped) && equipped.InventorySlot < _inventory.totalSlots)
                {
                    var exact = _inventory.GetSlot(equipped.InventorySlot);
                    if (exact != null && exact.ItemId == itemId) return exact;
                }

                for (int i = 0; i < _inventory.totalSlots; i++)
                {
                    var bag = _inventory.GetSlot(i);
                    if (bag != null && bag.IsEquipped && bag.ItemId == itemId) return bag;
                }
            }

            return new InventoryItem { ItemId = itemId, Quantity = 1, IsEquipped = true };
        }

        bool TryGetSerializedItemId(EquipmentSlot slot, out int itemId)
        {
            itemId = 0;
            if (string.IsNullOrEmpty(_equipmentData)) return false;

            foreach (string entry in _equipmentData.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int separator = entry.LastIndexOf(':');
                if (separator <= 0 ||
                    !Enum.TryParse(entry.Substring(0, separator), out EquipmentSlot serializedSlot) ||
                    serializedSlot != slot ||
                    !int.TryParse(entry.Substring(separator + 1), out itemId))
                    continue;
                return itemId > 0;
            }

            return false;
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
            EquipFromInventory(inventorySlot, targetSlot);
        }

        [Server]
        public bool EquipFromInventory(ushort inventorySlot, EquipmentSlot targetSlot)
        {
            if (_inventory == null || inventorySlot >= _inventory.totalSlots) return false;
            if (_inventory.RejectQuestMutation()) return false;

            InventoryItem item = _inventory.GetSlot(inventorySlot);
            if (item == null || item.IsEmpty || item.IsEquipped) return false;
            var data = ItemDatabase.Instance?.GetEquipment(item.ItemId);
            if (data == null)
            {
                ShowEquipWarning("This item cannot be equipped.");
                return false;
            }
            if (_stats == null)
            {
                ShowEquipWarning("Character stats are not ready.");
                return false;
            }
            // A second sword goes to the off hand (where the shield would be) instead of replacing the first one.
            if (targetSlot == EquipmentSlot.Weapon && IsOffhandSword(item.ItemId) && TypeOf(EquipmentSlot.Weapon) == 1 && !_equippedItems.ContainsKey(EquipmentSlot.Shield)) targetSlot = EquipmentSlot.Shield;
            if (!CanWear(item.ItemId, data, targetSlot, out var slot))
            {
                ShowEquipWarning("This item does not fit that slot or your character does not meet its requirements.");
                return false;
            }

            TOP.Data.PkoTables.Items.TryGetValue(item.ItemId, out var pi);
            int type = pi != null ? pi.Type : 0;
            // Two-handed weapons free the off hand; a shield cannot be worn together with one.
            if (slot == EquipmentSlot.Weapon && TwoHanded(type)) UnequipItem(EquipmentSlot.Shield);
            if (slot == EquipmentSlot.Shield && TwoHanded(TypeOf(EquipmentSlot.Weapon))) UnequipItem(EquipmentSlot.Weapon);
            if (slot == EquipmentSlot.Weapon && type != 1 && TypeOf(EquipmentSlot.Shield) == 1) UnequipItem(EquipmentSlot.Shield);

            EquipItem(item, slot);
            item.IsEquipped = true;
            _inventory.SerializeInventory();
            return true;
        }

        void ShowEquipWarning(string message)
        {
            GetComponent<PlayerController>()?.RpcShowMessage(message, PlayerMessageType.Warning);
        }
    }
}
