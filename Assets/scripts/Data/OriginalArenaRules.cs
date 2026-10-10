using System;
using System.Collections.Generic;
using System.Linq;
using TOP.Inventory;
using UnityEngine;

namespace TOP.Data
{
    public static class OriginalArenaRules
    {
        public const int MedalId = 3849, Copies = 20, CloseSeconds = 11;
        public static readonly Vector3 ArgentBar = new Vector3(2207 - TOP.Player.WorldBlockGrid.OriginX, 2, TOP.Player.WorldBlockGrid.OriginZ - 2887);
        public static bool CanObtain(int level, ulong gold, int medalCount, int emptySlot) => level > 25 && gold >= 50000 && medalCount == 0 && emptySlot >= 0;
        public static bool UniqueMedal(IEnumerable<InventoryItem> items, out InventoryItem medal)
        {
            var medals = items.Where(i => i != null && i.ItemId == MedalId).ToArray();
            medal = medals.Length == 1 ? medals[0] : null;
            return medal != null && medal.Quantity == 1 && !medal.IsEquipped;
        }
        public static bool Eligible(IEnumerable<InventoryItem> items, out InventoryItem medal)
            => UniqueMedal(items, out medal) && medal.MedalHonor >= -300 && medal.MedalHonor <= 30000;
        public static int KillHonor(int attackerLevel, int targetLevel)
        {
            int difference = attackerLevel - targetLevel;
            return difference < -5 ? 2 : difference > -5 && difference < 10 ? 1 : 0;
        }
        public static int HonorChange(bool won, bool party, int ownCount, int enemyCount, int ownAverage, int enemyAverage)
        {
            int reward = 2 * (party && !won ? ownCount : enemyCount);
            int difference = ownAverage - enemyAverage;
            int amount = reward;
            if (difference > 0)
                amount = won ? reward / ((difference + 10) / 10) : reward * Math.Min(3, (difference + 10) / 10);
            else if (difference < 0)
            {
                int divisor = (int)Math.Floor((difference - 10) / 10d);
                amount = won ? reward * -Math.Max(-3, divisor) : (int)Math.Floor(reward * -1d / divisor);
            }
            return won ? amount : -amount;
        }
    }
}
