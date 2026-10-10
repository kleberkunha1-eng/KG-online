using System;
using System.Collections.Generic;
using UnityEngine;
namespace TOP.Data
{
    public static class OriginalSkillParameters
    {
        [Serializable] class Catalog { public Entry[] Entries; }
        [Serializable] public class Entry { public int Id; public int[] Costs, Cooldowns; }
        static Dictionary<int, Entry> entries;
        static void Load()
        {
            if (entries != null) return;
            entries = new Dictionary<int, Entry>();
            var asset = Resources.Load<TextAsset>("PKO/OriginalSkillParameters");
            if (asset == null) return;
            var catalog = JsonUtility.FromJson<Catalog>(asset.text);
            foreach (var entry in catalog.Entries) entries[entry.Id] = entry;
        }
        public static int Cost(int id, int level, int fallback) { Load(); return entries.TryGetValue(id, out var e) && e.Costs != null && e.Costs.Length > 0 ? e.Costs[Mathf.Clamp(level, 0, e.Costs.Length - 1)] : fallback; }
        public static float Cooldown(int id, int level, float fallback) { Load(); return entries.TryGetValue(id, out var e) && e.Cooldowns != null && e.Cooldowns.Length > 0 ? e.Cooldowns[Mathf.Clamp(level, 0, e.Cooldowns.Length - 1)] / 1000f : fallback; }
    }
}
