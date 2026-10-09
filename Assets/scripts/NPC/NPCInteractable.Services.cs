using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using TOP.Data;
using TOP.Player;
using UnityEngine;

namespace TOP.NPC
{
    [Serializable]
    public sealed class NPCRecipe
    {
        public int ResultItemId;
        public int MaterialItemId;
        public int MaterialQuantity = 10;
        public int BottleItemId = 1779;
        public int GoldCost = 50;
    }

    public partial class NPCInteractable
    {
        [SerializeField] NPCRecipe[] recipes = Array.Empty<NPCRecipe>();
        [SerializeField] int fullHealCost = -1;
        [SerializeField] bool noviceHealWaiver;
        public IReadOnlyList<NPCRecipe> Recipes => recipes ?? Array.Empty<NPCRecipe>();
        public int FullHealCost => fullHealCost;

        public int[] ShopItemIds() => (shopItems ?? Array.Empty<string>())
            .Select(id => int.TryParse(id, out int parsed) ? parsed : 0)
            .Where(id => PkoTables.Items.TryGetValue(id, out var item) && item.Price > 0 && item.Tradeable)
            .Distinct().ToArray();

        static int[] QuestIds(string[] ids) => (ids ?? Array.Empty<string>())
            .Select(id => int.TryParse(id, out int parsed) ? parsed : 0)
            .Where(QuestTable.All.ContainsKey).Distinct().ToArray();

        [TargetRpc]
        void TargetOpenDialogue(NetworkConnectionToClient connection, string[] lines, int[] items, int[] offers, int[] turnIns) =>
            TOP.UI.NPCDialogueUI.Open(this, lines, items, offers, turnIns);

        bool TryCustomer(NetworkConnectionToClient sender, out PlayerController controller, out PlayerInventory inventory)
        {
            controller = sender?.identity != null ? sender.identity.GetComponent<PlayerController>() : null;
            inventory = controller != null ? controller.GetComponent<PlayerInventory>() : null;
            if (inventory != null && inventory.RejectQuestMutation()) return false;
            var movement = controller != null ? controller.GetComponent<PlayerMovement>() : null;
            if (movement != null && movement.ActiveNpc == this && inventory != null && CanInteract(movement)) return true;
            controller?.RpcShowMessage("Interacao indisponivel. Fale com o NPC e mantenha-se perto dele, vivo e sem duelo/comercio.", PlayerMessageType.Warning);
            return false;
        }

        [Command(requiresAuthority = false)]
        public void CmdBuyItem(int itemId, int quantity, NetworkConnectionToClient sender = null)
        {
            if (!TryCustomer(sender, out var controller, out var inventory)) return;
            if (!ShopItemIds().Contains(itemId) || !PkoTables.Items.TryGetValue(itemId, out var item)
                || quantity <= 0 || quantity > item.Stack)
            {
                controller.RpcShowMessage("Item ou quantidade nao vendido por este NPC.", PlayerMessageType.Warning);
                return;
            }
            ulong price = (ulong)item.Price * (ulong)quantity;
            if (controller.Gold < price)
            { controller.RpcShowMessage("Ouro insuficiente.", PlayerMessageType.Warning); return; }
            int slot = inventory.FindEmptySlot();
            if (slot < 0)
            { controller.RpcShowMessage("Inventario cheio.", PlayerMessageType.Warning); return; }
            if (!inventory.AddItem(itemId, quantity, (ushort)slot))
            { controller.RpcShowMessage("Nao foi possivel adicionar a compra ao inventario.", PlayerMessageType.Warning); return; }
            controller.Gold -= price;
            controller.RpcShowMessage("Compra concluida: " + item.Name + " x" + quantity + ".", PlayerMessageType.Success);
        }

        [Command(requiresAuthority = false)]
        public void CmdSellItem(int slotIndex, int quantity, NetworkConnectionToClient sender = null)
        {
            if (!TryCustomer(sender, out var controller, out var inventory)) return;
            if (ShopItemIds().Length == 0)
            { controller.RpcShowMessage("Este NPC nao possui loja configurada.", PlayerMessageType.Warning); return; }
            var slot = inventory.GetSlot(slotIndex);
            if (slot == null || slot.IsEquipped || quantity <= 0 || quantity > slot.Quantity
                || !PkoTables.Items.TryGetValue(slot.ItemId, out var item) || !item.Tradeable || item.Price < 2)
            { controller.RpcShowMessage("Item ou quantidade indisponivel para venda.", PlayerMessageType.Warning); return; }
            if (slot.RefineLevel > 0 || slot.Gems.Any(gem => gem >= 0))
            { controller.RpcShowMessage("Venda bloqueada para proteger equipamento refinado ou com gemas.", PlayerMessageType.Warning); return; }
            ulong price = (ulong)(item.Price / 2) * (ulong)quantity;
            if (ulong.MaxValue - controller.Gold < price)
            { controller.RpcShowMessage("Limite de ouro excedido.", PlayerMessageType.Warning); return; }
            inventory.RemoveItem((ushort)slotIndex, quantity);
            controller.Gold += price;
            controller.RpcShowMessage("Venda concluida: " + item.Name + " x" + quantity + ".", PlayerMessageType.Success);
        }

