using UnityEngine;
using Mirror;
using TOP.Data;
using TOP.Inventory;

namespace TOP.Player
{
    // Sistema de forja (forgeitem.txt do cliente original): aumenta o RefineLevel de um item do
    // inventario em +1 por tentativa, consumindo materiais e ouro fixos por nivel, com taxa de
    // sucesso decrescente. Em caso de falha o item cai para o "failed level" da tabela (podendo
    // ficar no mesmo nivel, cair um nivel, ou ser zerado no nivel 12). Diferente do refino usado
    // pelo painel admin (PkoGems/CmdAdminGenerate), que define o nivel livremente sem custo.
    public class PlayerForge : NetworkBehaviour
    {
        PlayerController _pc;
        PlayerInventory _inventory;

        void Awake()
        {
            _pc = GetComponent<PlayerController>();
            _inventory = GetComponent<PlayerInventory>();
        }

        public PlayerInventory Inventory => _inventory;
        public PlayerController Controller => _pc;

        [Command]
        public void CmdForgeItem(int slotIndex)
        {
            if (_inventory == null || _pc == null) return;
            var item = _inventory.GetSlot(slotIndex);
            if (item == null) { _pc.RpcShowMessage("Slot vazio.", PlayerMessageType.Warning); return; }
            if (item.IsEquipped) { _pc.RpcShowMessage("Desequipe o item antes de forjar.", PlayerMessageType.Warning); return; }

            int targetLevel = item.RefineLevel + 1;
            if (!ForgeTable.ByLevel.TryGetValue(targetLevel, out var def))
            { _pc.RpcShowMessage("Esse item ja esta no nivel maximo de forja.", PlayerMessageType.Warning); return; }

            if (!PkoTables.Items.TryGetValue(item.ItemId, out var pkoItem) || pkoItem.EquipSlots == null || pkoItem.EquipSlots.Length == 0)
            { _pc.RpcShowMessage("Esse item nao pode ser forjado.", PlayerMessageType.Warning); return; }

            if (_pc.Gold < (ulong)def.RequiredGold)
            { _pc.RpcShowMessage("Ouro insuficiente para forjar (" + def.RequiredGold + ").", PlayerMessageType.Warning); return; }

            foreach (var (matId, qty) in def.Requirements)
                if (matId > 0 && qty > 0 && _inventory.GetItemCount(matId) < qty)
                { _pc.RpcShowMessage("Faltam materiais para a forja.", PlayerMessageType.Warning); return; }

            // Tudo validado: consome ouro e materiais, depois rola o sucesso.
            if (!_pc.SpendGold((ulong)def.RequiredGold)) { _pc.RpcShowMessage("Ouro insuficiente.", PlayerMessageType.Warning); return; }
            foreach (var (matId, qty) in def.Requirements)
                if (matId > 0 && qty > 0) _inventory.RemoveItemById(matId, qty);

            bool success = Random.Range(0, 100) < def.SuccessRate;
            int newLevel = success ? def.Level : def.FailedLevel;
            _inventory.SetItemRefine(slotIndex, newLevel);

            string itemName = pkoItem.Name ?? "Item";
            _pc.RpcShowMessage(success
                ? $"Forja bem-sucedida! {itemName} agora e +{newLevel}."
                : $"A forja falhou. {itemName} ficou em +{newLevel}.",
                success ? PlayerMessageType.Success : PlayerMessageType.Warning);
        }
    }
}
