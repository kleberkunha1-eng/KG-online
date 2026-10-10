using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mirror;
using TOP.Core;
using TOP.Data;
using TOP.Network;
using TOP.Player;
using TOP.Services;
using UnityEngine;

namespace TOP.NPC
{
    public partial class NPCInteractable
    {
        internal const int OriginalBankSlotCount = 32;
        readonly HashSet<long> bankOperations = new HashSet<long>();

        [Serializable]
        sealed class BankSnapshot
        {
            public List<BankItemView> Items = new List<BankItemView>();
        }

        [Serializable]
        sealed class BankItemView
        {
            public ushort SlotIndex;
            public int ItemId;
            public int Quantity;
        }

        static BankSnapshot CreateBankSnapshot(List<InventoryItemData> items) => new BankSnapshot
        {
            Items = (items ?? new List<InventoryItemData>()).Where(item => item != null)
                .Select(item => new BankItemView { SlotIndex = item.SlotIndex, ItemId = item.ItemId, Quantity = item.Quantity }).ToList()
        };

        [Command(requiresAuthority = false)]
        public void CmdOpenBank(NetworkConnectionToClient sender = null)
        {
            if (npcType != NPCType.Banker || !TryBankCustomer(sender, out var controller, out _)) return;
            var data = controller.GetCharacterData();
            if (data.BankStorageVersion != 1 || data.BankItems == null)
            {
                controller.RpcShowMessage("Banco indisponivel: atualize a API e o esquema de persistencia.", PlayerMessageType.Warning);
                return;
            }
            TargetOpenBank(sender, JsonUtility.ToJson(CreateBankSnapshot(data.BankItems)));
        }

        [Command(requiresAuthority = false)]
        public void CmdDepositBankItem(int inventorySlot, int quantity, NetworkConnectionToClient sender = null)
        {
            if (npcType != NPCType.Banker || !TryBankCustomer(sender, out var controller, out _)) return;
            _ = TransferBankAsync(controller, inventorySlot, quantity, true);
        }

        [Command(requiresAuthority = false)]
        public void CmdWithdrawBankItem(int bankSlot, int quantity, NetworkConnectionToClient sender = null)
        {
            if (npcType != NPCType.Banker || !TryBankCustomer(sender, out var controller, out _)) return;
            _ = TransferBankAsync(controller, bankSlot, quantity, false);
        }

        bool TryBankCustomer(NetworkConnectionToClient sender, out PlayerController controller, out PlayerInventory inventory)
        {
            if (!TryCustomer(sender, out controller, out inventory)) return false;
            var mail = controller.GetComponent<PlayerMail>();
            if (controller.ArenaInstanceId > 0 || controller.BoatOperationPending || controller.BankOperationPending || bankOperations.Contains(controller.CharacterId)
                || (mail != null && mail.InventoryRequestPending))
            {
                controller.RpcShowMessage("Banco indisponivel durante outra operacao, comercio, duelo ou arena.", PlayerMessageType.Warning);
                return false;
            }
            if (!controller.BankStorageAvailable || controller.GetCharacterData()?.BankStorageVersion != 1)
            {
                controller.RpcShowMessage("Banco indisponivel: use uma sessao com armazenamento compativel.", PlayerMessageType.Warning);
                return false;
            }
            return true;
        }

        bool CanContinueBankTransfer(PlayerController controller)
        {
            var mail = controller != null ? controller.GetComponent<PlayerMail>() : null;
            if (this == null || controller == null || controller.connectionToClient == null
                || !controller.BankOperationPending || !bankOperations.Contains(controller.CharacterId)
                || controller.ArenaInstanceId > 0 || controller.BoatOperationPending
                || (mail != null && mail.InventoryRequestPending)) return false;
            var movement = controller.GetComponent<PlayerMovement>();
            return movement != null && movement.ActiveNpc == this && CanInteract(movement);
        }
        [TargetRpc]
        void TargetOpenBank(NetworkConnectionToClient connection, string json)
        {
            TOP.UI.NPCDialogueUI.Close();
            TOP.UI.BankStorageUI.Open(this, json);
        }

