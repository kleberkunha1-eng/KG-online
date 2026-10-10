using System;
using System.Collections.Generic;
using TOP.Core;

namespace TOP.Data
{
    public struct OriginalDeathPenaltyResult
    {
        public bool Applied;
        public ulong ExperienceLost;
        public int ConsumedProtectionItem;
        public int EquipmentWorn;
        public bool LostStamina;
    }

    public static class OriginalDeathPenalty
    {
        static readonly HashSet<string> PlayerKillPenaltyMaps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "puzzleworld", "puzzleworld2", "abandonedcity", "abandonedcity2", "abandonedcity3", "darkswamp",
            "hell", "hell2", "hell3", "hell4", "hell5", "heilong"
        };
        static readonly HashSet<string> NoPenaltyMaps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "leiting2", "binglang2", "shalan2", "guildwar", "guildwar2"
        };
        static readonly HashSet<int> RepairableTypes = new HashSet<int>
        {
            1, 2, 3, 4, 7, 11, 20, 22, 23, 24, 27, 9, 25, 26, 81, 82, 83, 88
        };

        public static ulong CalculateExperienceLoss(int level, ulong experience, ulong nextLevelExperience)
        {
            if (level <= 10 || experience == 0 || nextLevelExperience == 0) return 0;
            ulong loss = nextLevelExperience / 50UL;
            return Math.Min(loss, experience);
        }

        public static OriginalDeathPenaltyResult Apply(CharacterData character, bool killedByPlayer, int serverHour)
        {
            var result = new OriginalDeathPenaltyResult();
            if (character == null) return result;

            bool night = serverHour <= 6 || serverHour >= 18;
            bool pirateSet = HasSet(character.Inventory, true);
            bool deathSet = HasSet(character.Inventory, false);

            if (killedByPlayer)
            {
                result.LostStamina = character.CurrentSp != 0;
                character.CurrentSp = 0;
                result.Applied = result.LostStamina;
                if (!PlayerKillPenaltyMaps.Contains(character.MapName ?? string.Empty)) return result;

                int pvpProtectionItem = FindSpecificProtectionItem(character.Inventory, 3846);
                if (pvpProtectionItem != 0 && ConsumeProtectionItem(character.Inventory, pvpProtectionItem))
                {
                    result.ConsumedProtectionItem = pvpProtectionItem;
                    result.Applied = true;
                    return result;
                }

                ulong progressLimit = Math.Min(PkoTables.ExpToNextLevel(character.Level) / 50UL, character.Exp);
                ulong loss = character.Exp == 0 ? 0 : (ulong)Math.Max(0, character.Level) * (ulong)Math.Max(0, character.Level) * 20UL;
                loss = Math.Min(loss, progressLimit);
                if (character.Level >= 80) loss /= 50UL;
                result.ExperienceLost = loss;
                character.Exp -= loss;
                result.EquipmentWorn = WearEquipment(character.Inventory);
                result.Applied = result.Applied || loss > 0 || result.EquipmentWorn > 0;
                return result;
            }
            if (NoPenaltyMaps.Contains(character.MapName ?? string.Empty)) return result;
            if (string.Equals(character.MapName, "garner2", StringComparison.OrdinalIgnoreCase))
            {
                result.LostStamina = character.CurrentSp != 0;
                character.CurrentSp = 0;
                result.Applied = result.LostStamina;
                return result;
            }
            if (character.Level <= 10 || AvoidNightPenalty(character.Level, night, pirateSet, deathSet)) return result;

            result.LostStamina = character.CurrentSp != 0;
            character.CurrentSp = 0;
            result.Applied = result.LostStamina;
            if (string.Equals(character.MapName, "secretgarden", StringComparison.OrdinalIgnoreCase)
                || string.Equals(character.MapName, "teampk", StringComparison.OrdinalIgnoreCase)) return result;

            int protectionItem = FindProtectionItem(character.Inventory);
            if (protectionItem != 0 && ConsumeProtectionItem(character.Inventory, protectionItem))
            {
                result.ConsumedProtectionItem = protectionItem;
                result.Applied = true;
                return result;
            }

            ulong nextLevelExperience = PkoTables.ExpToNextLevel(character.Level);
            result.ExperienceLost = CalculateExperienceLoss(character.Level, character.Exp, nextLevelExperience);
            character.Exp -= result.ExperienceLost;
            if (character.Level > 20)
                result.EquipmentWorn = WearEquipment(character.Inventory);
            result.Applied = result.Applied || result.ExperienceLost > 0 || result.EquipmentWorn > 0;
            return result;
        }

        static bool AvoidNightPenalty(int level, bool night, bool pirateSet, bool deathSet)
        {
            return night && ((level >= 70 && pirateSet) || (level >= 75 && deathSet));
        }

        static bool HasSet(List<InventoryItemData> inventory, bool pirate)
        {
            InventoryItemData body = FindEquipped(inventory, EquipmentSlot.Armor);
            InventoryItemData hands = FindEquipped(inventory, EquipmentSlot.Gloves);
            InventoryItemData feet = FindEquipped(inventory, EquipmentSlot.Boots);
            bool useOriginalIds = body == null || hands == null || feet == null
                || body.ItemId < 5000 || hands.ItemId < 5000 || feet.ItemId < 5000;
            int bodyId = useOriginalIds ? ItemId(body) : FusionId(body);
            int handsId = useOriginalIds ? ItemId(hands) : FusionId(hands);
            int feetId = useOriginalIds ? ItemId(feet) : FusionId(feet);
            if (pirate)
                return bodyId >= 2530 && bodyId <= 2545 && (bodyId - 2530) % 3 == 0
                    && handsId >= 2531 && handsId <= 2546 && (handsId - 2531) % 3 == 0
                    && feetId >= 2532 && feetId <= 2547 && (feetId - 2532) % 3 == 0
                    && ItemId(body) >= 5000 && ItemId(hands) >= 5000 && ItemId(feet) >= 5000;
            return bodyId >= 2817 && bodyId <= 2832 && (bodyId - 2817) % 3 == 0
                && handsId >= 2818 && handsId <= 2833 && (handsId - 2818) % 3 == 0
                && feetId >= 2819 && feetId <= 2834 && (feetId - 2819) % 3 == 0;
        }

        static InventoryItemData FindEquipped(List<InventoryItemData> inventory, EquipmentSlot slot)
        {
            if (inventory == null) return null;
            foreach (InventoryItemData item in inventory)
            {
                if (item == null || !item.IsEquipped || !PkoTables.Items.TryGetValue(item.ItemId, out PkoItem pkoItem)) continue;
                if (PkoTables.SlotOf(pkoItem) == slot) return item;
            }
            return null;
        }

        static int ItemId(InventoryItemData item) => item == null ? 0 : item.ItemId;
        static int FusionId(InventoryItemData item) => item == null ? 0 : item.FusionItemId;

        static int FindSpecificProtectionItem(List<InventoryItemData> inventory, int itemId)
        {
            if (inventory == null) return 0;
            foreach (InventoryItemData item in inventory)
                if (item != null && item.ItemId == itemId && item.Quantity > 0) return itemId;
            return 0;
        }
        static int FindProtectionItem(List<InventoryItemData> inventory)
        {
            if (inventory == null) return 0;
            int[] priority = { 3846, 3047, 5609 };
            foreach (int id in priority)
                foreach (InventoryItemData item in inventory)
                    if (item != null && item.ItemId == id && item.Quantity > 0) return id;
            return 0;
        }

        static bool ConsumeProtectionItem(List<InventoryItemData> inventory, int itemId)
        {
            for (int i = 0; i < inventory.Count; i++)
            {
                InventoryItemData item = inventory[i];
                if (item == null || item.ItemId != itemId || item.Quantity <= 0) continue;
                item.Quantity--;
                if (item.Quantity == 0) inventory.RemoveAt(i);
                return true;
            }
            return false;
        }

        static int WearEquipment(List<InventoryItemData> inventory)
        {
            if (inventory == null) return 0;
            int worn = 0;
            foreach (InventoryItemData item in inventory)
            {
                if (item == null || !item.IsEquipped || !PkoTables.Items.TryGetValue(item.ItemId, out PkoItem pkoItem)
                    || !RepairableTypes.Contains(pkoItem.Type) || pkoItem.Durability <= 0) continue;
                EquipmentSlot slot = PkoTables.SlotOf(pkoItem);
                if (slot != EquipmentSlot.Helmet && slot != EquipmentSlot.Armor && slot != EquipmentSlot.Gloves
                    && slot != EquipmentSlot.Boots && slot != EquipmentSlot.Shield && slot != EquipmentSlot.Weapon) continue;
                int maximum = pkoItem.Durability;
                int current = item.Durability == 0 ? maximum : item.Durability;
                int loss = maximum / 20;
                current -= loss;
                if (current < 50) current = 49;
                item.Durability = (ushort)Math.Max(0, Math.Min(ushort.MaxValue, current));
                worn++;
            }
            return worn;
        }
    }
}
