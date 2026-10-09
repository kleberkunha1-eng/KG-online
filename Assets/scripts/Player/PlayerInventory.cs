using UnityEngine;
using Mirror;
using System;
using System.Collections.Generic;
using System.Globalization;
using TOP.Data;        // ✅ ADICIONADO: InventoryItemData está aqui
using TOP.Inventory;
using TOP.Systems;
using TOP.Core;

namespace TOP.Player
{
    public partial class PlayerInventory : NetworkBehaviour
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
            if (RejectQuestMutation()) return;
            for (int i = 0; i < _slots.Length; i++)
                _slots[i] = null;

            if (items != null) foreach (var item in items)
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
                        DatabaseId = item.Id,
                        UniqueItemId = item.UniqueItemId,
                        IsLocked = item.IsLocked,
                        OwnerCharacterId = item.OwnerCharacterId,
                        Gems = new[] { item.GemSlot1 ?? -1, item.GemSlot2 ?? -1, item.GemSlot3 ?? -1 }
                    };
                }
            }

            SerializeInventory();
            OnInventoryChanged?.Invoke();
            Debug.Log($"[PlayerInventory] Inicializado com {items?.Count ?? 0} itens do banco.");
        }

        public List<InventoryItemData> GetInventoryData()
        {
            var items = new List<InventoryItemData>();

            for (int i = 0; i < _slots.Length; i++)
            {
                var slot = _slots[i];
                if (slot == null || slot.IsEmpty) continue;
                if (string.IsNullOrWhiteSpace(slot.UniqueItemId))
                    slot.UniqueItemId = System.Guid.NewGuid().ToString();

                items.Add(new InventoryItemData
                {
                    SlotIndex = slot.SlotIndex,
                    ItemId = slot.ItemId,
                    Quantity = slot.Quantity,
                    Durability = (ushort)slot.Durability,
                    RefineLevel = slot.RefineLevel,
                    IsEquipped = slot.IsEquipped,
                    Id = slot.DatabaseId,
                    UniqueItemId = slot.UniqueItemId,
                    IsLocked = slot.IsLocked,
                    OwnerCharacterId = slot.OwnerCharacterId,
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
            if (RejectQuestMutation()) return false;
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

            TOP.Data.PkoTables.Items.TryGetValue(itemId, out var pkoItem);
            _slots[slotIndex] = new InventoryItem
            {
                ItemId = itemId,
                Quantity = quantity,
                SlotIndex = slotIndex,
                Durability = pkoItem != null && pkoItem.Durability > 0 ? pkoItem.Durability : 0
            };

            SerializeInventory();
            return true;
        }

        [Server]
        public void RemoveItem(ushort slotIndex, int quantity)
        {
            if (RejectQuestMutation()) return;
            if (slotIndex >= _slots.Length || _slots[slotIndex] == null) return;

            _slots[slotIndex].Quantity -= quantity;
            if (_slots[slotIndex].Quantity <= 0)
                _slots[slotIndex] = null;

            SerializeInventory();
        }

        // Quantidade total de um item (somando todos os slots nao equipados), usado por sistemas
        // que precisam pre-validar requisitos antes de consumir (ex.: forja).
        [Server]
        public int GetItemCount(int itemId)
        {
            int total = 0;
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i] != null && _slots[i].ItemId == itemId && !_slots[i].IsEquipped) total += _slots[i].Quantity;
            return total;
        }

        // Ajusta o nivel de refino/forja de um slot especifico e resserializa o inventario.
        // Usado pelo sistema de forja (PlayerForge).
        [Server]
        public bool SetItemRefine(int slotIndex, int refineLevel)
        {
            if (RejectQuestMutation()) return false;
            if (slotIndex < 0 || slotIndex >= _slots.Length || _slots[slotIndex] == null) return false;
            _slots[slotIndex].RefineLevel = Mathf.Clamp(refineLevel, 0, TOP.Data.PkoGems.MaxRefine);
            SerializeInventory();
            return true;
        }

        // Remove 'quantity' unidades de um item (podendo abranger varios slots empilhados).
        // Usado por sistemas que tiram itens do inventario sem UI (correio, comercio).
        // Retorna false (sem remover nada) se o jogador nao tiver a quantidade total exigida.
        [Server]
        public bool RemoveItemById(int itemId, int quantity)
        {
            if (RejectQuestMutation()) return false;
            if (quantity <= 0) return true;
            int total = 0;
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i] != null && _slots[i].ItemId == itemId && !_slots[i].IsEquipped) total += _slots[i].Quantity;
            if (total < quantity) return false;

            int remaining = quantity;
            for (int i = 0; i < _slots.Length && remaining > 0; i++)
            {
                if (_slots[i] == null || _slots[i].ItemId != itemId || _slots[i].IsEquipped) continue;
                int take = Mathf.Min(remaining, _slots[i].Quantity);
                _slots[i].Quantity -= take;
                remaining -= take;
                if (_slots[i].Quantity <= 0) _slots[i] = null;
            }
            SerializeInventory();
            return true;
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
        public event System.Action<int, bool, string> AdminGenerationCompleted;
        int nextAdminRequestId;

        public int NewAdminGenerationRequestId()
        {
            nextAdminRequestId = nextAdminRequestId == int.MaxValue ? 1 : nextAdminRequestId + 1;
            return nextAdminRequestId;
        }

        [TargetRpc]
        void TargetAdminGenerationResult(bool success, string message)
        {
            AdminGenerationCompleted?.Invoke(0, success, message);
            if (!success) Debug.LogWarning("[Admin] " + message);
        }

        [TargetRpc]
        void TargetAdminRequestResult(int requestId, bool success, string message)
        {
            AdminGenerationCompleted?.Invoke(requestId, success, message);
            if (!success) Debug.LogWarning("[Admin] " + message);
        }

        void ReplyAdminGeneration(int requestId, bool success, string message)
        {
            if (!success) Debug.LogWarning("[Admin] " + message);
            if (requestId > 0) TargetAdminRequestResult(requestId, success, message);
            else TargetAdminGenerationResult(success, message);
        }

        async System.Threading.Tasks.Task<bool> AuthorizeAdminGeneration(int requestId)
        {
            var pc = GetComponent<TOP.Player.PlayerController>();
            var manager = TOP.Network.TOPNetworkManager.Instance;
            if (pc == null || manager == null || connectionToClient == null)
            {
                ReplyAdminGeneration(requestId, false, "Sessao do servidor indisponivel. Entre pelo login.");
                return false;
            }
            string token = manager.GetSessionToken(connectionToClient.connectionId);
            if (string.IsNullOrEmpty(token))
            {
                ReplyAdminGeneration(requestId, false, "Sessao expirada. Entre novamente pelo login.");
                return false;
            }
            var database = TOP.Services.DatabaseService.Instance;
            if (database == null)
            {
                Debug.LogError("[Admin] Servico de dados indisponivel no servidor.");
                ReplyAdminGeneration(requestId, false, "Servico de dados indisponivel no servidor. Verifique sua inicializacao.");
                return false;
            }
            bool authorized = await database.IsAdminAsync(pc.AccountId, token);
            if (this == null || connectionToClient == null || !connectionToClient.isAuthenticated) return false;
            if (!authorized)
                ReplyAdminGeneration(requestId, false, "Permissao admin nao confirmada pela API. Verifique a conta e a conexao.");
            return authorized;
        }

        [Command]
        public async void CmdAdminGive(int itemId, int quantity)
        {
            await GiveAdminItems(0, itemId, quantity);
        }

        [Command]
        public async void CmdRequestAdminGive(int requestId, int itemId, int quantity)
        {
            if (requestId <= 0) { Debug.LogWarning("[Admin] Identificador de pedido invalido."); return; }
            await GiveAdminItems(requestId, itemId, quantity);
        }

        async System.Threading.Tasks.Task GiveAdminItems(int requestId, int itemId, int quantity)
        {
            if (quantity < 1 || !TOP.Data.PkoTables.Items.TryGetValue(itemId, out var definition)
                || quantity > (long)Mathf.Max(1, definition.Stack) * _slots.Length)
            {
                ReplyAdminGeneration(requestId, false, "Item ou quantidade invalida no servidor.");
                return;
            }
            if (!await AuthorizeAdminGeneration(requestId)) return;
            int requested = quantity;
            int max = Mathf.Max(1, TOP.Data.PkoTables.Items[itemId].Stack);
            while (quantity > 0)
            {
                int slot = FindEmptySlot();
                if (slot == -1) break;
                int n = Mathf.Min(max, quantity);
                if (!AddItem(itemId, n, (ushort)slot)) break;
                quantity -= n;
            }
            ReplyAdminGeneration(requestId, quantity == 0, quantity == 0
                ? $"Recebido: item #{itemId} x{requested}."
                : $"Inventario cheio: recebidos {requested - quantity} de {requested} itens.");
        }

        // Gera um equipamento ja refinado/engastado no primeiro slot vazio. O servidor revalida tudo.
        [Command]
        public async void CmdAdminGenerate(int itemId, int refine, int sockets, int gem1, int gem2, int gem3)
        {
            await GenerateAdminEquipment(0, itemId, refine, sockets, gem1, gem2, gem3);
        }

        [Command]
        public async void CmdRequestAdminGenerate(int requestId, int itemId, int refine, int sockets, int gem1, int gem2, int gem3)
        {
            if (requestId <= 0) { Debug.LogWarning("[Admin] Identificador de pedido invalido."); return; }
            await GenerateAdminEquipment(requestId, itemId, refine, sockets, gem1, gem2, gem3);
        }

        async System.Threading.Tasks.Task GenerateAdminEquipment(int requestId, int itemId, int refine, int sockets,
            int gem1, int gem2, int gem3)
        {
            if (!TOP.Data.PkoTables.Items.TryGetValue(itemId, out var it))
            {
                ReplyAdminGeneration(requestId, false, "Item nao encontrado no catalogo do servidor.");
                return;
            }
            if (!await AuthorizeAdminGeneration(requestId)) return;
            int slot = FindEmptySlot();
            if (slot == -1 || !AddItem(itemId, 1, (ushort)slot))
            {
                ReplyAdminGeneration(requestId, false, "Inventario cheio. Libere um slot.");
                return;
            }
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
            ReplyAdminGeneration(requestId, true, $"Recebido: item #{itemId} (+{item.RefineLevel}).");
        }

        // Apaga um item da bolsa (itens equipados precisam ser removidos antes).
        [Command]
        public void CmdDeleteItem(ushort slotIndex)
        {
            if (RejectQuestMutation()) return;
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
            MoveItemOnServer(fromSlot, toSlot);
        }

        [Server]
        public bool MoveItemOnServer(ushort fromSlot, ushort toSlot)
        {
            if (RejectQuestMutation()) return false;
            if (fromSlot >= _slots.Length || toSlot >= _slots.Length) return false;
            if ((_slots[fromSlot] != null && _slots[fromSlot].IsEquipped) || (_slots[toSlot] != null && _slots[toSlot].IsEquipped)) return false;

            InventoryItem temp = _slots[toSlot];
            _slots[toSlot] = _slots[fromSlot];
            if (_slots[toSlot] != null) _slots[toSlot].SlotIndex = toSlot;

            _slots[fromSlot] = temp;
            if (_slots[fromSlot] != null) _slots[fromSlot].SlotIndex = fromSlot;

            SerializeInventory();
            return true;
        }

        [Command]
        public void CmdEquipItem(ushort inventorySlot, EquipmentSlot targetSlot)
        {
            PlayerEquipment equipment = GetComponent<PlayerEquipment>();
            equipment?.EquipFromInventory(inventorySlot, targetSlot);
        }

        [Command]
        public void CmdDropItem(ushort slotIndex, int quantity, Vector3 dropPosition)
        {
            DropItemOnServer(slotIndex, quantity, dropPosition);
        }

        [Server]
        public bool DropItemOnServer(ushort slotIndex, int quantity, Vector3 dropPosition)
        {
            if (RejectQuestMutation()) return false;
            if (slotIndex >= _slots.Length || quantity <= 0) return false;
            InventoryItem item = _slots[slotIndex];
            if (item == null || item.IsEquipped || item.Quantity < quantity) return false;
            WorldItemManager worldItemManager = GameObject.FindAnyObjectByType<WorldItemManager>();
            if (worldItemManager == null) return false;

            worldItemManager.SpawnWorldItem(item.ItemId, quantity, dropPosition);

            item.Quantity -= quantity;
            if (item.Quantity <= 0)
                _slots[slotIndex] = null;

            SerializeInventory();
            return true;
        }

        [Command]
        public void CmdUseItem(ushort slotIndex)
        {
            PlayerConsumables consumables = GetComponent<PlayerConsumables>();
            if (consumables != null)
                consumables.UseItemOnServer(slotIndex);
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
            UnequipItemToSlotOnServer(slot, toSlot);
        }

        [Server]
        public bool UnequipItemToSlotOnServer(EquipmentSlot slot, ushort toSlot)
        {
            if (RejectQuestMutation()) return false;
            PlayerEquipment equipment = GetComponent<PlayerEquipment>();
            if (equipment == null || toSlot >= _slots.Length) return false;
            InventoryItem equippedItem = equipment.GetEquippedItem(slot);
            int id = equippedItem?.ItemId ?? 0;
            if (id == 0) return false;
            int from = equippedItem != null ? equippedItem.SlotIndex : -1;
            if (from < 0 || from >= _slots.Length || _slots[from] == null || !_slots[from].IsEquipped || _slots[from].ItemId != id)
                from = FindItemSlot(id);
            if (from < 0 || (toSlot != from && _slots[toSlot] != null)) return false;

            equipment.UnequipItem(slot);
            if (toSlot == from) return true;
            _slots[toSlot] = _slots[from]; _slots[toSlot].SlotIndex = toSlot; _slots[from] = null;
            SerializeInventory();
            return true;
        }

        // The bag record of a worn item stays reserved (so it persists) until it is taken off.
        [Server]
        public void ReleaseEquipped(int itemId)
        {
            if (RejectQuestMutation()) return;
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i] != null && _slots[i].IsEquipped && _slots[i].ItemId == itemId) { _slots[i].IsEquipped = false; break; }
            SerializeInventory();
        }

        [Server]
        public void ReleaseEquippedSlot(ushort slotIndex, int itemId)
        {
            if (RejectQuestMutation()) return;
            if (slotIndex < _slots.Length && _slots[slotIndex] != null && _slots[slotIndex].IsEquipped && _slots[slotIndex].ItemId == itemId)
            {
                _slots[slotIndex].IsEquipped = false;
                SerializeInventory();
                return;
            }

            ReleaseEquipped(itemId);
        }

        [Command]
        public void CmdPickupWorldItem(NetworkIdentity worldItemIdentity)
        {
            WorldItem worldItem = worldItemIdentity?.GetComponent<WorldItem>();
            if (worldItem != null)
                worldItem.Pickup(this);
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
                    list.Add($"{i}:{_slots[i].ItemId}:{_slots[i].Quantity}:{(_slots[i].IsEquipped ? 1 : 0)}:{_slots[i].RefineLevel}:{_slots[i].Gems[0]}:{_slots[i].Gems[1]}:{_slots[i].Gems[2]}:{_slots[i].Durability}:{(_slots[i].IsLocked ? 1 : 0)}");
                }
            }
            _inventoryData = string.Join(";", list);
        }

        void OnInventoryDataChanged(string oldValue, string newValue)
        {
            // Host hooks must not replace server instances with the client's reduced payload.
            if (isServer) { OnInventoryChanged?.Invoke(); return; }
            DeserializeInventory(newValue);
            OnInventoryChanged?.Invoke();
        }

        void DeserializeInventory(string data)
        {
            Array.Clear(_slots, 0, _slots.Length);
            if (string.IsNullOrEmpty(data)) return;

            foreach (string entry in data.Split(';'))
            {
                if (string.IsNullOrEmpty(entry)) continue;

                string[] fields = entry.Split(':');
                // O campo de durabilidade (indice 8) foi adicionado depois; entradas antigas com 8 campos
                // ainda sao aceitas e assumem durabilidade cheia (tratada como 0 => 100% na UI).
                if ((fields.Length < 8 || fields.Length > 10) ||
                    !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot) ||
                    !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int itemId) ||
                    !int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int quantity) ||
                    !int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int equipped) ||
                    !int.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int refine) ||
                    !int.TryParse(fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int gem1) ||
                    !int.TryParse(fields[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int gem2) ||
                    !int.TryParse(fields[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out int gem3) ||
                    slot < 0 || slot >= _slots.Length || itemId <= 0 || quantity <= 0)
                {
                    Debug.LogWarning("[PlayerInventory] Ignorando entrada de inventario invalida: " + entry);
                    continue;
                }
                int durability = 0;
                if (fields.Length >= 9) int.TryParse(fields[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out durability);
                int locked = 0;
                if (fields.Length == 10 && (!int.TryParse(fields[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out locked)
                    || (locked != 0 && locked != 1)))
                {
                    Debug.LogWarning("[PlayerInventory] Estado de bloqueio invalido: " + entry);
                    continue;
                }

                _slots[slot] = new InventoryItem
                {
                    ItemId = itemId,
                    Quantity = quantity,
                    SlotIndex = (ushort)slot,
                    IsEquipped = equipped != 0,
                    RefineLevel = refine,
                    Gems = new[] { gem1, gem2, gem3 },
                    Durability = durability,
                    IsLocked = locked != 0
                };
            }
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