        async Task TransferBankAsync(PlayerController controller, int slotIndex, int quantity, bool deposit)
        {
            var inventory = controller != null ? controller.GetComponent<PlayerInventory>() : null;
            if (controller == null || inventory == null || DatabaseService.Instance == null || controller.connectionToClient == null) return;
            long characterId = controller.CharacterId;
            if (!bankOperations.Add(characterId)) return;
            controller.BankOperationPending = true;
            PlayerInventory.QuestInventoryTransaction reservation = null;
            try
            {
                if (!CanContinueBankTransfer(controller))
                {
                    controller.RpcShowMessage("Interacao bancaria indisponivel.", PlayerMessageType.Warning);
                    return;
                }
                reservation = inventory.PrepareQuestTransaction(Array.Empty<QuestCollectionItem>(), 0, 0, out string error);
                if (reservation == null)
                {
                    if (!string.IsNullOrEmpty(error)) controller.RpcShowMessage(error, PlayerMessageType.Warning);
                    return;
                }
                CharacterData live = null, prepared = null;
                string message = null;
                var saved = await DatabaseService.Instance.PersistCharacterAsync(characterId, () =>
                {
                    if (this == null || controller == null || controller.connectionToClient == null)
                        throw new InvalidOperationException("Player disconnected during bank operation.");
                    if (!CanContinueBankTransfer(controller))
                        throw new BankTransferRejectedException("Interacao bancaria indisponivel.");
                    live = controller.GetCharacterData();
                    prepared = live.CopySnapshot();
                    prepared.Inventory = CloneItems(reservation.PreparedItems);
                    prepared.BankItems = prepared.BankItems ?? new List<InventoryItemData>();
                    bool moved = deposit
                        ? Deposit(prepared, inventory, slotIndex, quantity, out message)
                        : Withdraw(prepared, slotIndex, quantity, out message);
                    if (!moved) throw new BankTransferRejectedException(message);
                    prepared.BankStorageVersion = 1;
                    return prepared;
                }, TOPNetworkManager.Instance.GetSessionToken(controller.connectionToClient.connectionId), 0);
                if (!saved.success)
                {
                    if (controller != null && controller.connectionToClient != null)
                    {
                        controller.RpcShowMessage(saved.error == "INVALID_CHARACTER" ? "Operacao bancaria rejeitada." : "Banco nao confirmado; reconecte para recarregar o save autoritativo.", PlayerMessageType.Warning);
                        if (saved.error == "RELOAD_REQUIRED" || saved.error == "SAVE_CONFLICT" || saved.error == "OPERATION_MISMATCH")
                            controller.connectionToClient.Disconnect();
                    }
                    return;
                }
                live.SaveRevision = prepared.SaveRevision;
                live.BankItems = prepared.BankItems;
                live.BankStorageVersion = 1;
                live.Inventory = prepared.Inventory;
                reservation.Dispose();
                reservation = null;
                inventory.InitializeFromData(prepared.Inventory);
                TargetOpenBank(controller.connectionToClient, JsonUtility.ToJson(CreateBankSnapshot(prepared.BankItems)));
                controller.RpcShowMessage(message, PlayerMessageType.Success);
            }
            catch (BankTransferRejectedException e)
            {
                if (controller != null && controller.connectionToClient != null)
                    controller.RpcShowMessage(e.Message, PlayerMessageType.Warning);
            }
            catch (Exception e)
            {
                Debug.LogError("[Bank] Durable operation failed: " + e.Message);
                if (controller != null && controller.connectionToClient != null)
                    controller.connectionToClient.Disconnect();
            }
            finally
            {
                reservation?.Dispose();
                bankOperations.Remove(characterId);
                if (controller != null) controller.BankOperationPending = false;
            }
        }

        internal static bool Deposit(CharacterData data, PlayerInventory inventory, int inventorySlot, int quantity, out string error)
        {
            error = "Item ou quantidade indisponivel para deposito.";
            if (inventorySlot < 0 || inventorySlot >= inventory.totalSlots || quantity <= 0) return false;
            var source = data.Inventory.FirstOrDefault(item => item != null && item.SlotIndex == inventorySlot);
            if (source == null || source.Quantity < quantity || source.IsEquipped || source.IsLocked
                || (source.OwnerCharacterId.HasValue && source.OwnerCharacterId.Value != data.Id)
                || IsOriginalBankProhibitedItem(source.ItemId)) return false;
            int maxStack = PkoTables.Items.TryGetValue(source.ItemId, out var definition) ? Mathf.Max(1, definition.Stack) : 1;
            var bank = data.BankItems;
            int remaining = quantity;
            foreach (var target in bank.Where(item => item != null && StackMatches(item, source) && item.Quantity < maxStack))
            {
                int moved = Mathf.Min(remaining, maxStack - target.Quantity);
                target.Quantity += moved;
                remaining -= moved;
                if (remaining == 0) break;
            }
            while (remaining > 0)
            {
                int free = FirstFreeSlot(bank, OriginalBankSlotCount);
                if (free < 0) { error = "Banco cheio."; return false; }
                int moved = Mathf.Min(remaining, maxStack);
                var stored = CopyItem(source);
                stored.SlotIndex = (ushort)free;
                stored.Quantity = moved;
                if (quantity != source.Quantity || moved != source.Quantity) stored.UniqueItemId = Guid.NewGuid().ToString();
                bank.Add(stored);
                remaining -= moved;
            }
            source.Quantity -= quantity;
            if (source.Quantity <= 0) data.Inventory.Remove(source);
            error = "Deposito concluido.";
            return true;
        }

