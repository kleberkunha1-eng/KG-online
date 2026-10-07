using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections.Generic;
using TOP.Core;
using TOP.Player;
using TOP.Inventory;

namespace TOP.Inventory
{
    public class InventoryUI : MonoBehaviour
    {
        public static InventoryUI Instance { get; private set; }

        [Header("Player Reference")]
        [SerializeField] public PlayerInventory playerInventory;
        [SerializeField] private PlayerEquipment playerEquipment;

        [Header("Panels")]
        [SerializeField] private GameObject inventoryPanel;
        [SerializeField] private Transform inventoryGrid;
        [SerializeField] private Transform equipmentPanel;

        [Header("Tooltip")]
        [SerializeField] private GameObject tooltipPanel;
        [SerializeField] private TextMeshProUGUI tooltipName;
        [SerializeField] private TextMeshProUGUI tooltipStats;

        [Header("Config")]
        [SerializeField] private KeyCode toggleKey = KeyCode.I;

        [Header("Drag Canvas")]
        [SerializeField] private Canvas dragCanvas;
        public Canvas DragCanvas => dragCanvas;

        [Header("Drag Handle")]
        [SerializeField] private Transform dragHandle;
        private bool isPanelDragging = false;
        private Vector2 dragOffset;
        private RectTransform panelRectTransform;

        private ItemSlotUI[] inventorySlots;
        private Dictionary<EquipmentSlot, EquipmentSlotUI> equipmentSlots = new Dictionary<EquipmentSlot, EquipmentSlotUI>();
        private int selectedSlot = -1;
        private bool isSetup = false;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        void Start()
        {
            // ✅ NÃO busca mais o player aqui. Espera o PlayerInventory se registrar.
            // Só inicializa a UI (slots, canvas, etc.)
            InitializeUI();
        }

        void Update()
        {
            if (playerInventory == null || !playerInventory.isLocalPlayer)
                return;

            if (Input.GetKeyDown(toggleKey))
            {
                ToggleInventory();
            }
        }

        // ✅ NOVO: Chamado pelo PlayerInventory quando o Player(Clone) spawna
        public void SetPlayerInventory(PlayerInventory inv)
        {
            if (inv == null || !inv.isLocalPlayer) return;
            if (isSetup) return;  // Já configurado

            Debug.Log($"[InventoryUI] ✅ PlayerInventory registrado: {inv.name}");
            playerInventory = inv;
            playerEquipment = inv.GetComponent<PlayerEquipment>();

            SetupEvents();
            isSetup = true;

            RefreshInventory();
            RefreshEquipment();
        }

        void InitializeUI()
        {
            Debug.Log("[InventoryUI] Inicializando UI...");

            FindExistingSlots();

            if (inventoryPanel != null)
            {
                panelRectTransform = inventoryPanel.GetComponent<RectTransform>();
                if (dragHandle == null)
                {
                    var title = inventoryPanel.transform.Find("Title");
                    if (title != null) dragHandle = title;
                }
            }

            if (dragCanvas == null)
            {
                var existing = GameObject.Find("DragCanvas");
                if (existing != null)
                {
                    dragCanvas = existing.GetComponent<Canvas>();
                    Debug.Log("[InventoryUI] ✅ DragCanvas encontrado");
                }
            }

            inventoryPanel?.SetActive(false);
            tooltipPanel?.SetActive(false);
        }

        void SetupEvents()
        {
            if (playerInventory == null) return;

            playerInventory.OnInventoryChanged += RefreshInventory;
            playerInventory.OnSlotChanged += OnInventorySlotChanged;
            playerInventory.OnItemAdded += OnItemAdded;
            playerInventory.OnItemRemoved += OnItemRemoved;

            if (playerEquipment != null)
            {
                playerEquipment.OnItemEquipped += OnItemEquipped;
                playerEquipment.OnItemUnequipped += OnItemUnequipped;
                playerEquipment.OnVisualsChanged += RefreshEquipment;
                Debug.Log("[InventoryUI] ✅ PlayerEquipment conectado");
            }
        }