        [Command(requiresAuthority = false)]
        public void CmdQuestAction(int questId, bool turnIn, NetworkConnectionToClient sender = null)
        {
            if (!TryCustomer(sender, out var controller, out _)) return;
            var quests = controller.GetComponent<PlayerQuests>();
            if (quests == null) { controller.RpcShowMessage("Sistema de missoes indisponivel.", PlayerMessageType.Warning); return; }
            if (turnIn && QuestIds(completesQuests).Contains(questId))
            {
                quests.ServerTurnInQuest(questId);
                return;
            }
            if (!turnIn && quests.GetOfferableQuestsFor(QuestIds(availableQuests)).Contains(questId))
            {
                quests.ServerOfferQuest(questId);
                return;
            }
            controller.RpcShowMessage("Missao nao disponivel neste NPC ou requisitos incompletos.", PlayerMessageType.Warning);
        }

        public bool OffersQuest(int id) => QuestIds(availableQuests).Contains(id);
        public bool ReceivesQuest(int id) => QuestIds(completesQuests).Contains(id);

        [Command(requiresAuthority = false)]
        public void CmdOpenForge(NetworkConnectionToClient sender = null)
        {
            if (!TryCustomer(sender, out var controller, out _)) return;
            if (npcType != TOP.Core.NPCType.Blacksmith)
            { controller.RpcShowMessage("Este NPC nao e um ferreiro.", PlayerMessageType.Warning); return; }
            controller.RpcOpenForge();
        }

        [Command(requiresAuthority = false)]
        public void CmdFullHeal(NetworkConnectionToClient sender = null)
        {
            if (!TryCustomer(sender, out var controller, out _)) return;
            if (fullHealCost < 0)
            { controller.RpcShowMessage("Servico de cura original nao configurado.", PlayerMessageType.Warning); return; }
            var stats = controller.GetComponent<PlayerStats>();
            if (stats.CurrentHp == stats.MaxHp && stats.CurrentMp == stats.MaxMp && stats.CurrentSp == stats.MaxSp)
            { controller.RpcShowMessage("Sua vida, mana e stamina ja estao completas.", PlayerMessageType.Info); return; }
            bool free = noviceHealWaiver && controller.Level < 6
                && (controller.GetComponent<PlayerQuests>()?.CompletedQuests.Contains(500) ?? false);
            ulong cost = free ? 0 : (ulong)fullHealCost;
            if (controller.Gold < cost)
            { controller.RpcShowMessage("Ouro insuficiente para cura completa.", PlayerMessageType.Warning); return; }
            stats.SetCurrentHpMpSp(stats.MaxHp, stats.MaxMp, stats.MaxSp);
            controller.Gold -= cost;
            controller.RpcShowMessage("Vida, mana e stamina restauradas.", PlayerMessageType.Success);
        }

        [Command(requiresAuthority = false)]
        public void CmdCraftRecipe(int resultItemId, NetworkConnectionToClient sender = null)
        {
            if (!TryCustomer(sender, out var controller, out var inventory)) return;
            var recipe = Recipes.FirstOrDefault(r => r != null && r.ResultItemId == resultItemId);
            if (recipe == null || recipe.MaterialQuantity <= 0 || recipe.GoldCost < 0
                || !PkoTables.Items.ContainsKey(recipe.ResultItemId))
            { controller.RpcShowMessage("Receita original nao configurada neste NPC.", PlayerMessageType.Warning); return; }
            if (controller.Gold < (ulong)recipe.GoldCost)
            { controller.RpcShowMessage("Ouro insuficiente para preparar a receita.", PlayerMessageType.Warning); return; }
            var snapshot = inventory.GetInventoryData();
            if (!PlayerInventory.TryConsumeMaterials(snapshot, recipe.BottleItemId, 1)
                || !PlayerInventory.TryConsumeMaterials(snapshot, recipe.MaterialItemId, recipe.MaterialQuantity))
            { controller.RpcShowMessage("Materiais insuficientes ou protegidos (equipados/refinados/com sockets).", PlayerMessageType.Warning); return; }
            int slot = 0;
            while (slot < inventory.totalSlots && snapshot.Any(item => item.SlotIndex == slot)) slot++;
            if (slot == inventory.totalSlots)
            { controller.RpcShowMessage("Inventario sem espaco para receber a receita.", PlayerMessageType.Warning); return; }
            var result = PkoTables.Items[recipe.ResultItemId];
            snapshot.Add(new InventoryItemData { SlotIndex = (ushort)slot, ItemId = result.Id, Quantity = 1,
                Durability = (ushort)Mathf.Clamp(result.Durability, 0, ushort.MaxValue) });
            inventory.InitializeFromData(snapshot);
            controller.Gold -= (ulong)recipe.GoldCost;
            controller.RpcShowMessage("Preparado: " + result.Name + ".", PlayerMessageType.Success);
        }

    }
}
