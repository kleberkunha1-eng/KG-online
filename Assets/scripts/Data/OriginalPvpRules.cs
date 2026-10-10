using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TOP.Data
{
    public static class OriginalPvpRules
    {
        struct MapPolicy
        {
            public int Type;
            public string Resource;

            public MapPolicy(int type, string resource)
            {
                Type = type;
                Resource = resource;
            }
        }

        static readonly Dictionary<string, MapPolicy> PvpMaps = new Dictionary<string, MapPolicy>(StringComparer.OrdinalIgnoreCase)
        {
            { "abandonedcity", new MapPolicy(4, "PKO/PKAreas/abandonedcity.atr") },
            { "abandonedcity2", new MapPolicy(4, "PKO/PKAreas/abandonedcity2.atr") },
            { "abandonedcity3", new MapPolicy(4, "PKO/PKAreas/abandonedcity3.atr") },
            { "darkswamp", new MapPolicy(4, "PKO/PKAreas/darkswamp.atr") },
            { "DreamIsland", new MapPolicy(4, "PKO/PKAreas/DreamIsland.atr") },
            { "garner2", new MapPolicy(3, "PKO/PKAreas/garner2.atr") },
            { "heilong", new MapPolicy(4, "PKO/PKAreas/heilong.atr") },
            { "hell", new MapPolicy(4, "PKO/PKAreas/hell.atr") },
            { "hell2", new MapPolicy(4, "PKO/PKAreas/hell2.atr") },
            { "hell3", new MapPolicy(4, "PKO/PKAreas/hell3.atr") },
            { "hell4", new MapPolicy(4, "PKO/PKAreas/hell4.atr") },
            { "hell5", new MapPolicy(4, "PKO/PKAreas/hell5.atr") },
            { "PKmap", new MapPolicy(4, "PKO/PKAreas/PKmap.atr") },
            { "prisonisland", new MapPolicy(4, "PKO/PKAreas/prisonisland.atr") },
            { "puzzleworld", new MapPolicy(4, "PKO/PKAreas/puzzleworld.atr") },
            { "puzzleworld2", new MapPolicy(4, "PKO/PKAreas/puzzleworld2.atr") },
            { "teampk", new MapPolicy(3, "PKO/teampk.atr") }
        };

        static readonly Dictionary<string, byte[]> AttributeCache = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

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

        public static bool IsPvpMap(string mapName) => PvpMaps.ContainsKey(mapName ?? string.Empty);

        public static bool RequiresGuildData(string mapName)
        {
            return PvpMaps.TryGetValue(mapName ?? string.Empty, out MapPolicy policy)
                && (policy.Type == 2 || policy.Type == 4);
        }

        public static bool CanFightInArea(int mapType, bool mapCanPk, ushort sourceArea, ushort targetArea,
            int sourceParty, int targetParty, long sourceGuild, long targetGuild, int sourceSide, int targetSide)
        {
            return mapCanPk && !IsSafe(sourceArea) && !IsSafe(targetArea)
                && IsLand(sourceArea) && IsLand(targetArea)
                && AreEnemies(mapType, sourceParty, targetParty, sourceGuild, targetGuild, sourceSide, targetSide);
        }

        public static bool CanFightInMap(string sourceMap, Vector3 sourcePosition, int sourceParty, long sourceGuild,
            bool sourceGuildKnown, int sourceSide, string targetMap, Vector3 targetPosition, int targetParty,
            long targetGuild, bool targetGuildKnown, int targetSide)
        {
            if (string.IsNullOrWhiteSpace(sourceMap) || !string.Equals(sourceMap, targetMap, StringComparison.OrdinalIgnoreCase)
                || !PvpMaps.TryGetValue(sourceMap, out MapPolicy policy)) return false;
            if ((policy.Type == 2 || policy.Type == 4) && (!sourceGuildKnown || !targetGuildKnown)) return false;
            return TryMapAttributes(sourceMap, sourcePosition, out ushort sourceArea)
                && TryMapAttributes(targetMap, targetPosition, out ushort targetArea)
                && CanFightInArea(policy.Type, true, sourceArea, targetArea, sourceParty, targetParty,
                    sourceGuild, targetGuild, sourceSide, targetSide);
        }

        public static bool CanFightInArena(string sourceMap, Vector3 sourcePosition, int sourceParty,
            string targetMap, Vector3 targetPosition, int targetParty)
        {
            return CanFightInMap(sourceMap, sourcePosition, sourceParty, 0, true, 0,
                targetMap, targetPosition, targetParty, 0, true, 0);
        }

        public static bool TryMapAttributes(string mapName, Vector3 position, out ushort attributes)
        {
            attributes = 0;
            if (!PvpMaps.TryGetValue(mapName ?? string.Empty, out MapPolicy policy)) return false;
            if (!float.IsFinite(position.x) || !float.IsFinite(position.z)) return false;
            if (!TryLoadAttributes(policy, out byte[] data)) return false;
            return TryReadAttributes(data, Mathf.FloorToInt(position.x + TOP.Player.WorldBlockGrid.OriginX),
                Mathf.FloorToInt(TOP.Player.WorldBlockGrid.OriginZ - position.z), out attributes);
        }

        public static bool TryArenaAttributes(Vector3 position, out ushort attributes) => TryMapAttributes("teampk", position, out attributes);

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

        static bool TryLoadAttributes(MapPolicy policy, out byte[] data)
        {
            if (AttributeCache.TryGetValue(policy.Resource, out data)) return true;
            TextAsset asset = Resources.Load<TextAsset>(policy.Resource);
            if (asset == null)
            {
                Debug.LogError("[PK] Original map attributes missing for " + policy.Resource + "; lethal combat blocked.");
                data = null;
                return false;
            }
            data = asset.bytes;
            AttributeCache[policy.Resource] = data;
            return true;
        }

        static uint ReadUInt32(byte[] data, int offset) => (uint)(data[offset] | data[offset + 1] << 8
            | data[offset + 2] << 16 | data[offset + 3] << 24);

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
    }
}