        void FindExistingSlots()
        {
            if (inventoryGrid != null)
            {
                var slots = inventoryGrid.GetComponentsInChildren<ItemSlotUI>(true);
                inventorySlots = slots;
                Debug.Log($"[InventoryUI] {slots.Length} ItemSlotUI encontrados");

                for (int i = 0; i < slots.Length; i++)
                    slots[i].Initialize(i, this);
            }

            if (equipmentPanel != null)
            {
                var equipSlots = equipmentPanel.GetComponentsInChildren<EquipmentSlotUI>(true);
                Debug.Log($"[InventoryUI] {equipSlots.Length} EquipmentSlotUI encontrados");

                foreach (var slot in equipSlots)
                {
                    slot.Initialize(this);
                    if (!equipmentSlots.ContainsKey(slot.SlotType))
                        equipmentSlots.Add(slot.SlotType, slot);
                }
            }
        }

        public void ToggleInventory()
        {
            if (inventoryPanel == null) return;
            bool willShow = !inventoryPanel.activeSelf;
            inventoryPanel.SetActive(willShow);
            tooltipPanel?.SetActive(false);
            Debug.Log($"[InventoryUI] Inventário {(willShow ? "ABERTO" : "FECHADO")}");
        }

        public void CloseInventory()
        {
            inventoryPanel?.SetActive(false);
            tooltipPanel?.SetActive(false);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (playerInventory != null)
            {
                playerInventory.OnInventoryChanged -= RefreshInventory;
                playerInventory.OnSlotChanged -= OnInventorySlotChanged;
                playerInventory.OnItemAdded -= OnItemAdded;
                playerInventory.OnItemRemoved -= OnItemRemoved;
            }
            if (playerEquipment != null)
            {
                playerEquipment.OnItemEquipped -= OnItemEquipped;
                playerEquipment.OnItemUnequipped -= OnItemUnequipped;
                playerEquipment.OnVisualsChanged -= RefreshEquipment;
            }
        }

        void OnInventorySlotChanged(int index, InventoryItem newSlot)
        {
            Debug.Log($"[InventoryUI] OnInventorySlotChanged index:{index}, ItemId:{newSlot?.ItemId ?? 0}");
            if (inventorySlots == null || index < 0 || index >= inventorySlots.Length) return;

            if (newSlot == null)
            {
                inventorySlots[index].Clear();
                return;
            }

            var itemData = ItemDatabase.Instance?.GetItem(newSlot.ItemId);
            inventorySlots[index].SetItem(itemData, newSlot.Quantity, DurabilityPercent(newSlot, itemData));
        }

        void OnItemAdded(InventoryItem item, int slotIndex)
        {
            Debug.Log($"[InventoryUI] OnItemAdded: ItemId={item.ItemId} x{item.Quantity} no slot {slotIndex}");
            RefreshInventory();
        }

        void OnItemRemoved(InventoryItem item, int slotIndex)
        {
            Debug.Log($"[InventoryUI] OnItemRemoved: ItemId={item.ItemId} x{item.Quantity} do slot {slotIndex}");
            RefreshInventory();
        }

        void OnItemEquipped(InventoryItem item, EquipmentSlot slot)
        {
            Debug.Log($"[InventoryUI] OnItemEquipped: {slot} - ItemId={item.ItemId}");
            RefreshEquipment();
            RefreshInventory();
        }

        void OnItemUnequipped(InventoryItem item, EquipmentSlot slot)
        {
            Debug.Log($"[InventoryUI] OnItemUnequipped: {slot} - ItemId={item.ItemId}");
            RefreshEquipment();
            RefreshInventory();
        }

        void RefreshInventory()
        {
            if (playerInventory == null || inventorySlots == null) return;

            for (int i = 0; i < inventorySlots.Length; i++)
            {
                InventoryItem slot = playerInventory.GetSlot((ushort)i);
                if (slot == null)
                {
                    inventorySlots[i].Clear();
                    continue;
                }

                var itemData = ItemDatabase.Instance?.GetItem(slot.ItemId);
                if (itemData != null)
                    inventorySlots[i].SetItem(itemData, slot.Quantity, DurabilityPercent(slot, itemData));
                else
                    inventorySlots[i].Clear();
            }
        }

