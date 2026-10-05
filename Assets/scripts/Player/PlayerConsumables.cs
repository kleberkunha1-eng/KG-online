using UnityEngine;
using Mirror;
using TOP.Core;
using TOP.Inventory;
using TOP.Systems;

namespace TOP.Player
{
    public class PlayerConsumables : NetworkBehaviour
    {
        private PlayerStats _stats;
        private PlayerInventory _inventory;

        void Awake()
        {
            _stats = GetComponent<PlayerStats>();
            _inventory = GetComponent<PlayerInventory>();
        }

        [Command]
        public void CmdUseItem(ushort slotIndex)
        {
            // Validação de segurança no Servidor
            if (_inventory == null) return;

            InventoryItem item = _inventory.GetItem(slotIndex);
            if (item == null) return;

            ItemData itemData = ItemDatabase.Instance?.GetItem(item.ItemId);
            if (itemData == null || itemData.itemType != ItemType.Consumable) return;

            ConsumableData consumable = itemData as ConsumableData;
            if (consumable == null) return;

            // Aplicação dos efeitos nos Stats
            if (consumable.restoreHP > 0)
                _stats.Heal(consumable.restoreHP);

            if (consumable.restoreMP > 0)
                _stats.RestoreMp(consumable.restoreMP);

            if (consumable.restoreSP > 0)
                _stats.RestoreSp(consumable.restoreSP);

            // Sistema de Buffs
            if (consumable.buffs != null && consumable.buffs.Length > 0)
            {
                BuffManager buffManager = Object.FindAnyObjectByType<BuffManager>();  
                if (buffManager != null)
                {
                    buffManager.ApplyBuff(netId, consumable.buffs[0].buffName, consumable.buffs[0].value, consumable.buffs[0].duration);
                }
            }

            // Consome o item (remove 1 unidade)
            _inventory.RemoveItem(slotIndex, 1);

            // Feedback Visual/UI
            PlayerController controller = GetComponent<PlayerController>();
            if (controller != null)
            {
                controller.RpcShowMessage($"Usou {itemData.itemName}", PlayerMessageType.Success);
            }
        }
    }
}