using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using TOP.Data;
using UnityEngine;

namespace TOP.Player
{
    public partial class PlayerInventory
    {
        QuestInventoryTransaction questTransaction;
        public bool HasQuestTransaction => questTransaction != null;

        public bool RejectQuestMutation()
        {
            if (!HasQuestTransaction) return false;
            var controller = GetComponent<PlayerController>();
            if (controller != null && connectionToClient != null)
                controller.RpcShowMessage("Aguarde a confirmacao da operacao atomica para alterar o inventario.", PlayerMessageType.Warning);
            else Debug.LogWarning("[PlayerInventory] Inventario reservado para operacao atomica.");
            return true;
        }

        [Server]
        public QuestInventoryTransaction PrepareQuestTransaction(int collectItemId, int collectQuantity,
            int rewardItemId, int rewardQuantity, out string error)
        {
            if (collectQuantity < 0)
            { error = "Requisitos de inventario da missao invalidos."; return null; }
            return PrepareQuestTransaction(collectQuantity > 0
                ? new[] { new QuestCollectionItem { ItemId = collectItemId, Quantity = collectQuantity } }
                : Array.Empty<QuestCollectionItem>(), rewardItemId, rewardQuantity, out error);
        }

        [Server]
        public QuestInventoryTransaction PrepareQuestTransaction(IReadOnlyList<QuestCollectionItem> materials,
            int rewardItemId, int rewardQuantity, out string error)
        {
            error = null;
            if (materials == null || rewardQuantity < 0
                || materials.Any(item => item == null || item.Quantity <= 0 || !PkoTables.Items.ContainsKey(item.ItemId))
                || materials.Select(item => item.ItemId).Distinct().Count() != materials.Count)
            { error = "Requisitos de inventario da missao invalidos."; return null; }
            if (HasQuestTransaction || (GetComponent<PlayerTrade>()?.InTrade ?? false)
                || (GetComponent<PlayerMail>()?.InventoryRequestPending ?? false))
            { error = "Aguarde a operacao de inventario em andamento."; return null; }
            var snapshot = GetInventoryData();
            foreach (var material in materials)
            {
                if (!TryConsumeMaterials(snapshot, material.ItemId, material.Quantity))
                { error = "Faltam itens de missao disponiveis (sem equipamento, refino ou gemas)."; return null; }
            }
            if (rewardQuantity > 0)
            {
                if (!PkoTables.Items.TryGetValue(rewardItemId, out var reward) || rewardQuantity > Mathf.Max(1, reward.Stack))
                { error = "Item de recompensa nao configurado corretamente."; return null; }
                int slot = 0;
                while (slot < totalSlots && snapshot.Any(i => i.SlotIndex == slot)) slot++;
                if (slot == totalSlots)
                { error = "Libere espaco no inventario para receber a recompensa."; return null; }
                snapshot.Add(new InventoryItemData { SlotIndex = (ushort)slot, ItemId = rewardItemId, Quantity = rewardQuantity,
                    Durability = (ushort)Mathf.Clamp(reward.Durability, 0, ushort.MaxValue) });
            }
            questTransaction = new QuestInventoryTransaction(this, snapshot);
            return questTransaction;
        }

        internal static bool IsUsableQuestMaterial(bool equipped, bool locked, int refineLevel, bool hasGems)
            => !equipped && !locked && refineLevel == 0 && !hasGems;

        public long GetQuestMaterialCount(int itemId)
        {
            long count = 0;
            foreach (var slot in _slots)
                if (slot != null && slot.ItemId == itemId && IsUsableQuestMaterial(slot.IsEquipped, slot.IsLocked,
                    slot.RefineLevel, slot.Gems.Any(gem => gem >= 0))) count += slot.Quantity;
            return count;
        }

        internal static bool TryConsumeMaterials(List<InventoryItemData> items, int itemId, int amount)
        {
            if (amount <= 0) return false;
            var eligible = items.Where(item => item.ItemId == itemId && IsUsableQuestMaterial(item.IsEquipped,
                item.IsLocked, item.RefineLevel, item.GemSlot1 != null || item.GemSlot2 != null || item.GemSlot3 != null)).ToArray();
            if (eligible.Sum(item => (long)item.Quantity) < amount) return false;
            foreach (var item in eligible)
            {
                int take = Mathf.Min(amount, item.Quantity);
                item.Quantity -= take;
                amount -= take;
                if (item.Quantity == 0) items.Remove(item);
                if (amount == 0) break;
            }
            return true;
        }

        public sealed class QuestInventoryTransaction : IDisposable
        {
            PlayerInventory inventory;
            readonly List<InventoryItemData> result;

            internal QuestInventoryTransaction(PlayerInventory inventory, List<InventoryItemData> result)
            {
                this.inventory = inventory;
                this.result = result;
            }

            internal List<InventoryItemData> PreparedItems => result;

            public void Commit()
            {
                if (inventory == null) throw new InvalidOperationException("Inventario indisponivel para confirmar entrega.");
                var target = inventory;
                Dispose();
                target.InitializeFromData(result);
            }

            public void Dispose()
            {
                if (inventory != null) inventory.questTransaction = null;
                inventory = null;
            }
        }
    }
}
