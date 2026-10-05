// Assets/Scripts/Data/ItemDatabase.cs
using UnityEngine;
using TOP.Core;
using System.Collections.Generic;

namespace TOP.Inventory
{

    public class ItemDatabase : MonoBehaviour
    {
        [Header("Configuração")]
        [SerializeField] private string itemsResourcePath = "Items";

        private static ItemDatabase _instance;
        public static ItemDatabase Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<ItemDatabase>();
                    if (_instance == null && Application.isPlaying)
                    {
                        var go = new GameObject("ItemDatabase");
                        _instance = go.AddComponent<ItemDatabase>();
                    }
                }
                return _instance;
            }
        }

        private Dictionary<int, ItemData> itemsById = new Dictionary<int, ItemData>();
        private Dictionary<int, EquipmentData> equipmentById = new Dictionary<int, EquipmentData>();
        private bool isInitialized = false;

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            Initialize();
        }

        public void Initialize()
        {
            if (isInitialized) return;
            LoadItemsFromResources();
            foreach (var pko in TOP.Data.PkoTables.Items.Values)
            {
                if (pko.Id <= 0 || itemsById.ContainsKey(pko.Id)) continue;
                var data = TOP.Data.PkoTables.ToItemData(pko);
                itemsById.Add(pko.Id, data);
                if (data is EquipmentData eq) equipmentById.Add(pko.Id, eq);
            }
            isInitialized = true;
            Debug.Log($"[ItemDatabase] Inicializado com {itemsById.Count} items ({equipmentById.Count} equipamentos)");
        }

        void LoadItemsFromResources()
        {
            ItemData[] allItems = Resources.LoadAll<ItemData>(itemsResourcePath);
            foreach (var item in allItems)
            {
                if (item == null || item.itemId <= 0)
                {
                    Debug.LogWarning($"[ItemDatabase] Item inválido encontrado em Resources/{itemsResourcePath}");
                    continue;
                }
                if (!itemsById.ContainsKey(item.itemId))
                {
                    itemsById.Add(item.itemId, item);
                    if (item is EquipmentData equip)
                        equipmentById.Add(item.itemId, equip);
                }
                else
                {
                    Debug.LogWarning($"[ItemDatabase] ID duplicado: {item.itemId}");
                }
            }
        }

        public ItemData GetItem(int itemId)
        {
            if (itemId <= 0) return null;
            if (!isInitialized) Initialize();
            return itemsById.TryGetValue(itemId, out var item) ? item : null;
        }

        public EquipmentData GetEquipment(int itemId)
        {
            if (itemId <= 0) return null;
            if (!isInitialized) Initialize();
            return equipmentById.TryGetValue(itemId, out var item) ? item : null;
        }

        public Sprite GetIcon(int itemId)
        {
            return GetItem(itemId)?.icon;
        }

        public string GetItemName(int itemId)
        {
            return GetItem(itemId)?.itemName ?? "Desconhecido";
        }

        public IReadOnlyCollection<ItemData> GetAllItems()
        {
            if (!isInitialized) Initialize();
            return itemsById.Values;
        }

        public IReadOnlyCollection<EquipmentData> GetAllEquipment()
        {
            if (!isInitialized) Initialize();
            return equipmentById.Values;
        }

        public bool TryGetItem(int itemId, out ItemData item)
        {
            if (!isInitialized) Initialize();
            return itemsById.TryGetValue(itemId, out item);
        }

        // ========== NOVOS MÉTODOS PARA PLAYERCONTROLLER ==========

        /// <summary>
        /// Retorna o slot de equipamento para um item.
        /// 0=Helmet, 1=Armor, 2=Weapon, 3=Shield, 4=Gloves, 5=Boots, 
        /// 6=Cape, 7=Belt, 255=Não equipável
        /// </summary>
        public byte GetEquipSlot(int itemId)
        {
            var item = GetItem(itemId);
            if (item == null) return 255;

            if (item is EquipmentData equip)
            {
                return equip.slot switch
                {
                    EquipmentSlot.Helmet => 0,
                    EquipmentSlot.Armor => 1,
                    EquipmentSlot.Weapon => 2,
                    EquipmentSlot.Shield => 3,
                    EquipmentSlot.Gloves => 4,
                    EquipmentSlot.Boots => 5,
                    EquipmentSlot.Cape => 6,
                    EquipmentSlot.Belt => 7,
                    _ => 255
                };
            }

            return 255;
        }

        /// <summary>
        /// Verifica se o item é stackável.
        /// </summary>
        public bool IsStackable(int itemId)
        {
            var item = GetItem(itemId);
            return item?.IsStackable ?? false;
        }

        /// <summary>
        /// Retorna quantidade máxima por stack.
        /// </summary>
        public int GetMaxStack(int itemId)
        {
            var item = GetItem(itemId);
            return item?.maxStack ?? 1;
        }

        /// <summary>
        /// Retorna stats do item para cálculos de equipamento.
        /// </summary>
        public ItemStats GetItemStats(int itemId)
        {
            var item = GetItem(itemId);
            if (item == null) return new ItemStats();

            var stats = new ItemStats();

            if (item is EquipmentData equip)
            {
                stats.Str = equip.bonusSTR;
                stats.Agi = equip.bonusAGI;
                stats.Spr = equip.bonusINT; // INT no seu código = SPR no meu
                stats.Hp = equip.bonusHP;
                stats.Mp = equip.bonusMP;
                stats.Defense = equip.bonusDefense;
                stats.MinDamage = equip.bonusAttack;
                stats.MaxDamage = equip.bonusMagicAttack;
            }

            return stats;
        }
    }

    [System.Serializable]
    public class ItemStats
    {
        public int Str;
        public int Agi;
        public int Con; // Não existe no seu, mas mantido para compatibilidade
        public int Spr; // Mapeado para INT
        public int Hp;
        public int Mp;
        public int Defense;
        public int MinDamage;
        public int MaxDamage;
    }
}
