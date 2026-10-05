using UnityEngine;
using Mirror;
using System.Collections.Generic;
using System.Linq;
using TOP.Core;
using TOP.Inventory;


namespace TOP.Systems
{
    public class WorldItemManager : NetworkBehaviour
    {
        public static WorldItemManager Instance { get; private set; }
        
        [Header("Config")]
        [SerializeField] private GameObject worldItemPrefab;
        [SerializeField] private float defaultDespawnTime = 300f;
        [SerializeField] private int maxGroundItems = 500;
        
        private readonly Dictionary<long, WorldItemEntry> _groundItems = new Dictionary<long, WorldItemEntry>();
        private long _nextWorldItemId = 1;

        public override void OnStartServer()
        {
            base.OnStartServer();
            Instance = this;
            Debug.Log("[WorldItemManager] ✅ Servidor iniciado!");
        }

        // ============================================================
        // SPAWN ITEMS
        // ============================================================

        [Server]
        public long SpawnDroppedItem(int itemId, int quantity, Vector3 position)
        {
            if (_groundItems.Count >= maxGroundItems)
                RemoveOldestItem();

            long worldItemId = _nextWorldItemId++;

            GameObject obj = Instantiate(worldItemPrefab, position, Quaternion.identity);
            obj.name = $"WorldItem_{worldItemId}_{itemId}";

            WorldItem worldItem = obj.GetComponent<WorldItem>();
            ItemData itemData = ItemDatabase.Instance?.GetItem(itemId);
            
            if (itemData != null && worldItem != null)
            {
                worldItem.Initialize(itemData, quantity, defaultDespawnTime);
            }

            NetworkServer.Spawn(obj);

            var entry = new WorldItemEntry
            {
                Id = worldItemId,
                ItemId = itemId,
                Quantity = quantity,
                Position = position,
                SpawnTime = Time.time,
                DespawnTime = Time.time + defaultDespawnTime,
                GameObject = obj
            };

            _groundItems[worldItemId] = entry;
            Debug.Log($"[WorldItemManager] Spawned {itemId}x{quantity} (ID:{worldItemId})");

            return worldItemId;
        }

        [Server]
        public long SpawnWorldItem(int itemId, int quantity, Vector3 position)
        {
            return SpawnDroppedItem(itemId, quantity, position);
        }

        // ============================================================
        // PICKUP
        // ============================================================

        [Server]
        public bool RemoveWorldItem(long worldItemId)
        {
            if (!_groundItems.TryGetValue(worldItemId, out var entry)) return false;

            if (entry.GameObject != null)
                NetworkServer.Destroy(entry.GameObject);

            _groundItems.Remove(worldItemId);
            Debug.Log($"[WorldItemManager] Removed item {worldItemId}");
            return true;
        }

        // ============================================================
        // CLEANUP
        // ============================================================

        void Update()
        {
            if (!isServer) return;

            float now = Time.time;
            var toRemove = _groundItems.Where(kvp => now >= kvp.Value.DespawnTime).Select(kvp => kvp.Key).ToList();

            foreach (var id in toRemove)
                RemoveWorldItem(id);
        }

        void RemoveOldestItem()
        {
            var oldest = _groundItems.OrderBy(kvp => kvp.Value.SpawnTime).FirstOrDefault();
            if (oldest.Key != 0)
                RemoveWorldItem(oldest.Key);
        }

        // ============================================================
        // TEST SPAWNER
        // ============================================================

        [ContextMenu("Spawn Test Item")]
        [Server]
        public void SpawnTestItem()
        {
            Vector3 pos = transform.position + Random.insideUnitSphere * 5f + Vector3.up;
            SpawnDroppedItem(1001, 1, pos);  // Health Potion
        }
    }

    [System.Serializable]
    public class WorldItemEntry
    {
        public long Id;
        public int ItemId;
        public int Quantity;
        public Vector3 Position;
        public float SpawnTime;
        public float DespawnTime;
        public GameObject GameObject;
    }
}