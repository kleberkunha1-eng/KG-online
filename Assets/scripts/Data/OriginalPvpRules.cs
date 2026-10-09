using UnityEngine;
using System.Linq;

namespace TOP.Data
{
    public static class OriginalPvpRules
    {
        public static bool AreEnemies(int mapType, int attackerParty, int targetParty, long attackerGuild,
            long targetGuild, int attackerSide, int targetSide)
        {
            bool sameParty = attackerParty != 0 && attackerParty == targetParty;
            bool sameGuild = attackerGuild != 0 && attackerGuild == targetGuild;
            switch (mapType)
            {
                case 2: return !sameGuild;
                case 3: return !sameParty;
                case 4: return !sameParty && !sameGuild;
                case 5: return attackerSide != targetSide;
                default: return false;
            }
        }

        public static bool IsSafe(ushort attributes) => (attributes & 2) != 0;
        public static bool IsLand(ushort attributes) => (attributes & 1) != 0 || (attributes & 8) != 0;

        public static bool ArenaLosesStamina(int level, int serverHour, int[] equippedItems)
        {
            if (level <= 10) return false;
            bool night = serverHour <= 6 || serverHour >= 18;
            bool deathSet = equippedItems != null
                && equippedItems.Any(id => id >= 2817 && id <= 2832 && (id - 2817) % 3 == 0)
                && equippedItems.Any(id => id >= 2818 && id <= 2833 && (id - 2818) % 3 == 0)
                && equippedItems.Any(id => id >= 2819 && id <= 2834 && (id - 2819) % 3 == 0);
            return !(level >= 75 && night && deathSet);
        }

        public static bool TryReadAttributes(byte[] data, int x, int y, out ushort attributes)
        {
            attributes = 0;
            if (data == null || data.Length < 8 || x < 0 || y < 0) return false;
            uint width = ReadUInt32(data, 0), height = ReadUInt32(data, 4);
            if (width < 2 || height < 2 || width > (data.LongLength - 8) / 3
                || height > (data.LongLength - 8) / 3 || 8L + 3L * width * height != data.LongLength
                || x >= width - 1 || y >= height - 1) return false;
            int offset = checked((int)(8L + 3L * (y * (long)width + x)));
            attributes = (ushort)(data[offset] | data[offset + 1] << 8);
            return true;
        }

        static uint ReadUInt32(byte[] data, int offset) => (uint)(data[offset] | data[offset + 1] << 8
            | data[offset + 2] << 16 | data[offset + 3] << 24);

        static byte[] arenaAttributes;
        public static bool CanFightInArena(string sourceMap, Vector3 sourcePosition, int sourceParty,
            string targetMap, Vector3 targetPosition, int targetParty)
        {
            return sourceMap == "teampk" && targetMap == sourceMap
                && TryArenaAttributes(sourcePosition, out ushort sourceArea)
                && TryArenaAttributes(targetPosition, out ushort targetArea)
                && !IsSafe(sourceArea) && !IsSafe(targetArea) && IsLand(sourceArea) && IsLand(targetArea)
                && AreEnemies(3, sourceParty, targetParty, 0, 0, 0, 0);
        }

        public static bool TryArenaAttributes(Vector3 position, out ushort attributes)
        {
            if (arenaAttributes == null)
            {
                var asset = Resources.Load<TextAsset>("PKO/teampk.atr");
                if (asset == null)
                {
                    Debug.LogError("[PK] Original arena attributes missing; lethal combat blocked.");
                    attributes = 0;
                    return false;
                }
                arenaAttributes = asset.bytes;
            }
            if (!float.IsFinite(position.x) || !float.IsFinite(position.z)) { attributes = 0; return false; }
            return TryReadAttributes(arenaAttributes, Mathf.FloorToInt(position.x + TOP.Player.WorldBlockGrid.OriginX),
                Mathf.FloorToInt(TOP.Player.WorldBlockGrid.OriginZ - position.z), out attributes);
        }
    }
}