        // A durabilidade atual do item (0 = sem desgaste salvo ainda) vira 0-100% para a UI.
        static int DurabilityPercent(InventoryItem slot, ItemData itemData)
        {
            int max = itemData is EquipmentData eq && eq.maxDurability > 0 ? eq.maxDurability : 100;
            if (slot.Durability <= 0) return 100;
            return Mathf.Clamp(Mathf.RoundToInt(100f * slot.Durability / max), 0, 100);
        }

        void RefreshEquipment()
        {
            if (playerEquipment == null) return;

            foreach (var kvp in equipmentSlots)
            {
                var equippedItem = playerEquipment.GetEquippedItem(kvp.Key);
                if (equippedItem != null)
                {
                    var data = ItemDatabase.Instance.GetEquipment(equippedItem.ItemId);
                    if (data != null)
                        kvp.Value.SetItem(data);
                    else
                        kvp.Value.Clear();
                }
                else
                {
                    kvp.Value.Clear();
                }
            }
        }

        public void OnSlotDoubleClick(int slotIndex)
        {
            if (playerInventory == null) return;
            var slot = playerInventory.GetSlot((ushort)slotIndex);
            if (slot == null) return;

            var itemData = ItemDatabase.Instance?.GetItem(slot.ItemId);
            if (itemData == null) return;

            if (itemData is EquipmentData equipData)
                playerInventory.CmdEquipItem((ushort)slotIndex, equipData.slot);
            else
                playerInventory.CmdUseItem((ushort)slotIndex);
        }

        public void OnSlotSelected(int slotIndex)
        {
            selectedSlot = slotIndex;
            for (int i = 0; i < inventorySlots.Length; i++)
                inventorySlots[i].SetSelected(i == slotIndex);
        }

        public void OnSlotDropped(int fromIndex, int toIndex)
        {
            playerInventory?.CmdMoveItem((ushort)fromIndex, (ushort)toIndex);
        }

        public void OnSlotDraggedToEquipment(int inventoryIndex, EquipmentSlot targetSlot)
        {
            var slot = playerInventory.GetSlot((ushort)inventoryIndex);
            if (slot == null) return;

            var equipData = ItemDatabase.Instance?.GetEquipment(slot.ItemId);
            if (equipData == null) return;

            // Espadas de uma mão podem ir para o slot de escudo (dual wield); o servidor já permite isso
            // em PlayerEquipment.CanWear, então a UI precisa aceitar o mesmo caso para não bloquear o drag.
            bool offhandSword = targetSlot == EquipmentSlot.Shield && equipData.slot == EquipmentSlot.Weapon
                && TOP.Data.PkoTables.Items.TryGetValue(slot.ItemId, out var pi) && pi.Type == 1;

            if (equipData.slot == targetSlot || offhandSword)
                playerInventory.CmdEquipItem((ushort)inventoryIndex, targetSlot);
        }

        public void OnEquipmentDoubleClick(EquipmentSlot slot)
        {
            playerInventory?.CmdUnequipItem(slot);
        }

        public void OnEquipmentDraggedToInventory(EquipmentSlot slot, int targetInventoryIndex)
        {
            // Usa o destino arrastado: se o slot estiver ocupado o servidor rejeita e o item
            // permanece equipado (UnequipItemToSlotOnServer só move quando o alvo está livre).
            playerInventory?.CmdUnequipItemTo(slot, (ushort)targetInventoryIndex);
        }

        public void ShowTooltip(ItemData item, Vector3 position)
        {
            if (item == null || tooltipPanel == null) return;
            tooltipName.text = item.itemName;

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            if (item is EquipmentData equip)
            {
                if (equip.bonusAttack > 0) sb.AppendLine($"ATAQUE +{equip.bonusAttack}");
                if (equip.bonusDefense > 0) sb.AppendLine($"DEFESA +{equip.bonusDefense}");
                if (equip.bonusSTR > 0) sb.AppendLine($"FOR +{equip.bonusSTR}");
                if (equip.bonusAGI > 0) sb.AppendLine($"AGI +{equip.bonusAGI}");
                if (equip.bonusHP > 0) sb.AppendLine($"HP +{equip.bonusHP}");
            }
            tooltipStats.text = sb.ToString();

            Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(null, position);
            tooltipPanel.transform.position = screenPos + new Vector2(80, 0);
            tooltipPanel.SetActive(true);
        }

        public void HideTooltip()
        {
            tooltipPanel?.SetActive(false);
        }
    }
}