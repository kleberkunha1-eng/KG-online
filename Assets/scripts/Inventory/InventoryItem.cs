using System;
using Mirror;

namespace TOP.Inventory
{
    [Serializable]
    public class InventoryItem
    {
        // Propriedades PascalCase (usadas pelo sistema namespaced)
        public int ItemId;
        public int Quantity;
        public ushort SlotIndex;
        public int Durability;
        public int RefineLevel;
        public bool IsEquipped;
        // Sockets: -1 = sem socket, 0 = socket vazio, >0 = id do item da gema.
        public int[] Gems = { -1, -1, -1 };

        public int SocketCount { get { int n = 0; foreach (var g in Gems) if (g >= 0) n++; return n; } }

        // Aliases camelCase para compatibilidade com codigo legado
        public int itemId { get => ItemId; set => ItemId = value; }
        public int quantity { get => Quantity; set => Quantity = value; }
        public int durability { get => Durability; set => Durability = value; }
        public int refineLevel { get => RefineLevel; set => RefineLevel = value; }

        public bool IsEmpty => ItemId == 0;

        public static InventoryItem Empty => new InventoryItem { ItemId = 0, Quantity = 0, Durability = -1, RefineLevel = 0 };
    }
}