        internal static bool Withdraw(CharacterData data, int bankSlot, int quantity, out string error)
        {
            error = "Item ou quantidade indisponivel para retirada.";
            if (bankSlot < 0 || bankSlot >= OriginalBankSlotCount || quantity <= 0) return false;
            var source = data.BankItems.FirstOrDefault(item => item != null && item.SlotIndex == bankSlot);
            if (source == null || source.Quantity < quantity || source.IsEquipped || source.IsLocked
                || (source.OwnerCharacterId.HasValue && source.OwnerCharacterId.Value != data.Id)) return false;
            int maxStack = PkoTables.Items.TryGetValue(source.ItemId, out var definition) ? Mathf.Max(1, definition.Stack) : 1;
            int remaining = quantity;
            foreach (var target in data.Inventory.Where(item => item != null && StackMatches(item, source) && item.Quantity < maxStack))
            {
                int moved = Mathf.Min(remaining, maxStack - target.Quantity);
                target.Quantity += moved;
                remaining -= moved;
                if (remaining == 0) break;
            }
            while (remaining > 0)
            {
                int free = FirstFreeSlot(data.Inventory, 40);
                if (free < 0) { error = "Libere espaco no inventario."; return false; }
                int moved = Mathf.Min(remaining, maxStack);
                var restored = CopyItem(source);
                restored.SlotIndex = (ushort)free;
                restored.Quantity = moved;
                if (quantity != source.Quantity || moved != source.Quantity) restored.UniqueItemId = Guid.NewGuid().ToString();
                data.Inventory.Add(restored);
                remaining -= moved;
            }
            source.Quantity -= quantity;
            if (source.Quantity <= 0) data.BankItems.Remove(source);
            error = "Retirada concluida.";
            return true;
        }

        static bool StackMatches(InventoryItemData a, InventoryItemData b) => a.ItemId == b.ItemId
            && a.Durability == b.Durability && a.FusionItemId == b.FusionItemId && a.RefineLevel == b.RefineLevel
            && a.IsEquipped == b.IsEquipped && a.IsLocked == b.IsLocked && a.OwnerCharacterId == b.OwnerCharacterId
            && a.GemSlot1 == b.GemSlot1 && a.GemSlot2 == b.GemSlot2 && a.GemSlot3 == b.GemSlot3
            && a.MedalHonor == b.MedalHonor && a.MedalWins == b.MedalWins && a.MedalEntries == b.MedalEntries
            && a.MedalKills == b.MedalKills && a.MedalDeaths == b.MedalDeaths;

        static int FirstFreeSlot(List<InventoryItemData> items, int limit)
        {
            for (int slot = 0; slot < limit; slot++)
                if (!items.Any(item => item != null && item.SlotIndex == slot)) return slot;
            return -1;
        }

        static List<InventoryItemData> CloneItems(List<InventoryItemData> items) => items == null
            ? new List<InventoryItemData>()
            : items.Where(item => item != null).Select(CopyItem).ToList();

        static InventoryItemData CopyItem(InventoryItemData item) => new InventoryItemData
        {
            Id = item.Id, UniqueItemId = string.IsNullOrWhiteSpace(item.UniqueItemId) ? Guid.NewGuid().ToString() : item.UniqueItemId,
            SlotIndex = item.SlotIndex, ItemId = item.ItemId, Quantity = item.Quantity, Durability = item.Durability,
            FusionItemId = item.FusionItemId, MedalHonor = item.MedalHonor, MedalWins = item.MedalWins, MedalEntries = item.MedalEntries,
            MedalKills = item.MedalKills, MedalDeaths = item.MedalDeaths, IsEquipped = item.IsEquipped, IsLocked = item.IsLocked,
            OwnerCharacterId = item.OwnerCharacterId, RefineLevel = item.RefineLevel,
            GemSlot1 = item.GemSlot1, GemSlot2 = item.GemSlot2, GemSlot3 = item.GemSlot3,
        };

        static bool IsOriginalBankProhibitedItem(int itemId) => itemId == 2520 || itemId == 2521 || itemId == 6341
            || itemId == 6343 || itemId == 6347 || itemId == 6359 || itemId == 6370 || itemId == 6371
            || itemId == 6373 || (itemId >= 6376 && itemId <= 6378) || (itemId >= 6383 && itemId <= 6385);

        sealed class BankTransferRejectedException : Exception
        {
            public BankTransferRejectedException(string message) : base(message) { }
        }
    }
}
