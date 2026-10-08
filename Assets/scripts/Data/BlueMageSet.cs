using System;
using System.Collections.Generic;
using TOP.Core;

namespace TOP.Data
{
    public static class BlueMageSet
    {
        public sealed class Piece
        {
            public readonly int Id, TemplateId, Type, OriginalSlot;
            public readonly string Key, Name;
            public readonly EquipmentSlot Slot;
            public Piece(int id, string key, string name, int template, int type, int originalSlot, EquipmentSlot slot)
            {
                Id = id; Key = key; Name = name; TemplateId = template;
                Type = type; OriginalSlot = originalSlot; Slot = slot;
            }
        }

        static readonly Piece[] pieces =
        {
            new Piece(990010, "helm", "Blue Mage Helm", 2202, 20, 0, EquipmentSlot.Helmet),
            new Piece(990011, "chestplate", "Blue Mage Chestplate", 365, 22, 2, EquipmentSlot.Armor),
            new Piece(990012, "gloves", "Blue Mage Gloves", 541, 23, 3, EquipmentSlot.Gloves),
            new Piece(990013, "pants", "Blue Mage Pants", 7300, 82, 12, EquipmentSlot.Belt),
            new Piece(990014, "boots", "Blue Mage Boots", 717, 24, 4, EquipmentSlot.Boots)
        };
        public static IReadOnlyList<Piece> Pieces => pieces;
        public static bool TryGet(int id, out Piece piece)
        {
            foreach (var candidate in pieces)
                if (candidate.Id == id) { piece = candidate; return true; }
            piece = null;
            return false;
        }

        public static string PartPath(int id, int race) => $"PkoChar/BlueMageSet/{id}_{race}";
        public static string IconPath(Piece piece) => "PKOUI/icon/bluemage_" + piece.Key;
        public static bool Covers(float y, float pelvis, int coverage) =>
            ((coverage & 1) != 0 && y >= pelvis) || ((coverage & 2) != 0 && y <= pelvis);

        public static void Register(Dictionary<int, PkoItem> items)
        {
            foreach (var piece in pieces)
            {
                if (items.ContainsKey(piece.Id))
                    throw new InvalidOperationException("Blue Mage item ID collision: " + piece.Id);
                if (!items.TryGetValue(piece.TemplateId, out var template))
                    throw new InvalidOperationException("Blue Mage equipment template missing: " + piece.TemplateId);
                bool pants = piece.Slot == EquipmentSlot.Belt;
                items.Add(piece.Id, new PkoItem
                {
                    Id = piece.Id, Name = piece.Name, Icon = "bluemage_" + piece.Key,
                    Model = "BlueMage_" + piece.Key, Type = piece.Type, EquipSlots = new[] { piece.OriginalSlot },
                    Level = 10, Stack = 1, Durability = template.Durability,
                    Price = template.Price, Tradeable = true,
                    Def = pants ? 0 : template.Def, Spr = pants ? 0 : template.Spr,
                    Con = pants ? 0 : template.Con, Hp = pants ? 0 : template.Hp,
                    Sp = pants ? 0 : template.Sp, Resist = pants ? 0 : template.Resist,
                    MaxSockets = template.MaxSockets,
                    Description = pants
                        ? "Blue Mage set trousers. Equipped in the belt slot; available to every race."
                        : "Enchanted blue mage armor. Available to every race."
                });
            }
        }
    }
}
