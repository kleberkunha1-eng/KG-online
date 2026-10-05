using UnityEngine;
using Mirror;
using System;
using System.Collections.Generic;
using TOP.Data;        // ✅ ADICIONADO: InventoryItemData está aqui
using TOP.Inventory;
using TOP.Systems;
using TOP.Core;

namespace TOP.Player
{
    public class PlayerInventory : NetworkBehaviour
    {
        [SyncVar(hook = nameof(OnInventoryDataChanged))]
        private string _inventoryData = "";

        private readonly InventoryItem[] _slots = new InventoryItem[40];

        public event Action OnInventoryChanged;
        public event Action<int, InventoryItem> OnSlotChanged;
        public event Action<InventoryItem, int> OnItemAdded;
        public event Action<InventoryItem, int> OnItemRemoved;

        public int totalSlots => _slots.Length;

        // =================================================================================
        // INICIALIZAÇÃO DO BANCO
        // =================================================================================
        public void InitializeFromData(List<InventoryItemData> items)
        {
            if (items == null) return;

            for (int i = 0; i < _slots.Length; i++)
                _slots[i] = null;

            foreach (var item in items)
            {
                if (item.SlotIndex < _slots.Length)
                {
                    _slots[item.SlotIndex] = new InventoryItem
                    {
                        ItemId = item.ItemId,
                        Quantity = item.Quantity,
                        SlotIndex = item.SlotIndex,
                        Durability = (int)item.Durability,
                        RefineLevel = item.RefineLevel,
                        IsEquipped = item.IsEquipped,
                        Gems = new[] { item.GemSlot1 ?? -1, item.GemSlot2 ?? -1, item.GemSlot3 ?? -1 }
                    };
                }
            }

            SerializeInventory();
            OnInventoryChanged?.Invoke();
            Debug.Log($"[PlayerInventory] Inicializado com {items.Count} itens do banco.");
        }

        public List<InventoryItemData> GetInventoryData()
        {
            var items = new List<InventoryItemData>();

            for (int i = 0; i < _slots.Length; i++)
            {
                var slot = _slots[i];
                if (slot == null || slot.IsEmpty) continue;

                items.Add(new InventoryItemData
                {
                    SlotIndex = slot.SlotIndex,
                    ItemId = slot.ItemId,
                    Quantity = slot.Quantity,
                    Durability = (ushort)slot.Durability,
                    RefineLevel = slot.RefineLevel,
                    IsEquipped = slot.IsEquipped,
                    GemSlot1 = slot.Gems[0] >= 0 ? slot.Gems[0] : (int?)null,
                    GemSlot2 = slot.Gems[1] >= 0 ? slot.Gems[1] : (int?)null,
                    GemSlot3 = slot.Gems[2] >= 0 ? slot.Gems[2] : (int?)null
                });
            }

            return items;
        }

        // =================================================================================
        // AWAKE & REGISTRO
        // =================================================================================
        void Awake()
        {
            for (int i = 0; i < _slots.Length; i++)
                _slots[i] = null;
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            Invoke(nameof(RegisterInInventoryUI), 0.1f);
        }

        void RegisterInInventoryUI()
        {
            if (InventoryUI.Instance != null)
            {
                InventoryUI.Instance.SetPlayerInventory(this);
                Debug.Log($"[PlayerInventory] Registrado no InventoryUI: {name}");
            }
            else
            {
                Invoke(nameof(RegisterInInventoryUI), 0.5f);
            }
        }

