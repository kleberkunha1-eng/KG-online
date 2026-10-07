using UnityEngine;
using Mirror;
using TOP.Player;
using TOP.Core;
using TOP.Inventory;

namespace TOP.Inventory
{

    [RequireComponent(typeof(PlayerInventory))]
    [RequireComponent(typeof(PlayerEquipment))]
    public class InventorySystem : NetworkBehaviour
    {
        [Header("Config")]
        [SerializeField] private int inventorySlots = 48;

        [Header("Debug")]
        [SerializeField] private bool showDebugLogs = true;

        private PlayerInventory playerInventory;
        private PlayerEquipment playerEquipment;

        public event System.Action OnInventoryChanged;

        void Awake()
        {
            playerInventory = GetComponent<PlayerInventory>();
            playerEquipment = GetComponent<PlayerEquipment>();
        }

        void Start()
        {
            playerInventory.OnSlotChanged += (index, slot) => OnInventoryChanged?.Invoke();
            playerInventory.OnItemAdded += (item, qty) => OnInventoryChanged?.Invoke();
            playerInventory.OnItemRemoved += (item, qty) => OnInventoryChanged?.Invoke();
        }

        [Command]
        public void CmdEquipFromInventory(ushort inventoryIndex)
        {
            if (playerInventory == null || playerEquipment == null || inventoryIndex >= playerInventory.totalSlots) return;
            var item = playerInventory.GetSlot(inventoryIndex);
            var data = item != null ? ItemDatabase.Instance?.GetEquipment(item.ItemId) : null;
            if (data != null) playerEquipment.EquipFromInventory(inventoryIndex, data.slot);
        }

        [Command]
        public void CmdUnequipToInventory(EquipmentSlot slot)
        {
            playerEquipment?.UnequipItem(slot);
        }

        [Command]
        public void CmdMoveItem(int fromIndex, int toIndex)
        {
            if (playerInventory == null || fromIndex < 0 || toIndex < 0 || fromIndex >= playerInventory.totalSlots || toIndex >= playerInventory.totalSlots) return;
            playerInventory.MoveItemOnServer((ushort)fromIndex, (ushort)toIndex);
        }

        [Command]
        public void CmdDragEquipToSlot(ushort inventoryIndex, EquipmentSlot targetSlot)
        {
            playerEquipment?.EquipFromInventory(inventoryIndex, targetSlot);
        }

        [Command]
        public void CmdDragUnequipToSlot(EquipmentSlot slot, ushort targetInventoryIndex)
        {
            playerInventory?.UnequipItemToSlotOnServer(slot, targetInventoryIndex);
        }

        public int InventorySlots => playerInventory.totalSlots;
    }
}
