using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TOP.Character
{
    public static class PkoWingPose
    {
        [Serializable]
        public class Entry
        {
            public int race;
            public int itemId;
            public Vector3 position;
            public Vector3 euler;
            public float scale = 1f;
        }

        public const float MinScale = .1f, MaxScale = 150000f;
        [Serializable] class Store
        {
            public int version;
            public List<Entry> entries = new List<Entry>();
        }
        static Store store = new Store { version = 1 };
        static bool loaded;
        public static string UserPath => Path.Combine(Application.persistentDataPath, "WingPose.json");

        public static Entry Get(int race, int itemId)
        {
            Load();
            var entry = store.entries.Find(e => e.race == race && e.itemId == itemId);
            if (entry != null) return entry;
            entry = new Entry { race = race, itemId = itemId };
            store.entries.Add(entry);
            return entry;
        }

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            try
            {
                var resource = Resources.Load<TextAsset>("PkoChar/WingPose");
                string json = File.Exists(UserPath) ? File.ReadAllText(UserPath) : resource != null ? resource.text : null;
                if (json == null) return;
                var data = Parse(json);
                store = data;
            }
            catch (Exception e) { Debug.LogError("[PkoWingPose] Cannot load adjustments: " + e); }
        }

        static Store Parse(string json)
        {
            var data = JsonUtility.FromJson<Store>(json);
            if (data == null || data.entries == null || data.entries.Exists(e => e == null
                || e.race < 0 || e.race >= PkoCharacterVisual.Races || e.itemId <= 0
                || !Finite(e.position) || !Finite(e.euler)))
                throw new InvalidDataException("Invalid wing pose settings.");
            if (data.version == 0)
                foreach (var entry in data.entries) entry.scale = 1f;
            else if (data.version != 1)
                throw new InvalidDataException("Unsupported wing pose settings version.");
            if (data.entries.Exists(e => !float.IsFinite(e.scale) || e.scale < MinScale || e.scale > MaxScale))
                throw new InvalidDataException("Invalid wing scale settings.");
            data.version = 1;
            return data;
        }

        static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

        public static string ToJson()
        {
            Load();
            return JsonUtility.ToJson(store, true);
        }

        public static string Save()
        {
            string json = ToJson();
            Directory.CreateDirectory(Application.persistentDataPath);
            File.WriteAllText(UserPath, json);
#if UNITY_EDITOR
            string projectPath = Path.Combine(Application.dataPath, "Resources", "PkoChar", "WingPose.json");
            File.WriteAllText(projectPath, json);
            return projectPath;
#else
            return UserPath;
#endif
        }
    }
}