        // =================================================================================
        // GETTERS
        // =================================================================================
        public int FindEmptySlot()
        {
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i] == null) return i;
            return -1;
        }

        public int FindItemSlot(int itemId)
        {
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i] != null && _slots[i].ItemId == itemId) return i;
            return -1;
        }

        public InventoryItem GetSlot(int index)
        {
            if (index < 0 || index >= _slots.Length) return null;
            return _slots[index];
        }

        public InventoryItem GetItem(ushort slotIndex)
        {
            return GetSlot(slotIndex);
        }

        // =================================================================================
        // SERVER LOGIC
        // =================================================================================
        [Server]
        public bool AddItem(int itemId, int quantity, ushort slotIndex = 0)
        {
            if (slotIndex == 0 && _slots[0] != null)
            {
                int empty = FindEmptySlot();
                if (empty == -1) return false;
                slotIndex = (ushort)empty;
            }
            else if (slotIndex >= _slots.Length || _slots[slotIndex] != null)
            {
                int empty = FindEmptySlot();
                if (empty == -1) return false;
                slotIndex = (ushort)empty;
            }

            _slots[slotIndex] = new InventoryItem
            {
                ItemId = itemId,
                Quantity = quantity,
                SlotIndex = slotIndex
            };

            SerializeInventory();
            return true;
        }

        [Server]
        public void RemoveItem(ushort slotIndex, int quantity)
        {
            if (slotIndex >= _slots.Length || _slots[slotIndex] == null) return;

            _slots[slotIndex].Quantity -= quantity;
            if (_slots[slotIndex].Quantity <= 0)
                _slots[slotIndex] = null;

            SerializeInventory();
        }

        // =================================================================================
        // COMMANDS
        // =================================================================================
        [Command]
        public void CmdAddItem(int itemId, int quantity)
        {
            int slot = FindEmptySlot();
            if (slot != -1)
                AddItem(itemId, quantity, (ushort)slot);
        }

        // Entrega de itens pelo painel admin: o servidor confere is_admin da conta no banco.
        [Command]
        public async void CmdAdminGive(int itemId, int quantity)
        {
            var pc = GetComponent<TOP.Player.PlayerController>();
            if (pc == null || quantity < 1 || !TOP.Data.PkoTables.Items.ContainsKey(itemId)) return;
            string token = TOP.Network.TOPNetworkManager.Instance.GetSessionToken(connectionToClient.connectionId);
            if (!await TOP.Services.DatabaseService.Instance.IsAdminAsync(pc.AccountId, token)) { Debug.LogWarning("[Admin] Conta " + pc.AccountId + " nao e admin."); return; }
            int max = Mathf.Max(1, TOP.Data.PkoTables.Items[itemId].Stack);
            while (quantity > 0)
            {
                int slot = FindEmptySlot();
                if (slot == -1) break;
                int n = Mathf.Min(max, quantity);
                AddItem(itemId, n, (ushort)slot);
                quantity -= n;
            }
        }

        // Gera um equipamento ja refinado/engastado no primeiro slot vazio. O servidor revalida tudo.
        [Command]
        public async void CmdAdminGenerate(int itemId, int refine, int sockets, int gem1, int gem2, int gem3)
        {
            var pc = GetComponent<TOP.Player.PlayerController>();
            if (pc == null || !TOP.Data.PkoTables.Items.TryGetValue(itemId, out var it)) return;
            string token = TOP.Network.TOPNetworkManager.Instance.GetSessionToken(connectionToClient.connectionId);
            if (!await TOP.Services.DatabaseService.Instance.IsAdminAsync(pc.AccountId, token)) { Debug.LogWarning("[Admin] Conta " + pc.AccountId + " nao e admin."); return; }
            int slot = FindEmptySlot();
            if (slot == -1) return;
            if (!AddItem(itemId, 1, (ushort)slot)) return;
            var item = _slots[slot];
            if (TOP.Data.PkoGems.CanSocket(it))
            {
                item.RefineLevel = Mathf.Clamp(refine, 0, TOP.Data.PkoGems.MaxRefine);
                sockets = Mathf.Clamp(sockets, 0, it.MaxSockets);
                var gems = new[] { gem1, gem2, gem3 };
                for (int i = 0; i < sockets; i++)
                    item.Gems[i] = gems[i] > 0 && TOP.Data.PkoGems.Accepts(it, gems[i]) ? gems[i] : 0;
            }
            SerializeInventory();
        }

        // Apaga um item da bolsa (itens equipados precisam ser removidos antes).
        [Command]
        public void CmdDeleteItem(ushort slotIndex)
        {
            if (slotIndex >= _slots.Length || _slots[slotIndex] == null || _slots[slotIndex].IsEquipped) return;
            _slots[slotIndex] = null;
            SerializeInventory();
        }
        // Admin: troca a classe do personagem (para testar equipamentos de outras classes).
        [Command]
        public async void CmdAdminSetClass(int classId)
        {
            var pc = GetComponent<TOP.Player.PlayerController>();
            var cls = GetComponent<TOP.Player.PlayerClass>();
            if (pc == null || cls == null || !System.Enum.IsDefined(typeof(TOP.Core.CharacterClass), classId)) return;
            string token = TOP.Network.TOPNetworkManager.Instance.GetSessionToken(connectionToClient.connectionId);
            if (!await TOP.Services.DatabaseService.Instance.IsAdminAsync(pc.AccountId, token)) return;
            cls.SetClass((TOP.Core.CharacterClass)classId);
        }
        [Command]
        public async void CmdAdminSetLevel(int level)
        {
            var pc = GetComponent<TOP.Player.PlayerController>();
            string token = TOP.Network.TOPNetworkManager.Instance.GetSessionToken(connectionToClient.connectionId);
            if (pc == null || !await TOP.Services.DatabaseService.Instance.IsAdminAsync(pc.AccountId, token)) return;
            pc.AdminSetLevel(level);
        }

        [Command]
        public async void CmdAdminSetGold(long gold)
        {
            var pc = GetComponent<TOP.Player.PlayerController>();
            string token = TOP.Network.TOPNetworkManager.Instance.GetSessionToken(connectionToClient.connectionId);
            if (pc == null || !await TOP.Services.DatabaseService.Instance.IsAdminAsync(pc.AccountId, token)) return;
            pc.AdminSetGold((ulong)System.Math.Max(0, gold));
        }

        [Command]
        public async void CmdAdminAddPoints(int stat, int skill)
        {
            var pc = GetComponent<TOP.Player.PlayerController>();
            string token = TOP.Network.TOPNetworkManager.Instance.GetSessionToken(connectionToClient.connectionId);
            if (pc == null || !await TOP.Services.DatabaseService.Instance.IsAdminAsync(pc.AccountId, token)) return;
            pc.AdminAddPoints(stat, skill);
        }

        [Command]
        public void CmdAddItemDebug(int itemId, int quantity)
        {
            int slot = FindEmptySlot();
            if (slot != -1)
                AddItem(itemId, quantity, (ushort)slot);
        }

        [Command]
        public void CmdRemoveItemDebug(ushort slotIndex, int quantity)
        {
            RemoveItem(slotIndex, quantity);
        }

        [Command]
        public void CmdMoveItem(ushort fromSlot, ushort toSlot)
        {
            if (fromSlot >= _slots.Length || toSlot >= _slots.Length) return;
            if ((_slots[fromSlot] != null && _slots[fromSlot].IsEquipped) || (_slots[toSlot] != null && _slots[toSlot].IsEquipped)) return;

            InventoryItem temp = _slots[toSlot];
            _slots[toSlot] = _slots[fromSlot];
            if (_slots[toSlot] != null) _slots[toSlot].SlotIndex = toSlot;

            _slots[fromSlot] = temp;
            if (_slots[fromSlot] != null) _slots[fromSlot].SlotIndex = fromSlot;

            SerializeInventory();
        }

        [Command]
        public void CmdEquipItem(ushort inventorySlot, EquipmentSlot targetSlot)
        {
            PlayerEquipment equipment = GetComponent<PlayerEquipment>();
            if (equipment == null) return;

            InventoryItem item = _slots[inventorySlot];
            if (item == null) return;

            equipment.CmdEquipItem(inventorySlot, targetSlot);
        }

        [Command]
        public void CmdDropItem(ushort slotIndex, int quantity, Vector3 dropPosition)
        {
            InventoryItem item = _slots[slotIndex];
            if (item == null || item.Quantity < quantity) return;

            WorldItemManager worldItemManager = GameObject.FindAnyObjectByType<WorldItemManager>();
            if (worldItemManager != null)
            {
                worldItemManager.SpawnWorldItem(item.ItemId, quantity, dropPosition);
            }

            item.Quantity -= quantity;
            if (item.Quantity <= 0)
                _slots[slotIndex] = null;

            SerializeInventory();
        }

        [Command]
        public void CmdUseItem(ushort slotIndex)
        {
            PlayerConsumables consumables = GetComponent<PlayerConsumables>();
            if (consumables != null)
                consumables.CmdUseItem(slotIndex);
        }

        [Command]
        public void CmdUnequipItem(EquipmentSlot slot)
        {
            PlayerEquipment equipment = GetComponent<PlayerEquipment>();
            if (equipment != null)
                equipment.UnequipItem(slot);
        }

        // Drag from the equipment window onto a bag slot: the item goes back to that slot when it is free.
        [Command]
        public void CmdUnequipItemTo(EquipmentSlot slot, ushort toSlot)
        {
            PlayerEquipment equipment = GetComponent<PlayerEquipment>();
            if (equipment == null) return;
            int id = equipment.GetEquippedItem(slot)?.ItemId ?? 0;
            equipment.UnequipItem(slot);
            if (id == 0 || toSlot >= _slots.Length || _slots[toSlot] != null) return;
            int from = FindItemSlot(id);
            if (from < 0) return;
            _slots[toSlot] = _slots[from]; _slots[toSlot].SlotIndex = toSlot; _slots[from] = null;
            SerializeInventory();
        }

        // The bag record of a worn item stays reserved (so it persists) until it is taken off.
        [Server]
        public void ReleaseEquipped(int itemId)
        {
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i] != null && _slots[i].IsEquipped && _slots[i].ItemId == itemId) { _slots[i].IsEquipped = false; break; }
            SerializeInventory();
        }

        [Command]
        public void CmdPickupWorldItem(NetworkIdentity worldItemIdentity)
        {
            WorldItem worldItem = worldItemIdentity?.GetComponent<WorldItem>();
            if (worldItem != null)
                worldItem.CmdPickup();
        }

        // =================================================================================
        // SERIALIZATION
        // =================================================================================
        [Server]
        public void SerializeInventory()
        {
            List<string> list = new List<string>();
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] != null)
                {
                    list.Add($"{i}:{_slots[i].ItemId}:{_slots[i].Quantity}:{(_slots[i].IsEquipped ? 1 : 0)}:{_slots[i].RefineLevel}:{_slots[i].Gems[0]}:{_slots[i].Gems[1]}:{_slots[i].Gems[2]}");
                }
            }
            _inventoryData = string.Join(";", list);
        }

        void OnInventoryDataChanged(string oldValue, string newValue)
        {
            OnInventoryChanged?.Invoke();
        }

        // =================================================================================
        // DEBUG
        // =================================================================================
        public void DebugGiveItem(int itemId, int quantity)
        {
            if (isLocalPlayer)
                CmdAddItemDebug(itemId, quantity);
        }
    }
}