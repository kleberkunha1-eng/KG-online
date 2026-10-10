using System;
using System.Linq;
using UnityEngine;
using Mirror;
using TOP.Data;
using TOP.Inventory;
namespace TOP.Player
{
    public class PlayerForge : NetworkBehaviour
    {
        PlayerController _pc;
        PlayerInventory _inventory;
        void Awake() { _pc = GetComponent<PlayerController>(); _inventory = GetComponent<PlayerInventory>(); }
        public PlayerInventory Inventory => _inventory;
        public PlayerController Controller => _pc;
        void RequireGoldie()
        {
            var movement = GetComponent<PlayerMovement>();
            var npc = movement != null ? movement.ActiveNpc : null;
            if (npc == null || npc.NpcName.IndexOf("Goldie", StringComparison.OrdinalIgnoreCase) < 0 || !npc.CanInteract(movement)) throw new InvalidOperationException("Fale com Blacksmith Goldie para usar a forja.");
        }
        public static InventoryItemData Available(CharacterData data, int slot)
        {
            var item = data.Inventory.Find(i => i.SlotIndex == slot);
            if (item == null || item.IsEquipped || item.IsLocked || (item.OwnerCharacterId.HasValue && item.OwnerCharacterId.Value >= 0 && item.OwnerCharacterId.Value != data.Id)) throw new InvalidOperationException("Item indisponivel.");
            return item;
        }
        public static void Consume(CharacterData data, int id, int quantity, params int[] exclude)
        {
            var items = data.Inventory.Where(i => i.ItemId == id && !i.IsEquipped && !i.IsLocked && i.RefineLevel == 0 && !exclude.Contains(i.SlotIndex) && (!i.OwnerCharacterId.HasValue || i.OwnerCharacterId.Value < 0 || i.OwnerCharacterId.Value == data.Id)).ToArray();
            if (items.Sum(i => i.Quantity) < quantity) throw new InvalidOperationException("Faltam materiais: " + id);
            foreach (var item in items) { int n = Math.Min(quantity, item.Quantity); item.Quantity -= n; quantity -= n; if (item.Quantity == 0) data.Inventory.Remove(item); if (quantity == 0) break; }
        }
        public static void Charge(CharacterData data, ulong cost) { if (data.Gold < cost) throw new InvalidOperationException("Ouro insuficiente."); data.Gold -= cost; }
        [Command]
        public async void CmdForgeItem(int slotIndex)
        {
            string result = "";
            bool saved = await PlayerDurableAction.Run(_pc, data =>
            {
                RequireGoldie();
                var item = Available(data, slotIndex);
                if (!PkoTables.Items.TryGetValue(item.FusionItemId > 0 ? item.FusionItemId : item.ItemId, out var definition) || definition.EquipSlots.Length == 0 || definition.Type == 59 || PkoTables.IsApparel(definition)) throw new InvalidOperationException("Item nao forjavel.");
                if (!ForgeTable.ByLevel.TryGetValue(item.RefineLevel + 1, out var rule)) throw new InvalidOperationException("Nivel maximo.");
                Charge(data, (ulong)rule.RequiredGold);
                foreach (var material in rule.Requirements) Consume(data, material.itemId, material.qty, slotIndex);
                bool success = UnityEngine.Random.Range(0, 100) < rule.SuccessRate;
                item.RefineLevel = success ? rule.Level : rule.FailedLevel;
                result = success ? "Forja bem-sucedida: +" + item.RefineLevel : "Forja falhou: +" + item.RefineLevel;
            });
            if (saved && _pc != null) _pc.RpcShowMessage(result, PlayerMessageType.Info);
        }
        [Command]
        public async void CmdFuseItem(int apparelSlot, int equipmentSlot)
        {
            bool saved = await PlayerDurableAction.Run(_pc, data =>
            {
                RequireGoldie();
                if (apparelSlot == equipmentSlot) throw new InvalidOperationException("Escolha dois itens distintos.");
                var apparel = Available(data, apparelSlot); var equipment = Available(data, equipmentSlot);
                if (!PkoTables.Items.TryGetValue(apparel.ItemId, out var a) || !PkoTables.Items.TryGetValue(equipment.FusionItemId > 0 ? equipment.FusionItemId : equipment.ItemId, out var e) || !PkoTables.IsApparel(a) || PkoTables.IsApparel(e) || a.Type != e.Type || apparel.Durability < a.Durability || (a.OriginalMaxDurability != 23000 && a.OriginalMaxDurability != 25000) || apparel.FusionItemId != 0 || a.Type == 27) throw new InvalidOperationException("Apparel completo e equipamento compativel exigidos.");
                Consume(data, 453, 1, apparelSlot, equipmentSlot);
                if (equipment.RefineLevel > 0 || equipment.GemSlot1 >= 0 || equipment.GemSlot2 >= 0 || equipment.GemSlot3 >= 0) Consume(data, 454, 1, apparelSlot, equipmentSlot);
                Charge(data, (ulong)Math.Max(1, e.Level) * 1000);
                apparel.FusionItemId = equipment.FusionItemId > 0 ? equipment.FusionItemId : equipment.ItemId;
                apparel.Durability = 500;
                apparel.RefineLevel = equipment.RefineLevel;
                apparel.GemSlot1 = equipment.GemSlot1; apparel.GemSlot2 = equipment.GemSlot2; apparel.GemSlot3 = equipment.GemSlot3;
                data.Inventory.Remove(equipment);
            });
            if (saved && _pc != null) _pc.RpcShowMessage("Fusion concluida.", PlayerMessageType.Success);
        }
        public static int CombineChance(int level, int type) => type == 49 ? Math.Max(0, 110 - 10 * level) : level <= 3 ? 100 : Math.Max(0, 115 - 5 * level);
        [Command]
        public async void CmdCombineGems(int leftSlot, int rightSlot, int scrollSlot)
        {
            bool success = false;
            bool saved = await PlayerDurableAction.Run(_pc, data =>
            {
                RequireGoldie();
                if (leftSlot == rightSlot || scrollSlot == leftSlot || scrollSlot == rightSlot) throw new InvalidOperationException("Slots devem ser distintos.");
                var left = Available(data, leftSlot); var right = Available(data, rightSlot); var scroll = Available(data, scrollSlot);
                if (!PkoTables.Items.TryGetValue(left.ItemId, out var gem) || (gem.Type != 49 && gem.Type != 50) || left.ItemId != right.ItemId || left.RefineLevel != right.RefineLevel || left.Quantity != 1 || right.Quantity != 1 || !PkoTables.Items.TryGetValue(scroll.ItemId, out var book) || book.Type != 47) throw new InvalidOperationException("Duas gemas identicas e Combining Scroll exigidos.");
                int level = Math.Max(1, left.RefineLevel);
                if (level >= 9) throw new InvalidOperationException("Limite de combinacao do core: nivel 9.");
                Charge(data, 5000);
                if (--scroll.Quantity == 0) data.Inventory.Remove(scroll);
                data.Inventory.Remove(right);
                success = UnityEngine.Random.Range(0, 100) < CombineChance(level, gem.Type);
                if (success) left.RefineLevel = level + 1; else data.Inventory.Remove(left);
            });
            if (saved && _pc != null) _pc.RpcShowMessage(success ? "Gema combinada." : "Combinacao falhou; gemas consumidas.", PlayerMessageType.Info);
        }
    }
}
